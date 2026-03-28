from pathlib import Path


def resolve_path(path_str: str) -> Path:
    return Path(path_str).expanduser().resolve()


def ensure_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    return path


def get_project_root() -> Path:
    return Path(__file__).resolve().parents[1]
