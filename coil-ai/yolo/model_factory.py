from __future__ import annotations

from pathlib import Path

from common import TrainingError, get_project_root, resolve_path


def resolve_model_reference(model_value: str | None) -> Path:
    if model_value:
        return resolve_path(model_value)

    project_root = get_project_root()
    candidates = [
        project_root / "assets" / "weights" / "yolov8n.pt",
        project_root / "assets" / "weights" / "yolov8l.pt",
        project_root / "yolov8n.pt",
        project_root / "yolov8l.pt",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate.resolve(strict=False)

    raise TrainingError(
        "no YOLO model reference was found. pass --model, or place yolov8n.pt / yolov8l.pt in assets/weights."
    )


def _register_custom_modules() -> None:
    try:
        from ultralytics.nn import tasks as yolo_tasks
    except ImportError:
        return

    try:
        from yolo.models.modules import C2FRVB
    except Exception:
        return

    if getattr(yolo_tasks, "C2FRVB", None) is None:
        setattr(yolo_tasks, "C2FRVB", C2FRVB)


def create_model(model_reference: Path):
    try:
        from ultralytics import YOLO
    except ImportError as exc:
        raise TrainingError(
            "ultralytics is required to run YOLO training. install requirements-train.txt first."
        ) from exc

    _register_custom_modules()
    return YOLO(str(model_reference))
