from __future__ import annotations

import shutil
from pathlib import Path

from common import TrainingError, find_latest_file, log_info, log_progress, log_step


def export_onnx(*, best_checkpoint: Path, out_dir: Path, imgsz: int) -> Path:
    try:
        from ultralytics import YOLO
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "ultralytics is required to export YOLO ONNX. Install requirements-train.txt."
        ) from exc

    log_step("export onnx")
    log_info(f"checkpoint for export: {best_checkpoint}")

    export_model = YOLO(str(best_checkpoint))
    try:
        exported = export_model.export(format="onnx", imgsz=imgsz, simplify=False)
    except Exception as exc:
        raise TrainingError(f"YOLO ONNX export failed: {exc}") from exc

    export_path = Path(str(exported)).resolve(strict=False)
    if not export_path.exists():
        fallback_export = find_latest_file(best_checkpoint.parent.parent, "*.onnx")
        if fallback_export is not None:
            export_path = fallback_export.resolve(strict=False)
    if not export_path.exists():
        raise TrainingError(f"YOLO export reported success but file was not found: {export_path}")

    expected_path = out_dir / "yolo.onnx"
    if export_path != expected_path:
        shutil.copy2(export_path, expected_path)

    if not expected_path.exists():
        raise TrainingError(f"Expected ONNX output was not created: {expected_path}")

    # TODO: export graph verification
    log_info(f"exported onnx: {expected_path}")
    log_progress(100)
    return expected_path
