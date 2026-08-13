"""Serialize / deserialize the WSP1 political asset binary format.

Layout (all integers little-endian, gzip container with mtime=0 and no name so
two builds are byte-identical):

::

    magic           u32     = 0x31505357 ("WSP1")
    version         u8      = 1
    borderYear      u16     = 2026
    buildId         dotnet  length-prefixed UTF-8
    nDeFacto        u32
    nSovereignty    u32
    nDisputed       u32
    nCities         u32
    de_facto_countries   (sorted by stable_id then name)
    sovereignty_claims   (sorted by stable_id then name)
    disputed_areas       (sorted by stable_id then name)
    cities               (sorted by stable_id then name)

A ``dotnet`` string is a 7-bit variable-length prefix (little-endian, as written
by .NET ``BinaryWriter.Write(string)``) followed by UTF-8 bytes; this matches
the convention already used for the WSG1 raster bundle. A 3-char admin id is
stored as 3 raw ASCII bytes (NE codes are always 3 chars).

Country / sovereignty record (same shape):

::

    stableId     3 bytes
    adminId      3 bytes
    sovereignId 3 bytes
    isoA3Eh      3 bytes
    name         dotnet
    nameLong     dotnet
    sovereignName dotnet
    continent    dotnet
    regionUn     dotnet
    subregion    dotnet
    featureClass dotnet
    type         dotnet
    noteAdm0     dotnet
    wikidataId   dotnet
    popEst       i64
    ringCount    u32
    rings        (for each: pointCount u32, then pointCount*(f64 lon, f64 lat))

Disputed record:

::

    stableId     3 bytes   (BRK_A3)
    adminId      3 bytes
    sovereignId 3 bytes
    isoA3Eh      3 bytes
    name         dotnet
    nameLong     dotnet
    adminName    dotnet
    sovereignName dotnet
    type         dotnet
    noteAdm0     dotnet
    noteBrk      dotnet
    wikidataId   dotnet
    popEst       i64
    ringCount    u32
    rings

City record:

::

    stableId     i64   (NE_ID)
    name         dotnet
    nameAscii    dotnet
    featureClass dotnet
    adminId      3 bytes
    sovereignId 3 bytes
    adminName    dotnet
    sovereignName dotnet
    scalerank    i32
    natScale     i32
    isCapital    u8
    isWorldCity  u8
    isMegaCity   u8
    popMax       i64
    popMin       i64
    longitude    f64
    latitude     f64
    wikidataId   dotnet
"""
from __future__ import annotations

import gzip
import struct
from pathlib import Path

import political
from political import (
    CityRecord,
    CountryRecord,
    DisputedRecord,
    PoliticalAsset,
    PoliticalError,
    FORMAT_VERSION,
    MAGIC,
    SUPPORTED_BORDER_YEAR,
)


def _dotnet_string(value: str) -> bytes:
    data = value.encode("utf-8")
    n = len(data)
    prefix = bytearray()
    while n >= 0x80:
        prefix.append((n & 0x7F) | 0x80)
        n >>= 7
    prefix.append(n)
    return bytes(prefix) + data


def _read_dotnet_string(buf: bytes, pos: int) -> tuple[str, int]:
    n = 0
    shift = 0
    while True:
        b = buf[pos]
        pos += 1
        n |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    text = buf[pos:pos + n].decode("utf-8")
    return text, pos + n


def _fixed3(value: str) -> bytes:
    if len(value) != 3:
        raise PoliticalError(f"expected a 3-character id, got {value!r}")
    return value.encode("ascii")


def _read_fixed3(buf: bytes, pos: int) -> tuple[str, int]:
    return buf[pos:pos + 3].decode("ascii"), pos + 3


def _write_rings(out: bytearray, rings: tuple) -> None:
    out += struct.pack("<I", len(rings))
    for ring in rings:
        pts = ring.points
        out += struct.pack("<I", len(pts))
        for lon, lat in pts:
            out += struct.pack("<dd", lon, lat)


def _read_rings(buf: bytes, pos: int) -> tuple[tuple, int]:
    (count,) = struct.unpack_from("<I", buf, pos)
    pos += 4
    rings = []
    for _ in range(count):
        (npts,) = struct.unpack_from("<I", buf, pos)
        pos += 4
        pts = []
        for _ in range(npts):
            lon, lat = struct.unpack_from("<dd", buf, pos)
            pos += 16
            pts.append((lon, lat))
        rings.append(political.Ring(points=tuple(pts)))
    return tuple(rings), pos


def _write_country(out: bytearray, rec: CountryRecord) -> None:
    out += _fixed3(rec.stable_id)
    out += _fixed3(rec.admin_id)
    out += _fixed3(rec.sovereign_id)
    out += _fixed3(rec.iso_a3_eh)
    out += _dotnet_string(rec.name)
    out += _dotnet_string(rec.name_long)
    out += _dotnet_string(rec.sovereign_name)
    out += _dotnet_string(rec.continent)
    out += _dotnet_string(rec.region_un)
    out += _dotnet_string(rec.subregion)
    out += _dotnet_string(rec.feature_class)
    out += _dotnet_string(rec.type)
    out += _dotnet_string(rec.note_adm0)
    out += _dotnet_string(rec.wikidata_id)
    out += struct.pack("<q", rec.pop_est)
    _write_rings(out, rec.rings)


def _read_country(buf: bytes, pos: int) -> tuple[CountryRecord, int]:
    stable, pos = _read_fixed3(buf, pos)
    admin, pos = _read_fixed3(buf, pos)
    sov, pos = _read_fixed3(buf, pos)
    iso, pos = _read_fixed3(buf, pos)
    name, pos = _read_dotnet_string(buf, pos)
    name_long, pos = _read_dotnet_string(buf, pos)
    sov_name, pos = _read_dotnet_string(buf, pos)
    continent, pos = _read_dotnet_string(buf, pos)
    region_un, pos = _read_dotnet_string(buf, pos)
    subregion, pos = _read_dotnet_string(buf, pos)
    fclass, pos = _read_dotnet_string(buf, pos)
    rtype, pos = _read_dotnet_string(buf, pos)
    note, pos = _read_dotnet_string(buf, pos)
    wiki, pos = _read_dotnet_string(buf, pos)
    (pop,) = struct.unpack_from("<q", buf, pos)
    pos += 8
    rings, pos = _read_rings(buf, pos)
    return CountryRecord(
        stable_id=stable, admin_id=admin, sovereign_id=sov, iso_a3_eh=iso,
        name=name, name_long=name_long, sovereign_name=sov_name, continent=continent,
        region_un=region_un, subregion=subregion, feature_class=fclass, type=rtype,
        note_adm0=note, wikidata_id=wiki, pop_est=pop, rings=rings,
    ), pos


def _write_disputed(out: bytearray, rec: DisputedRecord) -> None:
    out += _fixed3(rec.stable_id)
    out += _fixed3(rec.admin_id)
    out += _fixed3(rec.sovereign_id)
    out += _fixed3(rec.iso_a3_eh)
    out += _dotnet_string(rec.name)
    out += _dotnet_string(rec.name_long)
    out += _dotnet_string(rec.admin_name)
    out += _dotnet_string(rec.sovereign_name)
    out += _dotnet_string(rec.type)
    out += _dotnet_string(rec.note_adm0)
    out += _dotnet_string(rec.note_brk)
    out += _dotnet_string(rec.wikidata_id)
    out += struct.pack("<q", rec.pop_est)
    _write_rings(out, rec.rings)


def _read_disputed(buf: bytes, pos: int) -> tuple[DisputedRecord, int]:
    stable, pos = _read_fixed3(buf, pos)
    admin, pos = _read_fixed3(buf, pos)
    sov, pos = _read_fixed3(buf, pos)
    iso, pos = _read_fixed3(buf, pos)
    name, pos = _read_dotnet_string(buf, pos)
    name_long, pos = _read_dotnet_string(buf, pos)
    admin_name, pos = _read_dotnet_string(buf, pos)
    sov_name, pos = _read_dotnet_string(buf, pos)
    rtype, pos = _read_dotnet_string(buf, pos)
    note, pos = _read_dotnet_string(buf, pos)
    note_brk, pos = _read_dotnet_string(buf, pos)
    wiki, pos = _read_dotnet_string(buf, pos)
    (pop,) = struct.unpack_from("<q", buf, pos)
    pos += 8
    rings, pos = _read_rings(buf, pos)
    return DisputedRecord(
        stable_id=stable, admin_id=admin, sovereign_id=sov, iso_a3_eh=iso,
        name=name, name_long=name_long, admin_name=admin_name, sovereign_name=sov_name,
        type=rtype, note_adm0=note, note_brk=note_brk, wikidata_id=wiki, pop_est=pop,
        rings=rings,
    ), pos


def _write_city(out: bytearray, rec: CityRecord) -> None:
    out += struct.pack("<q", rec.stable_id)
    out += _dotnet_string(rec.name)
    out += _dotnet_string(rec.name_ascii)
    out += _dotnet_string(rec.feature_class)
    out += _fixed3(rec.admin_id)
    out += _fixed3(rec.sovereign_id)
    out += _dotnet_string(rec.admin_name)
    out += _dotnet_string(rec.sovereign_name)
    out += struct.pack("<ii", rec.scalerank, rec.nat_scale)
    out += struct.pack("<BBB", rec.is_capital, rec.is_world_city, rec.is_mega_city)
    out += struct.pack("<qq", rec.pop_max, rec.pop_min)
    out += struct.pack("<dd", rec.longitude, rec.latitude)
    out += _dotnet_string(rec.wikidata_id)


def _read_city(buf: bytes, pos: int) -> tuple[CityRecord, int]:
    (sid,) = struct.unpack_from("<q", buf, pos)
    pos += 8
    name, pos = _read_dotnet_string(buf, pos)
    name_ascii, pos = _read_dotnet_string(buf, pos)
    fclass, pos = _read_dotnet_string(buf, pos)
    admin, pos = _read_fixed3(buf, pos)
    sov, pos = _read_fixed3(buf, pos)
    admin_name, pos = _read_dotnet_string(buf, pos)
    sov_name, pos = _read_dotnet_string(buf, pos)
    scalerank, nat_scale = struct.unpack_from("<ii", buf, pos)
    pos += 8
    cap, wc, mc = struct.unpack_from("<BBB", buf, pos)
    pos += 3
    pop_max, pop_min = struct.unpack_from("<qq", buf, pos)
    pos += 16
    lon, lat = struct.unpack_from("<dd", buf, pos)
    pos += 16
    wiki, pos = _read_dotnet_string(buf, pos)
    return CityRecord(
        stable_id=sid, name=name, name_ascii=name_ascii, feature_class=fclass,
        admin_id=admin, sovereign_id=sov, admin_name=admin_name, sovereign_name=sov_name,
        scalerank=scalerank, nat_scale=nat_scale, is_capital=cap, is_world_city=wc,
        is_mega_city=mc, pop_max=pop_max, pop_min=pop_min, longitude=lon, latitude=lat,
        wikidata_id=wiki,
    ), pos


def serialize(asset: PoliticalAsset, build_id: str) -> bytes:
    out = bytearray()
    out += struct.pack("<IBH", MAGIC, FORMAT_VERSION, asset.border_year)
    out += _dotnet_string(build_id)
    out += struct.pack("<IIII", len(asset.de_facto_countries), len(asset.sovereignty_claims),
                       len(asset.disputed_areas), len(asset.cities))
    for rec in asset.de_facto_countries:
        _write_country(out, rec)
    for rec in asset.sovereignty_claims:
        _write_country(out, rec)
    for rec in asset.disputed_areas:
        _write_disputed(out, rec)
    for rec in asset.cities:
        _write_city(out, rec)
    return bytes(out)


def deserialize(payload: bytes) -> tuple[PoliticalAsset, str]:
    pos = 0
    (magic, version, year) = struct.unpack_from("<IBH", payload, pos)
    pos += 7
    if magic != MAGIC:
        raise PoliticalError(f"bad WSP1 magic {magic:#x}")
    if version != FORMAT_VERSION:
        raise PoliticalError(f"unsupported WSP1 version {version}")
    build_id, pos = _read_dotnet_string(payload, pos)
    n_df, n_sov, n_disp, n_cit = struct.unpack_from("<IIII", payload, pos)
    pos += 16
    asset = PoliticalAsset(border_year=year)
    for _ in range(n_df):
        rec, pos = _read_country(payload, pos)
        asset.de_facto_countries.append(rec)
    for _ in range(n_sov):
        rec, pos = _read_country(payload, pos)
        asset.sovereignty_claims.append(rec)
    for _ in range(n_disp):
        rec, pos = _read_disputed(payload, pos)
        asset.disputed_areas.append(rec)
    for _ in range(n_cit):
        rec, pos = _read_city(payload, pos)
        asset.cities.append(rec)
    if pos != len(payload):
        raise PoliticalError(f"trailing {len(payload) - pos} bytes in WSP1 payload")
    return asset, build_id


def write_political(out_dir: Path, asset: PoliticalAsset, build_id: str) -> Path:
    path = out_dir / political.ASSET_FILENAME
    payload = serialize(asset, build_id)
    with path.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as handle:
            handle.write(payload)
    return path


def read_political(path: Path) -> tuple[PoliticalAsset, str]:
    with gzip.open(path, "rb") as handle:
        payload = handle.read()
    return deserialize(payload)
