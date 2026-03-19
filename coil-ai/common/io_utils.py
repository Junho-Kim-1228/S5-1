from __future__ import annotations

from pathlib import Path


IMAGE_EXTENSIONS = {".bmp", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"}


def ensure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def count_files(root: Path, extensions: set[str]) -> int:
    return sum(1 for path in root.rglob("*") if path.is_file() and path.suffix.lower() in extensions)


def count_images(root: Path) -> int:
    return count_files(root, IMAGE_EXTENSIONS)


def find_latest_file(root: Path, pattern: str) -> Path | None:
    candidates = sorted(root.rglob(pattern), key=lambda path: path.stat().st_mtime, reverse=True)
    return candidates[0] if candidates else None
