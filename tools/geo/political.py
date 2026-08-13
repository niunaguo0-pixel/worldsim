"""Build the versioned political asset from the locked Natural Earth admin-0 layers.

The asset replaces the hand-written ``political-2026.tsv`` with a deterministic
derivative of Natural Earth v5.1.2 1:10m:

* ``ne_10m_admin_0_countries`` -- de-facto admin-0 control (258 records)
* ``ne_10m_admin_0_sovereignty`` -- sovereignty attribution (209 records)
* ``ne_10m_admin_0_disputed_areas`` -- disputed / breakaway / overlay units
* ``ne_10m_populated_places`` -- populated places / capitals

Two views are emitted from the same locked source polygons:

* **DeFactoControl** -- one record per de-facto admin-0 unit, geometry from the
  countries layer, tagged with its sovereign (``SOV_A3``).
* **SovereigntyClaims** -- one record per sovereignty, geometry from the
  sovereignty layer.

Disputed areas are emitted verbatim with their source ``TYPE`` / ``NOTE_ADM0``
/ ``NOTE_BRK`` and claimant ``ADMIN`` / ``SOVEREIGNT`` / ``ADM0_A3`` /
``SOV_A3``. No adjudication is fabricated: Natural Earth does not take sides,
and neither does this builder.

The 2026 snapshot constraint is enforced: ``border_year`` must be exactly 2026.
Any other year fails closed until later tasks add historical snapshots.

The on-disk container is a gzip-compressed self-describing binary ``WSP1``
(WorldSim Political v1). Records are sorted by ``(stable_id, name)`` so two
builds from the same cache produce byte-identical output. See
``political_binary`` for the layout.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Sequence

from dbf_reader import DbfTable
from fetch_sources import extract_dir
from shapefile_reader import Shape, find_layer, iter_shapes

MAGIC = 0x31505357  # "WSP1" little-endian
FORMAT_VERSION = 1
SUPPORTED_BORDER_YEAR = 2026
ASSET_FILENAME = "political-2026.wgeo.gz"
ASSET_FILENAME_TSV = "political-2026.tsv"  # legacy hand-written file this replaces
ISO_MISSING = "-99"

SRC_ADMIN0 = "ne-10m-admin-0"
SRC_SOVEREIGNTY = "ne-10m-admin-0-sovereignty"
SRC_DISPUTED = "ne-10m-disputed"
SRC_POP_PLACES = "ne-10m-populated-places"

LAYER_ADMIN0 = "ne_10m_admin_0_countries"
LAYER_SOVEREIGNTY = "ne_10m_admin_0_sovereignty"
LAYER_DISPUTED = "ne_10m_admin_0_disputed_areas"
LAYER_POP_PLACES = "ne_10m_populated_places"

ADMIN0_COLUMNS = (
    "featurecla", "ADMIN", "ADM0_A3", "SOVEREIGNT", "SOV_A3",
    "ISO_A3_EH", "NAME", "NAME_LONG", "CONTINENT", "REGION_UN",
    "SUBREGION", "TYPE", "NOTE_ADM0", "WIKIDATAID", "POP_EST",
)
DISPUTED_COLUMNS = (
    "featurecla", "ADMIN", "ADM0_A3", "SOVEREIGNT", "SOV_A3",
    "ISO_A3_EH", "NAME", "NAME_LONG", "TYPE", "NOTE_ADM0",
    "NOTE_BRK", "WIKIDATAID", "POP_EST",
)
CITY_COLUMNS = (
    "SCALERANK", "NATSCALE", "FEATURECLA", "NAME", "NAMEASCII",
    "ADM0CAP", "WORLDCITY", "MEGACITY", "SOV0NAME", "SOV_A3",
    "ADM0NAME", "ADM0_A3", "LATITUDE", "LONGITUDE", "POP_MAX",
    "POP_MIN", "NE_ID", "WIKIDATAID",
)


class PoliticalError(RuntimeError):
    """Locked political sources missing/malformed, or the border year is wrong."""


@dataclass(frozen=True)
class Ring:
    points: tuple[tuple[float, float], ...]


@dataclass(frozen=True)
class CountryRecord:
    stable_id: str
    admin_id: str
    sovereign_id: str
    iso_a3_eh: str
    name: str
    name_long: str
    sovereign_name: str
    continent: str
    region_un: str
    subregion: str
    feature_class: str
    type: str
    note_adm0: str
    wikidata_id: str
    pop_est: int
    rings: tuple[Ring, ...]


@dataclass(frozen=True)
class DisputedRecord:
    stable_id: str
    admin_id: str
    sovereign_id: str
    iso_a3_eh: str
    name: str
    name_long: str
    admin_name: str
    sovereign_name: str
    type: str
    note_adm0: str
    note_brk: str
    wikidata_id: str
    pop_est: int
    rings: tuple[Ring, ...]


@dataclass(frozen=True)
class CityRecord:
    stable_id: int
    name: str
    name_ascii: str
    feature_class: str
    admin_id: str
    sovereign_id: str
    admin_name: str
    sovereign_name: str
    scalerank: int
    nat_scale: int
    is_capital: int
    is_world_city: int
    is_mega_city: int
    pop_max: int
    pop_min: int
    longitude: float
    latitude: float
    wikidata_id: str


@dataclass
class PoliticalAsset:
    border_year: int
    de_facto_countries: list[CountryRecord] = field(default_factory=list)
    sovereignty_claims: list[CountryRecord] = field(default_factory=list)
    disputed_areas: list[DisputedRecord] = field(default_factory=list)
    cities: list[CityRecord] = field(default_factory=list)


def _shape_rings(shape: Shape) -> tuple[Ring, ...]:
    rings = []
    for part in shape.parts:
        if part.ndim != 2 or part.shape[1] != 2:
            raise PoliticalError("political geometry must be 2-D lon/lat arrays")
        if part.shape[0] < 3:
            continue
        rings.append(Ring(points=tuple((float(x), float(y)) for x, y in part)))
    return tuple(rings)


def _str(value: Any) -> str:
    if value is None:
        return ""
    return str(value).strip()


def _fixed3(value: Any) -> str:
    text = _str(value)
    if len(text) != 3:
        raise PoliticalError(f"expected a 3-character admin id, got {text!r}")
    return text


def _int_or_minus1(value: Any) -> int:
    if value is None:
        return -1
    try:
        return int(value)
    except (TypeError, ValueError):
        return -1


def _pop_est(value: Any) -> int:
    if value is None:
        return -1
    try:
        f = float(value)
    except (TypeError, ValueError):
        return -1
    if f != f:
        return -1
    if f >= 0:
        return int(f + 0.5)
    return -int(-f + 0.5)


def _stable_id_for_country(iso_a3_eh: str, admin_id: str) -> str:
    # Kept for reference; the de-facto view uses ADM0_A3 (unique per admin-0 unit)
    # and the sovereignty view uses SOV_A3 (unique per sovereignty), because
    # ISO_A3_EH is NOT unique across NE admin-0 records (e.g. BRA is shared by
    # Brazil and Brazilian Islands, AUS by Australia and several territories).
    if iso_a3_eh and iso_a3_eh != ISO_MISSING:
        return iso_a3_eh
    return admin_id


def _require_layer(source_id: str, lock: dict[str, Any], cache: Path, layer: str) -> Path:
    by_id = {src["id"]: src for src in lock["sources"]}
    if source_id not in by_id:
        raise PoliticalError(f"lock is missing required source {source_id}")
    directory = extract_dir(by_id[source_id], cache)
    if not directory.is_dir():
        raise PoliticalError(
            f"{source_id}: extracted shapefiles missing at {directory}. Run fetch first."
        )
    return find_layer(directory, layer)


def _read_brk_a3(shp_path: Path, table: DbfTable, index: int) -> str:
    try:
        field = table.field("BRK_A3")
    except Exception as exc:
        raise PoliticalError(f"{shp_path.name}: BRK_A3 field missing") from exc
    base = table._start + index * table.record_length + field.offset
    cell = table._raw[base: base + field.length]
    return cell.decode("latin-1").replace("\x00", "").strip()


def _read_country_view(
    shp_path: Path, columns: Sequence[str], stable_id_field: str
) -> tuple[list[CountryRecord], dict[str, int]]:
    """Read an admin-0 layer (countries or sovereignty) into CountryRecords.

    ``stable_id_field`` selects the unique key: ``ADM0_A3`` for the de-facto
    countries view (258 unique units) and ``SOV_A3`` for the sovereignty view
    (209 unique sovereigns). ISO_A3_EH is NOT unique across NE admin-0 records
    (e.g. BRA is shared by Brazil and Brazilian Islands), so it is carried as
    an attribute, not used as the stable id.

    Returns the records sorted by (stable_id, name) and a stats dict with the
    raw record count plus the number of records skipped (Null geometry only).
    """
    table = DbfTable(shp_path.with_suffix(".dbf"))
    cols = {name: table.column(name) for name in columns}
    shapes = list(iter_shapes(shp_path))
    if len(shapes) != table.record_count:
        raise PoliticalError(
            f"{shp_path.name}: geometry has {len(shapes)} records but dbf has "
            f"{table.record_count}"
        )
    records: list[CountryRecord] = []
    skipped = 0
    for index, shape in enumerate(shapes):
        if shape.shape_type == 0:
            skipped += 1
            continue
        iso = _str(cols["ISO_A3_EH"][index])
        admin_id = _fixed3(cols["ADM0_A3"][index])
        sovereign_id = _fixed3(cols["SOV_A3"][index])
        stable_id = admin_id if stable_id_field == "ADM0_A3" else sovereign_id
        records.append(
            CountryRecord(
                stable_id=stable_id,
                admin_id=admin_id,
                sovereign_id=sovereign_id,
                iso_a3_eh=iso or ISO_MISSING,
                name=_str(cols["NAME"][index]),
                name_long=_str(cols["NAME_LONG"][index]),
                sovereign_name=_str(cols["SOVEREIGNT"][index]),
                continent=_str(cols["CONTINENT"][index]),
                region_un=_str(cols["REGION_UN"][index]),
                subregion=_str(cols["SUBREGION"][index]),
                feature_class=_str(cols["featurecla"][index]),
                type=_str(cols["TYPE"][index]),
                note_adm0=_str(cols["NOTE_ADM0"][index]),
                wikidata_id=_str(cols["WIKIDATAID"][index]),
                pop_est=_pop_est(cols["POP_EST"][index]),
                rings=_shape_rings(shape),
            )
        )
    records.sort(key=lambda r: (r.stable_id, r.name))
    return records, {"recordCount": table.record_count, "skippedNull": skipped}


def _read_disputed(
    shp_path: Path, columns: Sequence[str]
) -> tuple[list[DisputedRecord], dict[str, int]]:
    table = DbfTable(shp_path.with_suffix(".dbf"))
    cols = {name: table.column(name) for name in columns}
    shapes = list(iter_shapes(shp_path))
    if len(shapes) != table.record_count:
        raise PoliticalError(
            f"{shp_path.name}: geometry has {len(shapes)} records but dbf has "
            f"{table.record_count}"
        )
    records: list[DisputedRecord] = []
    skipped = 0
    for index, shape in enumerate(shapes):
        if shape.shape_type == 0:
            skipped += 1
            continue
        records.append(
            DisputedRecord(
                stable_id=_fixed3(_read_brk_a3(shp_path, table, index)),
                admin_id=_fixed3(cols["ADM0_A3"][index]),
                sovereign_id=_fixed3(cols["SOV_A3"][index]),
                iso_a3_eh=_str(cols["ISO_A3_EH"][index]) or ISO_MISSING,
                name=_str(cols["NAME"][index]),
                name_long=_str(cols["NAME_LONG"][index]),
                admin_name=_str(cols["ADMIN"][index]),
                sovereign_name=_str(cols["SOVEREIGNT"][index]),
                type=_str(cols["TYPE"][index]),
                note_adm0=_str(cols["NOTE_ADM0"][index]),
                note_brk=_str(cols["NOTE_BRK"][index]),
                wikidata_id=_str(cols["WIKIDATAID"][index]),
                pop_est=_pop_est(cols["POP_EST"][index]),
                rings=_shape_rings(shape),
            )
        )
    records.sort(key=lambda r: (r.stable_id, r.name))
    return records, {"recordCount": table.record_count, "skippedNull": skipped}


def _read_cities(
    shp_path: Path, columns: Sequence[str]
) -> tuple[list[CityRecord], dict[str, int]]:
    table = DbfTable(shp_path.with_suffix(".dbf"))
    cols = {name: table.column(name) for name in columns}
    shapes = list(iter_shapes(shp_path))
    if len(shapes) != table.record_count:
        raise PoliticalError(
            f"{shp_path.name}: geometry has {len(shapes)} records but dbf has "
            f"{table.record_count}"
        )
    records: list[CityRecord] = []
    skipped = 0
    for index, shape in enumerate(shapes):
        if shape.shape_type == 0:
            skipped += 1
            continue
        if not shape.parts:
            raise PoliticalError(f"{shp_path.name}: city record {index} has no point")
        point = shape.parts[0]
        if point.shape != (1, 2):
            raise PoliticalError(
                f"{shp_path.name}: city record {index} is not a single point"
            )
        records.append(
            CityRecord(
                stable_id=_int_or_minus1(cols["NE_ID"][index]),
                name=_str(cols["NAME"][index]),
                name_ascii=_str(cols["NAMEASCII"][index]),
                feature_class=_str(cols["FEATURECLA"][index]),
                admin_id=_fixed3(cols["ADM0_A3"][index]),
                sovereign_id=_fixed3(cols["SOV_A3"][index]),
                admin_name=_str(cols["ADM0NAME"][index]),
                sovereign_name=_str(cols["SOV0NAME"][index]),
                scalerank=_int_or_minus1(cols["SCALERANK"][index]),
                nat_scale=_int_or_minus1(cols["NATSCALE"][index]),
                is_capital=int(bool(cols["ADM0CAP"][index])),
                is_world_city=int(bool(cols["WORLDCITY"][index])),
                is_mega_city=int(bool(cols["MEGACITY"][index])),
                pop_max=_int_or_minus1(cols["POP_MAX"][index]),
                pop_min=_int_or_minus1(cols["POP_MIN"][index]),
                longitude=float(point[0, 0]),
                latitude=float(point[0, 1]),
                wikidata_id=_str(cols["WIKIDATAID"][index]),
            )
        )
    records.sort(key=lambda r: (r.stable_id, r.name))
    return records, {"recordCount": table.record_count, "skippedNull": skipped}


def build_political(
    lock: dict[str, Any], cache: Path, border_year: int = SUPPORTED_BORDER_YEAR
) -> PoliticalAsset:
    """Read the four locked NE layers and assemble the PoliticalAsset."""
    if border_year != SUPPORTED_BORDER_YEAR:
        raise PoliticalError(
            f"border_year {border_year} is not supported: this asset is a 2026 "
            f"snapshot of Natural Earth current data, not a historical border "
            f"product. Only {SUPPORTED_BORDER_YEAR} is allowed until later tasks "
            f"add historical snapshots."
        )
    admin0 = _require_layer(SRC_ADMIN0, lock, cache, LAYER_ADMIN0)
    sov = _require_layer(SRC_SOVEREIGNTY, lock, cache, LAYER_SOVEREIGNTY)
    disp = _require_layer(SRC_DISPUTED, lock, cache, LAYER_DISPUTED)
    cities = _require_layer(SRC_POP_PLACES, lock, cache, LAYER_POP_PLACES)

    de_facto, _ = _read_country_view(admin0, ADMIN0_COLUMNS, stable_id_field="ADM0_A3")
    sovereignty, _ = _read_country_view(sov, ADMIN0_COLUMNS, stable_id_field="SOV_A3")
    disputed, _ = _read_disputed(disp, DISPUTED_COLUMNS)
    city_list, _ = _read_cities(cities, CITY_COLUMNS)
    return PoliticalAsset(
        border_year=border_year,
        de_facto_countries=de_facto,
        sovereignty_claims=sovereignty,
        disputed_areas=disputed,
        cities=city_list,
    )
