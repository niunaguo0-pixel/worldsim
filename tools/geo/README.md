# WorldSim geo builder

Python 3.10+. `fetch` / `verify` / `bootstrap` / `print-build-id` are standard
library only; `build` also needs `numpy` (see `requirements.txt`):

```powershell
python -m pip install -r tools/geo/requirements.txt
```

Commands:

```powershell
python tools/geo/build_geo.py print-build-id
python tools/geo/build_geo.py fetch
python tools/geo/build_geo.py verify
python tools/geo/build_geo.py build --output WorldSim/Assets/StreamingAssets/Geo/v1
python tools/geo/build_geo.py build-political --output build/geo-political
```

`print-build-id` hashes the canonical contents of `sources.lock.json` (sorted keys, compact JSON, sources sorted by id). `sha256Pin.date` is excluded so re-pinning the same bytes does not change `buildId`; source SHA-256 values remain in the digest. `build-political` writes only `political-2026.wgeo.gz` (no raster chunks) and accepts `--border-year` (only 2026 is supported; any other year fails closed).

## Cache, bootstrap/pin, and fail-closed fetch

Daily work uses `fetch` / `verify` against the already-pinned lock. `bootstrap` (alias `pin`) is **first-time only**: unpinned sources, or an explicit `bootstrap --force` re-pin. If `sha256` is already a valid 64-hex digest, default `bootstrap` only verifies cache/lock and refuses to overwrite the pin (including pin date).

`bootstrap` without an existing digest:

1. HEADs the locked HTTPS URL and refuses to continue if the status is not 200 or `Content-Length` disagrees with the locked byte length
2. Downloads only into `tools/geo/.geo-cache/`
3. Verifies the locked byte length; Köppen also verifies the Figshare-published MD5
4. Computes SHA-256 of those exact bytes and atomically updates `sources.lock.json` with the digest plus pin date/method
5. Inspects the Köppen zip for the real 1991–2020 classification GeoTIFF path and GDAL_NODATA tag

Ordinary `fetch` / `verify` still fail closed when `sha256` is null (`sha256BlockFetch` true). After pin, `sha256BlockFetch` is false and fetch is allowed. Production fetch streams by locked `bytes` and fails immediately if the stream would exceed that length; it never `response.read()`s the whole body into memory. Zip members that are symlinks or other special files, or that use `..` / absolute paths / destinations outside the extract directory, are refused member-by-member (no `extractall`). A cache hit that already matches the lock is reused. A mismatched cache fails closed. HTTP 206 resume requires `Content-Range` whose start equals already-downloaded bytes and whose complete size matches the lock.

Do not invent digests. If an authoritative URL 404s or its length changes, stop and correct the lock from the official landing page.

## Current build fidelity

`build` reads the verified `.geo-cache` only; the embedded `SAMPLE_*` tables are gone. Output grids are Low 2° (180×90), Mid 1° (360×180), High 0.5° (720×360), all equirectangular cell centres traversed y-major then x-minor. gzip timestamps stay `mtime=0`. Running `build` emits a lock-derived `buildId` and `sourcesLockSha256`; do not overwrite the committed StreamingAssets bundle until a later task regenerates it together with the C# and CI assertions.

Legacy `python tools/geo/build_geo.py --output <dir>` still maps to `build`.

| Output field | Source | How |
| --- | --- | --- |
| land / water flags | NE 1:10m `land`, `lakes` | even-odd point-in-polygon at the cell centre; lakes count as water |
| coast flag | NE 1:10m `coastline` | land cell crossed by a coastline segment, or orthogonally adjacent to water |
| river flag | NE 1:10m `rivers_lake_centerlines` | land cell crossed by a river centreline |
| elevation (int16 m) | ETOPO 2022 60″ ice surface | integer-metre mean of the covered source pixels |
| slope (0.1°) | derived from elevation | `atan(rise / run)` to the steepest orthogonal neighbour, clamped to 25.5° |
| climate zone, biome | Köppen-Geiger V3 1991–2020 | majority class per cell (ties to the lowest code), then the explicit table in `koppen.py` |
| baseline temperature, rainfall | **derived proxies** | class + latitude + elevation, see below |

**Water cells have no upstream climate at all.** Köppen classifies land and leaves water as NoData, and no ocean temperature/precipitation source is locked, so climate zone, temperature and rainfall on every non-land cell are synthetic latitude bands invented by the builder (`OCEAN_CLIMATE_BANDS` / `OCEAN_RAINFALL_BANDS` in `koppen.py`, see `WATER_CLIMATE_IS_SYNTHETIC`). They are flagged `IsInterpolated` so they are not mistaken for upstream data; water elevation does remain sourced from ETOPO.

Locked sources (see `sources.lock.json` and `NOTICE.md`):

- Natural Earth v5.1.2 1:10m land / coastline / lakes / rivers / admin-0 countries / admin-0 sovereignty / populated places / disputed areas
- NOAA ETOPO 2022 60 arc-second ice-surface GeoTIFF
- GloH2O Köppen-Geiger V3 1991–2020 classification (figshare 21789074.v2 `koppen_geiger_tif.zip`)

Glottolog is out of scope.

## Temperature and rainfall are derived, not sourced

The locked Köppen product ships **classifications only** — it contains no temperature or precipitation raster, and no temperature/precipitation source is locked. `baseline_temperature_c` and `annual_rainfall_mm` are therefore deterministic climatological *proxies*: each Köppen class carries a reference annual mean temperature, reference absolute latitude, reference elevation and reference annual rainfall, and a cell's value is the class reference plus bounded latitude and elevation corrections. All constants live in `koppen.py` next to the class table. Treat these two fields as plausible stand-ins for simulation, never as measured climate data.

Slope is an **angle in tenths of a degree** (`WorldMapBundleReader` divides the byte by 10), computed between cell centres. Because a 0.5° cell is ~55 km wide, the cell-mean terrain is gentle: the steepest cell on the current cache is about 8.5° (about 7.1° on land), so gameplay thresholds written against the old sampler (`Slope > 18`, `>= 20`) no longer trigger. The old embedded sampler was no better — its steepest cell was about 13.4°, also below 18/20 — so this is not a regression introduced by the real-source build; at this resolution slope has little dynamic range and the "too steep to settle" / "mountain natural border" rules are in practice carried by the elevation branches (`Highland` climate, `Alpine` biome). Raising the slope signal requires either a sub-cell roughness measure computed on the native 60″ grid or lower thresholds; both are out of scope here and belong with the C#/CI update.

## Missing upstream values

A cell whose upstream value is NoData is filled from the nearest valid cell using a fixed Chebyshev-ring order (bounded to 4°), then by a fixed latitude-band fallback, and gets `IsInterpolated`. Cells with a valid source pixel are never touched by a fallback formula. On the current cache, ETOPO covers every cell and only 2 High-grid land cells (tiny islands below the 0.5° Köppen grid) need a class fill.

## Readers

`build` needs no GDAL. Two in-tree readers cover exactly what the locked sources use, and fail closed otherwise:

- `tiff_tags.py` — standard-library (Big)TIFF IFD-0 tag reader, so `fetch`/`bootstrap` can inspect GeoTIFF metadata without numpy.
- `geotiff.py` — block-wise pixel decoding: strips or tiles, 1 sample/pixel, contiguous planar config, compression none/LZW/Deflate, predictor none/horizontal/floating-point, `ModelPixelScale` + `ModelTiepoint` geographic transform. Blocks are decoded one at a time, so the 466 MB ETOPO GeoTIFF is never held in memory.
- `shapefile_reader.py` — `.shp` geometry only (Null/Point/PolyLine/Polygon and their Z/M variants, Z/M arrays skipped). `.dbf` attributes are read by the political build through `dbf_reader.py`.

## Political asset (political-2026.wgeo.gz)

`build` also emits `political-2026.wgeo.gz`, a deterministic derivative of the
locked Natural Earth v5.1.2 1:10m admin-0 layers. It replaces the old
hand-written `political-2026.tsv`. The container is a gzip-compressed
self-describing binary `WSP1` (WorldSim Political v1); see `political_binary.py`
for the byte layout. It is independent of the WSG1 raster chunks and carries
its own `buildId` and `borderYear` in the header.

Contents (all sorted by `(stable_id, name)` so two builds are byte-identical):

- **DeFactoControl** view — one record per de-facto admin-0 unit from
  `ne_10m_admin_0_countries` (258 records), real multipolygon geometry, tagged
  with `deFactoAdminId` (ADM0_A3) and `sovereignId` (SOV_A3). `stableId` is
  ADM0_A3, the unique key per de-facto unit (ISO_A3_EH is NOT unique across NE
  admin-0 — e.g. BRA is shared by Brazil and Brazilian Islands — so it is
  carried as an attribute, not the stable id).
- **SovereigntyClaims** view — one record per sovereignty from
  `ne_10m_admin_0_sovereignty` (209 records), real multipolygon geometry;
  `stableId` is SOV_A3.
- **Disputed areas** — `ne_10m_admin_0_disputed_areas` (99 records) emitted
  verbatim with source `TYPE` / `NOTE_ADM0` / `NOTE_BRK` and claimant
  `ADMIN` / `SOVEREIGNT` / `ADM0_A3` / `SOV_A3`. No adjudication is fabricated:
  Natural Earth does not take sides, and neither does this builder.
- **Cities** — all populated places from `ne_10m_populated_places` (7342
  records) with name, coordinates, population, capital/world/mega flags and
  admin/sovereign attribution. `stableId` is the NE_ID.

The 2026 snapshot constraint is enforced: `border_year` must be exactly 2026.
Natural Earth current data is a 2026 snapshot, not a historical border product;
any other year fails closed with a clear message until later tasks add
historical snapshots. The manifest lists the asset with its SHA-256.

## Local verification

```powershell
python -m unittest discover -s tools/geo -p "test_*.py"
python tools/geo/build_geo.py print-build-id
python tools/geo/build_geo.py fetch
python tools/geo/build_geo.py verify
```

Tests that need the populated `.geo-cache` (full-planet build, byte-for-byte
determinism, real raster headers) are skipped unless the cache is present and
`WORLDSIM_GEO_SLOW_TESTS=1`:

```powershell
$env:WORLDSIM_GEO_SLOW_TESTS = "1"
python -m unittest discover -s tools/geo -p "test_*.py"
```

To confirm determinism by hand, build twice into different directories and
compare `manifest.txt` — it lists the SHA-256 of every chunk and asset:

```powershell
python tools/geo/build_geo.py build --output build/geo-a
python tools/geo/build_geo.py build --output build/geo-b --skip-verify
Compare-Object (Get-Content build/geo-a/manifest.txt) (Get-Content build/geo-b/manifest.txt)
```

First-time pin (or intentional re-pin) only:

```powershell
python tools/geo/build_geo.py bootstrap
python tools/geo/build_geo.py bootstrap --force
```
