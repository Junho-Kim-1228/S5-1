from __future__ import annotations

from pathlib import Path
from typing import Any

from common import TrainingError, count_files, log_info, log_step, log_warn


LABEL_EXTENSION = ".txt"


def _load_yaml(path: Path) -> dict[str, Any]:
    try:
        import yaml
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "PyYAML is required to validate YOLO data.yaml. Install requirements-train.txt."
        ) from exc

    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as exc:
        raise TrainingError(f"Failed to parse YAML file: {path}") from exc

    if not isinstance(data, dict):
        raise TrainingError(f"data.yaml must contain a YAML mapping: {path}")
    return data


def validate_workspace(workspace: Path) -> dict[str, Any]:
    log_step("validate workspace")
    if not workspace.exists():
        raise TrainingError(f"Workspace does not exist: {workspace}")
    if not workspace.is_dir():
        raise TrainingError(f"Workspace is not a directory: {workspace}")

    required_dirs = {
        "images/train": workspace / "images" / "train",
        "images/val": workspace / "images" / "val",
        "labels/train": workspace / "labels" / "train",
        "labels/val": workspace / "labels" / "val",
    }
    for label, path in required_dirs.items():
        if not path.exists():
            raise TrainingError(f"Required YOLO directory is missing: {label} ({path})")
        if not path.is_dir():
            raise TrainingError(f"Required YOLO path is not a directory: {label} ({path})")

    data_yaml = workspace / "data.yaml"
    if not data_yaml.exists():
        raise TrainingError(f"Required YOLO dataset config is missing: {data_yaml}")

    yaml_data = _load_yaml(data_yaml)
    image_extensions = {".bmp", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"}
    images_train_count = count_files(required_dirs["images/train"], image_extensions)
    images_val_count = count_files(required_dirs["images/val"], image_extensions)
    labels_train_count = count_files(required_dirs["labels/train"], {LABEL_EXTENSION})
    labels_val_count = count_files(required_dirs["labels/val"], {LABEL_EXTENSION})

    if images_train_count == 0:
        raise TrainingError(f"No training images were found in {required_dirs['images/train']}")
    if images_val_count == 0:
        raise TrainingError(f"No validation images were found in {required_dirs['images/val']}")
    if labels_train_count == 0:
        raise TrainingError(f"No training labels were found in {required_dirs['labels/train']}")
    if labels_val_count == 0:
        raise TrainingError(f"No validation labels were found in {required_dirs['labels/val']}")

    missing_yaml_keys = [key for key in ("train", "val", "names") if key not in yaml_data]
    if missing_yaml_keys:
        raise TrainingError(f"data.yaml is missing required keys: {', '.join(missing_yaml_keys)}")

    log_info(f"workspace: {workspace}")
    log_info(f"data.yaml: {data_yaml}")
    log_info(
        f"image counts: train={images_train_count}, val={images_val_count} | "
        f"label counts: train={labels_train_count}, val={labels_val_count}"
    )

    if images_train_count != labels_train_count:
        log_warn(
            "train image/label count mismatch detected. "
            "Ultralytics may still run if some images are intentionally unlabeled."
        )
    if images_val_count != labels_val_count:
        log_warn(
            "val image/label count mismatch detected. "
            "Ultralytics may still run if some images are intentionally unlabeled."
        )

    return {
        "data_yaml": data_yaml,
        "counts": {
            "images_train": images_train_count,
            "images_val": images_val_count,
            "labels_train": labels_train_count,
            "labels_val": labels_val_count,
        },
    }
