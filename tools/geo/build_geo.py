#!/usr/bin/env python3
"""Build WorldSim's offline geo-v1 bundles from auditable fixed real-Earth samples.

This intentionally produces a simplified derivative, not a complete copy of any source
dataset. Replace SAMPLE_* tables with downloaded source adapters when full data is available.
"""
from __future__ import annotations
import argparse, gzip, hashlib, math, pathlib, shutil, struct

BUILD_ID = "geo-v1-simplified-real-samples-20260813"
SCHEMA = "1"

# Coarse public-domain land-outline samples derived from Natural Earth concepts.
# These are intentionally sparse and unsuitable for navigation/cartography.
LAND_POLYGONS = [
    [(-168,72),(-140,70),(-125,55),(-115,50),(-95,50),(-82,25),(-98,15),(-120,25),(-130,45),(-168,60)], # N America
    [(-82,12),(-72,8),(-50,5),(-35,-5),(-48,-55),(-70,-55),(-80,-15)],                                  # S America
    [(-17,37),(10,37),(35,32),(50,12),(42,-35),(18,-35),(5,-5),(-17,15)],                                # Africa
    [(-10,36),(0,58),(25,72),(80,75),(180,65),(160,45),(145,35),(130,20),(105,5),(75,8),(55,28),(35,40)], # Eurasia
    [(112,-10),(154,-10),(153,-43),(115,-36)],                                                            # Australia
    [(-73,60),(-20,60),(-18,83),(-55,84)],                                                                # Greenland
    [(-180,-72),(180,-72),(180,-90),(-180,-90)],                                                          # Antarctica
    [(95,5),(141,6),(145,-10),(115,-10)],                                                                 # Maritime SE Asia
    [(-140,55),(-60,55),(-60,75),(-140,75)],                                                              # Canadian north
    [(35,12),(60,12),(58,30),(35,30)],                                                                     # Arabian peninsula
]

MOUNTAINS = [ # lon, lat, peak metres, spread degrees
    (86,28,6200,8),(-73,-20,4800,3.5),(-72,-40,3200,7),(-110,45,3000,9),
    (38,9,3000,5),(8,46,2500,3),(140,36,1800,3),(147,-6,2800,4)
]
DESERTS = [(15,24,18),(48,24,12),(102,42,8),(134,-25,16),(-70,-23,7),(-112,34,8)]
RAINFORESTS = [(-62,-4,18),(23,0,11),(113,1,14),(147,-6,7)]
RIVERS = [ # lon1,lat1,lon2,lat2,width degrees (coarse corridors)
    (31,-2,31,31,1.5),(-72,-5,-50,-2,1.8),(104,31,121,31,1.4),
    (-95,47,-90,29,1.3),(78,30,90,23,1.5),(44,33,48,30,1.2)
]

BIOME = {"Ocean":0,"Ice":1,"Tundra":2,"BorealForest":3,"TemperateForest":4,
         "Grassland":5,"Desert":6,"Savanna":7,"TropicalRainforest":8,"Alpine":9,"Wetland":10}
CLIMATE = {"Polar":0,"Subpolar":1,"Temperate":2,"Arid":3,"Subtropical":4,"Tropical":5,"Highland":6}

def inside(lon, lat, polygon):
    hit = False
    j = len(polygon)-1
    for i, (xi, yi) in enumerate(polygon):
        xj, yj = polygon[j]
        if ((yi > lat) != (yj > lat)) and lon < (xj-xi)*(lat-yi)/(yj-yi+1e-12)+xi:
            hit = not hit
        j = i
    return hit

def land(lon, lat):
    # Coarse Mediterranean water cutout corrects the Africa/Eurasia envelope overlap.
    mediterranean = [(-6,31),(10,35),(35,31),(35,38),(10,44),(-6,37)]
    if inside(lon, lat, mediterranean):
        return False
    return any(inside(lon, lat, p) for p in LAND_POLYGONS)

def distance(lon, lat, clon, clat):
    return math.hypot((lon-clon)*math.cos(math.radians((lat+clat)/2)), lat-clat)

def segment_distance(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay
    t = max(0, min(1, ((px-ax)*dx+(py-ay)*dy)/(dx*dx+dy*dy+1e-9)))
    return math.hypot(px-(ax+t*dx), py-(ay+t*dy))

def elevation(lon, lat, is_land):
    if not is_land:
        return -4000
    value = 180 + 180*math.cos(math.radians(lat))
    for x,y,peak,spread in MOUNTAINS:
        value += peak*math.exp(-(distance(lon,lat,x,y)/spread)**2)
    return max(-11000, min(8800, round(value)))

def climate(lon, lat, elev):
    a = abs(lat)
    if elev >= 2500: return "Highland"
    if a >= 75: return "Polar"
    if a >= 58: return "Subpolar"
    if any(distance(lon,lat,x,y) <= r for x,y,r in DESERTS): return "Arid"
    if a <= 23.5: return "Tropical"
    if a <= 35: return "Subtropical"
    return "Temperate"

def rainfall(lon, lat, climate_name):
    rain = {"Polar":180,"Subpolar":450,"Temperate":800,"Arid":180,
            "Subtropical":650,"Tropical":1150,"Highland":700}[climate_name]
    if any(distance(lon,lat,x,y) <= r for x,y,r in RAINFORESTS): rain = 2200
    return rain

def biome(is_land, climate_name, elev, lat, rain):
    if not is_land: return "Ocean"
    if elev >= 3000: return "Alpine"
    if climate_name == "Polar": return "Ice"
    if climate_name == "Subpolar": return "Tundra" if abs(lat)>=67 else "BorealForest"
    if climate_name == "Arid": return "Desert" if rain<350 else "Grassland"
    if climate_name == "Tropical": return "TropicalRainforest" if rain>=1600 else "Savanna"
    if climate_name == "Subtropical": return "Desert" if rain<500 else "TemperateForest"
    if climate_name == "Highland": return "Alpine"
    return "TemperateForest" if rain>=700 else "Grassland"

def river(lon, lat, is_land):
    return is_land and any(segment_distance(lon,lat,*r[:4]) <= r[4] for r in RIVERS)

def sample(lon, lat, step):
    is_land = land(lon,lat)
    elev = elevation(lon,lat,is_land)
    clim = climate(lon,lat,elev)
    rain = rainfall(lon,lat,clim)
    near_water = not is_land
    coast = is_land and any(not land(lon+dx,lat+dy) for dx,dy in ((step,0),(-step,0),(0,step),(0,-step)))
    has_river = river(lon,lat,is_land)
    max_diff = 0
    if is_land:
        for dx,dy in ((step,0),(-step,0),(0,step),(0,-step)):
            max_diff=max(max_diff,abs(elev-elevation(lon+dx,lat+dy,land(lon+dx,lat+dy))))
    slope=min(25.5,max_diff/max(1,111000*step)*100)
    temp=28-abs(lat)*0.55-elev*0.0065 if is_land else 20-abs(lat)*0.35
    flags=(1 if is_land else 0)|(2 if coast else 0)|(4 if near_water or coast or has_river else 0)|(8 if has_river else 0)
    return flags, BIOME[biome(is_land,clim,elev,lat,rain)], CLIMATE[clim], elev, round(slope*10), round(temp*10), rain

def dotnet_string(value):
    data=value.encode("utf-8")
    n=len(data); prefix=bytearray()
    while n>=0x80: prefix.append((n&0x7f)|0x80); n >>= 7
    prefix.append(n)
    return bytes(prefix)+data

def build_bundle(out, lod_name, lod_value, width):
    height=width//2; step=360/width
    raw=bytearray(struct.pack("<iiBii",0x31475357,1,lod_value,width,height))
    raw += dotnet_string(BUILD_ID)
    raw += struct.pack("<i",width*height)
    for y in range(height):
        lat=90-(y+.5)*180/height
        for x in range(width):
            lon=-180+(x+.5)*360/width
            flags,b,c,e,s,t,r=sample(lon,lat,step)
            raw += struct.pack("<BBBhBhH",flags,b,c,e,s,t,r)
    path=out/f"{lod_name.lower()}-global.wgeo.gz"
    with gzip.GzipFile(filename="", mode="wb", fileobj=path.open("wb"), mtime=0) as f: f.write(raw)
    return path

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--output", required=True)
    args=ap.parse_args(); out=pathlib.Path(args.output); out.mkdir(parents=True,exist_ok=True)
    chunks=[]
    for name,value,width in (("Low",2,180),("Mid",1,360),("High",0,720)):
        path=build_bundle(out,name,value,width)
        chunks.append((name.lower()+"-global",name,path.name,sha(path)))
    here=pathlib.Path(__file__).resolve().parent
    assets=[]
    for name in ("political-2026.tsv","biome-probes.tsv","NOTICE.md"):
        shutil.copyfile(here/name,out/name)
        assets.append((name,sha(out/name)))
    canonical="\n".join(
        ["chunk|"+"|".join(c) for c in chunks] +
        ["asset|"+"|".join(a) for a in assets]).encode()
    manifest_checksum=hashlib.sha256(canonical).hexdigest()
    lines=["# WorldSim geo-v1 offline derivative","schemaVersion="+SCHEMA,"buildId="+BUILD_ID,
           "fidelity=simplified-real-earth-fixed-samples-not-full-source",
           "manifestChecksum="+manifest_checksum]
    lines += ["chunk="+"|".join(c) for c in chunks]
    lines += ["asset="+"|".join(a) for a in assets]
    (out/"manifest.txt").write_text("\n".join(lines)+"\n",encoding="utf-8")
    print(f"built {BUILD_ID}: "+", ".join(f"{name}={(out/name).stat().st_size}" for _,_,name,_ in chunks))

if __name__=="__main__": main()
