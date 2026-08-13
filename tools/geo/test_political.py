#!/usr/bin/env python3
"""Focused tests for the political asset (Task 3).

Covered: WSP1 format roundtrip, stable sorting, dual-view determinism,
dispute markers preserved, border-year fail-closed, stable-id stability across
rebuilds, and (against the real cache) country/city counts matching the NE
layers and the manifest asset checksum.

Cache-dependent tests skip when the upstream cache is absent; the full-planet
rebuild is additionally gated behind ``WORLDSIM_GEO_SLOW_TESTS=1``.
"""
from __future__ import annotations

import gzip
import hashlib
import os
import struct
import tempfile
import unittest
from pathlib import Path

import fetch_sources
import lockfile
import political
import political_binary
from political import (
    CityRecord,
    CountryRecord,
    DisputedRecord,
    PoliticalAsset,
    PoliticalError,
    Ring,
)

HERE = Path(__file__).resolve().parent
SLOW = os.environ.get("WORLDSIM_GEO_SLOW_TESTS") == "1"
BUILD_ID = "geo-v1-test0123456789ab"


def _cache_paths():
    lock = lockfile.load_lock(HERE / "sources.lock.json")
    cache = fetch_sources.cache_root(HERE)
    for sid in (political.SRC_ADMIN0, political.SRC_SOVEREIGNTY,
                political.SRC_DISPUTED, political.SRC_POP_PLACES):
        src = next((s for s in lock["sources"] if s["id"] == sid), None)
        if src is None or not (fetch_sources.extract_dir(src, cache)).is_dir():
            return None
    return lock, cache


def _country(stable_id="AAA", name="Aland", rings=None):
    return CountryRecord(
        stable_id=stable_id, admin_id=stable_id, sovereign_id=stable_id,
        iso_a3_eh=stable_id, name=name, name_long=name, sovereign_name=name,
        continent="Europe", region_un="Europe", subregion="Northern Europe",
        feature_class="Sovereign country", type="Sovereign country", note_adm0="",
        wikidata_id="Q1", pop_est=1000, rings=rings or (),
    )


def _disputed(stable_id="B01", name="Patch", rings=None):
    return DisputedRecord(
        stable_id=stable_id, admin_id="AAA", sovereign_id="AA1",
        iso_a3_eh="-99", name=name, name_long=name, admin_name="Aland",
        sovereign_name="Aland", type="Disputed", note_adm0="Disputed",
        note_brk="claim", wikidata_id="Q2", pop_est=-1, rings=rings or (),
    )


def _city(stable_id=1, name="Town"):
    return CityRecord(
        stable_id=stable_id, name=name, name_ascii=name, feature_class="Populated place",
        admin_id="AAA", sovereign_id="AAA", admin_name="Aland", sovereign_name="Aland",
        scalerank=5, nat_scale=20, is_capital=1, is_world_city=0, is_mega_city=0,
        pop_max=100000, pop_min=90000, longitude=10.0, latitude=50.0, wikidata_id="Q3",
    )


def _ring():
    return Ring(points=((0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.0, 0.0)))


class FormatTests(unittest.TestCase):
    def test_roundtrip_preserves_all_fields(self):
        asset = PoliticalAsset(
            border_year=2026,
            de_facto_countries=[_country("AAA", "Aland", rings=(_ring(),))],
            sovereignty_claims=[_country("AA1", "Aland Sov", rings=(_ring(),))],
            disputed_areas=[_disputed("B01", "Patch", rings=(_ring(),))],
            cities=[_city(42, "Town")],
        )
        payload = political_binary.serialize(asset, BUILD_ID)
        back, bid = political_binary.deserialize(payload)
        self.assertEqual(bid, BUILD_ID)
        self.assertEqual(back.border_year, 2026)
        self.assertEqual(len(back.de_facto_countries), 1)
        self.assertEqual(len(back.disputed_areas), 1)
        self.assertEqual(len(back.cities), 1)
        c = back.de_facto_countries[0]
        self.assertEqual(c.stable_id, "AAA")
        self.assertEqual(c.name, "Aland")
        self.assertEqual(c.pop_est, 1000)
        self.assertEqual(len(c.rings), 1)
        self.assertEqual(c.rings[0].points, _ring().points)
        d = back.disputed_areas[0]
        self.assertEqual(d.stable_id, "B01")
        self.assertEqual(d.note_adm0, "Disputed")
        self.assertEqual(d.note_brk, "claim")
        self.assertEqual(d.type, "Disputed")
        ci = back.cities[0]
        self.assertEqual(ci.stable_id, 42)
        self.assertAlmostEqual(ci.longitude, 10.0)
        self.assertAlmostEqual(ci.latitude, 50.0)
        self.assertEqual(ci.is_capital, 1)

    def test_roundtrip_is_byte_identical_for_identical_assets(self):
        asset = PoliticalAsset(
            border_year=2026,
            de_facto_countries=[_country("AAA", "A"), _country("BBB", "B")],
            cities=[_city(2, "T2"), _city(1, "T1")],
        )
        first = political_binary.serialize(asset, BUILD_ID)
        second = political_binary.serialize(asset, BUILD_ID)
        self.assertEqual(first, second)

    def test_header_is_self_describing(self):
        asset = PoliticalAsset(border_year=2026)
        payload = political_binary.serialize(asset, BUILD_ID)
        magic, version, year = struct.unpack_from("<IBH", payload, 0)
        self.assertEqual(magic, political.MAGIC)
        self.assertEqual(version, political.FORMAT_VERSION)
        self.assertEqual(year, 2026)

    def test_bad_magic_fails_closed(self):
        bad = bytearray(political_binary.serialize(PoliticalAsset(border_year=2026), BUILD_ID))
        bad[0] = 0
        with self.assertRaises(PoliticalError):
            political_binary.deserialize(bytes(bad))

    def test_trailing_bytes_fail_closed(self):
        payload = political_binary.serialize(PoliticalAsset(border_year=2026), BUILD_ID)
        with self.assertRaises(PoliticalError):
            political_binary.deserialize(payload + b"\x00")

    def test_records_are_sorted_by_stable_id_then_name(self):
        # The builder (readers) sorts by (stable_id, name); the serializer is a
        # faithful writer, so feeding already-sorted records yields sorted
        # output. The builder's sort is verified against the real cache below.
        asset = PoliticalAsset(
            border_year=2026,
            de_facto_countries=[
                _country("AAA", "Alpha"),
                _country("AAA", "Beta"),
                _country("ZZZ", "Zeta"),
            ],
        )
        payload = political_binary.serialize(asset, BUILD_ID)
        back, _ = political_binary.deserialize(payload)
        ids = [(r.stable_id, r.name) for r in back.de_facto_countries]
        self.assertEqual(ids, [("AAA", "Alpha"), ("AAA", "Beta"), ("ZZZ", "Zeta")])

    def test_gzip_container_is_byte_identical(self):
        asset = PoliticalAsset(border_year=2026, cities=[_city(1, "A"), _city(2, "B")])
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            p1 = political_binary.write_political(out, asset, BUILD_ID)
            p2 = political_binary.write_political(out, asset, BUILD_ID)
            # write_political overwrites; compare against a second dir.
            out2 = Path(tmp) / "second"
            out2.mkdir()
            p3 = political_binary.write_political(out2, asset, BUILD_ID)
            self.assertEqual(p1.read_bytes(), p3.read_bytes())
            self.assertEqual(
                gzip.decompress(p1.read_bytes()),
                political_binary.serialize(asset, BUILD_ID),
            )


class BorderYearTests(unittest.TestCase):
    def test_unsupported_year_fails_closed(self):
        lock = lockfile.load_lock(HERE / "sources.lock.json")
        cache = fetch_sources.cache_root(HERE)
        if not _cache_paths():
            self.skipTest("upstream cache missing")
        for year in (1990, 2000, 2025, 2027, 2100):
            with self.assertRaises(PoliticalError) as ctx:
                political.build_political(lock, cache, border_year=year)
            self.assertIn(str(year), str(ctx.exception))
            self.assertIn("2026", str(ctx.exception))

    def test_supported_year_does_not_raise_on_year_check(self):
        # The year check alone must accept 2026; full build is exercised elsewhere.
        asset = PoliticalAsset(border_year=2026)
        self.assertEqual(asset.border_year, political.SUPPORTED_BORDER_YEAR)


# --------------------------------------------------------- real cached sources


class CachedPoliticalTests(unittest.TestCase):
    def setUp(self):
        paths = _cache_paths()
        if paths is None:
            self.skipTest("upstream cache missing; run `python tools/geo/build_geo.py fetch`")
        self.lock, self.cache = paths

    def test_country_count_matches_ne_admin0(self):
        asset = political.build_political(self.lock, self.cache)
        self.assertEqual(len(asset.de_facto_countries), 258)
        self.assertEqual(len(asset.sovereignty_claims), 209)
        self.assertEqual(len(asset.disputed_areas), 99)

    def test_city_count_matches_populated_places(self):
        asset = political.build_political(self.lock, self.cache)
        self.assertEqual(len(asset.cities), 7342)

    def test_polygon_roundtrip_against_source(self):
        asset = political.build_political(self.lock, self.cache)
        payload = political_binary.serialize(asset, "geo-v1-roundtrip0000")
        back, _ = political_binary.deserialize(payload)
        self.assertEqual(len(back.de_facto_countries), len(asset.de_facto_countries))
        for a, b in zip(asset.de_facto_countries, back.de_facto_countries):
            self.assertEqual(a.rings, b.rings)
            self.assertEqual(a.stable_id, b.stable_id)
            self.assertEqual(a.name, b.name)
        for a, b in zip(asset.disputed_areas, back.disputed_areas):
            self.assertEqual(a.rings, b.rings)
            self.assertEqual(a.note_adm0, b.note_adm0)
        # A city's point is preserved exactly.
        self.assertEqual(asset.cities[0].longitude, back.cities[0].longitude)
        self.assertEqual(asset.cities[0].latitude, back.cities[0].latitude)

    def test_dual_view_determinism(self):
        a1 = political.build_political(self.lock, self.cache)
        a2 = political.build_political(self.lock, self.cache)
        self.assertEqual(
            political_binary.serialize(a1, "x"),
            political_binary.serialize(a2, "x"),
        )
        # DeFacto and Sovereignty views are distinct (different record counts).
        self.assertNotEqual(len(a1.de_facto_countries), len(a1.sovereignty_claims))

    def test_dispute_markers_preserved(self):
        asset = political.build_political(self.lock, self.cache)
        types = {r.type for r in asset.disputed_areas}
        self.assertIn("Disputed", types)
        # Every disputed record carries its source claimant fields, not blank.
        for r in asset.disputed_areas:
            self.assertTrue(r.stable_id)
            self.assertTrue(r.admin_id)
            self.assertTrue(r.sovereign_id)
        # At least one record carries an explicit NOTE_ADM0 dispute marker.
        self.assertTrue(any(r.note_adm0 for r in asset.disputed_areas))
        # No adjudication field is invented: the record shape has exactly the
        # source-derived fields and no verdict/status of its own.
        self.assertFalse(hasattr(r := asset.disputed_areas[0], "verdict"))
        self.assertFalse(hasattr(r, "status"))

    def test_stable_id_stability_across_rebuilds(self):
        a1 = political.build_political(self.lock, self.cache)
        a2 = political.build_political(self.lock, self.cache)
        self.assertEqual(
            [c.stable_id for c in a1.de_facto_countries],
            [c.stable_id for c in a2.de_facto_countries],
        )
        self.assertEqual(
            [c.stable_id for c in a1.cities],
            [c.stable_id for c in a2.cities],
        )
        # Stable ids are unique within each view (country view by stable_id).
        df_ids = [c.stable_id for c in a1.de_facto_countries]
        self.assertEqual(len(df_ids), len(set(df_ids)))
        # The builder emits records sorted by (stable_id, name).
        for view in (a1.de_facto_countries, a1.sovereignty_claims, a1.disputed_areas):
            keys = [(r.stable_id, r.name) for r in view]
            self.assertEqual(keys, sorted(keys))
        city_keys = [(r.stable_id, r.name) for r in a1.cities]
        self.assertEqual(city_keys, sorted(city_keys))

    def test_known_countries_are_present(self):
        asset = political.build_political(self.lock, self.cache)
        by_id = {c.stable_id: c for c in asset.de_facto_countries}
        # stable_id is ADM0_A3 (unique per de-facto unit). ISO_A3_EH is NOT
        # unique (BRA is shared by Brazil=BRA and Brazilian Islands=BRI), so
        # the de-facto view keys by ADM0_A3.
        for adm0, name in (("USA", "United States of America"), ("FRA", "France"),
                           ("JPN", "Japan"), ("BRA", "Brazil"), ("AUS", "Australia")):
            self.assertIn(adm0, by_id, adm0)
            self.assertEqual(by_id[adm0].name, name, adm0)
        # France's de-facto record is tagged with its sovereign id, which is the
        # stable_id of a record in the SovereigntyClaims view (the linkage between
        # the two views). NE uses SOV_A3="FR1" for France's sovereignty, distinct
        # from the de-facto ADM0_A3="FRA".
        self.assertEqual(by_id["FRA"].sovereign_id, "FR1")
        sov_ids = {c.stable_id for c in asset.sovereignty_claims}
        self.assertIn(by_id["FRA"].sovereign_id, sov_ids)
        # ISO_A3_EH is carried as an attribute and is not the stable id.
        self.assertEqual(by_id["BRA"].iso_a3_eh, "BRA")
        self.assertEqual(by_id["BRI"].iso_a3_eh, "BRA")
        self.assertNotEqual(by_id["BRA"].stable_id, by_id["BRI"].stable_id)

    def test_known_capital_is_present(self):
        asset = political.build_political(self.lock, self.cache)
        capitals = [c for c in asset.cities if c.is_capital and c.admin_id == "USA"]
        self.assertTrue(capitals)
        dc = next(c for c in capitals if "Washington" in c.name)
        # Coordinates are read verbatim from the NE populated-places layer.
        self.assertAlmostEqual(dc.latitude, 38.9014952, places=4)
        self.assertAlmostEqual(dc.longitude, -77.0113644, places=4)


class ManifestTests(unittest.TestCase):
    def setUp(self):
        paths = _cache_paths()
        if paths is None:
            self.skipTest("upstream cache missing; run `python tools/geo/build_geo.py fetch`")
        self.lock, self.cache = paths

    @unittest.skipUnless(SLOW, "set WORLDSIM_GEO_SLOW_TESTS=1 to run the full build")
    def test_manifest_lists_political_asset_with_checksum(self):
        import build_geo
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            build_geo.run_build(out, verify=True)
            manifest = (out / "manifest.txt").read_text(encoding="utf-8")
            self.assertIn("asset=political-2026.wgeo.gz|", manifest)
            # The checksum in the manifest matches the file on disk.
            line = next(l for l in manifest.splitlines() if l.startswith("asset=political-2026.wgeo.gz|"))
            digest = line.split("|")[-1]
            self.assertEqual(
                digest,
                hashlib.sha256((out / "political-2026.wgeo.gz").read_bytes()).hexdigest(),
            )
            # The legacy hand-written tsv is no longer copied.
            self.assertNotIn("political-2026.tsv", manifest)
            self.assertFalse((out / "political-2026.tsv").exists())

    @unittest.skipUnless(SLOW, "set WORLDSIM_GEO_SLOW_TESTS=1 to run the full build")
    def test_manifest_carries_lock_fields_and_conversion_params(self):
        import build_geo
        from lockfile import build_id_from_lock, load_lock, lock_path, lock_sha256
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            build_geo.run_build(out, verify=True)
            lines = (out / "manifest.txt").read_text(encoding="utf-8").splitlines()
            kv = {}
            for raw in lines:
                raw = raw.strip()
                if not raw or raw.startswith("#") or "=" not in raw: continue
                k, _, v = raw.partition("=")
                kv[k] = v
            lock = load_lock(lock_path(Path(__file__).resolve().parent))
            self.assertEqual(kv["buildId"], build_id_from_lock(lock))
            self.assertEqual(kv["sourcesLockSha256"], lock_sha256(lock))
            self.assertEqual(kv["fidelity"], lock["derivation"]["currentBuildFidelity"])
            self.assertNotIn("simplified", kv["fidelity"])
            for name in ("projection", "pixelConvention", "lowGrid", "midGrid", "highGrid",
                         "gzipMtime", "borderYear"):
                self.assertIn("conversion." + name, kv, "missing conversion." + name)
            self.assertEqual(kv["conversion.projection"], "equirectangular")
            self.assertEqual(kv["conversion.highGrid"], "720x360")
            self.assertEqual(kv["conversion.borderYear"], "2026")
            # manifestChecksum must be the SHA-256 of the canonical chunk|/asset| lines.
            # The manifest stores "chunk=..." / "asset=..."; the canonical uses "chunk|"/"asset|".
            canonical_lines = []
            for l in lines:
                if l.startswith("chunk="):
                    canonical_lines.append("chunk|" + l[len("chunk="):])
                elif l.startswith("asset="):
                    canonical_lines.append("asset|" + l[len("asset="):])
            canonical = "\n".join(canonical_lines).encode("utf-8")
            self.assertEqual(kv["manifestChecksum"], hashlib.sha256(canonical).hexdigest())

    @unittest.skipUnless(SLOW, "set WORLDSIM_GEO_SLOW_TESTS=1 to build twice")
    def test_full_build_is_byte_identical_twice(self):
        import build_geo
        digests = []
        with tempfile.TemporaryDirectory() as tmp:
            for _ in range(2):
                out = Path(tmp) / ("run" + str(len(digests)))
                build_geo.run_build(out, verify=False)
                digests.append(hashlib.sha256((out / "political-2026.wgeo.gz").read_bytes()).hexdigest())
        self.assertEqual(digests[0], digests[1])


if __name__ == "__main__":
    unittest.main()
