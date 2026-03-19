from __future__ import annotations

from pathlib import Path
from typing import Any

from anoma.adapters import build_adapter
from anoma.metrics import extract_test_metrics
from common import TrainingError, ensure_directory, log_info, log_progress, log_step, log_warn


def _select_accelerator(device: str) -> str:
    try:
        import torch
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "PyTorch is required to train anomaly models. Install requirements-train.txt."
        ) from exc

    requested = device.strip().lower()
    if requested == "auto":
        return "gpu" if torch.cuda.is_available() else "cpu"
    if requested in {"cuda", "gpu"}:
        return "gpu"
    return "cpu"


def _resolve_best_model_path(engine: Any) -> Path | None:
    raw_best_model_path = getattr(engine, "best_model_path", None)
    if not raw_best_model_path:
        trainer = getattr(engine, "trainer", None)
        checkpoint_callback = getattr(trainer, "checkpoint_callback", None)
        raw_best_model_path = getattr(checkpoint_callback, "best_model_path", None)

    if not raw_best_model_path:
        return None

    best_model_path = Path(str(raw_best_model_path)).resolve(strict=False)
    return best_model_path if best_model_path.exists() else None


def train_model(*, args: Any, workspace: Path, out_dir: Path) -> dict[str, Any]:
    try:
        from anomalib.engine import Engine
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "anomalib is required to train anomaly models. Install requirements-train.txt."
        ) from exc

    log_step("train model")
    adapter = build_adapter(args.model)
    datamodule = adapter.create_datamodule(workspace, args)
    model = adapter.create_model(args)

    train_root = out_dir / args.project_name
    ensure_directory(train_root)

    accelerator = _select_accelerator(args.device)
    log_info(f"train project dir: {train_root}")
    log_info(f"accelerator: {accelerator}")
    log_info(f"epochs: {args.epochs}")
    log_progress(0)

    engine = Engine(
        default_root_dir=str(train_root),
        max_epochs=args.epochs,
        accelerator=accelerator,
        devices=1,
        deterministic=True,
        log_every_n_steps=1,
    )

    try:
        engine.fit(model=model, datamodule=datamodule)
    except Exception as exc:
        raise TrainingError(f"Anomaly training failed: {exc}") from exc

    best_model_path = _resolve_best_model_path(engine)

    metrics: dict[str, Any] = {}
    try:
        test_result = engine.test(
            model=model,
            datamodule=datamodule,
            ckpt_path=str(best_model_path) if best_model_path else None,
        )
        metrics = extract_test_metrics(test_result)
    except Exception as exc:
        log_warn(
            "anomaly test metrics could not be collected. "
            f"This can happen when the validation set contains only normal images: {exc}"
        )

    log_progress(90)
    return {
        "adapter": adapter,
        "engine": engine,
        "model": model,
        "best_model_path": best_model_path,
        "metrics": metrics,
    }
