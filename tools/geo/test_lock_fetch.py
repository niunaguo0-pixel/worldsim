#!/usr/bin/env python3
"""Focused tests for lock canonicalization, fail-closed fetch, and zip-slip refusal."""
from __future__ import annotations

import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

import fetch_sources
import lockfile

HERE = Path(__file__).resolve().parent


def _source(**overrides):
    base = {
        "id": "fixture",
        "product": "fixture zip",
        "version": "1",
        "url": "https://example.invalid/fixture.zip",
        "filename": "fixture.zip",
        "format": "zip-shapefile",
        "license": "public-domain",
        "crs": "EPSG:4326",
        "selectedLayers": ["fixture"],
        "bytes": None,
        "sha256": None,
    }
    base.update(overrides)
    return base


def _lock(sources):
    return {"sources": sources, "derivation": {"outputGrids": {"High": {"stepDegrees": 0.5}}}}


class LockTests(unittest.TestCase):
    def test_real_lock_loads_and_build_id_is_stable(self):
        lock = lockfile.load_lock(HERE / "sources.lock.json")
        first = lockfile.build_id_from_lock(lock)
        second = lockfile.build_id_from_lock(lockfile.load_lock(HERE / "sources.lock.json"))
        self.assertEqual(first, second)
        self.assertTrue(first.startswith("geo-v1-"))
        self.assertEqual(len(first), len("geo-v1-") + 16)
        self.assertNotIn("20260813", first)
        ids = [source["id"] for source in lock["sources"]]
        for required in (
            "ne-10m-land",
            "ne-10m-coastline",
            "ne-10m-lakes",
            "ne-10m-rivers",
            "ne-10m-admin-0",
            "ne-10m-admin-0-sovereignty",
            "ne-10m-populated-places",
            "ne-10m-disputed",
            "etopo-2022-60s-surface",
            "koppen-geiger-v3-1991-2020",
        ):
            self.assertIn(required, ids)
        self.assertTrue(all(source["url"].startswith("https://") for source in lock["sources"]))
        for source in lock["sources"]:
            digest = source.get("sha256")
            if digest is not None:
                self.assertEqual(lockfile.source_sha256(source), digest.lower())

    def test_canonical_hash_ignores_key_order_and_whitespace(self):
        lock_a = _lock([_source(id="b"), _source(id="a", filename="a.zip")])
        lock_b = json.loads(json.dumps(lock_a, indent=4))
        lock_b["sources"] = list(reversed(lock_b["sources"]))
        self.assertEqual(lockfile.lock_sha256(lock_a), lockfile.lock_sha256(lock_b))
        self.assertEqual(lockfile.build_id_from_lock(lock_a), lockfile.build_id_from_lock(lock_b))

    def test_build_id_changes_when_lock_content_changes(self):
        lock = _lock([_source()])
        other = _lock([_source(version="2")])
        self.assertNotEqual(lockfile.build_id_from_lock(lock), lockfile.build_id_from_lock(other))

    def test_https_is_required(self):
        with self.assertRaises(lockfile.LockError):
            lockfile.validate_lock(_lock([_source(url="http://example.invalid/x.zip")]))


class FetchTests(unittest.TestCase):
    def test_missing_sha256_fails_closed_without_download(self):
        calls = []

        def downloader(url):
            calls.append(url)
            return b"nope"

        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.fetch_source(_source(), Path(tmp), downloader=downloader)
        self.assertEqual(calls, [])
        self.assertIn("no SHA-256", str(ctx.exception))
        self.assertIn("https://example.invalid/fixture.zip", str(ctx.exception))

    def test_size_mismatch_fails_before_use(self):
        payload = b"hello-world"
        digest = hashlib.sha256(payload).hexdigest()
        source = _source(bytes=4, sha256=digest)
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.fetch_source(source, Path(tmp), downloader=lambda url: payload)
            self.assertIn("byte length mismatch", str(ctx.exception))
            self.assertFalse(any(Path(tmp).rglob("fixture.zip")))

    def test_sha256_mismatch_fails_before_use(self):
        payload = b"hello-world"
        source = _source(bytes=len(payload), sha256="0" * 64)
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.fetch_source(source, Path(tmp), downloader=lambda url: payload)
            self.assertIn("SHA-256 mismatch", str(ctx.exception))
            self.assertFalse(any(Path(tmp).rglob("fixture.zip")))

    def test_cached_file_is_reused(self):
        payload = b"cached-bytes"
        digest = hashlib.sha256(payload).hexdigest()
        source = _source(bytes=len(payload), sha256=digest, format="geotiff", filename="payload.bin")
        calls = []

        def downloader(url):
            calls.append(url)
            return b"should-not-run"

        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp)
            dest = fetch_sources.source_archive_path(source, cache)
            dest.parent.mkdir(parents=True)
            dest.write_bytes(payload)
            reused = fetch_sources.fetch_source(source, cache, downloader=downloader)
            self.assertEqual(reused, dest)
        self.assertEqual(calls, [])

    def test_zip_path_traversal_is_refused(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as zf:
            zf.writestr("../evil.txt", "nope")
            zf.writestr("ok/file.txt", "yes")
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / "bad.zip"
            archive.write_bytes(buffer.getvalue())
            dest = Path(tmp) / "out"
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.safe_extract_zip(archive, dest)
            self.assertIn("path traversal", str(ctx.exception))
            self.assertFalse((dest / "evil.txt").exists())
            self.assertFalse((Path(tmp) / "evil.txt").exists())

    def test_successful_fetch_extracts_zip_inside_cache(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as zf:
            zf.writestr("ne_fixture.shp", b"shp")
        payload = buffer.getvalue()
        source = _source(bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())
        with tempfile.TemporaryDirectory() as tmp:
            dest = fetch_sources.fetch_source(source, Path(tmp), downloader=lambda url: payload)
            extracted = fetch_sources.extract_dir(source, Path(tmp)) / "ne_fixture.shp"
            self.assertTrue(dest.is_file())
            self.assertTrue(extracted.is_file())
            self.assertEqual(extracted.read_bytes(), b"shp")

    def test_verify_unpinned_source_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.verify_cache([_source(bytes=4)], Path(tmp))
        self.assertIn("no SHA-256", str(ctx.exception))

    def test_bootstrap_pins_sha256_then_fetch_reuses_cache(self):
        payload = b"bootstrap-bytes"
        source = _source(
            bytes=len(payload),
            sha256=None,
            filename="payload.bin",
            format="geotiff",
            md5=hashlib.md5(payload).hexdigest(),
        )
        calls = []

        def downloader(url):
            calls.append(url)
            return payload

        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp) / "cache"
            lock_path = Path(tmp) / "sources.lock.json"
            lockfile.save_lock(lock_path, _lock([source]))
            fetch_sources.bootstrap_all(
                [source], cache, lock_file=lock_path, downloader=downloader
            )
            pinned = lockfile.load_lock(lock_path)["sources"][0]
            self.assertEqual(pinned["sha256"], hashlib.sha256(payload).hexdigest())
            self.assertEqual(pinned["sha256Pin"]["method"], "bootstrap-from-locked-https-url")
            self.assertTrue(pinned["sha256Pin"]["byteLengthVerified"])
            self.assertTrue(pinned["sha256Pin"]["md5Verified"])
            self.assertFalse(pinned["sha256BlockFetch"])
            reused = fetch_sources.fetch_source(
                pinned, cache, downloader=lambda url: b"should-not-run"
            )
            self.assertTrue(reused.is_file())
        self.assertEqual(calls, ["https://example.invalid/fixture.zip"])

    def test_bootstrap_md5_mismatch_fails_without_pin(self):
        payload = b"hello-world"
        source = _source(
            bytes=len(payload),
            sha256=None,
            filename="payload.bin",
            format="geotiff",
            md5="0" * 32,
        )
        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp) / "cache"
            lock_path = Path(tmp) / "sources.lock.json"
            lockfile.save_lock(lock_path, _lock([source]))
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.bootstrap_all(
                    [source], cache, lock_file=lock_path, downloader=lambda url: payload
                )
            self.assertIn("MD5 mismatch", str(ctx.exception))
            self.assertIsNone(lockfile.source_sha256(lockfile.load_lock(lock_path)["sources"][0]))

    def test_ordinary_fetch_still_fails_for_unpinned_source(self):
        payload = b"hello-world"
        source = _source(bytes=len(payload), sha256=None, filename="payload.bin", format="geotiff")
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.fetch_source(source, Path(tmp), downloader=lambda url: payload)
            self.assertIn("no SHA-256", str(ctx.exception))
            self.assertIn("bootstrap", str(ctx.exception))

    def test_real_lock_pin_state_matches_sha256_presence(self):
        lock = lockfile.load_lock(HERE / "sources.lock.json")
        unpinned = [src["id"] for src in lock["sources"] if lockfile.source_sha256(src) is None]
        if unpinned:
            self.skipTest("lock not yet bootstrapped: " + ", ".join(unpinned))
        for src in lock["sources"]:
            self.assertEqual(src["sha256Pin"]["method"], "bootstrap-from-locked-https-url")
            self.assertTrue(src["sha256Pin"]["byteLengthVerified"])
            self.assertEqual(len(src["sha256"]), 64)
            self.assertFalse(src["sha256BlockFetch"])


class ReviewFixTests(unittest.TestCase):
    def test_bootstrap_default_does_not_overwrite_existing_pin(self):
        payload = b"already-pinned"
        digest = hashlib.sha256(payload).hexdigest()
        source = _source(
            bytes=len(payload),
            sha256=digest,
            filename="payload.bin",
            format="geotiff",
            md5=hashlib.md5(payload).hexdigest(),
            sha256BlockFetch=False,
        )
        calls = []

        def downloader(url):
            calls.append(url)
            return payload

        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp) / "cache"
            dest = fetch_sources.source_archive_path(source, cache)
            dest.parent.mkdir(parents=True)
            dest.write_bytes(payload)
            lock_path = Path(tmp) / "sources.lock.json"
            lockfile.save_lock(lock_path, _lock([source]))
            lockfile.pin_source_sha256(
                lock_path,
                "fixture",
                digest,
                {
                    "date": "2020-01-01",
                    "method": "bootstrap-from-locked-https-url",
                    "byteLengthVerified": True,
                    "md5Verified": True,
                    "url": source["url"],
                    "bytes": len(payload),
                },
            )
            pinned_source = lockfile.load_lock(lock_path)["sources"][0]
            results = fetch_sources.bootstrap_all(
                [pinned_source], cache, lock_file=lock_path, downloader=downloader
            )
            after = lockfile.load_lock(lock_path)["sources"][0]
            self.assertFalse(results[0]["wrotePin"])
            self.assertTrue(results[0]["alreadyPinned"])
            self.assertEqual(after["sha256"], digest)
            self.assertEqual(after["sha256Pin"]["date"], "2020-01-01")
            self.assertFalse(after["sha256BlockFetch"])
        self.assertEqual(calls, [])

    def test_bootstrap_force_repins(self):
        payload = b"already-pinned"
        digest = hashlib.sha256(payload).hexdigest()
        source = _source(
            bytes=len(payload),
            sha256=digest,
            filename="payload.bin",
            format="geotiff",
            md5=hashlib.md5(payload).hexdigest(),
        )
        calls = []

        def downloader(url):
            calls.append(url)
            return payload

        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp) / "cache"
            dest = fetch_sources.source_archive_path(source, cache)
            dest.parent.mkdir(parents=True)
            dest.write_bytes(payload)
            lock_path = Path(tmp) / "sources.lock.json"
            lockfile.save_lock(lock_path, _lock([source]))
            lockfile.pin_source_sha256(
                lock_path,
                "fixture",
                digest,
                {
                    "date": "2020-01-01",
                    "method": "bootstrap-from-locked-https-url",
                    "byteLengthVerified": True,
                    "md5Verified": True,
                    "url": source["url"],
                    "bytes": len(payload),
                },
            )
            pinned_source = lockfile.load_lock(lock_path)["sources"][0]
            results = fetch_sources.bootstrap_all(
                [pinned_source],
                cache,
                lock_file=lock_path,
                downloader=downloader,
                force=True,
            )
            after = lockfile.load_lock(lock_path)["sources"][0]
            self.assertTrue(results[0]["wrotePin"])
            self.assertEqual(after["sha256"], digest)
            self.assertNotEqual(after["sha256Pin"]["date"], "2020-01-01")
            self.assertFalse(after["sha256BlockFetch"])
        self.assertEqual(calls, ["https://example.invalid/fixture.zip"])

    def test_zip_symlink_and_special_files_are_refused(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as zf:
            link = zipfile.ZipInfo("escape")
            link.create_system = 3
            link.external_attr = 0o120777 << 16
            zf.writestr(link, "/tmp/evil")
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / "link.zip"
            archive.write_bytes(buffer.getvalue())
            dest = Path(tmp) / "out"
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.safe_extract_zip(archive, dest)
            self.assertIn("symlink", str(ctx.exception))
            self.assertFalse((dest / "escape").exists())

        fifo = io.BytesIO()
        with zipfile.ZipFile(fifo, "w") as zf:
            special = zipfile.ZipInfo("pipe")
            special.create_system = 3
            special.external_attr = 0o010777 << 16
            zf.writestr(special, b"x")
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / "fifo.zip"
            archive.write_bytes(fifo.getvalue())
            dest = Path(tmp) / "out"
            with self.assertRaises(fetch_sources.FetchError) as ctx:
                fetch_sources.safe_extract_zip(archive, dest)
            self.assertIn("special", str(ctx.exception))
            self.assertFalse((dest / "pipe").exists())

    def test_append_limited_fails_before_writing_over_lock_bytes(self):
        handle = io.BytesIO()
        sha = hashlib.sha256()
        md5 = hashlib.md5()
        with self.assertRaises(fetch_sources.FetchError) as ctx:
            fetch_sources.append_limited(b"abcde", 3, 5, handle, sha, md5)
        self.assertIn("exceeded locked", str(ctx.exception))
        self.assertEqual(handle.getvalue(), b"")
        total = fetch_sources.append_limited(b"ab", 3, 5, handle, sha, md5)
        self.assertEqual(total, 5)
        self.assertEqual(handle.getvalue(), b"ab")

    def test_default_download_is_disabled(self):
        with self.assertRaises(fetch_sources.FetchError) as ctx:
            fetch_sources.default_download("https://example.invalid/x.bin")
        self.assertIn("full-body download is disabled", str(ctx.exception))

    def test_range_resume_requires_matching_content_range(self):
        start, end, complete = fetch_sources.parse_content_range("bytes 100-199/466")
        self.assertEqual((start, end, complete), (100, 199, 466))
        fetch_sources.assert_resume_content_range(
            100, 466, {"Content-Range": "bytes 100-199/466"}
        )
        with self.assertRaises(fetch_sources.FetchError) as ctx:
            fetch_sources.assert_resume_content_range(100, 466, {})
        self.assertIn("without Content-Range", str(ctx.exception))
        with self.assertRaises(fetch_sources.FetchError):
            fetch_sources.assert_resume_content_range(
                50, 466, {"Content-Range": "bytes 100-199/466"}
            )
        with self.assertRaises(fetch_sources.FetchError):
            fetch_sources.assert_resume_content_range(
                100, 466, {"Content-Range": "bytes 100-199/999"}
            )

    def test_build_id_ignores_pin_date_keeps_source_sha256(self):
        digest = "a" * 64
        lock_a = _lock(
            [_source(sha256=digest, sha256Pin={"date": "2020-01-01", "method": "x"})]
        )
        lock_b = _lock(
            [_source(sha256=digest, sha256Pin={"date": "2026-08-13", "method": "x"})]
        )
        lock_c = _lock(
            [_source(sha256="b" * 64, sha256Pin={"date": "2020-01-01", "method": "x"})]
        )
        self.assertEqual(
            lockfile.build_id_from_lock(lock_a), lockfile.build_id_from_lock(lock_b)
        )
        self.assertNotEqual(
            lockfile.build_id_from_lock(lock_a), lockfile.build_id_from_lock(lock_c)
        )

    def test_real_cache_matches_lock_when_present(self):
        lock = lockfile.load_lock(HERE / "sources.lock.json")
        cache = fetch_sources.cache_root(HERE)
        missing = [
            src["id"]
            for src in lock["sources"]
            if not fetch_sources.source_archive_path(src, cache).is_file()
        ]
        if missing:
            self.skipTest("cache missing: " + ", ".join(missing))
        paths = fetch_sources.verify_cache(lock["sources"], cache)
        self.assertEqual(len(paths), len(lock["sources"]))


if __name__ == "__main__":
    unittest.main()
