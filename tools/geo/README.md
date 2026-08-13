# WorldSim Geo v1 builder

Rebuild the committed derivative with Python 3:

```powershell
python tools/geo/build_geo.py --output WorldSim/Assets/StreamingAssets/Geo/v1
```

The build is deterministic: gzip timestamps are fixed, tile traversal is stable, and the
manifest pins every bundle SHA-256. Raw downloads belong in `tools/geo/.geo-cache/` and are
ignored by Git. The current build uses the fixed simplified real-Earth samples embedded in
the builder because complete upstream archives were not fetched in this environment.

Outputs:

- `low-global.wgeo.gz` — 180×90
- `mid-global.wgeo.gz` — 360×180
- `high-global.wgeo.gz` — 720×360
- `manifest.txt`, `political-2026.tsv`, `biome-probes.tsv`, `NOTICE.md`

All three files are global grids. Runtime synchronously retains global Low and only the
configured start-region subset of High; Mid/other presentation chunks may be cached
asynchronously without changing simulation state.
