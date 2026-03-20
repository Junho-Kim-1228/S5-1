from __future__ import annotations

from pathlib import Path
from typing import Any

from common import TrainingError, count_images, log_info, log_step


def validate_workspace(workspace: Path) -> dict[str, Any]:
    log_step("validate workspace")
    if not workspace.exists():
        raise TrainingError(f"Workspace does not exist: {workspace}")
    if not workspace.is_dir():
        raise TrainingError(f"Workspace is not a directory: {workspace}")

    train_dir = workspace / "train"
    val_dir = workspace / "val"
    for label, path in {"train": train_dir, "val": val_dir}.items():
        if not path.exists():
            raise TrainingError(f"Required anomaly directory is missing: {label} ({path})")
        if not path.is_dir():
            raise TrainingError(f"Required anomaly path is not a directory: {label} ({path})")

    train_count = count_images(train_dir)
    val_count = count_images(val_dir)
    if train_count == 0:
        raise TrainingError(f"No training images were found in {train_dir}")
    if val_count == 0:
        raise TrainingError(f"No validation images were found in {val_dir}")

    log_info(f"workspace: {workspace}")
    log_info(f"image counts: train={train_count}, val={val_count}")
    return {"train_dir": train_dir, "val_dir": val_dir, "counts": {"train": train_count, "val": val_count}}
