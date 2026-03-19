from __future__ import annotations

import argparse
from pathlib import Path
from typing import Sequence

from common import TrainingError, add_workspace_out_args, project_root, resolve_path


DEFAULT_MODEL_CANDIDATES = (
    "assets/weights/yolo11n.pt",
    "assets/weights/yolov8n.pt",
    "yolo11n.pt",
    "yolov8n.pt",
)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Train a YOLO detector and export it to ONNX for the WPF training UI."
    )
    add_workspace_out_args(parser)
    parser.add_argument(
        "--model",
        default=None,
        help="Base YOLO weight file. Defaults to assets/weights/yolo11n.pt or yolov8n.pt.",
    )
    parser.add_argument("--epochs", type=int, default=10, help="Training epochs.")
    parser.add_argument("--imgsz", type=int, default=640, help="Input image size.")
    parser.add_argument("--batch", type=int, default=16, help="Training batch size.")
    parser.add_argument("--workers", type=int, default=4, help="Dataloader worker count.")
    parser.add_argument(
        "--device",
        default="auto",
        help="Training device. Examples: auto, cpu, 0, 0,1.",
    )
    parser.add_argument("--seed", type=int, default=42, help="Random seed.")
    parser.add_argument(
        "--project-name",
        default="yolo_train",
        help="Directory name used under the output folder for training artifacts.",
    )
    return parser.parse_args(argv)


def resolve_base_model(model_arg: str | None) -> Path:
    if model_arg:
        return resolve_path(model_arg)

    root_dir = project_root()
    for relative_path in DEFAULT_MODEL_CANDIDATES:
        candidate = (root_dir / relative_path).resolve(strict=False)
        if candidate.exists():
            return candidate

    checked = ", ".join(DEFAULT_MODEL_CANDIDATES)
    raise TrainingError(
        "No local YOLO base weights were found. "
        f"Checked: explicit --model, {checked}."
    )
