"""Köppen-Geiger V3 class tables and the derived climate proxies.

The 31 raster values of the GloH2O Köppen-Geiger V3 maps (0 = NoData plus the 30
classes of ``legend.txt``) are mapped to WorldSim's ``ClimateZone`` and
``BiomeType`` through the single explicit table below, so every assignment can be
audited by reading one row.

**Temperature and rainfall are derived proxies, not sourced values.** The locked
Köppen product ships classifications only; it contains no temperature or
precipitation rasters. Both fields are therefore climatological stand-ins computed
the same way -- a per-class reference value plus bounded corrections for the cell's
deviation from that class's reference latitude and reference elevation -- using the
fixed constants documented in this module. They must not be presented as measured
climate data.

**Water has no upstream climate at all.** Köppen classifies land and leaves water as
NoData, so climate zone, temperature and rainfall on non-land cells are synthetic
latitude bands invented here; see :data:`WATER_CLIMATE_IS_SYNTHETIC`.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np

NODATA_CODE = 0
CLASS_COUNT = 30
VALUE_COUNT = CLASS_COUNT + 1

# ClimateZone (GeoContracts.cs)
POLAR, SUBPOLAR, TEMPERATE, ARID, SUBTROPICAL, TROPICAL, HIGHLAND = range(7)
# BiomeType (GeoContracts.cs)
OCEAN, ICE, TUNDRA, BOREAL_FOREST, TEMPERATE_FOREST = 0, 1, 2, 3, 4
GRASSLAND, DESERT, SAVANNA, TROPICAL_RAINFOREST, ALPINE, WETLAND = 5, 6, 7, 8, 9, 10


@dataclass(frozen=True)
class KoppenClass:
    """One row of the audit table.

    ``baseline_temperature_c`` is the proxy annual mean temperature for a cell that
    sits at this class's reference latitude and reference elevation; the other two
    reference columns anchor the latitude and elevation corrections. None of these
    are source measurements.
    """

    code: int
    symbol: str
    description: str
    climate_zone: int
    biome: int
    baseline_temperature_c: float
    reference_abs_latitude: float
    reference_elevation_m: float
    annual_rainfall_mm: int


CLASSES: tuple[KoppenClass, ...] = (
    KoppenClass(1, "Af", "Tropical, rainforest", TROPICAL, TROPICAL_RAINFOREST, 26.0, 5.0, 150.0, 2200),
    KoppenClass(2, "Am", "Tropical, monsoon", TROPICAL, TROPICAL_RAINFOREST, 26.5, 10.0, 200.0, 1800),
    KoppenClass(3, "Aw", "Tropical, savannah", TROPICAL, SAVANNA, 26.5, 15.0, 400.0, 1100),
    KoppenClass(4, "BWh", "Arid, desert, hot", ARID, DESERT, 24.0, 25.0, 400.0, 100),
    KoppenClass(5, "BWk", "Arid, desert, cold", ARID, DESERT, 11.0, 40.0, 900.0, 150),
    KoppenClass(6, "BSh", "Arid, steppe, hot", ARID, GRASSLAND, 24.0, 22.0, 500.0, 350),
    KoppenClass(7, "BSk", "Arid, steppe, cold", ARID, GRASSLAND, 9.0, 42.0, 800.0, 300),
    KoppenClass(8, "Csa", "Temperate, dry summer, hot summer", SUBTROPICAL, TEMPERATE_FOREST, 17.5, 37.0, 300.0, 550),
    KoppenClass(9, "Csb", "Temperate, dry summer, warm summer", TEMPERATE, TEMPERATE_FOREST, 14.0, 42.0, 300.0, 750),
    KoppenClass(10, "Csc", "Temperate, dry summer, cold summer", TEMPERATE, TEMPERATE_FOREST, 8.0, 48.0, 700.0, 900),
    KoppenClass(11, "Cwa", "Temperate, dry winter, hot summer", SUBTROPICAL, TEMPERATE_FOREST, 19.0, 27.0, 500.0, 1150),
    KoppenClass(12, "Cwb", "Temperate, dry winter, warm summer", TEMPERATE, TEMPERATE_FOREST, 16.0, 22.0, 1400.0, 950),
    KoppenClass(13, "Cwc", "Temperate, dry winter, cold summer", TEMPERATE, TEMPERATE_FOREST, 9.0, 20.0, 2200.0, 900),
    KoppenClass(14, "Cfa", "Temperate, no dry season, hot summer", SUBTROPICAL, TEMPERATE_FOREST, 17.5, 32.0, 200.0, 1200),
    KoppenClass(15, "Cfb", "Temperate, no dry season, warm summer", TEMPERATE, TEMPERATE_FOREST, 10.5, 48.0, 200.0, 950),
    KoppenClass(16, "Cfc", "Temperate, no dry season, cold summer", TEMPERATE, BOREAL_FOREST, 6.5, 58.0, 200.0, 1300),
    KoppenClass(17, "Dsa", "Cold, dry summer, hot summer", TEMPERATE, GRASSLAND, 10.0, 38.0, 1200.0, 450),
    KoppenClass(18, "Dsb", "Cold, dry summer, warm summer", TEMPERATE, GRASSLAND, 7.0, 42.0, 1300.0, 500),
    KoppenClass(19, "Dsc", "Cold, dry summer, cold summer", SUBPOLAR, BOREAL_FOREST, 2.0, 48.0, 1600.0, 600),
    KoppenClass(20, "Dsd", "Cold, dry summer, very cold winter", SUBPOLAR, BOREAL_FOREST, -3.0, 55.0, 1200.0, 400),
    KoppenClass(21, "Dwa", "Cold, dry winter, hot summer", TEMPERATE, TEMPERATE_FOREST, 9.0, 38.0, 400.0, 650),
    KoppenClass(22, "Dwb", "Cold, dry winter, warm summer", TEMPERATE, BOREAL_FOREST, 4.0, 45.0, 500.0, 550),
    KoppenClass(23, "Dwc", "Cold, dry winter, cold summer", SUBPOLAR, BOREAL_FOREST, -3.0, 53.0, 500.0, 450),
    KoppenClass(24, "Dwd", "Cold, dry winter, very cold winter", SUBPOLAR, BOREAL_FOREST, -12.0, 62.0, 600.0, 350),
    KoppenClass(25, "Dfa", "Cold, no dry season, hot summer", TEMPERATE, TEMPERATE_FOREST, 9.0, 41.0, 250.0, 850),
    KoppenClass(26, "Dfb", "Cold, no dry season, warm summer", TEMPERATE, TEMPERATE_FOREST, 5.0, 50.0, 250.0, 750),
    KoppenClass(27, "Dfc", "Cold, no dry season, cold summer", SUBPOLAR, BOREAL_FOREST, -3.0, 60.0, 300.0, 550),
    KoppenClass(28, "Dfd", "Cold, no dry season, very cold winter", SUBPOLAR, BOREAL_FOREST, -11.0, 66.0, 300.0, 350),
    KoppenClass(29, "ET", "Polar, tundra", POLAR, TUNDRA, -7.0, 72.0, 1500.0, 300),
    KoppenClass(30, "EF", "Polar, frost", POLAR, ICE, -30.0, 82.0, 2000.0, 150),
)

# --- derived-proxy constants (documented, not sourced) ----------------------
# A Köppen class already encodes most of the local elevation and latitude signal
# (tropical high mountains are ET, Arctic lowlands are also ET), so both
# corrections are measured against the class's own reference point and are bounded.
# The elevation coefficient is deliberately far below a free-air lapse rate: it
# only expresses the residual difference from the class reference elevation.
LATITUDE_GRADIENT_C_PER_DEG = 0.25
LATITUDE_TERM_LIMIT_C = 12.0
ELEVATION_RESIDUAL_C_PER_M = 0.0015
ELEVATION_TERM_LIMIT_C = 15.0
LAND_TEMPERATURE_LIMITS_C = (-60.0, 45.0)

# Rainfall uses the same shape as temperature: the class total is the value at the
# class's own reference latitude and elevation, and both corrections are bounded
# fractions of that total measured against those references.
#   * equatorward of the reference latitude the proxy gets wetter, reflecting the
#     stronger convective supply nearer the tropics within one class;
#   * above the reference elevation the proxy gets wetter, standing in for
#     orographic enhancement.
# Coefficients are deliberately small: the class already carries most of the signal,
# so these only spread values inside a class rather than redefining it.
RAINFALL_LATITUDE_FRACTION_PER_DEG = 0.010
RAINFALL_LATITUDE_FRACTION_LIMIT = 0.25
RAINFALL_ELEVATION_FRACTION_PER_100M = 0.020
RAINFALL_ELEVATION_FRACTION_LIMIT = 0.30
# Total multiplier is clamped so no class can be pushed to an implausible total.
RAINFALL_FACTOR_LIMITS = (0.55, 1.55)
# Plausibility clamp on the derived value, well inside the uint16 storage range.
LAND_RAINFALL_LIMITS_MM = (0, 8000)

OCEAN_SURFACE_TEMPERATURE_C = 28.0
OCEAN_LATITUDE_GRADIENT_C_PER_DEG = 0.36
OCEAN_TEMPERATURE_LIMITS_C = (-2.0, 30.0)

HIGHLAND_ELEVATION_M = 2500
HIGHLAND_MAX_ABS_LATITUDE = 60.0
ALPINE_ELEVATION_M = 3000

RAINFALL_LIMITS_MM = (0, 65535)

# Water cells carry no upstream climate at all: the Köppen product classifies land
# and marks water as NoData, so climate zone, baseline temperature and rainfall on
# every non-land cell come from the latitude bands below, which this builder invents.
# They must never be described as Köppen-derived. Because those three fields are
# unsourced, the build sets IsInterpolated on every water tile (see
# grid_build.build_grid); elevation on water cells does remain sourced from ETOPO.
WATER_CLIMATE_IS_SYNTHETIC = True

# |lat| upper bound -> value, evaluated in order.
OCEAN_CLIMATE_BANDS: tuple[tuple[float, int], ...] = (
    (23.5, TROPICAL),
    (40.0, SUBTROPICAL),
    (55.0, TEMPERATE),
    (66.5, SUBPOLAR),
    (90.0, POLAR),
)
OCEAN_RAINFALL_BANDS: tuple[tuple[float, int], ...] = (
    (10.0, 2000),
    (20.0, 1300),
    (30.0, 700),
    (40.0, 800),
    (50.0, 1000),
    (60.0, 900),
    (70.0, 600),
    (80.0, 350),
    (90.0, 200),
)
# Last-resort class when a land cell has no Köppen class anywhere within the
# nearest-neighbour search radius.
LATITUDE_FALLBACK_CLASSES: tuple[tuple[float, int], ...] = (
    (10.0, 1),   # Af
    (23.5, 3),   # Aw
    (35.0, 14),  # Cfa
    (50.0, 26),  # Dfb
    (60.0, 27),  # Dfc
    (70.0, 29),  # ET
    (90.0, 30),  # EF
)


def _table(field: str, dtype) -> np.ndarray:
    values = np.zeros(VALUE_COUNT, dtype=dtype)
    for entry in CLASSES:
        values[entry.code] = getattr(entry, field)
    return values


def validate_tables(classes: tuple[KoppenClass, ...] = CLASSES) -> None:
    """Fail closed if the audit table is not exactly codes 1..30 with valid targets."""
    codes = [entry.code for entry in classes]
    if codes != list(range(1, CLASS_COUNT + 1)):
        raise ValueError(f"Köppen table must cover codes 1..{CLASS_COUNT} in order, got {codes}")
    symbols = {entry.symbol for entry in classes}
    if len(symbols) != len(classes):
        raise ValueError("Köppen table has duplicate class symbols")
    for entry in classes:
        if not 0 <= entry.climate_zone <= HIGHLAND:
            raise ValueError(f"{entry.symbol}: climate zone {entry.climate_zone} out of range")
        if not OCEAN <= entry.biome <= WETLAND:
            raise ValueError(f"{entry.symbol}: biome {entry.biome} out of range")
        if entry.biome == OCEAN:
            raise ValueError(f"{entry.symbol}: land classes must not map to the Ocean biome")
        if not RAINFALL_LIMITS_MM[0] <= entry.annual_rainfall_mm <= RAINFALL_LIMITS_MM[1]:
            raise ValueError(f"{entry.symbol}: rainfall proxy out of storable range")
        # The class total is scaled by rainfall_factor, so check both extremes of
        # that multiplier land inside the plausibility clamp rather than relying on
        # the clamp to silently absorb a bad table row.
        for factor in RAINFALL_FACTOR_LIMITS:
            scaled = entry.annual_rainfall_mm * factor
            if not LAND_RAINFALL_LIMITS_MM[0] <= scaled <= LAND_RAINFALL_LIMITS_MM[1]:
                raise ValueError(
                    f"{entry.symbol}: rainfall {entry.annual_rainfall_mm} scaled by "
                    f"{factor} leaves the plausibility range {LAND_RAINFALL_LIMITS_MM}"
                )
        if not 0.0 <= entry.reference_abs_latitude <= 90.0:
            raise ValueError(f"{entry.symbol}: reference latitude must be an absolute latitude")
        if entry.reference_elevation_m < 0.0:
            raise ValueError(f"{entry.symbol}: reference elevation must not be negative")
        low, high = LAND_TEMPERATURE_LIMITS_C
        if not low <= entry.baseline_temperature_c <= high:
            raise ValueError(f"{entry.symbol}: baseline temperature outside the clamp range")


_LEGEND_LINE = re.compile(
    r"^\s*(?P<code>\d+)\s*:\s+(?P<symbol>\S+)\s+(?P<description>.*?)\s*\[[\d\s]+\]\s*$"
)


def parse_legend(path: Path) -> dict[int, tuple[str, str]]:
    """Parse the upstream ``legend.txt`` into ``{code: (symbol, description)}``.

    Only the numbered class lines are read; the citation prose around them is
    ignored. Fails closed when the file yields no classes, so a silently changed
    layout cannot be mistaken for agreement.
    """
    classes: dict[int, tuple[str, str]] = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        match = _LEGEND_LINE.match(line)
        if not match:
            continue
        code = int(match.group("code"))
        if code in classes:
            raise ValueError(f"{Path(path).name}: legend repeats class code {code}")
        classes[code] = (match.group("symbol"), match.group("description"))
    if not classes:
        raise ValueError(f"{Path(path).name}: no Köppen legend rows recognised")
    return classes


def reconcile_with_legend(
    path: Path, classes: tuple[KoppenClass, ...] = CLASSES
) -> dict[int, tuple[str, str]]:
    """Check the hardcoded table item by item against the cached legend file.

    Compares the code set and, for every code, both the class symbol and the
    description text. Any divergence raises, so an upstream legend revision cannot
    slip past the mapping table unnoticed.
    """
    legend = parse_legend(path)
    table = {entry.code: (entry.symbol, entry.description) for entry in classes}
    problems: list[str] = []
    missing = sorted(set(legend) - set(table))
    if missing:
        problems.append(
            "legend classes absent from the table: "
            + ", ".join(f"{code}={legend[code][0]}" for code in missing)
        )
    extra = sorted(set(table) - set(legend))
    if extra:
        problems.append(
            "table classes absent from the legend: "
            + ", ".join(f"{code}={table[code][0]}" for code in extra)
        )
    for code in sorted(set(legend) & set(table)):
        legend_symbol, legend_description = legend[code]
        table_symbol, table_description = table[code]
        if legend_symbol != table_symbol:
            problems.append(
                f"class {code}: legend symbol {legend_symbol!r} != table {table_symbol!r}"
            )
        if legend_description != table_description:
            problems.append(
                f"class {code} ({table_symbol}): legend description "
                f"{legend_description!r} != table {table_description!r}"
            )
    if problems:
        raise ValueError(
            f"{Path(path).name} disagrees with the Köppen mapping table: "
            + "; ".join(problems)
        )
    return legend


CLIMATE_ZONE_LUT = _table("climate_zone", np.uint8)
BIOME_LUT = _table("biome", np.uint8)
BASELINE_TEMPERATURE_LUT = _table("baseline_temperature_c", np.float64)
REFERENCE_LATITUDE_LUT = _table("reference_abs_latitude", np.float64)
REFERENCE_ELEVATION_LUT = _table("reference_elevation_m", np.float64)
RAINFALL_LUT = _table("annual_rainfall_mm", np.int32)
SYMBOLS: tuple[str, ...] = ("NoData",) + tuple(entry.symbol for entry in CLASSES)


def _band_lookup(bands: tuple[tuple[float, int], ...], abs_latitude: np.ndarray) -> np.ndarray:
    edges = np.array([bound for bound, _ in bands], dtype=np.float64)
    values = np.array([value for _, value in bands], dtype=np.int32)
    index = np.searchsorted(edges, abs_latitude, side="left")
    np.clip(index, 0, values.size - 1, out=index)
    return values[index]


def land_temperature_c(
    codes: np.ndarray, latitude: np.ndarray, elevation_m: np.ndarray
) -> np.ndarray:
    """Derived land temperature proxy.

    ``baseline(class) + latitude term + elevation term``, where both terms are the
    bounded deviation from the class's own reference latitude and elevation.
    """
    baseline = BASELINE_TEMPERATURE_LUT[codes]
    latitude_term = np.clip(
        LATITUDE_GRADIENT_C_PER_DEG * (REFERENCE_LATITUDE_LUT[codes] - np.abs(latitude)),
        -LATITUDE_TERM_LIMIT_C,
        LATITUDE_TERM_LIMIT_C,
    )
    elevation_term = np.clip(
        -ELEVATION_RESIDUAL_C_PER_M * (elevation_m - REFERENCE_ELEVATION_LUT[codes]),
        -ELEVATION_TERM_LIMIT_C,
        ELEVATION_TERM_LIMIT_C,
    )
    return np.clip(baseline + latitude_term + elevation_term, *LAND_TEMPERATURE_LIMITS_C)


def rainfall_factor(
    codes: np.ndarray, latitude: np.ndarray, elevation_m: np.ndarray
) -> np.ndarray:
    """Bounded multiplier applied to a class's reference rainfall total.

    Continuous and piecewise linear in both latitude and elevation, so neighbouring
    cells of one class cannot jump discontinuously.
    """
    latitude_term = np.clip(
        RAINFALL_LATITUDE_FRACTION_PER_DEG
        * (REFERENCE_LATITUDE_LUT[codes] - np.abs(latitude)),
        -RAINFALL_LATITUDE_FRACTION_LIMIT,
        RAINFALL_LATITUDE_FRACTION_LIMIT,
    )
    elevation_term = np.clip(
        RAINFALL_ELEVATION_FRACTION_PER_100M
        * (elevation_m - REFERENCE_ELEVATION_LUT[codes])
        / 100.0,
        -RAINFALL_ELEVATION_FRACTION_LIMIT,
        RAINFALL_ELEVATION_FRACTION_LIMIT,
    )
    return np.clip(1.0 + latitude_term + elevation_term, *RAINFALL_FACTOR_LIMITS)


def land_rainfall_mm(
    codes: np.ndarray, latitude: np.ndarray, elevation_m: np.ndarray
) -> np.ndarray:
    """Derived land rainfall proxy: class total scaled by :func:`rainfall_factor`.

    Like the temperature proxy this is a documented stand-in, not a measurement:
    the locked Köppen product carries no precipitation raster.
    """
    scaled = RAINFALL_LUT[codes] * rainfall_factor(codes, latitude, elevation_m)
    return np.clip(scaled, *LAND_RAINFALL_LIMITS_MM)


def ocean_temperature_c(latitude: np.ndarray) -> np.ndarray:
    """**Synthetic** sea-surface temperature fallback from absolute latitude alone.

    No locked source covers water. See :data:`WATER_CLIMATE_IS_SYNTHETIC`.
    """
    value = OCEAN_SURFACE_TEMPERATURE_C - OCEAN_LATITUDE_GRADIENT_C_PER_DEG * np.abs(latitude)
    return np.clip(value, *OCEAN_TEMPERATURE_LIMITS_C)


def ocean_rainfall_mm(latitude: np.ndarray) -> np.ndarray:
    """**Synthetic** over-water rainfall fallback. See :data:`WATER_CLIMATE_IS_SYNTHETIC`."""
    return _band_lookup(OCEAN_RAINFALL_BANDS, np.abs(latitude))


def ocean_climate_zone(latitude: np.ndarray) -> np.ndarray:
    """**Synthetic** over-water climate zone fallback.

    These are latitude bands invented by this builder, *not* Köppen classes: the
    Köppen product classifies land only and leaves water as NoData. See
    :data:`WATER_CLIMATE_IS_SYNTHETIC`.
    """
    return _band_lookup(OCEAN_CLIMATE_BANDS, np.abs(latitude)).astype(np.uint8)


def latitude_fallback_class(latitude: np.ndarray) -> np.ndarray:
    return _band_lookup(LATITUDE_FALLBACK_CLASSES, np.abs(latitude)).astype(np.uint8)


def apply_elevation_overrides(
    climate: np.ndarray,
    biome: np.ndarray,
    elevation_m: np.ndarray,
    latitude: np.ndarray,
    land: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    """Documented post-table overrides for terrain the Köppen classes cannot express.

    Köppen has no highland class, so elevation supplies one:

    * ``ClimateZone.Highland`` where elevation >= 2500 m and |lat| < 60. This also
      applies to high-altitude inland water, whose climate is highland rather
      than the latitude band used for open ocean; polar latitudes keep their
      Köppen-derived or ocean-band zone.
    * ``BiomeType.Alpine`` on **land** where elevation >= 3000 m, except
      permanent ice (Köppen EF), which stays ``Ice``. Water keeps ``Ocean`` so
      that ``biome == Ocean`` stays equivalent to "not land".
    """
    highland = (elevation_m >= HIGHLAND_ELEVATION_M) & (
        np.abs(latitude) < HIGHLAND_MAX_ABS_LATITUDE
    )
    climate = np.where(highland, np.uint8(HIGHLAND), climate).astype(np.uint8)
    alpine = land & (elevation_m >= ALPINE_ELEVATION_M) & (biome != ICE)
    biome = np.where(alpine, np.uint8(ALPINE), biome).astype(np.uint8)
    return climate, biome


validate_tables()
