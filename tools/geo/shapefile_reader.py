"""Minimal ESRI shapefile geometry reader for the locked Natural Earth layers.

Only the geometry needed by the raster build is read (.shp); attribute tables
(.dbf) are ignored. Supported shape types are Null, Point, PolyLine and Polygon
plus their Z/M variants, whose trailing Z/M arrays are skipped because the build
is strictly 2-D. Anything else fails closed.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

import numpy as np

SHP_FILE_CODE = 9994
SHP_VERSION = 1000

NULL = 0
POINT = 1
POLYLINE = 3
POLYGON = 5
POINT_Z = 11
POLYLINE_Z = 13
POLYGON_Z = 15
POINT_M = 21
POLYLINE_M = 23
POLYGON_M = 25

_POLY_TYPES = {POLYLINE, POLYGON, POLYLINE_Z, POLYGON_Z, POLYLINE_M, POLYGON_M}
_POINT_TYPES = {POINT, POINT_Z, POINT_M}
_SUPPORTED = _POLY_TYPES | _POINT_TYPES | {NULL}

HEADER_BYTES = 100


class ShapefileError(ValueError):
    """Malformed or unsupported shapefile."""


@dataclass(frozen=True)
class Shape:
    """One shapefile record: ``parts`` are 2-D vertex arrays of shape (n, 2) as lon/lat."""

    shape_type: int
    parts: tuple[np.ndarray, ...]


def iter_shapes(path: Path) -> Iterator[Shape]:
    """Stream records in file order; each record is decoded independently."""
    path = Path(path)
    with path.open("rb") as handle:
        header = handle.read(HEADER_BYTES)
        if len(header) != HEADER_BYTES:
            raise ShapefileError(f"{path.name}: shapefile header truncated")
        file_code = struct.unpack(">i", header[0:4])[0]
        if file_code != SHP_FILE_CODE:
            raise ShapefileError(f"{path.name}: bad shapefile file code {file_code}")
        version = struct.unpack("<i", header[28:32])[0]
        if version != SHP_VERSION:
            raise ShapefileError(f"{path.name}: unsupported shapefile version {version}")
        declared_type = struct.unpack("<i", header[32:36])[0]
        if declared_type not in _SUPPORTED:
            raise ShapefileError(f"{path.name}: unsupported shape type {declared_type}")
        file_length = struct.unpack(">i", header[24:28])[0] * 2
        while handle.tell() + 8 <= file_length:
            record_header = handle.read(8)
            if len(record_header) < 8:
                break
            _number, words = struct.unpack(">ii", record_header)
            length = words * 2
            if length < 4:
                raise ShapefileError(f"{path.name}: record content length {length} is invalid")
            content = handle.read(length)
            if len(content) != length:
                raise ShapefileError(f"{path.name}: record truncated")
            yield _decode_record(path, content)


def _decode_record(path: Path, content: bytes) -> Shape:
    shape_type = struct.unpack_from("<i", content, 0)[0]
    if shape_type == NULL:
        return Shape(NULL, ())
    if shape_type in _POINT_TYPES:
        point = np.frombuffer(content, dtype="<f8", count=2, offset=4).reshape(1, 2)
        return Shape(shape_type, (point.copy(),))
    if shape_type not in _POLY_TYPES:
        raise ShapefileError(f"{path.name}: unsupported shape type {shape_type} in record")
    num_parts, num_points = struct.unpack_from("<ii", content, 36)
    if num_parts < 0 or num_points < 0:
        raise ShapefileError(f"{path.name}: negative part/point count")
    parts_offset = 44
    points_offset = parts_offset + 4 * num_parts
    starts = np.frombuffer(content, dtype="<i4", count=num_parts, offset=parts_offset)
    points = np.frombuffer(content, dtype="<f8", count=2 * num_points, offset=points_offset)
    points = points.reshape(num_points, 2)
    bounds = list(starts) + [num_points]
    parts = []
    for index in range(num_parts):
        begin, end = int(bounds[index]), int(bounds[index + 1])
        if not 0 <= begin <= end <= num_points:
            raise ShapefileError(f"{path.name}: part range {begin}:{end} is out of bounds")
        if end - begin >= 2:
            parts.append(np.array(points[begin:end], dtype=np.float64))
    return Shape(shape_type, tuple(parts))


def read_parts(path: Path) -> list[np.ndarray]:
    """All vertex arrays across all records, in file order."""
    parts: list[np.ndarray] = []
    for shape in iter_shapes(path):
        parts.extend(shape.parts)
    return parts


def read_parts_with_attributes(
    path: Path, names: tuple[str, ...]
) -> tuple[list[np.ndarray], dict[str, list]]:
    """Vertex arrays plus the sidecar ``.dbf`` attributes of each part's record.

    Returned attribute lists are parallel to the parts list: a multi-part record
    repeats its attribute values once per part, so a caller can filter geometry by
    upstream fields without tracking record indices itself.
    """
    from dbf_reader import DbfTable

    path = Path(path)
    table = DbfTable(path.with_suffix(".dbf"))
    columns = {name: table.column(name) for name in names}
    parts: list[np.ndarray] = []
    expanded: dict[str, list] = {name: [] for name in names}
    for index, shape in enumerate(iter_shapes(path)):
        if index >= table.record_count:
            raise ShapefileError(
                f"{path.name}: geometry holds more records than {table.path.name} "
                f"({table.record_count})"
            )
        for part in shape.parts:
            parts.append(part)
            for name in names:
                expanded[name].append(columns[name][index])
    return parts, expanded


def find_layer(extracted_dir: Path, layer: str) -> Path:
    """Locate ``<layer>.shp`` inside an extracted Natural Earth archive."""
    candidate = Path(extracted_dir) / f"{layer}.shp"
    if candidate.is_file():
        return candidate
    matches = sorted(Path(extracted_dir).rglob(f"{layer}.shp"))
    if not matches:
        raise ShapefileError(f"{layer}.shp not found under {extracted_dir}")
    return matches[0]
