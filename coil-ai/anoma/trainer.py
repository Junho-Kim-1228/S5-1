from __future__ import annotations

import numpy as np
import torch

from anoma.config import AnomaConfig
from anoma.exporter import export_artifacts, save_debug_artifacts, save_inference_config
from anoma.metrics import compute_image_metrics, compute_score_distribution
from anoma.models.patchcore import PatchcoreModel
from anoma.models.padim import PadimModel
from anoma.workspace import build_dataloaders, prepare_dataset, validate_workspace
from common import log_info, log_progress, log_step


def _resolve_device(device: str) -> str:
    requested = device.strip().lower()
    if requested == "auto":
        return "cuda" if torch.cuda.is_available() else "cpu"
    return requested


def _build_model(config: AnomaConfig):
    device = _resolve_device(config.device)
    if config.model == "patchcore":
        return PatchcoreModel(
            image_size=config.image_size,
            device=device,
            embedding_dim=config.embedding_dim,
            memory_bank_size=config.memory_bank_size,
            seed=config.seed,
        )
    return PadimModel(
        image_size=config.image_size,
        device=device,
        embedding_dim=config.embedding_dim,
        covariance_eps=config.covariance_eps,
        seed=config.seed,
    )


def run_training(config: AnomaConfig) -> dict[str, object]:
    log_step("validate workspace")
    workspace_info = validate_workspace(config.workspace)
    raw_counts = workspace_info["counts"]
    log_info(
        "raw usable samples: "
        f"total={raw_counts['usable_total']} "
        f"normal={raw_counts['normal']} "
        f"defect={raw_counts['defect']} "
        f"excluded_review_needed={raw_counts['excluded_review_needed']}"
    )

    log_step("create dataset")
    dataset_info = prepare_dataset(
        workspace=config.workspace,
        samples=workspace_info["samples"],
        dataset_name=config.dataset_name,
        val_ratio=config.val_ratio,
        seed=config.seed,
    )
    dataset_counts = dataset_info["counts"]
    log_info(
        "dataset counts: "
        f"train_good={dataset_counts['train_good']} "
        f"val_good={dataset_counts['val_good']} "
        f"val_defect={dataset_counts['val_defect']}"
    )
    log_progress(20)

    log_step("build dataloaders")
    train_loader, eval_loader = build_dataloaders(
        train_samples=dataset_info["train_samples"],
        eval_samples=dataset_info["eval_samples"],
        image_size=config.image_size,
        batch_size=config.batch_size,
        num_workers=config.num_workers,
    )
    log_progress(40)

    model = _build_model(config)
    log_info(f"model info: {model.model_info()}")

    log_step("fit model")
    model.fit(train_loader)
    log_progress(70)

    log_step("evaluate model")
    predictions = model.predict_scores(eval_loader)
    metrics = compute_image_metrics(
        labels=np.asarray(predictions["labels"], dtype=np.int64),
        scores=np.asarray(predictions["scores"], dtype=np.float64),
    )
    distribution = compute_score_distribution(
        labels=np.asarray(predictions["labels"], dtype=np.int64),
        scores=np.asarray(predictions["scores"], dtype=np.float64),
    )
    log_info(f"metrics: {metrics}")
    log_info(f"score distribution: {distribution}")

    log_step("save debug outputs")
    debug_artifacts = save_debug_artifacts(
        out_dir=config.out_dir,
        predictions=predictions,
        metrics=metrics,
        distribution=distribution,
    )
    inference_config = save_inference_config(
        out_dir=config.out_dir,
        model=model,
        image_size=config.image_size,
        metrics=metrics,
    )
    log_progress(85)

    if config.skip_export:
        log_step("skip export")
        log_info("skip export enabled; metrics and debug outputs only.")
        artifacts = {"onnx": None, "state": None}
    else:
        artifacts = export_artifacts(model=model, out_dir=config.out_dir, image_size=config.image_size)
    log_progress(100)

    return {
        "model": model,
        "metrics": metrics,
        "dataset": dataset_counts,
        "artifacts": artifacts,
        "debug_artifacts": debug_artifacts,
        "inference_config": inference_config,
        "dataset_root": dataset_info["dataset_root"],
        "manifest_path": dataset_info["manifest_path"],
    }
