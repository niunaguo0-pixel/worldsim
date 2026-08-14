"""Minimal dBASE III/IV attribute reader for the locked Natural Earth ``.dbf`` files.

Only what the raster build needs is supported: reading whole columns of character
(``C``) and numeric (``N`` / ``F``) fields in record order, so a shapefile's
geometry can be filtered by upstream attributes such as ``scalerank`` and
``featurecla``. Anything else fails closed rather than guessing.

Natural Earth pads character fields with NUL bytes rather than spaces, so both are
stripped. Numeric fields are returned as ``int`` when the field declares zero
decimals and ``float`` otherwise; a blank numeric field yields ``None``.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass
from pathlib import Path

HEADER_BYTES = 32
FIELD_BYTES = 32
FIELD_TERMINATOR = 0x0D
SUPPORTED_TYPES = frozenset("CNF")


class DbfError(ValueError):
    """Malformed or unsupported dBASE table."""


@dataclass(frozen=True)
class DbfField:
    name: str
    type: str
    length: int
    decimals: int
    offset: int


class DbfTable:
    """Column-oriented read-only view of a ``.dbf`` file."""

    def __init__(self, path: Path) -> None:
        self.path = Path(path)
        raw = self.path.read_bytes()
        if len(raw) < HEADER_BYTES:
            raise DbfError(f"{self.path.name}: dbf header truncated")
        self.record_count, header_length, self.record_length = struct.unpack_from("<IHH", raw, 4)
        if header_length < HEADER_BYTES + FIELD_BYTES or self.record_length < 1:
            raise DbfError(f"{self.path.name}: implausible dbf header lengths")
        fields: list[DbfField] = []
        # Field offsets start at 1: byte 0 of every record is the deletion marker.
        offset = 1
        cursor = HEADER_BYTES
        while cursor < header_length and raw[cursor] != FIELD_TERMINATOR:
            if cursor + FIELD_BYTES > len(raw):
                raise DbfError(f"{self.path.name}: dbf field descriptor truncated")
            name = raw[cursor : cursor + 11].split(b"\x00", 1)[0].decode("ascii", "replace")
            ftype = chr(raw[cursor + 11])
            length = raw[cursor + 16]
            decimals = raw[cursor + 17]
            fields.append(DbfField(name, ftype, length, decimals, offset))
            offset += length
            cursor += FIELD_BYTES
        if not fields:
            raise DbfError(f"{self.path.name}: dbf declares no fields")
        if offset != self.record_length:
            raise DbfError(
                f"{self.path.name}: field widths total {offset} but records are "
                f"{self.record_length} bytes"
            )
        needed = header_length + self.record_count * self.record_length
        if len(raw) < needed:
            raise DbfError(
                f"{self.path.name}: dbf holds {len(raw)} bytes, need {needed} for "
                f"{self.record_count} records"
            )
        self._raw = raw
        self._start = header_length
        self.fields = tuple(fields)
        self._by_name = {field.name: field for field in fields}

    def field(self, name: str) -> DbfField:
        try:
            return self._by_name[name]
        except KeyError:
            available = ", ".join(sorted(self._by_name))
            raise DbfError(
                f"{self.path.name}: field {name!r} is missing; available: {available}"
            ) from None

    def column(self, name: str) -> list:
        """Whole column in record order. Fails closed on unsupported field types."""
        field = self.field(name)
        if field.type not in SUPPORTED_TYPES:
            raise DbfError(
                f"{self.path.name}: field {name!r} has unsupported type {field.type!r}"
            )
        values = []
        for index in range(self.record_count):
            base = self._start + index * self.record_length + field.offset
            cell = self._raw[base : base + field.length]
            if field.type == "C":
                values.append(cell.decode("latin-1").replace("\x00", "").strip())
                continue
            text = cell.decode("ascii", "replace").replace("\x00", "").strip()
            if not text:
                values.append(None)
                continue
            try:
                values.append(int(text) if field.decimals == 0 else float(text))
            except ValueError as exc:
                raise DbfError(
                    f"{self.path.name}: field {name!r} record {index} holds "
                    f"non-numeric {text!r}"
                ) from exc
        return values


def read_columns(path: Path, names: tuple[str, ...]) -> dict[str, list]:
    table = DbfTable(path)
    return {name: table.column(name) for name in names}
