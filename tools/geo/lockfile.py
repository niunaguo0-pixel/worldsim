"""Canonical lock loading and deterministic geo buildId derivation."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
from typing import Any

LOCK_NAME = "sources.lock.json"
BUILD_ID_PREFIX = "geo-v1-"
BUILD_ID_HEX_CHARS = 16
EPHEMERAL_PIN_KEYS = ("date",)
REQUIRED_SOURCE_FIELDS = (
    "id",
    "product",
    "version",
    "url",
    "filename",
    "license",
    "crs",
    "selectedLayers",
)


class LockError(ValueError):
    """Lock file is missing, malformed, or incomplete."""


def lock_path(here: Path | None = None) -> Path:
    root = Path(__file__).resolve().parent if here is None else here
    return root / LOCK_NAME


def load_lock(path: Path | None = None) -> dict[str, Any]:
    target = path or lock_path()
    if not target.is_file():
        raise LockError(f"lock file missing: {target}")
    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise LockError(f"lock file is not valid JSON: {target}") from exc
    if not isinstance(data, dict):
        raise LockError("lock root must be an object")
    validate_lock(data)
    return data


def validate_lock(lock: dict[str, Any]) -> None:
    sources = lock.get("sources")
    if not isinstance(sources, list) or not sources:
        raise LockError("lock.sources must be a non-empty array")
    seen: set[str] = set()
    for index, source in enumerate(sources):
        if not isinstance(source, dict):
            raise LockError(f"lock.sources[{index}] must be an object")
        missing = [field for field in REQUIRED_SOURCE_FIELDS if field not in source]
        if missing:
            raise LockError(f"lock.sources[{index}] missing fields: {', '.join(missing)}")
        source_id = source["id"]
        if not isinstance(source_id, str) or not source_id:
            raise LockError(f"lock.sources[{index}].id must be a non-empty string")
        if source_id in seen:
            raise LockError(f"duplicate source id: {source_id}")
        seen.add(source_id)
        layers = source["selectedLayers"]
        if not isinstance(layers, list) or not layers:
            raise LockError(f"{source_id}: selectedLayers must be a non-empty array")
        url = source["url"]
        if not isinstance(url, str) or not url.startswith("https://"):
            raise LockError(f"{source_id}: url must be an https URL")
    if "derivation" not in lock or not isinstance(lock["derivation"], dict):
        raise LockError("lock.derivation must be an object")


def _lock_for_digest(lock: dict[str, Any]) -> dict[str, Any]:
    """Copy lock without ephemeral pin metadata (sha256Pin.date). Source digests stay."""
    normalized = json.loads(json.dumps(lock))
    sources = normalized.get("sources")
    if isinstance(sources, list):
        normalized["sources"] = sorted(sources, key=lambda item: item["id"])
        for source in normalized["sources"]:
            pin = source.get("sha256Pin")
            if isinstance(pin, dict):
                for key in EPHEMERAL_PIN_KEYS:
                    pin.pop(key, None)
    return normalized


def canonical_lock_bytes(lock: dict[str, Any]) -> bytes:
    """Deterministic UTF-8 JSON: sorted object keys, compact separators, sources sorted by id."""
    normalized = _lock_for_digest(lock)
    return json.dumps(
        normalized,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    ).encode("utf-8")


def lock_sha256(lock: dict[str, Any]) -> str:
    return hashlib.sha256(canonical_lock_bytes(lock)).hexdigest()


def build_id_from_lock(lock: dict[str, Any]) -> str:
    return BUILD_ID_PREFIX + lock_sha256(lock)[:BUILD_ID_HEX_CHARS]


def save_lock(path: Path, lock: dict[str, Any]) -> None:
    """Atomically replace the lock file (write *.partial, then os.replace)."""
    validate_lock(lock)
    text = json.dumps(lock, indent=2, ensure_ascii=False) + "\n"
    tmp = path.with_name(path.name + ".partial")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)


def pin_source_sha256(
    path: Path,
    source_id: str,
    sha256: str,
    pin: dict[str, Any],
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    digest = sha256.strip().lower()
    if len(digest) != 64 or any(ch not in "0123456789abcdef" for ch in digest):
        raise LockError(f"{source_id}: cannot pin invalid sha256")
    lock = load_lock(path)
    for source in lock["sources"]:
        if source["id"] != source_id:
            continue
        source["sha256"] = digest
        source["sha256Pin"] = pin
        source["sha256BlockFetch"] = False
        if extra:
            source.update(extra)
        save_lock(path, lock)
        return load_lock(path)
    raise LockError(f"source not in lock: {source_id}")


def update_source_fields(path: Path, source_id: str, fields: dict[str, Any]) -> dict[str, Any]:
    lock = load_lock(path)
    for source in lock["sources"]:
        if source["id"] != source_id:
            continue
        source.update(fields)
        save_lock(path, lock)
        return load_lock(path)
    raise LockError(f"source not in lock: {source_id}")


def source_md5(source: dict[str, Any]) -> str | None:
    value = source.get("md5")
    if not isinstance(value, str) or not value:
        return None
    digest = value.strip().lower()
    if len(digest) != 32 or any(ch not in "0123456789abcdef" for ch in digest):
        raise LockError(f"{source.get('id', '?')}: md5 must be 32 lowercase hex chars")
    return digest


def source_sha256(source: dict[str, Any]) -> str | None:
    value = source.get("sha256")
    if value is None:
        return None
    if not isinstance(value, str) or not value:
        return None
    digest = value.strip().lower()
    if digest in {"null", "none", "unpublished", "unknown"}:
        return None
    if len(digest) != 64 or any(ch not in "0123456789abcdef" for ch in digest):
        raise LockError(f"{source.get('id', '?')}: sha256 must be 64 lowercase hex chars or null")
    return digest
