from __future__ import annotations

from pathlib import Path


def resolve_path(value: str) -> Path:
    return Path(value).expanduser().resolve(strict=False)


def project_root() -> Path:
    return Path(__file__).resolve().parents[1]
