from __future__ import annotations

from pathlib import Path
from typing import Any

from common import TrainingError, ensure_directory, log_info, log_progress, log_step, log_warn
from yolo.metrics import extract_metrics


def _normalize_device(device: str) -> str:
    try:
        import torch
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "PyTorch is required to train YOLO. Install requirements-train.txt."
        ) from exc

    requested = device.strip().lower()
    if requested == "auto":
        return "0" if torch.cuda.is_available() else "cpu"
    return device


def train_model(
    *,
    args: Any,
    out_dir: Path,
    data_yaml: Path,
    base_model_path: Path,
) -> dict[str, Any]:
    try:
        from ultralytics import YOLO
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "ultralytics is required to train YOLO. Install requirements-train.txt."
        ) from exc

    log_step("train model")
    train_project_dir = out_dir / args.project_name
    ensure_directory(train_project_dir)

    device = _normalize_device(args.device)
    log_info(f"base model: {base_model_path}")
    log_info(f"train project dir: {train_project_dir}")
    log_info(f"device: {device}")
    log_info(f"epochs: {args.epochs}")
    log_progress(0)

    # TODO: dataset transform
    # TODO: model selection
    model = YOLO(str(base_model_path))
    model.train(
        data=str(data_yaml),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        workers=args.workers,
        project=str(train_project_dir),
        name="run",
        exist_ok=True,
        device=device,
        seed=args.seed,
        verbose=True,
    )

    trainer = getattr(model, "trainer", None)
    if trainer is None:
        raise TrainingError("Ultralytics trainer state is unavailable after training.")

    save_dir = Path(str(getattr(trainer, "save_dir", train_project_dir / "run"))).resolve(strict=False)
    best_checkpoint = Path(str(getattr(trainer, "best", save_dir / "weights" / "best.pt"))).resolve(
        strict=False
    )
    if not best_checkpoint.exists():
        fallback_checkpoint = save_dir / "weights" / "last.pt"
        if fallback_checkpoint.exists():
            best_checkpoint = fallback_checkpoint.resolve(strict=False)
        else:
            raise TrainingError(f"Trained YOLO checkpoint was not found under {save_dir}")

    log_progress(90)
    log_info(f"best checkpoint: {best_checkpoint}")

    val_metrics: dict[str, Any] = {}
    try:
        validation_model = YOLO(str(best_checkpoint))
        val_result = validation_model.val(data=str(data_yaml), imgsz=args.imgsz, split="val")
        val_metrics = extract_metrics(val_result)
    except Exception as exc:
        log_warn(f"validation metrics could not be collected after training: {exc}")

    return {
        "train_dir": str(save_dir),
        "best_checkpoint": best_checkpoint,
        "metrics": val_metrics,
    }
