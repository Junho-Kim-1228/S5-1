from __future__ import annotations

from pathlib import Path
import shutil

from common.exceptions import CoilAIError
from common.logging_utils import get_logger
from common.logging_utils import log_info, log_progress, log_step, log_warn
from common.path_utils import ensure_dir, resolve_path
from common.seed import set_global_seed
from common.summary import build_train_summary, save_train_summary, utc_now_iso
from yolo.config import build_yolo_train_config
from yolo.exporter import export_yolo_to_onnx
from yolo.metrics import extract_yolo_metrics
from yolo.workspace import validate_yolo_workspace

logger = get_logger(__name__)


def _resolve_best_weights(train_results, artifacts_dir: Path) -> Path | None:
    save_dir = getattr(train_results, "save_dir", None)
    if save_dir:
        weights_dir = Path(str(save_dir)) / "weights"
        for name in ("best.pt", "last.pt"):
            candidate = weights_dir / name
            if candidate.exists():
                return candidate.resolve(strict=False)

    weights_dir = artifacts_dir / "train" / "weights"
    for name in ("best.pt", "last.pt"):
        candidate = weights_dir / name
        if candidate.exists():
            return candidate.resolve(strict=False)

    return None


def run_yolo_training(
    *,
    workspace: str,
    out_dir: str,
    model: str | None = None,
    epochs: int = 150,
    imgsz: int = 1024,
    batch: int = 4,
    device: str = "auto",
    seed: int = 42,
    workers: int | None = None,
    conf_val: float | None = None,
) -> None:
    workspace_path = resolve_path(workspace)
    out_path = ensure_dir(resolve_path(out_dir))
    started_at = utc_now_iso()
    export_path = out_path / "yolo.onnx"

    config = build_yolo_train_config(
        model=model,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        seed=seed,
        workers=workers,
        conf_val=conf_val,
    )

    try:
        from ultralytics import YOLO
    except ImportError as exc:
        raise CoilAIError(
            "ultralytics is required to run YOLO training. Install requirements-train.txt in your venv."
        ) from exc

    try:
        log_step(logger, "validate workspace")
        workspace_info = validate_yolo_workspace(workspace_path)
        data_yaml = workspace_path / "data.yaml"

        set_global_seed(config.seed)

        log_step(logger, "train model")
        log_info(logger, "Workspace: %s", workspace_path)
        log_info(logger, "Output: %s", out_path)
        log_info(logger, "Weights: %s", config.weights)
        log_info(logger, "Device: %s", config.device)
        log_info(logger, "Workers: %s", config.workers)
        log_info(logger, "Val confidence: %s", config.conf_val)
        log_info(logger, "Augmentation: %s", config.augmentation)
        use_one_to_many_head = config.variant.startswith("yolo26")
        if use_one_to_many_head:
            log_info(
                logger,
                "YOLO26 compatibility: end2end=False for validation and legacy ONNX output",
            )
        log_info(
            logger,
            "Workspace counts: train_images=%s val_images=%s train_labels=%s val_labels=%s",
            workspace_info["train_images"],
            workspace_info["val_images"],
            workspace_info["train_labels"],
            workspace_info["val_labels"],
        )
        log_progress(logger, 0)

        model_obj = YOLO(str(config.weights))
        artifacts_dir = out_path / "artifacts"
        train_kwargs = {
            "data": str(data_yaml),
            "epochs": config.epochs,
            "imgsz": config.imgsz,
            "batch": config.batch,
            "device": config.device,
            "workers": config.workers,
            "plots": False,
            "project": str(artifacts_dir),
            "name": "train",
            "exist_ok": True,
            "verbose": True,
            **config.augmentation,
        }
        if use_one_to_many_head:
            train_kwargs["end2end"] = False

        train_results = model_obj.train(
            **train_kwargs,
        )

        best_weights = _resolve_best_weights(train_results, artifacts_dir)
        eval_model = YOLO(str(best_weights)) if best_weights else model_obj
        if best_weights:
            log_info(logger, "Best weights: %s", best_weights)
            shutil.copy2(best_weights, out_path / "best.pt")
        else:
            log_warn(logger, "Best checkpoint was not found. Using in-memory trained model for val/export.")

        log_progress(logger, 90)
        val_kwargs = {
            "data": str(data_yaml),
            "workers": config.workers,
            "plots": False,
        }
        if config.conf_val is not None:
            val_kwargs["conf"] = config.conf_val
        if use_one_to_many_head:
            val_kwargs["end2end"] = False
        val_results = eval_model.val(**val_kwargs)
        metrics = extract_yolo_metrics(val_results)

        log_step(logger, "export onnx")
        export_yolo_to_onnx(
            eval_model,
            export_path,
            imgsz=config.imgsz,
            end2end=False,
        )
        log_progress(logger, 100)

        summary = build_train_summary(
            model_type="yolo",
            workspace=str(workspace_path),
            out_dir=str(out_path),
            started_at=started_at,
            finished_at=utc_now_iso(),
            success=True,
            metrics=metrics,
            export_path=str(export_path),
            notes=[],
            extras={
                "variant": config.variant,
                "weights": str(config.weights),
                "epochs": config.epochs,
                "imgsz": config.imgsz,
                "batch": config.batch,
                "device": config.device,
                "workers": config.workers,
                "conf_val": config.conf_val,
                "augmentation": config.augmentation,
                "end2end": False,
                "train_save_dir": str(train_results.save_dir) if hasattr(train_results, "save_dir") else None,
                "best_weights": str(best_weights) if best_weights else None,
            },
        )
        log_step(logger, "save summary")
        save_train_summary(out_path / "train_summary.json", summary)
        logger.info("[DONE] YOLO training completed successfully")
    except Exception as exc:
        try:
            summary = build_train_summary(
                model_type="yolo",
                workspace=str(workspace_path),
                out_dir=str(out_path),
                started_at=started_at,
                finished_at=utc_now_iso(),
                success=False,
                metrics={},
                export_path=str(export_path) if export_path.exists() else None,
                notes=[f"error={exc}"],
                extras={
                    "variant": config.variant,
                    "weights": str(config.weights),
                    "epochs": config.epochs,
                    "imgsz": config.imgsz,
                    "batch": config.batch,
                    "device": config.device,
                    "workers": config.workers,
                    "conf_val": config.conf_val,
                    "augmentation": config.augmentation,
                },
            )
            log_step(logger, "save summary")
            save_train_summary(out_path / "train_summary.json", summary)
        except Exception as summary_exc:
            log_warn(logger, "Failed to save failure summary: %s", summary_exc)
        raise
