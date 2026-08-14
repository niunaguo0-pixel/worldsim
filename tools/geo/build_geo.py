#!/usr/bin/env python3
"""Build WorldSim's offline geo-v1 bundles from the verified upstream cache.

Grids come from adapters over `tools/geo/.geo-cache` only: Natural Earth 5.1.2
1:10m vectors, NOAA ETOPO 2022 60" ice-surface elevation, and GloH2O
Köppen-Geiger V3 1991-2020 classification. Temperature and rainfall are
documented deterministic proxies derived from class, latitude and elevation
(see `koppen.py`), not measured values.
"""
from __future__ import annotations
import argparse, gzip, hashlib, pathlib, shutil, struct, sys

from fetch_sources import (
    FetchError,
    bootstrap_all,
    cache_root,
    extract_dir,
    fetch_all,
    inspect_koppen_1991_2020,
    source_archive_path,
    verify_cache,
)
from lockfile import LockError, build_id_from_lock, load_lock, lock_path, lock_sha256, update_source_fields

HERE = pathlib.Path(__file__).resolve().parent
LOCK = load_lock(lock_path(HERE))
BUILD_ID = build_id_from_lock(LOCK)
SCHEMA = "1"
MAGIC = 0x31475357  # "WSG1" little-endian
BUNDLE_VERSION = 1
# Hand-held assets copied verbatim into the output. The political asset is no
# longer hand-written: it is built from the locked Natural Earth admin-0 layers
# (see run_build) and emitted as political-2026.wgeo.gz alongside the chunks.
COPIED_ASSETS = ("biome-probes.tsv", "NOTICE.md")

def dotnet_string(value):
    data=value.encode("utf-8")
    n=len(data); prefix=bytearray()
    while n>=0x80: prefix.append((n&0x7f)|0x80); n >>= 7
    prefix.append(n)
    return bytes(prefix)+data

def bundle_bytes(grid, records) -> bytes:
    """WSG1 chunk: header, buildId, tile count, then y-major/x-minor packed tiles."""
    from grid_build import TILE_DTYPE, TILE_RECORD_BYTES

    if records.dtype != TILE_DTYPE:
        raise LockError("tile records must use the WSG1 record layout")
    if records.dtype.itemsize != TILE_RECORD_BYTES:
        raise LockError(
            f"WSG1 record must stay {TILE_RECORD_BYTES} bytes, got {records.dtype.itemsize}"
        )
    if records.size != grid.cells:
        raise LockError(f"{grid.name}: expected {grid.cells} tiles, got {records.size}")
    header = bytearray(
        struct.pack("<iiBii", MAGIC, BUNDLE_VERSION, grid.lod_value, grid.width, grid.height)
    )
    header += dotnet_string(BUILD_ID)
    header += struct.pack("<i", grid.cells)
    return bytes(header) + records.tobytes()

def write_bundle(out: pathlib.Path, grid, records) -> pathlib.Path:
    path = out / f"{grid.name.lower()}-global.wgeo.gz"
    payload = bundle_bytes(grid, records)
    # mtime=0 and an empty stored name keep the gzip container byte-identical.
    with path.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as handle:
            handle.write(payload)
    return path

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def run_build(output: pathlib.Path, verify: bool = True) -> None:
    from grid_build import build_all
    import political
    import political_binary

    cache = cache_root(HERE)
    if verify:
        verify_cache(LOCK["sources"], cache)
    results = build_all(LOCK, cache)
    out = output
    out.mkdir(parents=True, exist_ok=True)
    chunks = []
    for result in results:
        path = write_bundle(out, result.grid, result.records)
        chunks.append(
            (result.grid.name.lower() + "-global", result.grid.name, path.name, sha(path))
        )
    # Political asset: deterministic derivative of the locked NE admin-0 layers.
    # Built once per build, gzip container with mtime=0 so two builds match bytes.
    asset = political.build_political(LOCK, cache, border_year=political.SUPPORTED_BORDER_YEAR)
    political_path = political_binary.write_political(out, asset, BUILD_ID)
    built_assets = [(political.ASSET_FILENAME, sha(political_path))]
    assets = []
    for name in COPIED_ASSETS:
        shutil.copyfile(HERE / name, out / name)
        assets.append((name, sha(out / name)))
    canonical = "\n".join(
        ["chunk|" + "|".join(chunk) for chunk in chunks]
        + ["asset|" + "|".join(asset) for asset in built_assets + assets]
    ).encode()
    manifest_checksum = hashlib.sha256(canonical).hexdigest()
    derivation = LOCK["derivation"]
    fidelity = derivation["currentBuildFidelity"]
    grids = derivation["outputGrids"]
    conversion_params = [
        ("projection", derivation["projection"]),
        ("pixelConvention", derivation["pixelConvention"]),
        ("longitudeRange", f"{derivation['longitudeRange'][0]},{derivation['longitudeRange'][1]}"),
        ("latitudeRange", f"{derivation['latitudeRange'][0]},{derivation['latitudeRange'][1]}"),
        ("lowGrid", f"{grids['Low']['width']}x{grids['Low']['height']}"),
        ("midGrid", f"{grids['Mid']['width']}x{grids['Mid']['height']}"),
        ("highGrid", f"{grids['High']['width']}x{grids['High']['height']}"),
        ("gzipMtime", str(derivation["gzipMtime"])),
        ("borderYear", str(political.SUPPORTED_BORDER_YEAR)),
    ]
    lines = [
        "# WorldSim geo-v1 offline derivative",
        "schemaVersion=" + SCHEMA,
        "buildId=" + BUILD_ID,
        "sourcesLockSha256=" + lock_sha256(LOCK),
        "fidelity=" + fidelity,
        "manifestChecksum=" + manifest_checksum,
    ]
    lines += ["conversion." + name + "=" + value for name, value in conversion_params]
    lines += ["chunk=" + "|".join(chunk) for chunk in chunks]
    lines += ["asset=" + "|".join(asset) for asset in built_assets + assets]
    (out / "manifest.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"built {BUILD_ID}")
    for result, chunk in zip(results, chunks):
        stats = result.stats
        size = (out / chunk[2]).stat().st_size
        print(
            f"  {result.grid.name}: {result.grid.width}x{result.grid.height} "
            f"bytes={size} land={stats['land']} coast={stats['coast']} "
            f"river={stats['river']} interpolated={stats['interpolated']} "
            f"elev=[{stats['elevationMin']},{stats['elevationMax']}] "
            f"classNeighbourFill={stats['classFilledByNeighbour']} "
            f"classLatitudeFill={stats['classFilledByLatitude']}"
        )
    print(
        f"  political: deFacto={len(asset.de_facto_countries)} "
        f"sovereignty={len(asset.sovereignty_claims)} "
        f"disputed={len(asset.disputed_areas)} cities={len(asset.cities)} "
        f"bytes={(out / political.ASSET_FILENAME).stat().st_size}"
    )

def _parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        description="WorldSim geo builder: fetch locked sources, build derivative bundles, verify cache, print buildId."
    )
    sub = ap.add_subparsers(dest="cmd")
    sub.add_parser("fetch", help="stream locked sources into tools/geo/.geo-cache after SHA-256 verification")
    build = sub.add_parser("build", help="write Low/Mid/High bundles from the verified cache")
    build.add_argument("--output", required=True)
    build.add_argument(
        "--skip-verify",
        action="store_true",
        help="skip re-hashing the cache before building (cache must already be verified)",
    )
    pol = sub.add_parser(
        "build-political",
        help="write only the political-2026.wgeo.gz asset from the locked NE admin-0 layers",
    )
    pol.add_argument("--output", required=True)
    pol.add_argument(
        "--border-year",
        type=int,
        default=2026,
        help="border snapshot year; only 2026 is supported (fail-closed otherwise)",
    )
    pol.add_argument(
        "--skip-verify",
        action="store_true",
        help="skip re-hashing the cache before building (cache must already be verified)",
    )
    sub.add_parser("verify", help="fail-closed check of cached archives against the lock")
    sub.add_parser("print-build-id", help="print buildId derived from canonical lock content")
    bootstrap = sub.add_parser("bootstrap", help="first-time pin of unpinned sources; refuses to overwrite sha256 without --force")
    bootstrap.add_argument("--force", action="store_true", help="re-pin even when sha256 is already recorded")
    pin = sub.add_parser("pin", help="alias for bootstrap")
    pin.add_argument("--force", action="store_true", help="re-pin even when sha256 is already recorded")
    return ap

def _normalize_argv(argv: list[str]) -> list[str]:
    commands = {"fetch", "build", "build-political", "verify", "print-build-id", "bootstrap", "pin"}
    if argv and argv[0] in commands:
        return argv
    if any(arg == "--output" or arg.startswith("--output=") for arg in argv):
        return ["build", *argv]
    return argv

def main(argv=None) -> int:
    if sys.version_info < (3, 10):
        print("Python 3.10+ is required", file=sys.stderr)
        return 2
    argv = _normalize_argv(list(sys.argv[1:] if argv is None else argv))
    ap = _parser()
    args = ap.parse_args(argv)
    cmd = args.cmd
    if cmd is None:
        ap.print_help()
        return 2
    try:
        if cmd == "pin":
            cmd = "bootstrap"
        if cmd == "print-build-id":
            print(build_id_from_lock(load_lock(lock_path(HERE))))
            return 0
        if cmd == "bootstrap":
            lock_file = lock_path(HERE)
            cache = cache_root(HERE)
            lock = load_lock(lock_file)
            force = bool(getattr(args, "force", False))
            results = bootstrap_all(lock["sources"], cache, lock_file=lock_file, force=force)
            for item in results:
                action = "pinned" if item.get("wrotePin") else "verified"
                print(f"{action} {item['id']} sha256={item['sha256']} bytes={item['bytes']}")
            lock = load_lock(lock_file)
            wrote_any = any(item.get("wrotePin") for item in results)
            if force or wrote_any:
                koppen = next(src for src in lock["sources"] if src["id"] == "koppen-geiger-v3-1991-2020")
                info = inspect_koppen_1991_2020(
                    source_archive_path(koppen, cache),
                    extract_dir(koppen, cache),
                )
                raw_nodata = info["nodata"]
                parsed_nodata: int | float | str | None = raw_nodata
                if isinstance(raw_nodata, str) and raw_nodata:
                    try:
                        parsed_nodata = float(raw_nodata) if "." in raw_nodata else int(raw_nodata)
                    except ValueError:
                        parsed_nodata = raw_nodata
                nodata_source = (
                    "GeoTIFF GDAL_NODATA tag 42113 on the extracted 1991-2020 classification layer after bootstrap"
                    if raw_nodata is not None
                    else "GDAL_NODATA tag 42113 absent on the extracted 1991-2020 classification GeoTIFF"
                )
                lock = update_source_fields(
                    lock_file,
                    "koppen-geiger-v3-1991-2020",
                    {
                        "selectedLayers": [info["selectedLayer"]],
                        "selectedLayersNote": (
                            "Confirmed from koppen_geiger_tif.zip member list after bootstrap. "
                            "1991-2020 classification GeoTIFFs: "
                            + ", ".join(info["classificationLayers"])
                        ),
                        "nodata": parsed_nodata,
                        "nodataSource": nodata_source,
                        "observedClassificationLayers": info["classificationLayers"],
                    },
                )
                print(
                    "koppen layer="
                    + info["selectedLayer"]
                    + " nodata="
                    + repr(parsed_nodata)
                )
            paths = verify_cache(lock["sources"], cache)
            print("verified " + ", ".join(str(path) for path in paths))
            print("buildId=" + build_id_from_lock(lock))
            return 0
        if cmd == "fetch":
            lock = load_lock(lock_path(HERE))
            paths = fetch_all(lock["sources"], cache_root(HERE))
            print("fetched " + ", ".join(str(path) for path in paths))
            return 0
        if cmd == "verify":
            lock = load_lock(lock_path(HERE))
            paths = verify_cache(lock["sources"], cache_root(HERE))
            print("verified " + ", ".join(str(path) for path in paths))
            return 0
        if cmd == "build":
            from grid_build import BuildError

            try:
                run_build(pathlib.Path(args.output), verify=not args.skip_verify)
            except BuildError as exc:
                print(str(exc), file=sys.stderr)
                return 1
            return 0
        if cmd == "build-political":
            import political
            import political_binary
            from political import PoliticalError

            try:
                cache = cache_root(HERE)
                if not args.skip_verify:
                    verify_cache(LOCK["sources"], cache)
                asset = political.build_political(LOCK, cache, border_year=args.border_year)
                out = pathlib.Path(args.output)
                out.mkdir(parents=True, exist_ok=True)
                path = political_binary.write_political(out, asset, BUILD_ID)
                print(f"built {BUILD_ID}")
                print(
                    f"  political: borderYear={asset.border_year} "
                    f"deFacto={len(asset.de_facto_countries)} "
                    f"sovereignty={len(asset.sovereignty_claims)} "
                    f"disputed={len(asset.disputed_areas)} cities={len(asset.cities)} "
                    f"bytes={path.stat().st_size}"
                )
            except (PoliticalError, FetchError, LockError) as exc:
                print(str(exc), file=sys.stderr)
                return 1
            return 0
    except (FetchError, LockError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    ap.print_help()
    return 2

if __name__=="__main__":
    raise SystemExit(main())
