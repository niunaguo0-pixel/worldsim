"""Block-wise GeoTIFF reader for the locked ETOPO and Köppen-Geiger rasters.

Only what the locked sources actually use is supported, and anything else fails
closed instead of guessing:

* strips or 256x256 tiles, single sample per pixel, contiguous planar config
* compression: none (1), LZW (5), Deflate (8 / 32946)
* predictor: none (1), horizontal (2), floating point (3, little-endian files only)
* geographic model type with a ModelPixelScale + ModelTiepoint transform

Pixels are yielded one block at a time, so the 466 MB ETOPO GeoTIFF is never
materialised in memory: peak usage is one decoded 256x256 tile.
"""
from __future__ import annotations

import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

import numpy as np

import tiff_tags as tt
from tiff_tags import TiffError

COMPRESSION_NONE = 1
COMPRESSION_LZW = 5
COMPRESSION_DEFLATE = 8
COMPRESSION_DEFLATE_OLD = 32946

PREDICTOR_NONE = 1
PREDICTOR_HORIZONTAL = 2
PREDICTOR_FLOATING_POINT = 3

MODEL_TYPE_GEOGRAPHIC = 2
GEO_KEY_MODEL_TYPE = 1024
GEO_KEY_RASTER_TYPE = 1025
RASTER_PIXEL_IS_AREA = 1
RASTER_PIXEL_IS_POINT = 2

_SAMPLE_DTYPES = {
    (8, 1): "u1",
    (16, 1): "u2",
    (32, 1): "u4",
    (8, 2): "i1",
    (16, 2): "i2",
    (32, 2): "i4",
    (32, 3): "f4",
    (64, 3): "f8",
}


@dataclass(frozen=True)
class GeoTransform:
    """North-up equirectangular transform of a pixel-is-area raster."""

    origin_lon: float
    origin_lat: float
    step_lon: float
    step_lat: float

    def cell_center_lon(self, x: int) -> float:
        return self.origin_lon + (x + 0.5) * self.step_lon

    def cell_center_lat(self, y: int) -> float:
        return self.origin_lat - (y + 0.5) * self.step_lat


class GeoTiff:
    """Read-only handle over one GeoTIFF; decodes a single block at a time."""

    def __init__(self, path: Path) -> None:
        self.path = Path(path)
        tags = tt.read_ifd0(self.path)
        self.tags = tags
        self.endian = tags.endian
        self.width = int(tags.require(tt.IMAGE_WIDTH, "ImageWidth"))
        self.height = int(tags.require(tt.IMAGE_LENGTH, "ImageLength"))
        self.samples_per_pixel = int(tags.scalar(tt.SAMPLES_PER_PIXEL, 1))
        if self.samples_per_pixel != 1:
            raise TiffError(
                f"{self.path.name}: SamplesPerPixel={self.samples_per_pixel} is unsupported"
            )
        planar = int(tags.scalar(tt.PLANAR_CONFIG, 1))
        if planar != 1:
            raise TiffError(f"{self.path.name}: PlanarConfiguration={planar} is unsupported")
        self.bits_per_sample = int(tags.require(tt.BITS_PER_SAMPLE, "BitsPerSample"))
        self.sample_format = int(tags.scalar(tt.SAMPLE_FORMAT, 1))
        key = (self.bits_per_sample, self.sample_format)
        if key not in _SAMPLE_DTYPES:
            raise TiffError(
                f"{self.path.name}: BitsPerSample={self.bits_per_sample} with "
                f"SampleFormat={self.sample_format} is unsupported"
            )
        self.dtype = np.dtype(self.endian + _SAMPLE_DTYPES[key])
        self.bytes_per_sample = self.dtype.itemsize
        self.compression = int(tags.scalar(tt.COMPRESSION, COMPRESSION_NONE))
        if self.compression not in (
            COMPRESSION_NONE,
            COMPRESSION_LZW,
            COMPRESSION_DEFLATE,
            COMPRESSION_DEFLATE_OLD,
        ):
            raise TiffError(f"{self.path.name}: Compression={self.compression} is unsupported")
        self.predictor = int(tags.scalar(tt.PREDICTOR, PREDICTOR_NONE))
        if self.predictor not in (
            PREDICTOR_NONE,
            PREDICTOR_HORIZONTAL,
            PREDICTOR_FLOATING_POINT,
        ):
            raise TiffError(f"{self.path.name}: Predictor={self.predictor} is unsupported")
        if self.predictor == PREDICTOR_FLOATING_POINT and self.endian != "<":
            raise TiffError(
                f"{self.path.name}: big-endian floating-point predictor is not supported; "
                "refusing to guess the byte-plane order"
            )
        self.tile_width = tags.scalar(tt.TILE_WIDTH)
        self.tile_length = tags.scalar(tt.TILE_LENGTH)
        self.tiled = self.tile_width is not None and self.tile_length is not None
        if self.tiled:
            self.tile_width = int(self.tile_width)
            self.tile_length = int(self.tile_length)
            self.block_offsets = _as_list(tags.get(tt.TILE_OFFSETS), self.path, "TileOffsets")
            self.block_counts = _as_list(tags.get(tt.TILE_BYTE_COUNTS), self.path, "TileByteCounts")
            self.blocks_across = (self.width + self.tile_width - 1) // self.tile_width
            self.blocks_down = (self.height + self.tile_length - 1) // self.tile_length
        else:
            self.rows_per_strip = int(tags.scalar(tt.ROWS_PER_STRIP, self.height))
            if self.rows_per_strip <= 0:
                raise TiffError(f"{self.path.name}: RowsPerStrip must be positive")
            self.block_offsets = _as_list(tags.get(tt.STRIP_OFFSETS), self.path, "StripOffsets")
            self.block_counts = _as_list(tags.get(tt.STRIP_BYTE_COUNTS), self.path, "StripByteCounts")
            self.blocks_across = 1
            self.blocks_down = (self.height + self.rows_per_strip - 1) // self.rows_per_strip
        expected_blocks = self.blocks_across * self.blocks_down
        if len(self.block_offsets) != expected_blocks or len(self.block_counts) != expected_blocks:
            raise TiffError(
                f"{self.path.name}: expected {expected_blocks} blocks, found "
                f"{len(self.block_offsets)} offsets / {len(self.block_counts)} byte counts"
            )
        self.nodata_text = tags.ascii(tt.GDAL_NODATA)
        self.transform = self._read_transform()

    # ---- geometry -------------------------------------------------------

    def _read_transform(self) -> GeoTransform:
        if self.tags.get(tt.MODEL_TRANSFORMATION) is not None:
            raise TiffError(
                f"{self.path.name}: ModelTransformation is unsupported; "
                "expected ModelPixelScale + ModelTiepoint"
            )
        keys = tt.geo_keys(self.tags)
        model_type = keys.get(GEO_KEY_MODEL_TYPE)
        if model_type is not None and model_type != MODEL_TYPE_GEOGRAPHIC:
            raise TiffError(
                f"{self.path.name}: GeoTIFF model type {model_type} is not geographic (2)"
            )
        # Every downstream cell-centre calculation assumes a pixel covers an area
        # whose corner is the tiepoint. Under RasterPixelIsPoint the tiepoint is the
        # centre of the first pixel instead, which shifts the whole grid by half a
        # cell, so refuse rather than silently mis-locate the raster.
        raster_type = keys.get(GEO_KEY_RASTER_TYPE)
        if raster_type is None:
            raise TiffError(
                f"{self.path.name}: GeoTIFF GTRasterType (key 1025) is absent, so "
                "pixel-is-area cannot be confirmed"
            )
        if raster_type != RASTER_PIXEL_IS_AREA:
            kind = "RasterPixelIsPoint" if raster_type == RASTER_PIXEL_IS_POINT else "unknown"
            raise TiffError(
                f"{self.path.name}: GTRasterType={raster_type} ({kind}); this build "
                "requires RasterPixelIsArea (1)"
            )
        self.raster_type = raster_type
        scale = self.tags.get(tt.MODEL_PIXEL_SCALE)
        tiepoint = self.tags.get(tt.MODEL_TIEPOINT)
        if not isinstance(scale, list) or len(scale) < 2:
            raise TiffError(f"{self.path.name}: ModelPixelScale is missing")
        if not isinstance(tiepoint, list) or len(tiepoint) < 5:
            raise TiffError(f"{self.path.name}: ModelTiepoint is missing")
        raster_x, raster_y = float(tiepoint[0]), float(tiepoint[1])
        if raster_x != 0.0 or raster_y != 0.0:
            raise TiffError(
                f"{self.path.name}: ModelTiepoint raster point ({raster_x}, {raster_y}) "
                "is not the upper-left corner"
            )
        return GeoTransform(
            origin_lon=float(tiepoint[3]),
            origin_lat=float(tiepoint[4]),
            step_lon=float(scale[0]),
            step_lat=float(scale[1]),
        )

    def nodata_value(self) -> float | None:
        """GDAL_NODATA parsed as a float, or None when the tag is absent/non-numeric."""
        if not self.nodata_text:
            return None
        try:
            return float(self.nodata_text)
        except ValueError:
            return None

    def require_grid(
        self, width: int, height: int, step: float, origin_lon: float, origin_lat: float
    ) -> None:
        """Fail closed unless the raster is the exact global grid the build assumes."""
        problems = []
        if (self.width, self.height) != (width, height):
            problems.append(f"size {self.width}x{self.height} != {width}x{height}")
        if self.transform.step_lon != step or self.transform.step_lat != step:
            problems.append(
                f"pixel scale ({self.transform.step_lon}, {self.transform.step_lat}) != {step}"
            )
        if self.transform.origin_lon != origin_lon or self.transform.origin_lat != origin_lat:
            problems.append(
                f"origin ({self.transform.origin_lon}, {self.transform.origin_lat}) "
                f"!= ({origin_lon}, {origin_lat})"
            )
        if problems:
            raise TiffError(f"{self.path.name}: unexpected grid: " + "; ".join(problems))

    # ---- pixels ---------------------------------------------------------

    def iter_blocks(self) -> Iterator[tuple[int, int, np.ndarray]]:
        """Yield ``(x0, y0, block)`` in fixed row-major block order, clipped to the image."""
        with self.path.open("rb") as handle:
            for block_y in range(self.blocks_down):
                for block_x in range(self.blocks_across):
                    index = block_y * self.blocks_across + block_x
                    if self.tiled:
                        x0 = block_x * self.tile_width
                        y0 = block_y * self.tile_length
                        rows, cols = self.tile_length, self.tile_width
                    else:
                        x0 = 0
                        y0 = block_y * self.rows_per_strip
                        rows = min(self.rows_per_strip, self.height - y0)
                        cols = self.width
                    block = self._read_block(handle, index, rows, cols)
                    usable_rows = min(rows, self.height - y0)
                    usable_cols = min(cols, self.width - x0)
                    yield x0, y0, block[:usable_rows, :usable_cols]

    def _read_block(self, handle, index: int, rows: int, cols: int) -> np.ndarray:
        offset = int(self.block_offsets[index])
        count = int(self.block_counts[index])
        handle.seek(offset)
        raw = handle.read(count)
        if len(raw) != count:
            raise TiffError(f"{self.path.name}: block {index} truncated in file")
        data = self._decompress(raw, index)
        expected = rows * cols * self.bytes_per_sample
        if len(data) < expected:
            raise TiffError(
                f"{self.path.name}: block {index} decoded to {len(data)} bytes, expected {expected}"
            )
        data = data[:expected]
        return self._undo_predictor(data, rows, cols)

    def _decompress(self, raw: bytes, index: int) -> bytes:
        if self.compression == COMPRESSION_NONE:
            return raw
        if self.compression in (COMPRESSION_DEFLATE, COMPRESSION_DEFLATE_OLD):
            try:
                return zlib.decompress(raw)
            except zlib.error as exc:
                raise TiffError(f"{self.path.name}: block {index} inflate failed: {exc}") from exc
        return lzw_decode(raw)

    def _undo_predictor(self, data: bytes, rows: int, cols: int) -> np.ndarray:
        if self.predictor == PREDICTOR_FLOATING_POINT:
            planes = np.frombuffer(data, dtype=np.uint8).reshape(
                rows, self.bytes_per_sample, cols
            )
            planes = np.cumsum(
                planes.reshape(rows, self.bytes_per_sample * cols), axis=1, dtype=np.uint8
            ).reshape(rows, self.bytes_per_sample, cols)
            # Byte planes are stored most-significant first; the target file is little-endian.
            ordered = np.ascontiguousarray(planes[:, ::-1, :].transpose(0, 2, 1))
            return ordered.view(self.dtype).reshape(rows, cols)
        samples = np.frombuffer(data, dtype=self.dtype).reshape(rows, cols)
        if self.predictor == PREDICTOR_HORIZONTAL:
            return np.cumsum(samples, axis=1, dtype=self.dtype)
        return samples


def _as_list(value, path: Path, name: str) -> list[int]:
    if isinstance(value, list):
        return [int(item) for item in value]
    if isinstance(value, int):
        return [value]
    raise TiffError(f"{path.name}: required TIFF tag {name} is missing")


_LZW_CLEAR = 256
_LZW_EOI = 257
_LZW_MAX_BITS = 12


def lzw_decode(data: bytes) -> bytes:
    """TIFF-flavoured LZW: MSB-first packing, 9..12 bit codes, early code-width change."""
    out = bytearray()
    table: list[bytes] = []
    previous: bytes | None = None
    bit_position = 0
    bits = 9
    total_bits = len(data) * 8
    padded = data + b"\x00\x00\x00"
    while bit_position + bits <= total_bits:
        byte_index = bit_position >> 3
        window = int.from_bytes(padded[byte_index : byte_index + 3], "big")
        shift = 24 - (bit_position & 7) - bits
        code = (window >> shift) & ((1 << bits) - 1)
        bit_position += bits
        if code == _LZW_EOI:
            break
        if code == _LZW_CLEAR:
            table = [bytes((value,)) for value in range(256)] + [b"", b""]
            previous = None
            bits = 9
            continue
        if not table:
            raise TiffError("LZW stream does not start with a clear code")
        if code < len(table):
            entry = table[code]
        elif previous is not None:
            entry = previous + previous[:1]
        else:
            raise TiffError(f"LZW code {code} outside the table")
        out += entry
        if previous is not None:
            table.append(previous + entry[:1])
            if len(table) + 1 >= (1 << bits) and bits < _LZW_MAX_BITS:
                bits += 1
        previous = entry
    return bytes(out)
