#!/usr/bin/env python3
"""Focused tests for the real-source raster build.

Covered: Köppen class mapping, raster semantics, the missing-data interpolation
flag, grid dimensions/traversal, and repeatable output bytes. Tests that need the
upstream cache skip when it is absent; the full-planet build is additionally
gated behind ``WORLDSIM_GEO_SLOW_TESTS=1``.
"""
from __future__ import annotations

import dataclasses
import gzip
import hashlib
import math
import os
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

import numpy as np

import build_geo
import fetch_sources
import geotiff
import grid_build
import koppen
import lockfile
import rasterize
import tiff_tags
from grid_build import TILE_DTYPE, TILE_RECORD_BYTES
from rasterize import HIGH, LOW, MID, Grid

HERE = Path(__file__).resolve().parent
SLOW = os.environ.get("WORLDSIM_GEO_SLOW_TESTS") == "1"


def _cache_paths():
    lock = lockfile.load_lock(HERE / "sources.lock.json")
    cache = fetch_sources.cache_root(HERE)
    try:
        return grid_build.resolve_sources(lock, cache)
    except grid_build.BuildError:
        return None


# ---------------------------------------------------------------- class mapping


class KoppenMappingTests(unittest.TestCase):
    def test_table_covers_every_legend_class_exactly_once(self):
        koppen.validate_tables()
        self.assertEqual(len(koppen.CLASSES), 30)
        self.assertEqual([entry.code for entry in koppen.CLASSES], list(range(1, 31)))
        self.assertEqual(koppen.CLASSES[0].symbol, "Af")
        self.assertEqual(koppen.CLASSES[-1].symbol, "EF")
        self.assertEqual(len(koppen.SYMBOLS), 31)
        self.assertEqual(koppen.SYMBOLS[0], "NoData")

    def test_lookup_tables_have_one_slot_per_raster_value(self):
        for table in (
            koppen.CLIMATE_ZONE_LUT,
            koppen.BIOME_LUT,
            koppen.BASELINE_TEMPERATURE_LUT,
            koppen.REFERENCE_LATITUDE_LUT,
            koppen.REFERENCE_ELEVATION_LUT,
            koppen.RAINFALL_LUT,
        ):
            self.assertEqual(table.shape, (31,))

    def test_representative_classes_map_to_expected_enum_members(self):
        expected = {
            "Af": (koppen.TROPICAL, koppen.TROPICAL_RAINFOREST),
            "Aw": (koppen.TROPICAL, koppen.SAVANNA),
            "BWh": (koppen.ARID, koppen.DESERT),
            "BSk": (koppen.ARID, koppen.GRASSLAND),
            "Cfb": (koppen.TEMPERATE, koppen.TEMPERATE_FOREST),
            "Cfa": (koppen.SUBTROPICAL, koppen.TEMPERATE_FOREST),
            "Dfc": (koppen.SUBPOLAR, koppen.BOREAL_FOREST),
            "ET": (koppen.POLAR, koppen.TUNDRA),
            "EF": (koppen.POLAR, koppen.ICE),
        }
        by_symbol = {entry.symbol: entry for entry in koppen.CLASSES}
        for symbol, (zone, biome) in expected.items():
            entry = by_symbol[symbol]
            self.assertEqual(entry.climate_zone, zone, symbol)
            self.assertEqual(entry.biome, biome, symbol)
            self.assertEqual(int(koppen.CLIMATE_ZONE_LUT[entry.code]), zone, symbol)
            self.assertEqual(int(koppen.BIOME_LUT[entry.code]), biome, symbol)

    def test_every_class_maps_into_the_runtime_enum_ranges(self):
        self.assertTrue((koppen.CLIMATE_ZONE_LUT[1:] <= koppen.HIGHLAND).all())
        self.assertTrue((koppen.BIOME_LUT[1:] <= koppen.WETLAND).all())
        self.assertTrue((koppen.BIOME_LUT[1:] != koppen.OCEAN).all())

    def test_validate_tables_rejects_a_land_class_mapped_to_ocean(self):
        broken = list(koppen.CLASSES)
        broken[0] = dataclasses.replace(broken[0], biome=koppen.OCEAN)
        with self.assertRaises(ValueError):
            koppen.validate_tables(tuple(broken))

    def test_validate_tables_rejects_a_missing_class(self):
        with self.assertRaises(ValueError):
            koppen.validate_tables(koppen.CLASSES[:-1])

    def test_temperature_proxy_responds_to_latitude_and_elevation(self):
        code = np.array([26, 26], dtype=np.uint8)  # Dfb
        lat = np.array([40.0, 60.0])
        elevation = np.array([250.0, 250.0])
        warm, cold = koppen.land_temperature_c(code, lat, elevation)
        self.assertGreater(warm, cold)
        low, high = koppen.land_temperature_c(
            code, np.array([50.0, 50.0]), np.array([250.0, 2250.0])
        )
        self.assertGreater(low, high)
        entry = koppen.CLASSES[25]
        self.assertAlmostEqual(
            float(
                koppen.land_temperature_c(
                    np.array([26], dtype=np.uint8),
                    np.array([entry.reference_abs_latitude]),
                    np.array([entry.reference_elevation_m]),
                )[0]
            ),
            entry.baseline_temperature_c,
        )

    def test_rainfall_proxy_responds_to_latitude_and_elevation(self):
        code = np.array([14, 14], dtype=np.uint8)  # Cfa
        # Equatorward of the reference latitude -> wetter; poleward -> drier.
        lat = np.array([20.0, 40.0])
        elevation = np.array([200.0, 200.0])
        wet, dry = koppen.land_rainfall_mm(code, lat, elevation)
        self.assertGreater(wet, dry)
        # Above the reference elevation -> orographic enhancement -> wetter.
        low, high = koppen.land_rainfall_mm(
            code, np.array([32.0, 32.0]), np.array([200.0, 2200.0])
        )
        self.assertGreater(high, low)

    def test_temperature_and_rainfall_stay_inside_the_storable_range(self):
        codes = np.arange(1, 31, dtype=np.uint8)
        for latitude in (-90.0, -45.0, 0.0, 45.0, 90.0):
            for elevation in (-500.0, 0.0, 3000.0, 8848.0):
                values = koppen.land_temperature_c(
                    codes,
                    np.full(codes.shape, latitude),
                    np.full(codes.shape, elevation),
                )
                self.assertTrue((values >= koppen.LAND_TEMPERATURE_LIMITS_C[0]).all())
                self.assertTrue((values <= koppen.LAND_TEMPERATURE_LIMITS_C[1]).all())
                scaled = np.trunc(values * 10.0)
                self.assertTrue((scaled >= np.iinfo(np.int16).min).all())
                self.assertTrue((scaled <= np.iinfo(np.int16).max).all())
        rain = koppen.land_rainfall_mm(
            codes,
            np.full(codes.shape, 45.0),
            np.full(codes.shape, 500.0),
        )
        self.assertTrue((rain >= 0).all())
        self.assertTrue((rain <= np.iinfo(np.uint16).max).all())

    def test_ocean_proxies_follow_documented_latitude_bands(self):
        lat = np.array([0.0, 30.0, 50.0, 60.0, 80.0])
        zones = koppen.ocean_climate_zone(lat)
        self.assertEqual(
            list(zones),
            [koppen.TROPICAL, koppen.SUBTROPICAL, koppen.TEMPERATE, koppen.SUBPOLAR, koppen.POLAR],
        )
        temps = koppen.ocean_temperature_c(lat)
        self.assertTrue(np.all(np.diff(temps) < 0))
        self.assertGreaterEqual(temps.min(), koppen.OCEAN_TEMPERATURE_LIMITS_C[0])
        self.assertEqual(list(koppen.ocean_rainfall_mm(np.array([5.0, 85.0]))), [2000, 200])

    def test_latitude_fallback_class_is_monotonic_and_valid(self):
        lat = np.array([0.0, 20.0, 30.0, 45.0, 55.0, 65.0, 85.0])
        codes = koppen.latitude_fallback_class(lat)
        self.assertTrue((codes >= 1).all())
        self.assertTrue((codes <= 30).all())
        self.assertEqual(int(codes[0]), 1)
        self.assertEqual(int(codes[-1]), 30)

    def test_parse_legend_reads_numbered_class_lines(self):
        import tempfile
        legend = (
            "Legend linking numeric values to classes.\n"
            "    1:  Af   Tropical, rainforest                  [0 0 255]\n"
            "    2:  Am   Tropical, monsoon                     [0 120 255]\n"
            "prose line without a class\n"
        )
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "legend.txt"
            path.write_text(legend, encoding="utf-8")
            parsed = koppen.parse_legend(path)
        self.assertEqual(parsed[1], ("Af", "Tropical, rainforest"))
        self.assertEqual(parsed[2], ("Am", "Tropical, monsoon"))
        self.assertNotIn(0, parsed)

    def test_parse_legend_fails_closed_when_no_rows_are_recognised(self):
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "legend.txt"
            path.write_text("only prose, no numbered classes\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                koppen.parse_legend(path)

    def test_reconcile_with_legend_accepts_a_matching_legend(self):
        import tempfile
        rows = [
            f"    {entry.code}: {entry.symbol}   {entry.description}   [0 0 0]"
            for entry in koppen.CLASSES
        ]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "legend.txt"
            path.write_text("\n".join(rows) + "\n", encoding="utf-8")
            legend = koppen.reconcile_with_legend(path)
        self.assertEqual(len(legend), 30)
        self.assertEqual(legend[1][0], "Af")
        self.assertEqual(legend[30][0], "EF")

    def test_reconcile_with_legend_rejects_a_symbol_mismatch(self):
        import tempfile
        rows = [
            f"    {entry.code}: {entry.symbol}X   {entry.description}   [0 0 0]"
            for entry in koppen.CLASSES
        ]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "legend.txt"
            path.write_text("\n".join(rows) + "\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                koppen.reconcile_with_legend(path)

    def test_elevation_overrides_are_scoped_as_documented(self):
        climate = np.array([[koppen.TROPICAL, koppen.POLAR, koppen.TEMPERATE]], dtype=np.uint8)
        biome = np.array(
            [[koppen.TROPICAL_RAINFOREST, koppen.ICE, koppen.OCEAN]], dtype=np.uint8
        )
        elevation = np.array([[3200.0, 3200.0, 3200.0]])
        latitude = np.array([[10.0, -75.0, 20.0]])
        land = np.array([[True, True, False]])
        out_climate, out_biome = koppen.apply_elevation_overrides(
            climate, biome, elevation, latitude, land
        )
        # land below 60 deg becomes Highland/Alpine
        self.assertEqual(int(out_climate[0, 0]), koppen.HIGHLAND)
        self.assertEqual(int(out_biome[0, 0]), koppen.ALPINE)
        # polar latitudes keep their zone, and permanent ice keeps Ice
        self.assertEqual(int(out_climate[0, 1]), koppen.POLAR)
        self.assertEqual(int(out_biome[0, 1]), koppen.ICE)
        # high inland water becomes Highland but stays the Ocean biome
        self.assertEqual(int(out_climate[0, 2]), koppen.HIGHLAND)
        self.assertEqual(int(out_biome[0, 2]), koppen.OCEAN)


# ------------------------------------------------------------- grid definitions


class GridTests(unittest.TestCase):
    def test_output_grids_are_exactly_the_locked_dimensions(self):
        self.assertEqual((LOW.width, LOW.height, LOW.lod_value), (180, 90, 2))
        self.assertEqual((MID.width, MID.height, MID.lod_value), (360, 180, 1))
        self.assertEqual((HIGH.width, HIGH.height, HIGH.lod_value), (720, 360, 0))
        self.assertEqual([grid.step for grid in rasterize.GRIDS], [2.0, 1.0, 0.5])
        self.assertEqual([grid.cells for grid in rasterize.GRIDS], [16200, 64800, 259200])

    def test_cell_centres_are_equirectangular(self):
        for grid in rasterize.GRIDS:
            lons, lats = grid.lons(), grid.lats()
            self.assertEqual(lons.size, grid.width)
            self.assertEqual(lats.size, grid.height)
            self.assertAlmostEqual(lons[0], -180.0 + grid.step / 2)
            self.assertAlmostEqual(lons[-1], 180.0 - grid.step / 2)
            self.assertAlmostEqual(lats[0], 90.0 - grid.step / 2)
            self.assertAlmostEqual(lats[-1], -90.0 + grid.step / 2)
            self.assertTrue(np.all(np.diff(lons) > 0))
            self.assertTrue(np.all(np.diff(lats) < 0))

    def test_non_two_to_one_grid_is_rejected(self):
        with self.assertRaises(ValueError):
            Grid("Bad", 0, 100, 100)

    def test_traversal_is_y_major_then_x_minor(self):
        grid = Grid("T", 0, 8, 4)
        values = np.arange(grid.cells).reshape(grid.height, grid.width)
        flat = values.ravel()
        for y in range(grid.height):
            for x in range(grid.width):
                self.assertEqual(flat[y * grid.width + x], values[y, x])


# ----------------------------------------------------------- raster semantics


def _naive_polygon_mask(parts, grid):
    """Reference even-odd implementation with no row bucketing."""
    mask = np.zeros((grid.height, grid.width), dtype=bool)
    lons, lats = grid.lons(), grid.lats()
    for y, lat in enumerate(lats):
        crossings = []
        for part in parts:
            ring = np.asarray(part, dtype=np.float64)
            if ring[0, 0] != ring[-1, 0] or ring[0, 1] != ring[-1, 1]:
                ring = np.vstack([ring, ring[:1]])
            for i in range(ring.shape[0] - 1):
                ax, ay = ring[i]
                bx, by = ring[i + 1]
                if (ay > lat) != (by > lat):
                    crossings.append(ax + (lat - ay) * (bx - ax) / (by - ay))
        crossings.sort()
        for x, lon in enumerate(lons):
            count = sum(1 for value in crossings if value < lon)
            mask[y, x] = count % 2 == 1
    return mask


def _rect(lon0, lat0, lon1, lat1):
    return np.array(
        [[lon0, lat0], [lon1, lat0], [lon1, lat1], [lon0, lat1], [lon0, lat0]],
        dtype=np.float64,
    )


class RasterSemanticsTests(unittest.TestCase):
    def test_polygon_fill_uses_cell_centres(self):
        grid = LOW
        mask = rasterize.polygon_mask([_rect(0.0, 0.0, 10.0, 10.0)], grid)
        # Cell centres inside 0..10 degrees are at 1,3,5,7,9 -> 5 columns and 5 rows.
        self.assertEqual(int(mask.sum()), 25)
        lons, lats = grid.lons(), grid.lats()
        inside = np.argwhere(mask)
        for y, x in inside:
            self.assertTrue(0.0 < lons[x] < 10.0)
            self.assertTrue(0.0 < lats[y] < 10.0)

    def test_polygon_fill_matches_the_naive_reference(self):
        grid = Grid("T", 0, 72, 36)
        rng = np.random.default_rng(20260813)
        angles = np.sort(rng.uniform(0.0, 2 * np.pi, 24))
        radii = rng.uniform(10.0, 60.0, 24)
        blob = np.column_stack([radii * np.cos(angles), 0.5 * radii * np.sin(angles)])
        parts = [blob, _rect(-150.0, -70.0, -100.0, -40.0), _rect(100.0, 20.0, 170.0, 60.0)]
        np.testing.assert_array_equal(
            rasterize.polygon_mask(parts, grid), _naive_polygon_mask(parts, grid)
        )

    def test_polygon_hole_is_cut_by_even_odd_parity(self):
        grid = MID
        outer = _rect(0.0, 0.0, 20.0, 20.0)
        inner = _rect(5.0, 5.0, 15.0, 15.0)
        filled = rasterize.polygon_mask([outer], grid)
        holed = rasterize.polygon_mask([outer, inner], grid)
        self.assertEqual(int(filled.sum()), 400)
        self.assertEqual(int(holed.sum()), 400 - 100)
        self.assertTrue((holed <= filled).all())

    def test_polyline_marks_every_cell_it_crosses(self):
        grid = LOW
        # Latitude 1.0 is the centre of row 44; the segment spans -5..5 degrees of
        # longitude, crossing the meridians at -4, -2, 0, 2 and 4 degrees.
        line = np.array([[-5.0, 1.0], [5.0, 1.0]], dtype=np.float64)
        mask = rasterize.polyline_mask([line], grid)
        self.assertEqual(sorted({int(y) for y, _ in np.argwhere(mask)}), [44])
        self.assertEqual(
            sorted({int(x) for _, x in np.argwhere(mask)}), [87, 88, 89, 90, 91, 92]
        )

    def test_polyline_on_a_grid_line_picks_one_side_deterministically(self):
        grid = LOW
        line = np.array([[-5.0, 0.0], [5.0, 0.0]], dtype=np.float64)
        mask = rasterize.polyline_mask([line], grid)
        rows = sorted({int(y) for y, _ in np.argwhere(mask)})
        # A segment exactly on a latitude grid line lands in the cell below it.
        self.assertEqual(rows, [45])
        np.testing.assert_array_equal(mask, rasterize.polyline_mask([line[::-1]], grid))

    def test_polyline_diagonal_is_connected_without_gaps(self):
        grid = MID
        line = np.array([[0.0, 0.0], [10.0, 10.0]], dtype=np.float64)
        mask = rasterize.polyline_mask([line], grid)
        cells = np.argwhere(mask)
        self.assertGreaterEqual(len(cells), 20)
        # Every visited row between the endpoints must be present (no skipped rows).
        rows = {int(y) for y, _ in cells}
        self.assertEqual(rows, set(range(min(rows), max(rows) + 1)))

    def test_polyline_wraps_at_the_antimeridian(self):
        grid = LOW
        mask = rasterize.polyline_mask([np.array([[179.9, 0.0], [180.0, 0.0]])], grid)
        self.assertTrue(mask[:, 0].any() or mask[:, grid.width - 1].any())

    def test_orthogonal_neighbours_wrap_longitude_but_not_latitude(self):
        mask = np.zeros((4, 6), dtype=bool)
        mask[0, 0] = True
        result = rasterize.orthogonal_neighbor_any(mask)
        self.assertTrue(result[0, 1])
        self.assertTrue(result[0, 5])  # wrapped west neighbour
        self.assertTrue(result[1, 0])
        self.assertFalse(result[3, 0])  # no wrap over the pole
        self.assertFalse(result[0, 0])

    def test_majority_downsample_breaks_ties_to_the_lowest_class(self):
        block = np.array([[7, 7], [3, 3]], dtype=np.uint8)
        self.assertEqual(int(rasterize.majority_downsample(block, 2, 30)[0, 0]), 3)
        clear = np.array([[7, 7], [7, 3]], dtype=np.uint8)
        self.assertEqual(int(rasterize.majority_downsample(clear, 2, 30)[0, 0]), 7)

    def test_majority_downsample_ignores_nodata_and_keeps_empty_blocks_nodata(self):
        mixed = np.array([[0, 0], [0, 9]], dtype=np.uint8)
        self.assertEqual(int(rasterize.majority_downsample(mixed, 2, 30)[0, 0]), 9)
        empty = np.zeros((2, 2), dtype=np.uint8)
        self.assertEqual(int(rasterize.majority_downsample(empty, 2, 30)[0, 0]), 0)

    def test_majority_downsample_rejects_non_divisible_shapes(self):
        with self.assertRaises(ValueError):
            rasterize.majority_downsample(np.zeros((3, 3), dtype=np.uint8), 2, 30)

    def test_lake_cells_become_water_and_coast_needs_land(self):
        grid = MID
        parts = grid_build.VectorParts(
            land=[_rect(0.0, 0.0, 20.0, 20.0)],
            lakes=[_rect(8.0, 8.0, 12.0, 12.0)],
            coastline=[],
            rivers=[np.array([[2.0, 2.0], [18.0, 18.0]])],
            river_scalerank=[0],
        )
        masks = grid_build.vector_masks(parts, grid)
        self.assertFalse(masks.land[np.argmax(grid.lats() < 10.0), np.argmax(grid.lons() > 10.0)])
        self.assertTrue(masks.water[np.argmax(grid.lats() < 10.0), np.argmax(grid.lons() > 10.0)])
        self.assertTrue((masks.coast <= masks.land).all())
        self.assertTrue((masks.river <= masks.land).all())
        self.assertTrue((~masks.land <= masks.water).all())
        # the lake perimeter makes the surrounding land coastal
        self.assertTrue(masks.coast.any())


# ------------------------------------------------------------ elevation / slope


class ElevationTests(unittest.TestCase):
    def test_coarse_grids_reduce_the_high_grid_sums_exactly(self):
        rng = np.random.default_rng(7)
        values = rng.integers(-500, 4000, size=(HIGH.height, HIGH.width)).astype(np.float64)
        accumulator = grid_build.ElevationAccumulator(
            sums=values.copy(), counts=np.ones_like(values, dtype=np.int64)
        )
        high, _ = grid_build.elevation_for_grid(accumulator, HIGH)
        np.testing.assert_array_equal(high, values)
        low, missing = grid_build.elevation_for_grid(accumulator, LOW)
        self.assertEqual(low.shape, (LOW.height, LOW.width))
        self.assertFalse(missing.any())
        expected = grid_build.round_half_away_from_zero(
            values.reshape(LOW.height, 4, LOW.width, 4).sum(axis=(1, 3)) / 16.0
        )
        np.testing.assert_array_equal(low, expected)

    def test_round_half_away_from_zero_is_symmetric(self):
        values = np.array([-2.5, -1.5, -0.5, 0.5, 1.5, 2.5])
        np.testing.assert_array_equal(
            grid_build.round_half_away_from_zero(values),
            np.array([-3.0, -2.0, -1.0, 1.0, 2.0, 3.0]),
        )

    def test_slope_is_bounded_and_uses_ground_distance(self):
        grid = MID
        elevation = np.zeros((grid.height, grid.width))
        elevation[90, 100] = 8000.0
        slope = grid_build.slope_degrees(elevation, grid)
        self.assertLessEqual(slope.max(), grid_build.SLOPE_LIMIT_DEGREES)
        self.assertGreaterEqual(slope.min(), 0.0)
        self.assertGreater(slope[90, 101], 0.0)
        flat = grid_build.slope_degrees(np.full((grid.height, grid.width), 500.0), grid)
        self.assertEqual(float(flat.max()), 0.0)
        # A fixed height step yields a larger gradient nearer the poles, where the
        # zonal cell spacing in metres is smaller, until the cos floor clamps it.
        ramp = np.zeros((grid.height, grid.width))
        ramp[:, 10] = 100.0
        gradient = grid_build.slope_degrees(ramp, grid)
        self.assertGreater(gradient[5, 11], gradient[90, 11])

    def test_slope_is_the_arctangent_of_rise_over_run(self):
        grid = MID
        row = 90
        # The steepest neighbour of an isolated peak is zonal, so the run is the
        # east-west cell spacing at this row's own latitude.
        run_m = (
            grid.step
            * grid_build.METRES_PER_DEGREE
            * math.cos(math.radians(grid.lats()[row]))
        )
        elevation = np.zeros((grid.height, grid.width))
        elevation[row, 100] = run_m  # rise == run, i.e. 45 degrees
        slope = grid_build.slope_degrees(elevation, grid)
        # 45 degrees exceeds the storable byte range, so it clamps to the limit.
        self.assertEqual(float(slope[row, 100]), grid_build.SLOPE_LIMIT_DEGREES)
        elevation[row, 100] = run_m * math.tan(math.radians(10.0))
        slope = grid_build.slope_degrees(elevation, grid)
        self.assertAlmostEqual(float(slope[row, 100]), 10.0, places=9)

    def test_slope_stays_bounded_at_the_poles(self):
        grid = LOW
        rng = np.random.default_rng(11)
        elevation = rng.integers(-9000, 8000, size=(grid.height, grid.width)).astype(np.float64)
        slope = grid_build.slope_degrees(elevation, grid)
        self.assertTrue(np.isfinite(slope).all())
        self.assertLessEqual(slope.max(), grid_build.SLOPE_LIMIT_DEGREES)
        # An angle can never exceed 90 degrees regardless of the elevation step.
        self.assertLess(slope.max(), 90.0)


# --------------------------------------------------------- missing-data policy


class MissingDataTests(unittest.TestCase):
    def test_nearest_fill_uses_the_closest_valid_cell(self):
        values = np.zeros((5, 5), dtype=np.uint8)
        values[2, 0] = 9
        values[0, 4] = 3
        valid = values > 0
        target = np.ones_like(valid)
        filled, changed = grid_build.nearest_fill(values, valid, target & ~valid, 8)
        self.assertTrue((filled > 0).all())
        self.assertEqual(int(filled[2, 1]), 9)
        self.assertEqual(int(filled[1, 4]), 3)
        self.assertFalse(changed[2, 0])
        self.assertTrue(changed[2, 1])

    def test_nearest_fill_is_bounded_by_max_rounds(self):
        values = np.zeros((1, 12), dtype=np.uint8)
        values[0, 0] = 5
        valid = values > 0
        target = np.ones_like(valid)
        filled, changed = grid_build.nearest_fill(values, valid, target & ~valid, 2)
        self.assertEqual(int(filled[0, 1]), 5)
        self.assertEqual(int(filled[0, 2]), 5)
        # Longitude wraps, so two rings reach x=1,2 from the left and x=11,10 from the right.
        self.assertEqual(int(filled[0, 5]), 0)
        self.assertFalse(changed[0, 5])

    def test_nearest_fill_leaves_input_untouched(self):
        values = np.array([[0, 4]], dtype=np.uint8)
        original = values.copy()
        grid_build.nearest_fill(values, values > 0, np.array([[True, False]]), 4)
        np.testing.assert_array_equal(values, original)

    def _synthetic_inputs(self, class_code: int | None):
        parts = grid_build.VectorParts(
            land=[_rect(0.0, 0.0, 30.0, 30.0)], lakes=[], coastline=[], rivers=[],
            river_scalerank=[],
        )
        accumulator = grid_build.ElevationAccumulator(
            sums=np.full((HIGH.height, HIGH.width), 300.0),
            counts=np.ones((HIGH.height, HIGH.width), dtype=np.int64),
        )
        codes = np.zeros((HIGH.height, HIGH.width), dtype=np.uint8)
        if class_code is not None:
            codes[HIGH.height // 2 - 1, HIGH.width // 2 + 1] = class_code
        return parts, accumulator, codes

    def test_land_without_a_source_class_is_flagged_interpolated(self):
        parts, accumulator, codes = self._synthetic_inputs(class_code=15)  # Cfb
        result = grid_build.build_grid(LOW, parts, accumulator, codes)
        flags = result.records["flags"].reshape(LOW.height, LOW.width)
        biome = result.records["biome"].reshape(LOW.height, LOW.width)
        land = (flags & grid_build.FLAG_LAND) != 0
        interpolated = (flags & grid_build.FLAG_INTERPOLATED) != 0
        self.assertTrue(land.any())
        # Exactly one Koppen pixel carries a class, so every land cell but that one
        # is interpolated. Water cells are also interpolated because their climate
        # is a synthetic latitude band (Köppen leaves water as NoData), so the flag
        # now spans the whole grid minus the one seeded cell.
        self.assertEqual(int(interpolated[land].sum()), int(land.sum()) - 1)
        self.assertTrue(interpolated[~land].all())
        # The nearest-neighbour fill is bounded, so the rest falls back to latitude.
        stats = result.stats
        self.assertGreater(stats["classFilledByNeighbour"], 0)
        self.assertGreater(stats["classFilledByLatitude"], 0)
        self.assertEqual(
            stats["classFilledByNeighbour"] + stats["classFilledByLatitude"],
            int(interpolated[land].sum()),
        )
        # The one cell carrying the seeded Cfb pixel keeps real data.
        seed_y, seed_x = 44, 90
        self.assertTrue(land[seed_y, seed_x])
        self.assertFalse(interpolated[seed_y, seed_x])
        self.assertEqual(int(biome[seed_y, seed_x]), koppen.TEMPERATE_FOREST)
        # Land within the two-ring radius inherits that class through the fill.
        neighbours = [(0, 1), (0, 2), (-1, 0), (-1, 1), (-1, 2), (-2, 0), (-2, 1), (-2, 2)]
        self.assertEqual(stats["classFilledByNeighbour"], len(neighbours))
        for dy, dx in neighbours:
            self.assertEqual(
                int(biome[seed_y + dy, seed_x + dx]), koppen.TEMPERATE_FOREST, (dy, dx)
            )
            self.assertTrue(interpolated[seed_y + dy, seed_x + dx], (dy, dx))
        # One cell past the radius the fill stops and latitude takes over (|lat| < 10 -> Af).
        self.assertEqual(int(biome[seed_y, seed_x + 3]), koppen.TROPICAL_RAINFOREST)
        # Land far away follows the documented latitude fallback: |lat| 10..23.5 -> Aw.
        far_y = int((90.0 - 15.0) / LOW.step)
        far_x = int((25.0 + 180.0) / LOW.step)
        self.assertTrue(land[far_y, far_x])
        self.assertTrue(interpolated[far_y, far_x])
        self.assertEqual(int(biome[far_y, far_x]), koppen.SAVANNA)
        self.assertTrue((biome[land] != koppen.OCEAN).all())

    def test_land_beyond_the_neighbour_radius_uses_the_latitude_fallback(self):
        parts, accumulator, codes = self._synthetic_inputs(class_code=None)
        result = grid_build.build_grid(LOW, parts, accumulator, codes)
        flags = result.records["flags"].reshape(LOW.height, LOW.width)
        land = (flags & grid_build.FLAG_LAND) != 0
        interpolated = (flags & grid_build.FLAG_INTERPOLATED) != 0
        self.assertEqual(int(interpolated[land].sum()), int(land.sum()))
        self.assertTrue(interpolated[~land].all())
        self.assertEqual(result.stats["classFilledByLatitude"], int(land.sum()))
        biome = result.records["biome"].reshape(LOW.height, LOW.width)
        self.assertTrue((biome[land] != koppen.OCEAN).all())

    def test_missing_elevation_is_filled_and_flagged(self):
        parts, accumulator, codes = self._synthetic_inputs(class_code=15)
        accumulator.counts[:, :] = 1
        accumulator.counts[HIGH.height // 2 : HIGH.height // 2 + 4, 0:4] = 0
        accumulator.sums[HIGH.height // 2 : HIGH.height // 2 + 4, 0:4] = 0.0
        result = grid_build.build_grid(HIGH, parts, accumulator, codes)
        flags = result.records["flags"].reshape(HIGH.height, HIGH.width)
        elevation = result.records["elevation"].reshape(HIGH.height, HIGH.width)
        hole = (slice(HIGH.height // 2, HIGH.height // 2 + 4), slice(0, 4))
        self.assertTrue(((flags[hole] & grid_build.FLAG_INTERPOLATED) != 0).all())
        self.assertEqual(result.stats["elevationMissing"], 16)
        self.assertEqual(result.stats["elevationUnresolved"], 0)
        np.testing.assert_array_equal(elevation[hole], np.full((4, 4), 300))

    def test_unfilled_elevation_falls_back_to_the_latitude_band(self):
        # ELEVATION_LATITUDE_FALLBACK_M is deliberately non-zero in every band
        # (0 m is a real elevation, so guessing it would let an unresolved cell
        # masquerade as measured sea level): |lat|<60 -> 200, 60-70 -> 300,
        # 70-80 -> 1000, 80-90 -> 2000.
        latitudes = np.array([[0.0, 65.0, 75.0, 85.0]])
        np.testing.assert_array_equal(
            grid_build._band_metres(latitudes), np.array([[200.0, 300.0, 1000.0, 2000.0]])
        )


# ------------------------------------------------------------- GeoTIFF adapters


def _build_tiff(
    data: np.ndarray,
    *,
    compression: int,
    predictor: int,
    tile: tuple[int, int] | None,
    pixel_scale: float,
    nodata: str | None = None,
    geo_keys: list[int] | None = None,
) -> bytes:
    """Write a minimal single-IFD little-endian GeoTIFF for reader tests."""
    height, width = data.shape
    bps = data.dtype.itemsize

    def encode(block: np.ndarray) -> bytes:
        if predictor == geotiff.PREDICTOR_FLOATING_POINT:
            raw = block.tobytes()
            rows = block.shape[0]
            flat = np.frombuffer(raw, dtype=np.uint8).reshape(rows, block.shape[1], bps)
            planes = flat[:, :, ::-1].transpose(0, 2, 1).reshape(rows, -1)
            deltas = np.zeros_like(planes)
            deltas[:, 0] = planes[:, 0]
            deltas[:, 1:] = (planes[:, 1:].astype(np.int16) - planes[:, :-1]) & 0xFF
            payload = deltas.astype(np.uint8).tobytes()
        elif predictor == geotiff.PREDICTOR_HORIZONTAL:
            diffed = block.copy()
            diffed[:, 1:] = block[:, 1:] - block[:, :-1]
            payload = diffed.tobytes()
        else:
            payload = block.tobytes()
        if compression == geotiff.COMPRESSION_DEFLATE:
            return zlib.compress(payload)
        return payload

    blocks: list[bytes] = []
    if tile is None:
        blocks.append(encode(data))
        layout = {
            tiff_tags.ROWS_PER_STRIP: (3, [height]),
            tiff_tags.STRIP_OFFSETS: (4, None),
            tiff_tags.STRIP_BYTE_COUNTS: (4, None),
        }
    else:
        tile_w, tile_h = tile
        for y0 in range(0, height, tile_h):
            for x0 in range(0, width, tile_w):
                padded = np.zeros((tile_h, tile_w), dtype=data.dtype)
                chunk = data[y0 : y0 + tile_h, x0 : x0 + tile_w]
                padded[: chunk.shape[0], : chunk.shape[1]] = chunk
                blocks.append(encode(padded))
        layout = {
            tiff_tags.TILE_WIDTH: (3, [tile_w]),
            tiff_tags.TILE_LENGTH: (3, [tile_h]),
            tiff_tags.TILE_OFFSETS: (4, None),
            tiff_tags.TILE_BYTE_COUNTS: (4, None),
        }

    sample_format = {"u": 1, "i": 2, "f": 3}[data.dtype.kind]
    entries: list[tuple[int, int, list | str]] = [
        (tiff_tags.IMAGE_WIDTH, 3, [width]),
        (tiff_tags.IMAGE_LENGTH, 3, [height]),
        (tiff_tags.BITS_PER_SAMPLE, 3, [bps * 8]),
        (tiff_tags.COMPRESSION, 3, [compression]),
        (tiff_tags.PHOTOMETRIC, 3, [1]),
        (tiff_tags.SAMPLES_PER_PIXEL, 3, [1]),
        (tiff_tags.PLANAR_CONFIG, 3, [1]),
        (tiff_tags.PREDICTOR, 3, [predictor]),
        (tiff_tags.SAMPLE_FORMAT, 3, [sample_format]),
        (tiff_tags.MODEL_PIXEL_SCALE, 12, [pixel_scale, pixel_scale, 0.0]),
        (tiff_tags.MODEL_TIEPOINT, 12, [0.0, 0.0, 0.0, -180.0, 90.0, 0.0]),
        # GeoKeyDirectory: version=1, revision=1, minor=0, keyCount=2,
        # then (keyId, tagLocation, count, value) tuples: 1024=2 (geographic),
        # 1025=1 (RasterPixelIsArea). The reader fails closed without 1025.
        (tiff_tags.GEO_KEY_DIRECTORY, 3, geo_keys if geo_keys is not None else [1, 1, 0, 2, 1024, 0, 1, 2, 1025, 0, 1, 1]),
    ]
    for tag, (field_type, value) in layout.items():
        entries.append((tag, field_type, value if value is not None else []))
    if nodata is not None:
        entries.append((tiff_tags.GDAL_NODATA, 2, nodata + "\x00"))
    entries.sort(key=lambda item: item[0])

    header = struct.pack("<2sHI", b"II", 42, 8)
    count = len(entries)
    ifd_size = 2 + count * 12 + 4
    heap_start = len(header) + ifd_size

    # Oversized values go on a heap after the IFD; pixel blocks follow the heap.
    def value_bytes(field_type: int, value) -> bytes:
        if field_type == 2:
            return value.encode("ascii")
        if field_type == 3:
            return struct.pack("<" + "H" * len(value), *value)
        if field_type == 4:
            return struct.pack("<" + "I" * len(value), *value)
        return struct.pack("<" + "d" * len(value), *value)

    offsets_tag = tiff_tags.TILE_OFFSETS if tile is not None else tiff_tags.STRIP_OFFSETS
    counts_tag = tiff_tags.TILE_BYTE_COUNTS if tile is not None else tiff_tags.STRIP_BYTE_COUNTS
    resolved: list[tuple[int, int, int, bytes]] = []
    for tag, field_type, value in entries:
        if tag == offsets_tag:
            payload = b"\x00" * (4 * len(blocks))
            resolved.append((tag, field_type, len(blocks), payload))
        elif tag == counts_tag:
            resolved.append(
                (tag, field_type, len(blocks), struct.pack("<" + "I" * len(blocks), *[len(b) for b in blocks]))
            )
        else:
            payload = value_bytes(field_type, value)
            length = len(value)
            resolved.append((tag, field_type, length, payload))

    heap = bytearray()
    positions: dict[int, int] = {}
    for tag, _field_type, _count, payload in resolved:
        if len(payload) > 4:
            positions[tag] = heap_start + len(heap)
            heap += payload
    data_start = heap_start + len(heap)
    block_offsets = []
    cursor = data_start
    for block in blocks:
        block_offsets.append(cursor)
        cursor += len(block)
    offset_payload = struct.pack("<" + "I" * len(blocks), *block_offsets)
    if len(offset_payload) > 4:
        heap[positions[offsets_tag] - heap_start : positions[offsets_tag] - heap_start + len(offset_payload)] = offset_payload

    ifd = bytearray(struct.pack("<H", count))
    for tag, field_type, length, payload in resolved:
        if tag == offsets_tag and len(offset_payload) <= 4:
            payload = offset_payload
        if len(payload) <= 4:
            ifd += struct.pack("<HHI", tag, field_type, length) + payload.ljust(4, b"\x00")
        else:
            ifd += struct.pack("<HHII", tag, field_type, length, positions[tag])
    ifd += struct.pack("<I", 0)
    assert len(ifd) == ifd_size, (len(ifd), ifd_size)
    return bytes(header) + bytes(ifd) + bytes(heap) + b"".join(blocks)


class GeoTiffReaderTests(unittest.TestCase):
    def _roundtrip(self, data, **kwargs):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "probe.tif"
            path.write_bytes(_build_tiff(data, **kwargs))
            raster = geotiff.GeoTiff(path)
            out = np.zeros(data.shape, dtype=data.dtype)
            for x0, y0, block in raster.iter_blocks():
                out[y0 : y0 + block.shape[0], x0 : x0 + block.shape[1]] = block
            return raster, out

    def test_uncompressed_strip_roundtrip(self):
        data = np.arange(24, dtype="<i2").reshape(4, 6) - 10
        raster, out = self._roundtrip(
            data,
            compression=geotiff.COMPRESSION_NONE,
            predictor=geotiff.PREDICTOR_NONE,
            tile=None,
            pixel_scale=60.0,
        )
        np.testing.assert_array_equal(out, data)
        self.assertFalse(raster.tiled)
        self.assertEqual((raster.width, raster.height), (6, 4))

    def test_deflate_floating_point_predictor_tiled_roundtrip(self):
        rng = np.random.default_rng(3)
        data = (rng.normal(0, 2000, size=(20, 34))).astype("<f4")
        raster, out = self._roundtrip(
            data,
            compression=geotiff.COMPRESSION_DEFLATE,
            predictor=geotiff.PREDICTOR_FLOATING_POINT,
            tile=(16, 16),
            pixel_scale=1.0,
            nodata="-99999",
        )
        np.testing.assert_array_equal(out, data)
        self.assertTrue(raster.tiled)
        self.assertEqual(raster.nodata_value(), -99999.0)

    def test_horizontal_predictor_roundtrip(self):
        data = np.cumsum(np.arange(40, dtype="<u2").reshape(5, 8) % 7, axis=1).astype("<u2")
        _raster, out = self._roundtrip(
            data,
            compression=geotiff.COMPRESSION_DEFLATE,
            predictor=geotiff.PREDICTOR_HORIZONTAL,
            tile=None,
            pixel_scale=45.0,
        )
        np.testing.assert_array_equal(out, data)

    def test_transform_and_grid_assertions(self):
        data = np.zeros((4, 8), dtype="<u1")
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "probe.tif"
            path.write_bytes(
                _build_tiff(
                    data,
                    compression=geotiff.COMPRESSION_NONE,
                    predictor=geotiff.PREDICTOR_NONE,
                    tile=None,
                    pixel_scale=45.0,
                )
            )
            raster = geotiff.GeoTiff(path)
            self.assertEqual(raster.transform.origin_lon, -180.0)
            self.assertEqual(raster.transform.origin_lat, 90.0)
            self.assertAlmostEqual(raster.transform.cell_center_lon(0), -157.5)
            self.assertAlmostEqual(raster.transform.cell_center_lat(0), 67.5)
            raster.require_grid(8, 4, 45.0, -180.0, 90.0)
            with self.assertRaises(tiff_tags.TiffError):
                raster.require_grid(720, 360, 0.5, -180.0, 90.0)

    def test_lzw_decode_handles_repeats_and_table_growth(self):
        for payload in (
            b"A" * 600 + bytes(range(256)),
            bytes(range(256)) * 40,  # forces 10, 11 and 12 bit codes
            b"",
            b"\x00",
        ):
            self.assertEqual(geotiff.lzw_decode(_lzw_encode(payload)), payload, len(payload))

    def test_lzw_rejects_a_stream_without_a_clear_code(self):
        with self.assertRaises(tiff_tags.TiffError):
            geotiff.lzw_decode(b"\x00\x00\x00")

    def test_reader_requires_raster_pixel_is_area(self):
        data = np.zeros((4, 8), dtype="<u1")
        # No 1025 key at all -> fail closed.
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "no1025.tif"
            path.write_bytes(
                _build_tiff(
                    data,
                    compression=geotiff.COMPRESSION_NONE,
                    predictor=geotiff.PREDICTOR_NONE,
                    tile=None,
                    pixel_scale=45.0,
                    geo_keys=[1, 1, 0, 1, 1024, 0, 1, 2],
                )
            )
            with self.assertRaises(tiff_tags.TiffError):
                geotiff.GeoTiff(path)
        # 1025=2 (RasterPixelIsPoint) -> also fail closed.
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "point.tif"
            path.write_bytes(
                _build_tiff(
                    data,
                    compression=geotiff.COMPRESSION_NONE,
                    predictor=geotiff.PREDICTOR_NONE,
                    tile=None,
                    pixel_scale=45.0,
                    geo_keys=[1, 1, 0, 2, 1024, 0, 1, 2, 1025, 0, 1, 2],
                )
            )
            with self.assertRaises(tiff_tags.TiffError):
                geotiff.GeoTiff(path)


def _lzw_encode(data: bytes) -> bytes:
    """Reference TIFF LZW encoder (early code-width change), used only by tests."""
    out = bytearray()
    bit_buffer = 0
    bit_count = 0

    def emit(code: int, width: int) -> None:
        nonlocal bit_buffer, bit_count
        bit_buffer = (bit_buffer << width) | code
        bit_count += width
        while bit_count >= 8:
            out.append((bit_buffer >> (bit_count - 8)) & 0xFF)
            bit_count -= 8

    table: dict[bytes, int] = {bytes((value,)): value for value in range(256)}
    next_code = 258
    width = 9
    emit(256, width)
    current = b""
    for byte in data:
        candidate = current + bytes((byte,))
        if candidate in table:
            current = candidate
            continue
        emit(table[current], width)
        table[candidate] = next_code
        next_code += 1
        # The decoder's table lags the encoder's by one entry, so the encoder
        # switches width one code later than the decoder's own threshold.
        if next_code >= (1 << width) and width < 12:
            width += 1
        current = bytes((byte,))
    if current:
        emit(table[current], width)
    emit(257, width)
    if bit_count:
        out.append((bit_buffer << (8 - bit_count)) & 0xFF)
    return bytes(out)


# ------------------------------------------------------------ repeatable bytes


class RepeatableBytesTests(unittest.TestCase):
    def test_wsg1_record_layout_matches_the_runtime_reader(self):
        self.assertEqual(TILE_DTYPE.itemsize, TILE_RECORD_BYTES)
        self.assertEqual(
            TILE_DTYPE.names,
            ("flags", "biome", "climate", "elevation", "slope", "temperature", "rainfall"),
        )
        offsets = [TILE_DTYPE.fields[name][1] for name in TILE_DTYPE.names]
        self.assertEqual(offsets, [0, 1, 2, 3, 5, 6, 8])
        # The packed record must equal what WorldMapBundleReader.cs reads field by
        # field: byte, byte, byte, int16, byte, int16, uint16, all little-endian.
        record = np.zeros(1, dtype=TILE_DTYPE)
        values = (19, koppen.ALPINE, koppen.HIGHLAND, -8714, 255, -333, 2200)
        for name, value in zip(TILE_DTYPE.names, values):
            record[name] = value
        self.assertEqual(record.tobytes(), struct.pack("<BBBhBhH", *values))

    def test_bundle_bytes_are_stable_and_self_describing(self):
        grid = Grid("Low", 2, 4, 2)
        records = np.zeros(grid.cells, dtype=TILE_DTYPE)
        records["flags"] = np.arange(grid.cells, dtype=np.uint8)
        records["elevation"] = np.arange(grid.cells, dtype=np.int16) * -13
        first = build_geo.bundle_bytes(grid, records)
        second = build_geo.bundle_bytes(grid, records)
        self.assertEqual(first, second)
        magic, version, lod, width, height = struct.unpack_from("<iiBii", first, 0)
        self.assertEqual(magic, build_geo.MAGIC)
        self.assertEqual((version, lod, width, height), (1, 2, 4, 2))
        header = struct.calcsize("<iiBii")
        length = first[header]
        self.assertEqual(
            first[header + 1 : header + 1 + length].decode(), build_geo.BUILD_ID
        )
        tail = header + 1 + length
        self.assertEqual(struct.unpack_from("<i", first, tail)[0], grid.cells)
        self.assertEqual(len(first), tail + 4 + grid.cells * TILE_RECORD_BYTES)

    def test_bundle_bytes_reject_a_mismatched_tile_count(self):
        grid = Grid("Low", 2, 4, 2)
        with self.assertRaises(lockfile.LockError):
            build_geo.bundle_bytes(grid, np.zeros(3, dtype=TILE_DTYPE))

    def test_gzip_container_is_byte_identical_across_writes(self):
        grid = Grid("Low", 2, 8, 4)
        records = np.zeros(grid.cells, dtype=TILE_DTYPE)
        records["rainfall"] = np.arange(grid.cells, dtype=np.uint16) * 7
        with tempfile.TemporaryDirectory() as tmp:
            out_a = Path(tmp) / "a"
            out_b = Path(tmp) / "b"
            out_a.mkdir()
            out_b.mkdir()
            path_a = build_geo.write_bundle(out_a, grid, records)
            path_b = build_geo.write_bundle(out_b, grid, records)
            self.assertEqual(path_a.read_bytes(), path_b.read_bytes())
            self.assertEqual(
                hashlib.sha256(path_a.read_bytes()).hexdigest(),
                hashlib.sha256(path_b.read_bytes()).hexdigest(),
            )
            self.assertEqual(
                gzip.decompress(path_a.read_bytes()), build_geo.bundle_bytes(grid, records)
            )

    def test_build_grid_is_deterministic_for_identical_inputs(self):
        parts = grid_build.VectorParts(
            land=[_rect(-20.0, -10.0, 40.0, 50.0)],
            lakes=[_rect(0.0, 0.0, 6.0, 6.0)],
            coastline=[np.array([[-20.0, -10.0], [40.0, 50.0]])],
            rivers=[np.array([[10.0, 10.0], [30.0, 40.0]])],
            river_scalerank=[0],
        )
        rng = np.random.default_rng(5)
        accumulator = grid_build.ElevationAccumulator(
            sums=rng.integers(-4000, 5000, size=(HIGH.height, HIGH.width)).astype(np.float64),
            counts=np.ones((HIGH.height, HIGH.width), dtype=np.int64),
        )
        codes = rng.integers(0, 31, size=(HIGH.height, HIGH.width)).astype(np.uint8)
        first = grid_build.build_grid(MID, parts, accumulator, codes)
        second = grid_build.build_grid(MID, parts, accumulator, codes)
        self.assertEqual(first.records.tobytes(), second.records.tobytes())
        self.assertEqual(first.stats, second.stats)


# --------------------------------------------------------- real cached sources


class CachedSourceTests(unittest.TestCase):
    def setUp(self):
        self.paths = _cache_paths()
        if self.paths is None:
            self.skipTest("upstream cache missing; run `python tools/geo/build_geo.py fetch`")

    def test_koppen_layer_is_the_expected_global_half_degree_grid(self):
        codes = grid_build.load_koppen_codes(self.paths.koppen)
        self.assertEqual(codes.shape, (360, 720))
        self.assertLessEqual(int(codes.max()), koppen.CLASS_COUNT)
        # Known classes at 0.5 degree cell centres (legend.txt codes).
        for lon, lat, expected in (
            (15.25, 24.25, 4),   # central Sahara -> BWh
            (-62.25, -4.25, 1),  # Amazon basin -> Af
            (0.25, -80.25, 30),  # Antarctic interior -> EF
            (100.25, 65.25, 27), # central Siberia -> Dfc
        ):
            x = int((lon + 180.0) / 0.5)
            y = int((90.0 - lat) / 0.5)
            self.assertEqual(int(codes[y, x]), expected, f"{lon},{lat}")
        ocean_x = int((-150.0 + 180.0) / 0.5)
        ocean_y = int((90.0 - 0.0) / 0.5)
        self.assertEqual(int(codes[ocean_y, ocean_x]), koppen.NODATA_CODE)

    def test_etopo_header_matches_the_locked_global_grid(self):
        raster = geotiff.GeoTiff(self.paths.etopo)
        raster.require_grid(
            grid_build.ETOPO_WIDTH, grid_build.ETOPO_HEIGHT, grid_build.ETOPO_STEP, -180.0, 90.0
        )
        self.assertEqual(raster.dtype, np.dtype("<f4"))
        self.assertEqual(raster.compression, geotiff.COMPRESSION_DEFLATE)
        self.assertEqual(raster.predictor, geotiff.PREDICTOR_FLOATING_POINT)
        self.assertEqual(raster.nodata_value(), -99999.0)
        self.assertEqual(raster.blocks_across * raster.blocks_down, len(raster.block_offsets))

    def test_natural_earth_record_counts_match_the_shx_index(self):
        from shapefile_reader import iter_shapes

        for path, expected_types in (
            (self.paths.land, {5, 15, 25}),
            (self.paths.lakes, {5, 15, 25}),
            (self.paths.coastline, {3, 13, 23}),
            (self.paths.rivers, {3, 13, 23}),
        ):
            shapes = list(iter_shapes(path))
            # The .shx index holds one 8-byte entry per record after a 100-byte header.
            indexed = (path.with_suffix(".shx").stat().st_size - 100) // 8
            self.assertEqual(len(shapes), indexed, path.name)
            self.assertTrue({shape.shape_type for shape in shapes} <= expected_types, path.name)
            vertices = sum(part.shape[0] for shape in shapes for part in shape.parts)
            self.assertGreater(vertices, 100_000, path.name)
            for shape in shapes:
                for part in shape.parts:
                    self.assertEqual(part.ndim, 2)
                    self.assertEqual(part.shape[1], 2)
            longitudes = np.concatenate(
                [part[:, 0] for shape in shapes for part in shape.parts]
            )
            latitudes = np.concatenate(
                [part[:, 1] for shape in shapes for part in shape.parts]
            )
            self.assertGreaterEqual(longitudes.min(), -180.0001, path.name)
            self.assertLessEqual(longitudes.max(), 180.0001, path.name)
            self.assertGreaterEqual(latitudes.min(), -90.0001, path.name)
            self.assertLessEqual(latitudes.max(), 90.0001, path.name)

    @unittest.skipUnless(SLOW, "set WORLDSIM_GEO_SLOW_TESTS=1 to build the whole planet twice from cache bytes")
    def test_full_build_is_byte_identical_twice(self):
        # True end-to-end: re-read the cache bytes for each pass through
        # build_all (ETOPO stream, Köppen decode, NE shapefiles), so the
        # determinism claim covers the readers and not just the in-memory
        # accumulator that a reused-input test would exercise.
        lock = lockfile.load_lock(HERE / "sources.lock.json")
        cache = fetch_sources.cache_root(HERE)
        digests = []
        for _ in range(2):
            results = grid_build.build_all(lock, cache)
            digests.append(
                [hashlib.sha256(result.records.tobytes()).hexdigest() for result in results]
            )
            for result in results:
                self.assertEqual(result.records.size, result.grid.cells)
        self.assertEqual(digests[0], digests[1])
        self.assertEqual(len(set(digests[0])), 3)

    @unittest.skipUnless(SLOW, "set WORLDSIM_GEO_SLOW_TESTS=1 to stream the whole ETOPO raster")
    def test_streamed_elevation_covers_every_high_cell(self):
        accumulator = grid_build.aggregate_elevation(self.paths.etopo)
        self.assertEqual(accumulator.counts.shape, (HIGH.height, HIGH.width))
        self.assertTrue((accumulator.counts == 900).all())  # 30x30 ETOPO cells per High cell
        metres, missing = grid_build.elevation_for_grid(accumulator, HIGH)
        self.assertFalse(missing.any())
        self.assertGreater(metres.max(), 4000.0)
        self.assertLess(metres.min(), -7000.0)

    def test_koppen_table_reconciles_with_the_cached_legend(self):
        # The hardcoded CLASSES table must match the legend.txt shipped in the
        # same Köppen archive, code-for-code and symbol-for-symbol; build_all
        # already enforces this, but the assertion is worth a focused test.
        legend_path = grid_build.reconcile_koppen_legend(self.paths.koppen)
        self.assertEqual(legend_path.name, "legend.txt")


if __name__ == "__main__":
    unittest.main()
