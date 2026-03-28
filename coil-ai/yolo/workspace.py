from pathlib import Path
from common.exceptions import WorkspaceValidationError


IMAGE_EXTENSIONS = {".bmp", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"}


def _count_images(path: Path) -> int:
    return sum(1 for p in path.rglob("*") if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS)


def _count_labels(path: Path) -> int:
    return sum(1 for p in path.rglob("*.txt") if p.is_file())


def validate_yolo_workspace(workspace: Path) -> dict:
    required = [
        workspace / "data.yaml",
        workspace / "images" / "train",
        workspace / "images" / "val",
        workspace / "labels" / "train",
        workspace / "labels" / "val",
    ]

    missing = [str(p) for p in required if not p.exists()]
    if missing:
        raise WorkspaceValidationError(
            f"Invalid YOLO workspace. Missing paths: {missing}"
        )

    train_images = _count_images(workspace / "images" / "train")
    val_images = _count_images(workspace / "images" / "val")
    train_labels = _count_labels(workspace / "labels" / "train")
    val_labels = _count_labels(workspace / "labels" / "val")

    if train_images == 0 or val_images == 0:
        raise WorkspaceValidationError("Invalid YOLO workspace. Train/val images must not be empty.")
    if train_images != train_labels:
        raise WorkspaceValidationError(
            f"Train image/label count mismatch: images={train_images}, labels={train_labels}"
        )
    if val_images != val_labels:
        raise WorkspaceValidationError(
            f"Val image/label count mismatch: images={val_images}, labels={val_labels}"
        )

    return {
        "train_images": train_images,
        "val_images": val_images,
        "train_labels": train_labels,
        "val_labels": val_labels,
    }
