"""Deterministic equirectangular rasterisation of Natural Earth vector geometry.

Two rules, both fixed by output resolution and independent of vertex order:

* polygons (land, lakes) use even-odd scanline parity evaluated **at the cell
  centre**, so a cell is land exactly when its centre falls inside the polygon;
* polylines (coastline, rivers) use **cell intersection**: a cell is marked when
  the polyline enters it, found from the segment's crossings of the grid lines
  bounding that cell plus the cells containing its vertices. A segment lying
  exactly along a latitude grid line is assigned to the cell below it, which is
  arbitrary but fixed.

Both rules are pure functions of the source coordinates, so repeated builds from
the same cache produce identical masks.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Sequence

import numpy as np


@dataclass(frozen=True)
class Grid:
    """Global equirectangular cell-centre grid, traversed y-major then x-minor."""

    name: str
    lod_value: int
    width: int
    height: int

    def __post_init__(self) -> None:
        if self.width <= 0 or self.height <= 0:
            raise ValueError(f"{self.name}: grid dimensions must be positive")
        if self.width != self.height * 2:
            raise ValueError(
                f"{self.name}: equirectangular grid needs width == 2*height, "
                f"got {self.width}x{self.height}"
            )

    @property
    def step(self) -> float:
        return 360.0 / self.width

    @property
    def cells(self) -> int:
        return self.width * self.height

    def lons(self) -> np.ndarray:
        return -180.0 + (np.arange(self.width, dtype=np.float64) + 0.5) * self.step

    def lats(self) -> np.ndarray:
        return 90.0 - (np.arange(self.height, dtype=np.float64) + 0.5) * self.step


LOW = Grid("Low", 2, 180, 90)
MID = Grid("Mid", 1, 360, 180)
HIGH = Grid("High", 0, 720, 360)
GRIDS: tuple[Grid, ...] = (LOW, MID, HIGH)


def ragged_expand(counts: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Expand per-item repeat counts into (item index, offset-within-item) arrays."""
    counts = np.asarray(counts, dtype=np.int64)
    total = int(counts.sum())
    if total == 0:
        empty = np.zeros(0, dtype=np.int64)
        return empty, empty
    index = np.repeat(np.arange(counts.size, dtype=np.int64), counts)
    starts = np.zeros(counts.size, dtype=np.int64)
    np.cumsum(counts[:-1], out=starts[1:])
    offset = np.arange(total, dtype=np.int64) - starts[index]
    return index, offset


def _closed_edges(parts: Iterable[np.ndarray]) -> tuple[np.ndarray, ...]:
    """Edge endpoint arrays (x0, y0, x1, y1) for every ring, closing open rings."""
    xs0: list[np.ndarray] = []
    ys0: list[np.ndarray] = []
    xs1: list[np.ndarray] = []
    ys1: list[np.ndarray] = []
    for part in parts:
        ring = np.asarray(part, dtype=np.float64)
        if ring.ndim != 2 or ring.shape[1] != 2 or ring.shape[0] < 3:
            continue
        if ring[0, 0] != ring[-1, 0] or ring[0, 1] != ring[-1, 1]:
            ring = np.vstack([ring, ring[:1]])
        xs0.append(ring[:-1, 0])
        ys0.append(ring[:-1, 1])
        xs1.append(ring[1:, 0])
        ys1.append(ring[1:, 1])
    if not xs0:
        empty = np.zeros(0, dtype=np.float64)
        return empty, empty, empty, empty
    return (
        np.concatenate(xs0),
        np.concatenate(ys0),
        np.concatenate(xs1),
        np.concatenate(ys1),
    )


def polygon_mask(parts: Sequence[np.ndarray], grid: Grid) -> np.ndarray:
    """Even-odd point-in-polygon at every cell centre."""
    mask = np.zeros((grid.height, grid.width), dtype=bool)
    x0, y0, x1, y1 = _closed_edges(parts)
    if x0.size == 0:
        return mask
    lons = grid.lons()
    lats = grid.lats()
    step = grid.step
    # Bucket edges by the scanlines they can cross, padded by one row so the exact
    # half-open crossing test below decides membership rather than float rounding.
    y_min = np.minimum(y0, y1)
    y_max = np.maximum(y0, y1)
    row_lo = np.floor((90.0 - y_max) / step - 0.5).astype(np.int64)
    row_hi = np.ceil((90.0 - y_min) / step - 0.5).astype(np.int64) + 1
    np.clip(row_lo, 0, grid.height, out=row_lo)
    np.clip(row_hi, -1, grid.height - 1, out=row_hi)
    counts = np.maximum(row_hi - row_lo + 1, 0)
    edge_index, offset = ragged_expand(counts)
    if edge_index.size == 0:
        return mask
    rows = row_lo[edge_index] + offset
    order = np.argsort(rows, kind="stable")
    rows = rows[order]
    edge_index = edge_index[order]
    group_start = np.searchsorted(rows, np.arange(grid.height), side="left")
    group_end = np.searchsorted(rows, np.arange(grid.height), side="right")
    for row in range(grid.height):
        begin, end = int(group_start[row]), int(group_end[row])
        if begin == end:
            continue
        candidates = edge_index[begin:end]
        lat = lats[row]
        ey0 = y0[candidates]
        ey1 = y1[candidates]
        crossing = (ey0 > lat) != (ey1 > lat)
        if not crossing.any():
            continue
        selected = candidates[crossing]
        ay, by = y0[selected], y1[selected]
        ax, bx = x0[selected], x1[selected]
        crossings = ax + (lat - ay) * (bx - ax) / (by - ay)
        crossings.sort(kind="stable")
        parity = np.searchsorted(crossings, lons, side="left")
        mask[row] = (parity & 1).astype(bool)
    return mask


def _mark(mask: np.ndarray, cell_x: np.ndarray, cell_y: np.ndarray) -> None:
    height, width = mask.shape
    xi = np.mod(cell_x.astype(np.int64), width)
    yi = cell_y.astype(np.int64)
    keep = (yi >= 0) & (yi < height)
    if keep.any():
        mask[yi[keep], xi[keep]] = True


def polyline_mask(parts: Sequence[np.ndarray], grid: Grid) -> np.ndarray:
    """Mark every cell a polyline passes through (grid-line crossings + vertex cells)."""
    mask = np.zeros((grid.height, grid.width), dtype=bool)
    usable = [
        np.asarray(part, dtype=np.float64)
        for part in parts
        if np.asarray(part).ndim == 2 and np.asarray(part).shape[0] >= 2
    ]
    if not usable:
        return mask
    step = grid.step
    lengths = np.array([part.shape[0] for part in usable], dtype=np.int64)
    points = np.concatenate(usable)
    gx = (points[:, 0] + 180.0) / step
    gy = (90.0 - points[:, 1]) / step
    _mark(mask, np.floor(gx), np.floor(gy))
    ends = np.cumsum(lengths)
    same_part = np.ones(points.shape[0] - 1, dtype=bool)
    same_part[ends[:-1] - 1] = False
    x0, x1 = gx[:-1][same_part], gx[1:][same_part]
    y0, y1 = gy[:-1][same_part], gy[1:][same_part]
    # Antimeridian guard: a segment whose endpoints differ by more than half the
    # world is a wrap artefact, not a feature that genuinely spans the globe. Walking
    # it in grid space would paint a stripe of cells the whole way round through
    # longitude 0. Such segments are dropped from grid-line marking; their two vertex
    # cells are already marked above, so the feature is not lost, only the bogus
    # connection between the two sides. The locked Natural Earth layers split at the
    # antimeridian and contain none of these, and the count is exposed so a future
    # source that does not split cannot slip through unnoticed.
    wraps = np.abs(x1 - x0) > (grid.width / 2.0)
    keep = ~wraps
    x0, x1, y0, y1 = x0[keep], x1[keep], y0[keep], y1[keep]
    _mark_axis_crossings(mask, x0, y0, x1, y1, vertical=True)
    _mark_axis_crossings(mask, x0, y0, x1, y1, vertical=False)
    return mask


def count_antimeridian_wraps(parts: Sequence[np.ndarray]) -> int:
    """Segments spanning more than 180 degrees of longitude, i.e. wrap artefacts.

    Used to assert that a vector source splits its geometry at the antimeridian; see
    the guard in :func:`polyline_mask`.
    """
    total = 0
    for part in parts:
        array = np.asarray(part, dtype=np.float64)
        if array.ndim != 2 or array.shape[0] < 2:
            continue
        total += int((np.abs(np.diff(array[:, 0])) > 180.0).sum())
    return total


def _mark_axis_crossings(
    mask: np.ndarray,
    x0: np.ndarray,
    y0: np.ndarray,
    x1: np.ndarray,
    y1: np.ndarray,
    vertical: bool,
) -> None:
    """Mark the two cells on either side of each grid line the segments cross."""
    along0, along1 = (x0, x1) if vertical else (y0, y1)
    other0, other1 = (y0, y1) if vertical else (x0, x1)
    delta = along1 - along0
    lo = np.ceil(np.minimum(along0, along1))
    hi = np.floor(np.maximum(along0, along1))
    counts = np.floor(hi - lo + 1.0).astype(np.int64)
    np.clip(counts, 0, None, out=counts)
    counts[delta == 0.0] = 0
    segment, offset = ragged_expand(counts)
    if segment.size == 0:
        return
    line = lo[segment] + offset.astype(np.float64)
    fraction = (line - along0[segment]) / delta[segment]
    other = other0[segment] + fraction * (other1[segment] - other0[segment])
    other_cell = np.floor(other)
    if vertical:
        _mark(mask, line - 1.0, other_cell)
        _mark(mask, line, other_cell)
    else:
        _mark(mask, other_cell, line - 1.0)
        _mark(mask, other_cell, line)


def orthogonal_neighbor_any(mask: np.ndarray) -> np.ndarray:
    """True where any of the four orthogonal neighbours is True.

    Longitude wraps at the antimeridian; latitudes past the poles have no
    neighbour and therefore contribute nothing.
    """
    height, width = mask.shape
    result = np.zeros_like(mask)
    result |= np.roll(mask, 1, axis=1)
    result |= np.roll(mask, -1, axis=1)
    result[1:, :] |= mask[:-1, :]
    result[:-1, :] |= mask[1:, :]
    return result


def majority_downsample(
    codes: np.ndarray, factor: int, class_count: int, nodata: int = 0
) -> np.ndarray:
    """Majority class per factor x factor block; ties break to the lowest class code.

    ``nodata`` is excluded from the vote; a block with no valid class stays ``nodata``.
    """
    if factor < 1:
        raise ValueError("factor must be >= 1")
    height, width = codes.shape
    if height % factor or width % factor:
        raise ValueError(f"{height}x{width} is not divisible by factor {factor}")
    if factor == 1:
        return codes.astype(np.uint8, copy=True)
    out_h, out_w = height // factor, width // factor
    blocks = codes.reshape(out_h, factor, out_w, factor).transpose(0, 2, 1, 3)
    blocks = blocks.reshape(out_h * out_w, factor * factor)
    tally = np.zeros((blocks.shape[0], class_count + 1), dtype=np.int32)
    rows = np.arange(blocks.shape[0], dtype=np.int64)[:, None]
    np.add.at(tally, (rows, blocks.astype(np.int64)), 1)
    tally[:, nodata] = 0
    # argmax returns the first maximal index, i.e. the lowest class code on a tie.
    winner = tally.argmax(axis=1).astype(np.uint8)
    winner[tally.max(axis=1) == 0] = nodata
    return winner.reshape(out_h, out_w)
