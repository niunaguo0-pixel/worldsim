# Geo v1 data notice

WorldSim geo-v1 is an offline-reproducible derivative built from the locked upstream
archives below. It is a heavily reduced 2° / 1° / 0.5° resampling: **not** a complete
or cartographically accurate copy of those archives, and not for navigation or legal
boundary determination.

Baseline temperature and annual rainfall in the bundle are **derived proxies**, not
upstream measurements — no temperature or precipitation source is locked. See
"Derived climate proxies" below.

`buildId` is `geo-v1-` plus the first 16 hex characters of SHA-256 of the canonical
`tools/geo/sources.lock.json` (sorted keys, compact JSON, sources sorted by id).
`sha256Pin.date` and similar non-content pin timestamps are excluded from that
digest; each source's SHA-256 remains included, so re-pinning the same bytes does
not change `buildId`.

Raw upstream objects stay in `tools/geo/.geo-cache/` and are Git-ignored. `fetch`
refuses to download or extract unless the lock lists SHA-256 and the bytes match.
Authorities did not publish SHA-256 for these files; the lock records that gap
explicitly instead of inventing digests. Byte lengths below were taken from
authoritative HTTPS HEAD/API responses on 2026-08-13.

## Natural Earth v5.1.2 — public domain

Terms: https://www.naturalearthdata.com/about/terms-of-use/

Versioned NACIS CDN objects (CRS EPSG:4326, vector shapefiles, NoData n/a):

- land — https://naciscdn.org/naturalearth/5.1.2/10m/physical/ne_10m_land.zip (3269070 bytes)
- coastline — https://naciscdn.org/naturalearth/5.1.2/10m/physical/ne_10m_coastline.zip (3069451 bytes)
- lakes — https://naciscdn.org/naturalearth/5.1.2/10m/physical/ne_10m_lakes.zip (2349685 bytes)
- rivers — https://naciscdn.org/naturalearth/5.1.2/10m/physical/ne_10m_rivers_lake_centerlines.zip (2079507 bytes)
- admin-0 (de facto countries) — https://naciscdn.org/naturalearth/5.1.2/10m/cultural/ne_10m_admin_0_countries.zip (4930492 bytes)
- admin-0 sovereignty — https://naciscdn.org/naturalearth/5.1.2/10m/cultural/ne_10m_admin_0_sovereignty.zip (4906592 bytes)
- populated places — https://naciscdn.org/naturalearth/5.1.2/10m/cultural/ne_10m_populated_places.zip (2811199 bytes)
- disputed areas — https://naciscdn.org/naturalearth/5.1.2/10m/cultural/ne_10m_admin_0_disputed_areas.zip (215221 bytes)

Modification statement: the build rasterizes `land` minus `lakes` to the land/water
flags by even-odd point-in-polygon at each cell centre, and marks coast and river
flags from the `coastline` and `rivers_lake_centerlines` polylines by cell
intersection. The political asset (`political-2026.wgeo.gz`) is a deterministic
derivative of the `admin_0_countries`, `admin_0_sovereignty`,
`admin_0_disputed_areas` and `populated_places` layers: it preserves the source
polygon geometry and attributes verbatim, emits a DeFactoControl view (de-facto
admin-0) and a SovereigntyClaims view (sovereignty attribution), and carries
disputed areas with their source claimant/sovereign fields. No adjudication is
added — Natural Earth does not take sides, and neither does this builder.
Disputed source attributes are retained and no extra-legal
adjudication is added. Natural Earth SHA-256 is not published.

## ETOPO 2022 — CC0 1.0

Product page: https://www.ncei.noaa.gov/products/etopo-global-relief-model
DOI: https://doi.org/10.25921/fd45-gt74
License: https://creativecommons.org/publicdomain/zero/1.0/

Ice-surface 60 arc-second GeoTIFF:

https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/60s/60s_surface_elev_gtif/ETOPO_2022_v1_60s_N90W180_surface.tif
(465969062 bytes; CRS EPSG:4326; vertical EGM2008 / EPSG:3855; GDAL_NODATA tag
42113 = -99999, read from the cached GeoTIFF itself)

NOAA waives copyright worldwide through CC0. Modification statement: the build
reduces the 60 arc-second grid to Low 2° / Mid 1° / High 0.5° cell-centre grids by
taking the integer-metre mean of the source pixels covering each output cell, stores
the result as int16 metres, and derives from it a slope angle in tenths of a degree
(`atan` of the steepest orthogonal rise over run between cell centres, clamped to
25.5°). Both are cell aggregates, not point measurements. NOAA SHA-256 is not
published.

## Köppen-Geiger V3 1991–2020 — CC BY 4.0

Landing page: https://www.gloh2o.org/koppen/
Archive: figshare 21789074.v2 file 61012822 `koppen_geiger_tif.zip`
https://ndownloader.figshare.com/files/61012822
(130618411 bytes; published MD5 7fc2f5a15d4f5fe0ce59c9a9b502aa09;
independently pinned SHA-256 bb84453d4541f1a0bc5a804ead83f483c19ce70f16a5197f6d3a7b6a63e65562)
DOI: https://doi.org/10.6084/m9.figshare.21789074.v2
License: https://creativecommons.org/licenses/by/4.0/

Confirmed inner 1991–2020 classification GeoTIFF (from the zip member list after bootstrap):
`1991_2020/koppen_geiger_0p5.tif` (also present: `0p00833333`, `0p1`, `1p0`).
GeoTIFF GDAL_NODATA tag 42113 = 0.

Required attribution:

Beck, H.E., T.R. McVicar, N. Vergopolan, A. Berg, N.J. Lutsko, A. Dufour, Z. Zeng,
X. Jiang, A.I.J.M. van Dijk, D.G. Miralles. High-resolution (1 km) Köppen-Geiger
maps for 1901–2099 based on constrained CMIP6 projections, Scientific Data 10, 724,
doi:10.1038/s41597-023-02549-6 (2023).

Modification statement: the build extracts the 1991–2020 classification layer,
majority-resamples it onto the 2° / 1° / 0.5° grids (ties broken to the lowest class
code), and maps the 30 class codes onto the existing `ClimateZone` / `BiomeType`
enumerations through an explicit table. Class values themselves are not otherwise
altered. Redistributed builds must keep this attribution.

## Derived climate proxies — not upstream data

The locked Köppen archive contains classifications only, and WorldSim locks no
temperature or precipitation product. The bundle's baseline temperature (0.1 °C) and
annual rainfall (mm) are computed from the Köppen class plus bounded latitude and
elevation corrections against per-class reference points, with all constants recorded
in `tools/geo/koppen.py`. They are deterministic climatological stand-ins for
simulation. Do not cite, redistribute or display them as measured climate data, and
do not attribute them to GloH2O, NOAA or Natural Earth.

## Commands

```powershell
python tools/geo/build_geo.py print-build-id
python tools/geo/build_geo.py fetch
python tools/geo/build_geo.py verify
python tools/geo/build_geo.py build --output WorldSim/Assets/StreamingAssets/Geo/v1
python tools/geo/build_geo.py build-political --output build/geo-political
```

`bootstrap` / `bootstrap --force` are first-time pin or explicit re-pin only, not the daily path.

Glottolog is not part of this lock.
