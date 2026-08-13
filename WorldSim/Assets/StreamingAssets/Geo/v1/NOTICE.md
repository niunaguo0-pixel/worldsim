# Geo v1 data notice

`geo-v1-simplified-real-samples-20260813` is an offline-reproducible, deliberately
simplified real-Earth derivative. It is **not** a complete or cartographically accurate
copy of the upstream datasets.

Allowed upstream references:

- Natural Earth physical/cultural data — public domain:
  https://www.naturalearthdata.com/about/terms-of-use/
- ETOPO 2022 global relief — CC0:
  https://www.ncei.noaa.gov/products/etopo-global-relief-model
- Köppen-Geiger climate classification — CC BY 4.0:
  https://www.gloh2o.org/koppen/
- Glottolog language-family classification — CC BY 4.0:
  https://glottolog.org/meta/downloads

This repository does not commit raw upstream archives. `build_geo.py` uses sparse,
auditable fixed samples for continent envelopes, representative terrain/climate features,
and a partial 2026 political/city seed. The result is suitable only for the WorldSim MVP.
Replace the sample tables through the same builder interface before claiming full global
coverage. Attribution for CC BY material must remain in redistributed builds.
