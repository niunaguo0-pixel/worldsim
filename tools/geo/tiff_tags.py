"""Standard-library TIFF/BigTIFF IFD-0 tag reader.

Kept dependency-free so the fetch/bootstrap path in ``fetch_sources`` can inspect
cached GeoTIFF metadata without importing numpy. ``geotiff`` layers pixel decoding
on top of this.
"""
from __future__ import annotations

import struct
from pathlib import Path
from typing import Any

IMAGE_WIDTH = 256
IMAGE_LENGTH = 257
BITS_PER_SAMPLE = 258
COMPRESSION = 259
PHOTOMETRIC = 262
STRIP_OFFSETS = 273
SAMPLES_PER_PIXEL = 277
ROWS_PER_STRIP = 278
STRIP_BYTE_COUNTS = 279
PLANAR_CONFIG = 284
PREDICTOR = 317
COLOR_MAP = 320
TILE_WIDTH = 322
TILE_LENGTH = 323
TILE_OFFSETS = 324
TILE_BYTE_COUNTS = 325
SAMPLE_FORMAT = 339
MODEL_PIXEL_SCALE = 33550
MODEL_TIEPOINT = 33922
MODEL_TRANSFORMATION = 34264
GEO_KEY_DIRECTORY = 34735
GDAL_NODATA = 42113

# TIFF field type -> (byte size, struct code); None means the reader unpacks it specially.
_TYPES: dict[int, tuple[int, str | None]] = {
    1: (1, "B"),
    2: (1, None),   # ASCII
    3: (2, "H"),
    4: (4, "I"),
    5: (8, None),   # RATIONAL
    6: (1, "b"),
    7: (1, "B"),    # UNDEFINED
    8: (2, "h"),
    9: (4, "i"),
    10: (8, None),  # SRATIONAL
    11: (4, "f"),
    12: (8, "d"),
    16: (8, "Q"),
    17: (8, "q"),
    18: (8, "Q"),   # IFD8
}


class TiffError(ValueError):
    """Malformed or unsupported TIFF structure."""


class TiffTags:
    """IFD-0 tags of a (Big)TIFF plus the byte order needed to read pixel data."""

    def __init__(self, path: Path, endian: str, big: bool, tags: dict[int, Any]) -> None:
        self.path = path
        self.endian = endian
        self.big = big
        self.tags = tags

    def get(self, tag: int, default: Any = None) -> Any:
        return self.tags.get(tag, default)

    def scalar(self, tag: int, default: Any = None) -> Any:
        value = self.tags.get(tag)
        if value is None:
            return default
        if isinstance(value, (list, tuple)):
            if not value:
                return default
            return value[0]
        return value

    def require(self, tag: int, name: str) -> Any:
        value = self.scalar(tag)
        if value is None:
            raise TiffError(f"{self.path.name}: required TIFF tag {name} ({tag}) is missing")
        return value

    def ascii(self, tag: int) -> str | None:
        value = self.tags.get(tag)
        if not isinstance(value, str):
            return None
        return value.split("\x00", 1)[0].strip() or None


def read_ifd0(path: Path) -> TiffTags:
    """Read every IFD-0 tag. Later IFDs (reduced-resolution overviews) are ignored."""
    with path.open("rb") as handle:
        header = handle.read(16)
        if len(header) < 8:
            raise TiffError(f"{path.name}: file too short for a TIFF header")
        if header[:2] == b"II":
            endian = "<"
        elif header[:2] == b"MM":
            endian = ">"
        else:
            raise TiffError(f"{path.name}: not a TIFF (bad byte-order mark)")
        magic = struct.unpack(endian + "H", header[2:4])[0]
        if magic == 42:
            big = False
            ifd = struct.unpack(endian + "I", header[4:8])[0]
            entry_size, count_size, offset_code, offset_size = 12, 4, "I", 4
        elif magic == 43:
            big = True
            if struct.unpack(endian + "H", header[4:6])[0] != 8:
                raise TiffError(f"{path.name}: unsupported BigTIFF offset size")
            ifd = struct.unpack(endian + "Q", header[8:16])[0]
            entry_size, count_size, offset_code, offset_size = 20, 8, "Q", 8
        else:
            raise TiffError(f"{path.name}: unsupported TIFF magic {magic}")
        handle.seek(ifd)
        if big:
            entries = struct.unpack(endian + "Q", handle.read(8))[0]
        else:
            entries = struct.unpack(endian + "H", handle.read(2))[0]
        tags: dict[int, Any] = {}
        for _ in range(entries):
            entry = handle.read(entry_size)
            if len(entry) < entry_size:
                raise TiffError(f"{path.name}: truncated IFD entry")
            tag, field_type = struct.unpack(endian + "HH", entry[:4])
            count = struct.unpack(
                endian + ("Q" if count_size == 8 else "I"), entry[4 : 4 + count_size]
            )[0]
            value_field = entry[4 + count_size :]
            if field_type not in _TYPES:
                continue
            size, code = _TYPES[field_type]
            total = size * count
            if total <= offset_size:
                raw = value_field[:total]
            else:
                offset = struct.unpack(endian + offset_code, value_field[:offset_size])[0]
                keep = handle.tell()
                handle.seek(offset)
                raw = handle.read(total)
                handle.seek(keep)
            if len(raw) < total:
                raise TiffError(f"{path.name}: truncated value for tag {tag}")
            tags[tag] = _decode_value(endian, field_type, count, raw, code)
        return TiffTags(path, endian, big, tags)


def _decode_value(endian: str, field_type: int, count: int, raw: bytes, code: str | None) -> Any:
    if field_type == 2:
        return raw.decode("ascii", "replace")
    if field_type in (5, 10):
        numerator = "I" if field_type == 5 else "i"
        pairs = struct.unpack(endian + numerator * (2 * count), raw)
        return [
            (pairs[i], pairs[i + 1]) for i in range(0, len(pairs), 2)
        ]
    if field_type == 7:
        return raw
    assert code is not None
    return list(struct.unpack(endian + code * count, raw))


def read_gdal_nodata(path: Path) -> str | None:
    """Return the GDAL_NODATA (tag 42113) string, or None when the tag is absent."""
    try:
        return read_ifd0(path).ascii(GDAL_NODATA)
    except TiffError:
        return None


def geo_keys(tags: TiffTags) -> dict[int, int]:
    """Flatten the GeoTIFF GeoKeyDirectory short-valued keys (SHORT entries only)."""
    directory = tags.get(GEO_KEY_DIRECTORY)
    if not isinstance(directory, list) or len(directory) < 4:
        return {}
    count = directory[3]
    keys: dict[int, int] = {}
    for index in range(count):
        base = 4 + index * 4
        if base + 3 >= len(directory):
            break
        key_id, location, key_count, value = directory[base : base + 4]
        if location == 0 and key_count == 1:
            keys[key_id] = value
    return keys
