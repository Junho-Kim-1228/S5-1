import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from common.exceptions import CoilAIError
from common.path_utils import get_project_root, resolve_path


OFFICIAL_DETECTION_WEIGHT_PATTERN = re.compile(r"^yolo(?:v?\d+)[nslmx]\.pt$", re.IGNORECASE)


def _resolve_model_path(model: str | None) -> Path:
    project_root = get_project_root()

    if model:
        requested = Path(model)
        if requested.is_absolute():
            candidate = requested
        else:
            direct = resolve_path(model)
            candidate = direct if direct.exists() else project_root / model

        candidate = candidate.resolve(strict=False)
        if candidate.exists() and candidate.stat().st_size > 0:
            return candidate

        # Let Ultralytics download official detection checkpoints directly into
        # the project-local weights directory when a bare model ID is supplied.
        if requested == Path(requested.name) and OFFICIAL_DETECTION_WEIGHT_PATTERN.fullmatch(requested.name):
            return (project_root / "assets" / "weights" / requested.name).resolve(strict=False)

        raise CoilAIError(f"YOLO weights/model not found: {candidate}")

    candidates = [
        project_root / "assets" / "weights" / "yolov8n.pt",
        project_root / "assets" / "weights" / "yolov8l.pt",
        project_root / "yolov8n.pt",
        project_root / "yolov8l.pt",
    ]
    for candidate in candidates:
        if candidate.exists() and candidate.stat().st_size > 0:
            return candidate.resolve(strict=False)

    raise CoilAIError(
        "No YOLO weights found. Pass an official model ID such as yolo26m.pt, "
        "or place a local checkpoint under assets/weights."
    )


def _resolve_variant(weights: Path) -> str:
    stem = weights.stem.lower()
    if stem == "yolov8n":
        return "yolov8n_baseline"
    if stem == "yolov8l":
        return "yolov8l_baseline"
    return stem


def _resolve_device(device: str) -> str:
    if device != "auto":
        return device

    try:
        import torch
    except ImportError:
        return "cpu"

    return "0" if torch.cuda.is_available() else "cpu"


def _resolve_workers(workers: int | None) -> int:
    if workers is not None:
        return max(int(workers), 0)
    return 0 if os.name == "nt" else 8


def _resolve_conf_val(conf_val: float | None) -> float | None:
    if conf_val is None:
        return None
    if not (0.0 <= float(conf_val) <= 1.0):
        raise CoilAIError(f"YOLO validation confidence must be between 0 and 1: {conf_val}")
    return float(conf_val)


def _build_augmentation_config() -> dict[str, Any]:
    return {
        # Class-specific augmentation is handled offline in prepare_yolo_workspace.py.
        # Keep training-time augmentation disabled so only the intended samples are augmented.
        "fliplr": 0.0,
        "hsv_h": 0.0,
        "hsv_s": 0.0,
        "hsv_v": 0.0,
        "translate": 0.0,
        "flipud": 0.0,
        "degrees": 0.0,
        "scale": 0.0,
        "shear": 0.0,
        "perspective": 0.0,
        "mosaic": 0.0,
        "mixup": 0.0,
        "copy_paste": 0.0,
        "cutmix": 0.0,
        "erasing": 0.0,
        "auto_augment": None,
    }


@dataclass(frozen=True)
class YoloTrainConfig:
    weights: Path
    epochs: int
    imgsz: int
    batch: int
    device: str
    workers: int
    variant: str
    seed: int
    conf_val: float | None
    augmentation: dict[str, Any]


def build_yolo_train_config(
    *,
    model: str | None,
    epochs: int,
    imgsz: int,
    batch: int,
    device: str,
    seed: int,
    workers: int | None,
    conf_val: float | None,
) -> YoloTrainConfig:
    weights = _resolve_model_path(model)
    return YoloTrainConfig(
        weights=weights,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=_resolve_device(device),
        workers=_resolve_workers(workers),
        variant=_resolve_variant(weights),
        seed=seed,
        conf_val=_resolve_conf_val(conf_val),
        augmentation=_build_augmentation_config(),
    )
