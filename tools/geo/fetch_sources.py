"""Fail-closed download, integrity check, and zip extraction into .geo-cache."""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import ssl
import struct
import urllib.error
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterable

from lockfile import pin_source_sha256, source_md5, source_sha256

CACHE_NAME = ".geo-cache"
USER_AGENT = "WorldSim-geo-fetch/1.0"
REQUEST_HEADERS = {
    "User-Agent": USER_AGENT,
    "Accept-Encoding": "identity",
}
ABS_WIN_PATH = re.compile(r"^[A-Za-z]:")


class FetchError(RuntimeError):
    """Download or cache verification failed closed."""


def cache_root(here: Path | None = None) -> Path:
    root = Path(__file__).resolve().parent if here is None else here
    return (root / CACHE_NAME).resolve()


def source_archive_path(source: dict[str, Any], cache_dir: Path) -> Path:
    source_id = source["id"]
    filename = source["filename"]
    if (
        not isinstance(filename, str)
        or not filename
        or "/" in filename
        or "\\" in filename
        or filename in {".", ".."}
    ):
        raise FetchError(f"{source_id}: filename must be a basename without path separators")
    dest = (cache_dir / source_id / filename).resolve()
    if not _is_within(dest, cache_dir):
        raise FetchError(f"{source_id}: refused cache path outside {cache_dir}")
    return dest


def extract_dir(source: dict[str, Any], cache_dir: Path) -> Path:
    dest = (cache_dir / source["id"] / "extracted").resolve()
    if not _is_within(dest, cache_dir):
        raise FetchError(f"{source['id']}: refused extract path outside {cache_dir}")
    return dest


def unpublished_sha256_error(source: dict[str, Any]) -> FetchError:
    source_id = source["id"]
    url = source["url"]
    publication = source.get("sha256Publication") or "not-published-by-authority"
    md5 = source.get("md5")
    md5_line = f"\nPublished MD5 (not accepted as SHA-256 substitute): {md5}" if md5 else ""
    return FetchError(
        f"FETCH BLOCKED: source {source_id!r} has no SHA-256 in the lock "
        f"({publication}). Do not invent a digest. Obtain the object only from:\n"
        f"  {url}\n"
        f"Run `python tools/geo/build_geo.py bootstrap` to download that URL, "
        f"verify the locked byte length"
        f"{' and Figshare MD5' if md5 else ''}, compute SHA-256, and pin the lock. "
        f"Ordinary fetch refuses unpinned sources.{md5_line}"
    )


def verify_archive_bytes(source: dict[str, Any], data: bytes) -> str:
    digest = source_sha256(source)
    if digest is None:
        raise unpublished_sha256_error(source)
    expected_bytes = source.get("bytes")
    if expected_bytes is not None:
        if not isinstance(expected_bytes, int) or expected_bytes < 0:
            raise FetchError(f"{source['id']}: bytes must be a non-negative integer when set")
        if len(data) != expected_bytes:
            raise FetchError(
                f"{source['id']}: byte length mismatch: expected {expected_bytes}, got {len(data)}"
            )
    actual = hashlib.sha256(data).hexdigest()
    if actual != digest:
        raise FetchError(
            f"{source['id']}: SHA-256 mismatch: expected {digest}, got {actual}"
        )
    return actual


def verify_archive_file(source: dict[str, Any], path: Path) -> str:
    if not path.is_file():
        raise FetchError(f"{source['id']}: cache file missing: {path}")
    digest = source_sha256(source)
    if digest is None:
        raise unpublished_sha256_error(source)
    nbytes, actual, _md5 = hash_file(path)
    expected = source.get("bytes")
    if expected is not None:
        if not isinstance(expected, int) or expected < 0:
            raise FetchError(f"{source['id']}: bytes must be a non-negative integer when set")
        if nbytes != expected:
            raise FetchError(
                f"{source['id']}: byte length mismatch: expected {expected}, got {nbytes}"
            )
    if actual != digest:
        raise FetchError(
            f"{source['id']}: SHA-256 mismatch: expected {digest}, got {actual}"
        )
    return actual


UNIX_S_IFMT = 0o170000
UNIX_S_IFREG = 0o100000
UNIX_S_IFDIR = 0o040000
UNIX_S_IFLNK = 0o120000
CONTENT_RANGE_RE = re.compile(r"bytes\s+(\d+)-(\d+)/(\d+|\*)", re.I)


def zip_member_kind(info: zipfile.ZipInfo) -> str:
    name = info.filename.replace("\\", "/")
    unix_mode = (info.external_attr >> 16) & 0xFFFF
    fmt = unix_mode & UNIX_S_IFMT
    if fmt == UNIX_S_IFLNK:
        return "symlink"
    if fmt and fmt not in {UNIX_S_IFREG, UNIX_S_IFDIR}:
        return "special"
    if info.create_system == 3 and unix_mode:
        if fmt == UNIX_S_IFDIR or name.endswith("/"):
            return "dir"
        if fmt == UNIX_S_IFREG:
            return "file"
    if name.endswith("/") or info.is_dir() or (info.external_attr & 0x10):
        return "dir"
    return "file"


def parse_content_range(header: str) -> tuple[int, int, int | None]:
    match = CONTENT_RANGE_RE.match(header.strip())
    if not match:
        raise FetchError(f"unrecognized Content-Range: {header!r}")
    start, end = int(match.group(1)), int(match.group(2))
    complete = None if match.group(3) == "*" else int(match.group(3))
    if end < start:
        raise FetchError(f"invalid Content-Range: {header!r}")
    return start, end, complete


def assert_resume_content_range(already: int, expected: int | None, headers: Any) -> None:
    raw = headers.get("Content-Range") if hasattr(headers, "get") else None
    if not raw:
        raise FetchError("HTTP 206 resume without Content-Range; refusing to continue")
    start, _end, complete = parse_content_range(raw)
    if start != already:
        raise FetchError(f"Content-Range start {start} != already-downloaded {already}")
    if expected is not None and complete is not None and complete != expected:
        raise FetchError(
            f"Content-Range complete size {complete} != locked {expected}"
        )


def append_limited(
    chunk: bytes,
    total: int,
    expected: int | None,
    handle: Any,
    sha: Any,
    md5: Any,
) -> int:
    if expected is not None and total + len(chunk) > expected:
        raise FetchError(
            f"download exceeded locked {expected} bytes (have {total}, chunk {len(chunk)})"
        )
    handle.write(chunk)
    sha.update(chunk)
    md5.update(chunk)
    return total + len(chunk)


def _refuse_symlink_path(target: Path, root: Path) -> None:
    try:
        relative = target.relative_to(root)
    except ValueError as exc:
        raise FetchError(f"extract path escaped {root}: {target}") from exc
    cursor = root
    for part in relative.parts:
        cursor = cursor / part
        if cursor.is_symlink():
            raise FetchError(f"refusing extract through symlink {cursor}")


def unsafe_zip_member(name: str) -> bool:
    normalized = name.replace("\\", "/")
    if not normalized or normalized.endswith("/"):
        # directory entries are checked after stripping trailing slash
        normalized = normalized.rstrip("/")
        if not normalized:
            return True
    if normalized.startswith("/") or normalized.startswith("../") or "/../" in f"/{normalized}/":
        return True
    if ".." in Path(normalized).parts:
        return True
    if ABS_WIN_PATH.match(normalized) or normalized.startswith("//"):
        return True
    return False


def safe_extract_zip(archive: Path, dest: Path) -> None:
    dest = dest.resolve()
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as zf:
        for info in zf.infolist():
            _extract_zip_member(zf, info, dest, archive.name)


def _extract_zip_member(zf: zipfile.ZipFile, info: zipfile.ZipInfo, dest: Path, archive_name: str) -> Path | None:
    name = info.filename
    kind = zip_member_kind(info)
    if kind in {"symlink", "special"}:
        raise FetchError(f"{kind} refused in {archive_name}: {name!r}")
    if unsafe_zip_member(name):
        raise FetchError(f"path traversal refused in {archive_name}: {name!r}")
    relative = name.replace("\\", "/").rstrip("/")
    target = (dest / relative).resolve()
    if not _is_within(target, dest):
        raise FetchError(f"path traversal refused in {archive_name}: {name!r}")
    _refuse_symlink_path(target, dest)
    if kind == "dir":
        target.mkdir(parents=True, exist_ok=True)
        return target
    target.parent.mkdir(parents=True, exist_ok=True)
    _refuse_symlink_path(target.parent, dest)
    if target.exists() and target.is_symlink():
        raise FetchError(f"refusing to overwrite symlink {target}")
    with zf.open(info) as src, open(target, "wb") as out:
        shutil.copyfileobj(src, out)
    return target


def fetch_source(
    source: dict[str, Any],
    cache_dir: Path,
    downloader: Callable[[str], bytes] | None = None,
) -> Path:
    if source_sha256(source) is None:
        raise unpublished_sha256_error(source)
    url = source["url"]
    if not isinstance(url, str) or not url.startswith("https://"):
        raise FetchError(f"{source['id']}: refusing non-https URL: {url!r}")
    cache_dir = cache_dir.resolve()
    cache_dir.mkdir(parents=True, exist_ok=True)
    dest = source_archive_path(source, cache_dir)
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.is_file():
        verify_archive_file(source, dest)
        _extract_if_zip(source, dest, cache_dir)
        return dest
    locked = expected_bytes(source)
    digest = source_sha256(source)
    if downloader is not None:
        payload = downloader(url)
        verify_archive_bytes(source, payload)
        partial = dest.with_suffix(dest.suffix + ".partial")
        partial.write_bytes(payload)
        os.replace(partial, dest)
    else:
        partial = dest.with_suffix(dest.suffix + ".partial")
        try:
            nbytes, actual, _md5 = stream_download(url, partial, expected_bytes=locked)
        except FetchError:
            partial.unlink(missing_ok=True)
            raise
        if actual != digest:
            partial.unlink(missing_ok=True)
            raise FetchError(
                f"{source['id']}: SHA-256 mismatch: expected {digest}, got {actual}"
            )
        if nbytes != locked:
            partial.unlink(missing_ok=True)
            raise FetchError(
                f"{source['id']}: byte length mismatch: expected {locked}, got {nbytes}"
            )
        os.replace(partial, dest)
    _extract_if_zip(source, dest, cache_dir)
    return dest


def fetch_all(
    sources: Iterable[dict[str, Any]],
    cache_dir: Path,
    downloader: Callable[[str], bytes] | None = None,
) -> list[Path]:
    paths: list[Path] = []
    errors: list[str] = []
    for source in sources:
        try:
            paths.append(fetch_source(source, cache_dir, downloader=downloader))
        except FetchError as exc:
            errors.append(str(exc))
    if errors:
        raise FetchError("fetch failed closed:\n" + "\n\n".join(errors))
    return paths


def verify_cache(sources: Iterable[dict[str, Any]], cache_dir: Path) -> list[Path]:
    paths: list[Path] = []
    errors: list[str] = []
    for source in sources:
        try:
            if source_sha256(source) is None:
                raise unpublished_sha256_error(source)
            dest = source_archive_path(source, cache_dir)
            if not dest.is_file():
                raise FetchError(
                    f"{source['id']}: not in cache ({dest}). Run fetch after recording SHA-256."
                )
            verify_archive_file(source, dest)
            paths.append(dest)
        except FetchError as exc:
            errors.append(str(exc))
    if errors:
        raise FetchError("verify failed closed:\n" + "\n\n".join(errors))
    return paths


def default_download(url: str) -> bytes:
    raise FetchError(
        f"internal: full-body download is disabled; stream {url} with lock bytes instead"
    )


def _extract_if_zip(source: dict[str, Any], archive: Path, cache_dir: Path) -> None:
    fmt = str(source.get("format") or "")
    if "zip" not in fmt and archive.suffix.lower() != ".zip":
        return
    safe_extract_zip(archive, extract_dir(source, cache_dir))


def _is_within(child: Path, parent: Path) -> bool:
    try:
        child.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


GDAL_NODATA_TAG = 42113
_STREAM_CHUNK = 1024 * 1024
_BOOTSTRAP_TIMEOUT = 600


def expected_bytes(source: dict[str, Any]) -> int:
    value = source.get("bytes")
    if not isinstance(value, int) or value < 0:
        raise FetchError(f"{source.get('id', '?')}: bootstrap/fetch requires locked non-negative bytes")
    return value


def head_content_length(url: str) -> tuple[int, int | None]:
    if not url.startswith("https://"):
        raise FetchError(f"refusing non-https URL: {url!r}")
    request = urllib.request.Request(url, method="HEAD", headers=REQUEST_HEADERS)
    context = ssl.create_default_context()
    try:
        with urllib.request.urlopen(request, timeout=30, context=context) as response:
            status = getattr(response, "status", 200)
            raw = response.headers.get("Content-Length")
            length = int(raw) if raw and raw.isdigit() else None
            return status, length
    except urllib.error.HTTPError as exc:
        raise FetchError(
            f"HEAD {url} failed: HTTP {exc.code}. Check the official landing page; refusing to guess."
        ) from exc
    except urllib.error.URLError as exc:
        raise FetchError(f"HEAD {url} failed: {exc}") from exc


def hash_file(path: Path) -> tuple[int, str, str]:
    sha = hashlib.sha256()
    md5 = hashlib.md5()
    total = 0
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(_STREAM_CHUNK)
            if not chunk:
                break
            total += len(chunk)
            sha.update(chunk)
            md5.update(chunk)
    return total, sha.hexdigest(), md5.hexdigest()


def stream_download(url: str, partial: Path, expected_bytes: int | None = None, retries: int = 5) -> tuple[int, str, str]:
    if not url.startswith("https://"):
        raise FetchError(f"refusing non-https URL: {url!r}")
    sha = hashlib.sha256()
    md5 = hashlib.md5()
    total = 0
    partial.parent.mkdir(parents=True, exist_ok=True)
    if partial.exists():
        partial.unlink()
    last_error: FetchError | None = None
    context = ssl.create_default_context()
    for attempt in range(1, retries + 1):
        headers = dict(REQUEST_HEADERS)
        if total > 0:
            headers["Range"] = f"bytes={total}-"
        request = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=_BOOTSTRAP_TIMEOUT, context=context) as response:
                status = getattr(response, "status", 200)
                if total > 0 and status not in {206, 200}:
                    raise FetchError(f"resume GET {url} returned HTTP {status}")
                if total > 0 and status == 200:
                    sha = hashlib.sha256()
                    md5 = hashlib.md5()
                    total = 0
                    partial.unlink(missing_ok=True)
                elif total > 0 and status == 206:
                    assert_resume_content_range(total, expected_bytes, response.headers)
                encoding = (response.headers.get("Content-Encoding") or "identity").lower()
                if encoding not in {"", "identity"}:
                    raise FetchError(
                        f"refusing compressed GET ({encoding}) for {url}; want identity bytes matching the lock"
                    )
                raw_cl = response.headers.get("Content-Length")
                get_length = int(raw_cl) if raw_cl and raw_cl.isdigit() else None
                mode = "ab" if total > 0 and status == 206 else "wb"
                with partial.open(mode) as handle:
                    while True:
                        chunk = response.read(_STREAM_CHUNK)
                        if not chunk:
                            break
                        total = append_limited(chunk, total, expected_bytes, handle, sha, md5)
        except urllib.error.HTTPError as exc:
            raise FetchError(
                f"GET {url} failed: HTTP {exc.code}. Check the official landing page; refusing to guess."
            ) from exc
        except urllib.error.URLError as exc:
            last_error = FetchError(f"download failed: {url}: {exc}")
            print(f"retry {attempt}/{retries} after {last_error}", flush=True)
            continue
        if expected_bytes is not None and total == expected_bytes:
            return total, sha.hexdigest(), md5.hexdigest()
        if expected_bytes is not None and total < expected_bytes:
            last_error = FetchError(
                f"truncated GET for {url}: locked {expected_bytes}, read {total} (attempt {attempt})"
            )
            print(last_error, flush=True)
            continue
        if expected_bytes is not None and total > expected_bytes:
            partial.unlink(missing_ok=True)
            raise FetchError(
                f"GET body {total} bytes != locked {expected_bytes} for {url}. "
                f"GET Content-Length={get_length}. Check the official landing page; refusing to guess."
            )
        if get_length is not None and total != get_length and total == 0:
            last_error = FetchError(f"truncated GET for {url}: Content-Length {get_length}, read {total}")
            continue
        return total, sha.hexdigest(), md5.hexdigest()
    partial.unlink(missing_ok=True)
    raise last_error or FetchError(f"download failed: {url}")


def _require_bootstrap_integrity(source: dict[str, Any], nbytes: int, md5_digest: str) -> None:
    source_id = source["id"]
    locked = expected_bytes(source)
    if nbytes != locked:
        raise FetchError(
            f"{source_id}: byte length mismatch during bootstrap: locked {locked}, got {nbytes}. "
            f"Check {source.get('landingPage') or source['url']}; refusing to guess."
        )
    published_md5 = source_md5(source)
    if published_md5 is not None and md5_digest != published_md5:
        raise FetchError(
            f"{source_id}: MD5 mismatch during bootstrap: locked {published_md5}, got {md5_digest}"
        )


def bootstrap_source(
    source: dict[str, Any],
    cache_dir: Path,
    downloader: Callable[[str], bytes] | None = None,
    skip_head: bool = False,
    force: bool = False,
) -> dict[str, Any]:
    """Download or verify a source. Existing sha256 is not overwritten unless force=True."""
    source_id = source["id"]
    url = source["url"]
    if not isinstance(url, str) or not url.startswith("https://"):
        raise FetchError(f"{source_id}: refusing non-https URL: {url!r}")
    locked = expected_bytes(source)
    existing = source_sha256(source)
    if not skip_head and downloader is None and (existing is None or force):
        status, length = head_content_length(url)
        if status != 200:
            raise FetchError(
                f"{source_id}: HEAD status {status} for {url}. "
                f"Check {source.get('landingPage')}; refusing to guess."
            )
        if length is not None and length != locked:
            raise FetchError(
                f"{source_id}: authoritative Content-Length {length} != locked bytes {locked}. "
                f"Check {source.get('landingPage') or url}; refusing to guess."
            )
    cache_dir = cache_dir.resolve()
    dest = source_archive_path(source, cache_dir)
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.is_file() and not force:
        nbytes, digest, md5_digest = hash_file(dest)
        _require_bootstrap_integrity(source, nbytes, md5_digest)
    elif downloader is not None:
        payload = downloader(url)
        md5_digest = hashlib.md5(payload).hexdigest()
        digest = hashlib.sha256(payload).hexdigest()
        _require_bootstrap_integrity(source, len(payload), md5_digest)
        partial = dest.with_suffix(dest.suffix + ".partial")
        partial.write_bytes(payload)
        os.replace(partial, dest)
        nbytes = len(payload)
    else:
        nbytes, digest, md5_digest = _download_to_dest(source, dest, None)
        _require_bootstrap_integrity(source, nbytes, md5_digest)
    if existing is not None and digest != existing and not force:
        raise FetchError(
            f"{source_id}: computed SHA-256 {digest} != pinned {existing}; "
            f"refusing to overwrite (pass --force to re-pin)"
        )
    _extract_if_zip(source, dest, cache_dir)
    pin = {
        "date": datetime.now(timezone.utc).date().isoformat(),
        "method": "bootstrap-from-locked-https-url",
        "byteLengthVerified": True,
        "md5Verified": source_md5(source) is not None,
        "url": url,
        "bytes": nbytes,
    }
    return {
        "path": dest,
        "sha256": digest,
        "md5": md5_digest,
        "bytes": nbytes,
        "pin": pin,
        "alreadyPinned": existing is not None,
        "wrotePin": existing is None or force,
    }


def _download_to_dest(
    source: dict[str, Any],
    dest: Path,
    downloader: Callable[[str], bytes] | None,
) -> tuple[int, str, str]:
    locked = expected_bytes(source)
    url = source["url"]
    if downloader is not None:
        payload = downloader(url)
        md5_digest = hashlib.md5(payload).hexdigest()
        digest = hashlib.sha256(payload).hexdigest()
        _require_bootstrap_integrity(source, len(payload), md5_digest)
        partial = dest.with_suffix(dest.suffix + ".partial")
        partial.write_bytes(payload)
        os.replace(partial, dest)
        return len(payload), digest, md5_digest
    partial = dest.with_suffix(dest.suffix + ".partial")
    nbytes, digest, md5_digest = stream_download(url, partial, expected_bytes=locked)
    try:
        _require_bootstrap_integrity(source, nbytes, md5_digest)
    except FetchError:
        partial.unlink(missing_ok=True)
        raise
    os.replace(partial, dest)
    return nbytes, digest, md5_digest


def bootstrap_all(
    sources: Iterable[dict[str, Any]],
    cache_dir: Path,
    lock_file: Path | None = None,
    downloader: Callable[[str], bytes] | None = None,
    skip_head: bool = False,
    force: bool = False,
) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    errors: list[str] = []
    for source in sources:
        try:
            if downloader is None:
                print(f"bootstrap {source['id']} from {source['url']}", flush=True)
            result = bootstrap_source(
                source,
                cache_dir,
                downloader=downloader,
                skip_head=skip_head or downloader is not None,
                force=force,
            )
            if lock_file is not None and result["wrotePin"]:
                pin_source_sha256(lock_file, source["id"], result["sha256"], result["pin"])
            results.append({"id": source["id"], **result})
        except FetchError as exc:
            errors.append(str(exc))
    if errors:
        raise FetchError("bootstrap failed closed:\n" + "\n\n".join(errors))
    return results


def zip_member_names(archive: Path) -> list[str]:
    with zipfile.ZipFile(archive) as zf:
        return [name.replace("\\", "/") for name in zf.namelist()]


def safe_extract_member(archive: Path, member: str, dest: Path) -> Path:
    dest = dest.resolve()
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as zf:
        try:
            info = zf.getinfo(member)
        except KeyError as exc:
            raise FetchError(f"{member!r} not in {archive.name}") from exc
        extracted = _extract_zip_member(zf, info, dest, archive.name)
        if extracted is None:
            raise FetchError(f"failed to extract {member!r} from {archive.name}")
        return extracted


def read_geotiff_gdal_nodata(path: Path) -> str | None:
    """Read GeoTIFF GDAL_NODATA (tag 42113) without third-party GIS libraries."""
    with path.open("rb") as handle:
        header = handle.read(16)
        if len(header) < 8:
            return None
        if header[:2] == b"II":
            endian = "<"
        elif header[:2] == b"MM":
            endian = ">"
        else:
            return None
        magic = struct.unpack(endian + "H", header[2:4])[0]
        if magic == 42:
            ifd = struct.unpack(endian + "I", header[4:8])[0]
            handle.seek(ifd)
            count = struct.unpack(endian + "H", handle.read(2))[0]
            entry_size = 12
            count_size = 4
            offset_fmt, offset_size = "I", 4
        elif magic == 43:
            ifd = struct.unpack(endian + "Q", header[8:16])[0]
            handle.seek(ifd)
            count = struct.unpack(endian + "Q", handle.read(8))[0]
            entry_size = 20
            count_size = 8
            offset_fmt, offset_size = "Q", 8
        else:
            return None
        for _ in range(count):
            entry = handle.read(entry_size)
            if len(entry) < entry_size:
                return None
            tag, typ = struct.unpack(endian + "HH", entry[:4])
            nvals = struct.unpack(endian + ("Q" if count_size == 8 else "I"), entry[4:4 + count_size])[0]
            value_field = entry[-offset_size:]
            if tag != GDAL_NODATA_TAG:
                continue
            if typ != 2:
                return None
            if nvals <= offset_size:
                raw = value_field[:nvals]
            else:
                offset = struct.unpack(endian + offset_fmt, value_field)[0]
                pos = handle.tell()
                handle.seek(offset)
                raw = handle.read(nvals)
                handle.seek(pos)
            return raw.split(b"\x00", 1)[0].decode("ascii", "replace").strip() or None
        return None


def inspect_koppen_1991_2020(archive: Path, extract_to: Path) -> dict[str, Any]:
    names = zip_member_names(archive)
    period = [
        name
        for name in names
        if "1991_2020" in name.replace("\\", "/") and name.lower().endswith(".tif")
    ]
    classification = [
        name
        for name in period
        if "confidence" not in name.lower() and "koppen_geiger" in name.lower()
    ]
    if not classification:
        raise FetchError(
            "Köppen zip has no 1991_2020 classification GeoTIFF; members=" + ", ".join(names[:40])
        )
    preferred = [name for name in classification if "0p5" in name or "0p50" in name]
    chosen = preferred[0] if preferred else sorted(classification)[0]
    tif = safe_extract_member(archive, chosen, extract_to)
    nodata = read_geotiff_gdal_nodata(tif)
    return {
        "selectedLayer": chosen,
        "classificationLayers": sorted(classification),
        "periodTifs": sorted(period),
        "nodata": nodata,
        "extracted": str(tif),
    }

