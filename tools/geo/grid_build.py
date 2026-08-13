"""Build the Low/Mid/High global tile grids from the verified .geo-cache sources.

Adapter layout:

* Natural Earth 5.1.2 1:10m land + lakes polygons -> land/water mask
* Natural Earth 5.1.2 1:10m coastline + rivers polylines -> coast/river flags
* NOAA ETOPO 2022 60" ice-surface GeoTIFF -> elevation and slope
* GloH2O Köppen-Geiger V3 1991-2020 0.5 degree classification -> climate and biome

Water cells carry **no upstream climate**: Köppen classifies land and leaves water
as NoData, so climate zone, temperature and rainfall on every non-land cell are
synthetic latitude bands invented in ``koppen.py`` (see
``WATER_CLIMATE_IS_SYNTHETIC``), not Köppen-derived values. ``build_grid`` flags
every water cell ``IsInterpolated`` so those synthetic fields are not mistaken
for upstream data; water elevation does remain sourced from ETOPO.

Everything is a pure function of the cached bytes: fixed traversal order, integer
elevation accumulation, fixed tie-breaks, and no random or time-dependent input,
so two builds from the same cache produce identical chunk bytes.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

import numpy as np

import koppen
import rasterize
from fetch_sources import extract_dir, source_archive_path
from geotiff import GeoTiff
from rasterize import GRIDS, Grid
from shapefile_reader import find_layer, read_parts, read_parts_with_attributes

FLAG_LAND = 1
FLAG_COAST = 2
FLAG_WATER = 4
FLAG_RIVER = 8
FLAG_INTERPOLATED = 16

TILE_DTYPE = np.dtype(
    [
        ("flags", "u1"),
        ("biome", "u1"),
        ("climate", "u1"),
        ("elevation", "<i2"),
        ("slope", "u1"),
        ("temperature", "<i2"),
        ("rainfall", "<u2"),
    ]
)
TILE_RECORD_BYTES = 10

ETOPO_WIDTH = 21600
ETOPO_HEIGHT = 10800
ETOPO_STEP = 1.0 / 60.0
KOPPEN_WIDTH = 720
KOPPEN_HEIGHT = 360
KOPPEN_STEP = 0.5
KOPPEN_LEGEND_NAME = "legend.txt"

ELEVATION_LIMITS_M = (-32768, 32767)
# Slope ships as one unsigned byte of tenths of a degree, so 25.5 degrees is the
# largest representable angle (WorldMapBundleReader divides the byte by 10).
SLOPE_LIMIT_DEGREES = 25.5
SLOPE_STEPS = 256

# Boundaries between adjacent slope bytes, expressed as rise/run rather than as an
# angle: entry k is tan((k + 0.5) / 10 degrees). Quantising by comparison against
# this table means the per-cell path uses only subtraction, division and comparison
# -- all IEEE-754 exact operations -- instead of calling atan on every cell, whose
# last-bit result may differ between platforms and would then change the stored
# byte for cells sitting near a boundary. The table itself is built once from
# math.tan and pinned by digest in the tests, so a libm difference fails a test
# instead of silently producing different bundle bytes.
SLOPE_RISE_OVER_RUN_BOUNDARIES = np.array(
    [math.tan(math.radians((step + 0.5) / 10.0)) for step in range(SLOPE_STEPS - 1)],
    dtype=np.float64,
)
if not np.all(np.diff(SLOPE_RISE_OVER_RUN_BOUNDARIES) > 0.0):
    raise AssertionError("slope quantisation boundaries must be strictly increasing")
# Last-resort elevation when a cell has no ETOPO pixel and no valid cell within the
# nearest-neighbour radius: |lat| upper bound -> metres. ETOPO 2022 60" is globally
# complete, so this table exists for fail-safety rather than for the current sources.
# Every band is deliberately non-zero: 0 m is a genuine, common elevation, so using
# it as a guess would let an unresolved cell masquerade as real sea level. Cells
# filled from here always carry IsInterpolated as well, but the value alone should
# not look like measured lowland either.
ELEVATION_LATITUDE_FALLBACK_M: tuple[tuple[float, int], ...] = (
    (60.0, 200),
    (70.0, 300),
    (80.0, 1000),
    (90.0, 2000),
)
METRES_PER_DEGREE = 111319.4907932736  # WGS84 equatorial degree
POLAR_COS_FLOOR = math.cos(math.radians(85.0))
MAX_NEAREST_FILL_DEGREES = 4.0

# Fixed neighbour priority for the nearest-neighbour fill; ties inside one
# Chebyshev ring always resolve in this order.
RING_PRIORITY: tuple[tuple[int, int], ...] = (
    (-1, 0),
    (1, 0),
    (0, -1),
    (0, 1),
    (-1, -1),
    (-1, 1),
    (1, -1),
    (1, 1),
)

SOURCE_LAYERS = {
    "land": ("ne-10m-land", "ne_10m_land"),
    "lakes": ("ne-10m-lakes", "ne_10m_lakes"),
    "coastline": ("ne-10m-coastline", "ne_10m_coastline"),
    "rivers": ("ne-10m-rivers", "ne_10m_rivers_lake_centerlines"),
}

# Only true watercourses become river cells. `Lake Centerline` features trace a route
# through a lake, whose area is already water via the lakes polygons, and `Canal` is
# an artificial modern cut, so both are excluded rather than flagged as rivers.
RIVER_FEATURE_CLASSES = frozenset({"River", "River (Intermittent)"})

# Natural Earth publishes `scalerank` as the smallest map scale at which a river
# should be drawn (0 = most prominent). A 2-degree cell is ~222 km across, so
# admitting every minor tributary saturates the coarse grids -- at 1:10m detail the
# unfiltered layer marks 45% of all Low land cells as river, which tells a consumer
# nothing. Capping the rank per LOD keeps the flag meaningful and the rule stable:
# the caps rise with resolution, the admitted feature sets are strictly nested, and
# the resulting coverage stays near 5-8% of land at every LOD.
RIVER_SCALERANK_MAX_BY_LOD = {"Low": 2, "Mid": 4, "High": 6}
RIVER_ATTRIBUTES = ("scalerank", "featurecla")


class BuildError(RuntimeError):
    """A locked source is missing from the cache or does not match the expected grid."""


@dataclass(frozen=True)
class SourcePaths:
    land: Path
    lakes: Path
    coastline: Path
    rivers: Path
    etopo: Path
    koppen: Path


def resolve_sources(lock: dict[str, Any], cache: Path) -> SourcePaths:
    """Locate the cached raster/vector files this build reads. Fails closed if absent."""
    by_id = {source["id"]: source for source in lock["sources"]}
    missing = [
        source_id
        for source_id in (
            *(entry[0] for entry in SOURCE_LAYERS.values()),
            "etopo-2022-60s-surface",
            "koppen-geiger-v3-1991-2020",
        )
        if source_id not in by_id
    ]
    if missing:
        raise BuildError("lock is missing required sources: " + ", ".join(sorted(missing)))
    paths: dict[str, Path] = {}
    for key, (source_id, layer) in SOURCE_LAYERS.items():
        source = by_id[source_id]
        directory = extract_dir(source, cache)
        if not directory.is_dir():
            raise BuildError(
                f"{source_id}: extracted shapefiles missing at {directory}. Run fetch first."
            )
        paths[key] = find_layer(directory, layer)
    etopo = source_archive_path(by_id["etopo-2022-60s-surface"], cache)
    if not etopo.is_file():
        raise BuildError(f"etopo-2022-60s-surface: {etopo} is not in the cache. Run fetch first.")
    koppen_source = by_id["koppen-geiger-v3-1991-2020"]
    layer = koppen_source["selectedLayers"][0]
    koppen_tif = extract_dir(koppen_source, cache) / layer
    if not koppen_tif.is_file():
        raise BuildError(
            f"koppen-geiger-v3-1991-2020: {koppen_tif} is not in the cache. Run fetch first."
        )
    return SourcePaths(
        land=paths["land"],
        lakes=paths["lakes"],
        coastline=paths["coastline"],
        rivers=paths["rivers"],
        etopo=etopo,
        koppen=koppen_tif,
    )


# ---- shifts and nearest-neighbour fill -----------------------------------


def shift_from(values: np.ndarray, dy: int, dx: int, fill) -> np.ndarray:
    """``out[y, x] = values[y + dy, x + dx]``; longitude wraps, latitude does not."""
    out = np.roll(values, -dx, axis=1)
    if dy == 0:
        return out
    pad = np.full((abs(dy), values.shape[1]), fill, dtype=values.dtype)
    if dy > 0:
        return np.concatenate([out[dy:], pad], axis=0)
    return np.concatenate([pad, out[:dy]], axis=0)


def nearest_fill(
    values: np.ndarray, valid: np.ndarray, target: np.ndarray, max_rounds: int
) -> tuple[np.ndarray, np.ndarray]:
    """Fill ``target & ~valid`` from the nearest valid cell (Chebyshev-ring BFS).

    Returns the filled values and the mask of cells that were filled. Bounded by
    ``max_rounds`` rings; anything still unfilled is left to the caller's
    latitude fallback.
    """
    values = values.copy()
    valid = valid.copy()
    filled = np.zeros(values.shape, dtype=bool)
    zero = values.dtype.type(0)
    for _ in range(max_rounds):
        pending = target & ~valid
        if not pending.any():
            break
        picked = np.zeros(values.shape, dtype=bool)
        candidate = np.zeros(values.shape, dtype=values.dtype)
        for dy, dx in RING_PRIORITY:
            source_valid = shift_from(valid, dy, dx, False)
            source_values = shift_from(values, dy, dx, zero)
            take = pending & ~picked & source_valid
            if take.any():
                candidate[take] = source_values[take]
                picked |= take
        if not picked.any():
            break
        values[picked] = candidate[picked]
        valid |= picked
        filled |= picked
    return values, filled


def round_half_away_from_zero(values: np.ndarray) -> np.ndarray:
    """Deterministic symmetric rounding (numpy's default rint is half-to-even)."""
    return np.trunc(values + np.copysign(0.5, values))


def _band_metres(latitude: np.ndarray) -> np.ndarray:
    edges = np.array([bound for bound, _ in ELEVATION_LATITUDE_FALLBACK_M], dtype=np.float64)
    values = np.array([metres for _, metres in ELEVATION_LATITUDE_FALLBACK_M], dtype=np.float64)
    index = np.searchsorted(edges, np.abs(latitude), side="left")
    np.clip(index, 0, values.size - 1, out=index)
    return values[index]


# ---- vector layers -------------------------------------------------------


@dataclass
class VectorParts:
    land: list[np.ndarray]
    lakes: list[np.ndarray]
    coastline: list[np.ndarray]
    rivers: list[np.ndarray]
    #: ``scalerank`` of each entry in ``rivers``; ``None`` where upstream left it blank.
    river_scalerank: list[int | None]

    def rivers_for_lod(self, lod_name: str) -> list[np.ndarray]:
        """River parts admitted at this LOD, per :data:`RIVER_SCALERANK_MAX_BY_LOD`."""
        try:
            cap = RIVER_SCALERANK_MAX_BY_LOD[lod_name]
        except KeyError:
            raise BuildError(
                f"no river scalerank cap configured for LOD {lod_name!r}; "
                f"known: {sorted(RIVER_SCALERANK_MAX_BY_LOD)}"
            ) from None
        return [
            part
            for part, rank in zip(self.rivers, self.river_scalerank)
            # A blank scalerank cannot be shown to be prominent, so it is excluded
            # rather than silently admitted at every LOD.
            if rank is not None and rank <= cap
        ]


def load_vector_parts(paths: SourcePaths) -> VectorParts:
    river_parts, river_attributes = read_parts_with_attributes(paths.rivers, RIVER_ATTRIBUTES)
    kept: list[np.ndarray] = []
    ranks: list[int | None] = []
    for part, rank, feature_class in zip(
        river_parts, river_attributes["scalerank"], river_attributes["featurecla"]
    ):
        if feature_class not in RIVER_FEATURE_CLASSES:
            continue
        kept.append(part)
        ranks.append(rank)
    return VectorParts(
        land=read_parts(paths.land),
        lakes=read_parts(paths.lakes),
        coastline=read_parts(paths.coastline),
        rivers=kept,
        river_scalerank=ranks,
    )


@dataclass
class VectorMasks:
    land: np.ndarray
    water: np.ndarray
    coast: np.ndarray
    river: np.ndarray


def vector_masks(parts: VectorParts, grid: Grid) -> VectorMasks:
    """Land/water/coast/river masks for one grid.

    Lakes count as water, so they are subtracted from the land mask. A land cell
    is coastal when a coastline segment crosses it or when any orthogonal
    neighbour is water. Rivers stay a land feature, matching the flag semantics
    the runtime already consumes, and are filtered by ``scalerank`` for this LOD
    so a coarse grid is not saturated by tributaries it cannot resolve.
    """
    land_polygons = rasterize.polygon_mask(parts.land, grid)
    lakes = rasterize.polygon_mask(parts.lakes, grid)
    land = land_polygons & ~lakes
    water_body = ~land
    coastline_cells = rasterize.polyline_mask(parts.coastline, grid)
    river_cells = rasterize.polyline_mask(parts.rivers_for_lod(grid.name), grid)
    coast = land & (coastline_cells | rasterize.orthogonal_neighbor_any(water_body))
    river = river_cells & land
    water = water_body | coast | river
    return VectorMasks(land=land, water=water, coast=coast, river=river)


# ---- elevation -----------------------------------------------------------


@dataclass
class ElevationAccumulator:
    """Integer elevation sums and valid-pixel counts on the High (0.5 degree) grid."""

    sums: np.ndarray
    counts: np.ndarray


def aggregate_elevation(path: Path) -> ElevationAccumulator:
    """Stream the ETOPO GeoTIFF once, accumulating per High-grid-cell sums and counts.

    Peak memory is one decoded 256x256 tile. Source values are rounded to whole
    metres before accumulation so the sums are exact integers (representable in
    float64) and the result cannot depend on floating-point summation order.
    """
    raster = GeoTiff(path)
    raster.require_grid(ETOPO_WIDTH, ETOPO_HEIGHT, ETOPO_STEP, -180.0, 90.0)
    grid = rasterize.HIGH
    factor_x = ETOPO_WIDTH // grid.width
    factor_y = ETOPO_HEIGHT // grid.height
    if factor_x * grid.width != ETOPO_WIDTH or factor_y * grid.height != ETOPO_HEIGHT:
        raise BuildError("ETOPO dimensions are not an integer multiple of the High grid")
    nodata = raster.nodata_value()
    cells = grid.cells
    sums = np.zeros(cells, dtype=np.float64)
    counts = np.zeros(cells, dtype=np.int64)
    for x0, y0, block in raster.iter_blocks():
        rows, cols = block.shape
        values = round_half_away_from_zero(block.astype(np.float64))
        out_x = ((x0 + np.arange(cols, dtype=np.int64)) // factor_x)[None, :]
        out_y = ((y0 + np.arange(rows, dtype=np.int64)) // factor_y)[:, None]
        flat = (out_y * grid.width + out_x).ravel()
        values = values.ravel()
        if nodata is not None:
            keep = values != nodata
            if not keep.all():
                flat = flat[keep]
                values = values[keep]
        if flat.size == 0:
            continue
        sums += np.bincount(flat, weights=values, minlength=cells)
        counts += np.bincount(flat, minlength=cells)
    return ElevationAccumulator(
        sums=sums.reshape(grid.height, grid.width),
        counts=counts.reshape(grid.height, grid.width),
    )


def elevation_for_grid(
    accumulator: ElevationAccumulator, grid: Grid
) -> tuple[np.ndarray, np.ndarray]:
    """Mean ETOPO elevation in whole metres plus the mask of source-NoData cells.

    Coarser grids reduce the High-grid sums and counts, which is exactly the mean
    over the same set of source pixels because every High cell covers the same
    number of ETOPO cells.
    """
    high = rasterize.HIGH
    factor = high.width // grid.width
    if factor * grid.width != high.width or (high.height // factor) != grid.height:
        raise BuildError(f"{grid.name}: not an integer reduction of the High grid")
    if factor == 1:
        sums, counts = accumulator.sums, accumulator.counts
    else:
        sums = accumulator.sums.reshape(grid.height, factor, grid.width, factor).sum(axis=(1, 3))
        counts = accumulator.counts.reshape(grid.height, factor, grid.width, factor).sum(
            axis=(1, 3)
        )
    missing = counts == 0
    safe_counts = np.where(missing, 1, counts)
    metres = round_half_away_from_zero(sums / safe_counts)
    metres[missing] = 0.0
    return metres, missing


def slope_rise_over_run(elevation_m: np.ndarray, grid: Grid) -> np.ndarray:
    """Steepest ``rise / run`` to an orthogonal neighbour.

    The rise is the absolute elevation difference and the run is the ground distance
    between the two cell centres. Zonal spacing shrinks with cos(latitude); it is
    floored at the value for 85 degrees so polar cells stay bounded instead of
    producing a division blow-up.
    """
    lats = grid.lats()
    meridional = grid.step * METRES_PER_DEGREE
    cosines = np.maximum(np.cos(np.radians(lats)), POLAR_COS_FLOOR)
    zonal = (grid.step * METRES_PER_DEGREE * cosines)[:, None]
    values = elevation_m.astype(np.float64)
    steepest = np.zeros(values.shape, dtype=np.float64)
    for dy, dx in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        neighbour = shift_from(values, dy, dx, 0.0)
        present = shift_from(np.ones(values.shape, dtype=bool), dy, dx, False)
        distance = zonal if dy == 0 else meridional
        gradient = np.abs(neighbour - values) / distance
        steepest = np.where(present, np.maximum(steepest, gradient), steepest)
    return steepest


def slope_bytes(elevation_m: np.ndarray, grid: Grid) -> np.ndarray:
    """Slope quantised straight to the stored byte, in tenths of a degree.

    Degrees are what the runtime consumes: ``WorldMapBundleReader`` divides the byte
    by 10, and gameplay compares against 18-20 for "too steep", which is only
    meaningful as an angle. The value is produced by comparing ``rise / run``
    against :data:`SLOPE_RISE_OVER_RUN_BOUNDARIES` rather than by rounding
    ``atan``, so the result cannot shift by one tenth of a degree between platforms.
    Values above 25.5 degrees saturate at 255.
    """
    steepest = slope_rise_over_run(elevation_m, grid)
    return np.searchsorted(SLOPE_RISE_OVER_RUN_BOUNDARIES, steepest, side="left").astype(np.uint8)


def slope_degrees(elevation_m: np.ndarray, grid: Grid) -> np.ndarray:
    """The stored slope as degrees, i.e. exactly what the runtime will read."""
    return slope_bytes(elevation_m, grid).astype(np.float64) / 10.0


# ---- climate -------------------------------------------------------------


def load_koppen_codes(path: Path) -> np.ndarray:
    """Read the 0.5 degree classification into a 360x720 uint8 array of codes 0..30."""
    raster = GeoTiff(path)
    raster.require_grid(KOPPEN_WIDTH, KOPPEN_HEIGHT, KOPPEN_STEP, -180.0, 90.0)
    nodata = raster.nodata_value()
    if nodata is not None and nodata != koppen.NODATA_CODE:
        raise BuildError(
            f"{path.name}: GDAL_NODATA {nodata} does not match the expected class NoData "
            f"{koppen.NODATA_CODE}"
        )
    codes = np.zeros((KOPPEN_HEIGHT, KOPPEN_WIDTH), dtype=np.uint8)
    for x0, y0, block in raster.iter_blocks():
        codes[y0 : y0 + block.shape[0], x0 : x0 + block.shape[1]] = block
    out_of_range = codes > koppen.CLASS_COUNT
    if out_of_range.any():
        raise BuildError(
            f"{path.name}: {int(out_of_range.sum())} pixels hold codes above "
            f"{koppen.CLASS_COUNT}; the legend covers 1..{koppen.CLASS_COUNT}"
        )
    return codes


def codes_for_grid(codes_high: np.ndarray, grid: Grid) -> np.ndarray:
    """Majority class per output cell; ties break to the lowest class code."""
    high = rasterize.HIGH
    factor = high.width // grid.width
    if factor * grid.width != high.width:
        raise BuildError(f"{grid.name}: not an integer reduction of the Köppen 0.5 degree grid")
    return rasterize.majority_downsample(
        codes_high, factor, koppen.CLASS_COUNT, nodata=koppen.NODATA_CODE
    )


# ---- assembly ------------------------------------------------------------


@dataclass
class GridResult:
    grid: Grid
    records: np.ndarray
    stats: dict[str, Any]


def build_grid(
    grid: Grid,
    parts: VectorParts,
    accumulator: ElevationAccumulator,
    codes_high: np.ndarray,
) -> GridResult:
    masks = vector_masks(parts, grid)
    elevation, elevation_missing = elevation_for_grid(accumulator, grid)
    codes = codes_for_grid(codes_high, grid)
    lats = np.repeat(grid.lats()[:, None], grid.width, axis=1)

    max_rounds = max(1, int(MAX_NEAREST_FILL_DEGREES / grid.step))
    elevation_unresolved = np.zeros(elevation.shape, dtype=bool)
    if elevation_missing.any():
        elevation, elevation_filled = nearest_fill(
            elevation, ~elevation_missing, elevation_missing, max_rounds
        )
        elevation_unresolved = elevation_missing & ~elevation_filled
        if elevation_unresolved.any():
            fallback = _band_metres(lats)
            elevation = np.where(elevation_unresolved, fallback, elevation)

    class_valid = codes > koppen.NODATA_CODE
    needs_class = masks.land & ~class_valid
    codes, _class_filled = nearest_fill(codes, class_valid, needs_class, max_rounds)
    class_unresolved = masks.land & (codes == koppen.NODATA_CODE)
    if class_unresolved.any():
        fallback = koppen.latitude_fallback_class(lats)
        codes = np.where(class_unresolved, fallback, codes).astype(np.uint8)
    # IsInterpolated marks a tile whose stored values are not all sourced. Two
    # separate causes are folded into the one flag the format has:
    #   * land where the Köppen class or the ETOPO elevation had to be filled in;
    #   * every water cell, because its climate zone, temperature and rainfall are
    #     synthetic latitude bands rather than anything upstream (Köppen leaves
    #     water as NoData). Water elevation itself is still sourced from ETOPO.
    # WorldGeography.SynthesiseOceanTile already sets the same flag for the tiles it
    # invents, so this keeps one consistent meaning across builder and runtime.
    interpolated = needs_class | elevation_missing | ~masks.land

    elevation = np.clip(elevation, *ELEVATION_LIMITS_M)
    slope = slope_bytes(elevation, grid)

    if (masks.land & (codes == koppen.NODATA_CODE)).any():
        raise BuildError(f"{grid.name}: land cells still have no Köppen class after fallback")

    climate = np.where(masks.land, koppen.CLIMATE_ZONE_LUT[codes], koppen.ocean_climate_zone(lats))
    biome = np.where(masks.land, koppen.BIOME_LUT[codes], np.uint8(koppen.OCEAN))
    climate, biome = koppen.apply_elevation_overrides(
        climate.astype(np.uint8), biome.astype(np.uint8), elevation, lats, masks.land
    )
    temperature = np.where(
        masks.land,
        koppen.land_temperature_c(codes, lats, elevation),
        koppen.ocean_temperature_c(lats),
    )
    rainfall = np.where(
        masks.land,
        koppen.land_rainfall_mm(codes, lats, elevation),
        koppen.ocean_rainfall_mm(lats),
    )

    flags = np.zeros(elevation.shape, dtype=np.uint8)
    flags |= np.where(masks.land, FLAG_LAND, 0).astype(np.uint8)
    flags |= np.where(masks.coast, FLAG_COAST, 0).astype(np.uint8)
    flags |= np.where(masks.water, FLAG_WATER, 0).astype(np.uint8)
    flags |= np.where(masks.river, FLAG_RIVER, 0).astype(np.uint8)
    flags |= np.where(interpolated, FLAG_INTERPOLATED, 0).astype(np.uint8)

    records = np.zeros(grid.cells, dtype=TILE_DTYPE)
    records["flags"] = flags.ravel()
    records["biome"] = biome.ravel()
    records["climate"] = climate.ravel()
    records["elevation"] = elevation.ravel().astype(np.int16)
    records["slope"] = slope.ravel()
    records["temperature"] = round_half_away_from_zero(temperature * 10.0).ravel().astype(np.int16)
    records["rainfall"] = np.clip(
        rainfall, *koppen.RAINFALL_LIMITS_MM
    ).ravel().astype(np.uint16)

    stats = {
        "cells": grid.cells,
        "land": int(masks.land.sum()),
        "coast": int(masks.coast.sum()),
        "river": int(masks.river.sum()),
        "water": int(masks.water.sum()),
        "interpolated": int(interpolated.sum()),
        "syntheticWaterClimate": int((~masks.land).sum()),
        "elevationMissing": int(elevation_missing.sum()),
        "elevationUnresolved": int(elevation_unresolved.sum()),
        "classFilledByNeighbour": int((needs_class & ~class_unresolved).sum()),
        "classFilledByLatitude": int(class_unresolved.sum()),
        "elevationMin": int(records["elevation"].min()),
        "elevationMax": int(records["elevation"].max()),
        "riverFeatures": len(parts.rivers_for_lod(grid.name)),
        "riverScalerankMax": RIVER_SCALERANK_MAX_BY_LOD[grid.name],
        "slopeMaxDegrees": round(float(records["slope"].max()) / 10.0, 1),
    }
    return GridResult(grid=grid, records=records, stats=stats)


def reconcile_koppen_legend(koppen_raster: Path) -> Path:
    """Check the mapping table against the ``legend.txt`` shipped in the same archive.

    The legend sits at the root of the extracted Köppen zip while the rasters sit in
    period sub-folders, so walk up to the extraction root to find it. Missing or
    disagreeing legends fail the build: the mapping table is only auditable if it
    still matches what upstream published.
    """
    for parent in koppen_raster.parents:
        candidate = parent / KOPPEN_LEGEND_NAME
        if candidate.is_file():
            koppen.reconcile_with_legend(candidate)
            return candidate
        if parent.name == "extracted":
            break
    raise BuildError(
        f"{KOPPEN_LEGEND_NAME} was not found alongside {koppen_raster.name}; the "
        "Köppen mapping table cannot be reconciled with the upstream legend"
    )


def build_all(lock: dict[str, Any], cache: Path, grids: Iterable[Grid] = GRIDS) -> list[GridResult]:
    paths = resolve_sources(lock, cache)
    reconcile_koppen_legend(paths.koppen)
    parts = load_vector_parts(paths)
    accumulator = aggregate_elevation(paths.etopo)
    codes_high = load_koppen_codes(paths.koppen)
    return [build_grid(grid, parts, accumulator, codes_high) for grid in grids]
