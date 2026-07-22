from __future__ import annotations

import os
from pathlib import Path
from typing import Any

import numpy as np
import torch
import torch.nn as nn

from anoma.models.base import AnomalyModelBase
from common import TrainingError, log_info


class DinomalyInferenceModel(nn.Module):
    """Expose a stable ONNX contract independent of Anomalib internals."""

    def __init__(self, model: nn.Module) -> None:
        super().__init__()
        self.model = model

    def forward(self, image: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        predictions = self.model(image)
        anomaly_score = predictions.pred_score
        if anomaly_score.ndim == 1:
            anomaly_score = anomaly_score.unsqueeze(1)
        return anomaly_score, predictions.anomaly_map


class DinomalyModel(AnomalyModelBase):
    """Adapter around Anomalib 2.x Dinomaly using the coil-ai model contract."""

    def __init__(
        self,
        *,
        image_size: int,
        device: str,
        encoder_name: str,
        bottleneck_dropout: float,
        decoder_depth: int,
        max_steps: int,
        learning_rate: float,
        seed: int,
    ) -> None:
        self.image_size = image_size
        self.device = torch.device(device)
        self.encoder_name = encoder_name
        self.bottleneck_dropout = bottleneck_dropout
        self.decoder_depth = decoder_depth
        self.max_steps = max_steps
        self.learning_rate = learning_rate
        self.seed = seed

        if decoder_depth < 8:
            raise TrainingError(
                "Dinomaly decoder_depth must be at least 8 for the default two-group feature fusion."
            )

        # Keep the large pretrained encoder cache local to coil-ai. The folder is
        # ignored by git through assets/weights/* and can be copied for offline use.
        project_root = Path(__file__).resolve().parents[2]
        hf_home = project_root / "assets" / "weights" / "huggingface"
        hf_home.mkdir(parents=True, exist_ok=True)
        os.environ.setdefault("HF_HOME", str(hf_home))

        try:
            from anomalib.models import Dinomaly as AnomalibDinomaly
        except (ImportError, ModuleNotFoundError) as exc:
            raise TrainingError(
                "Dinomaly requires Anomalib 2.x. Activate .venv_dinomaly and "
                "install requirements-dinomaly.txt."
            ) from exc

        try:
            self.module = AnomalibDinomaly(
                encoder_name=encoder_name,
                bottleneck_dropout=bottleneck_dropout,
                decoder_depth=decoder_depth,
                precision="float32",
                pre_processor=False,
                post_processor=False,
                evaluator=False,
                visualizer=False,
            )
        except Exception as exc:
            raise TrainingError(
                "failed to initialize Dinomaly. The first run needs network access "
                "to download the pretrained DINOv2 encoder weights: "
                f"{exc}"
            ) from exc

        self.module.to(self.device)
        self.core_model: nn.Module = self.module.model

    def fit(self, train_loader) -> None:
        try:
            from anomalib.models.image.dinomaly.components import StableAdamW, WarmCosineScheduler
        except (ImportError, ModuleNotFoundError) as exc:
            raise TrainingError("Anomalib Dinomaly optimizer components are unavailable.") from exc

        parameters = self.module.trainable_modules.parameters()
        optimizer = StableAdamW(
            [{"params": parameters}],
            lr=self.learning_rate,
            betas=(0.9, 0.999),
            weight_decay=1e-4,
            amsgrad=True,
            eps=1e-8,
        )
        warmup_steps = min(100, max(0, self.max_steps - 1))
        scheduler = WarmCosineScheduler(
            optimizer,
            base_value=self.learning_rate,
            final_value=self.learning_rate * 0.1,
            total_iters=self.max_steps,
            warmup_iters=warmup_steps,
        )

        self.core_model.train()
        global_step = 0
        last_loss = float("nan")
        while global_step < self.max_steps:
            saw_batch = False
            for batch in train_loader:
                saw_batch = True
                images = batch["image"].to(self.device, non_blocking=True)
                optimizer.zero_grad(set_to_none=True)
                loss = self.core_model(images, global_step=global_step)
                if not torch.isfinite(loss):
                    raise TrainingError(f"Dinomaly loss became non-finite at step {global_step + 1}.")
                loss.backward()
                torch.nn.utils.clip_grad_norm_(self.module.trainable_modules.parameters(), max_norm=0.1)
                optimizer.step()
                scheduler.step()

                global_step += 1
                last_loss = float(loss.detach().cpu())
                if global_step == 1 or global_step % 50 == 0 or global_step == self.max_steps:
                    log_info(
                        f"Dinomaly step {global_step}/{self.max_steps} "
                        f"loss={last_loss:.6f} lr={optimizer.param_groups[0]['lr']:.8f}"
                    )
                if global_step >= self.max_steps:
                    break

            if not saw_batch:
                raise TrainingError("train_loader is empty. at least one good sample is required.")

        log_info(f"Dinomaly fitting complete: steps={global_step} final_loss={last_loss:.6f}")

    def predict_scores(self, eval_loader) -> dict[str, Any]:
        self.core_model.eval()
        scores: list[float] = []
        labels: list[int] = []
        paths: list[str] = []
        heatmaps: list[np.ndarray] = []

        with torch.no_grad():
            for batch in eval_loader:
                images = batch["image"].to(self.device, non_blocking=True)
                predictions = self.core_model(images)
                batch_scores = predictions.pred_score.detach().float().cpu().reshape(-1)
                batch_maps = predictions.anomaly_map.detach().float().cpu().squeeze(1)

                scores.extend(float(value) for value in batch_scores)
                labels.extend(int(value) for value in batch["label"])
                paths.extend(str(value) for value in batch["path"])
                heatmaps.extend(batch_maps.numpy())

        return {
            "scores": np.asarray(scores, dtype=np.float32),
            "labels": np.asarray(labels, dtype=np.int64),
            "paths": paths,
            "heatmaps": np.asarray(heatmaps, dtype=np.float32),
        }

    def export_onnx(self, path: Path, image_size: int) -> None:
        self.core_model.cpu().eval()
        export_model = DinomalyInferenceModel(self.core_model).eval()
        dummy_input = torch.randn(1, 3, image_size, image_size, dtype=torch.float32)

        torch.onnx.export(
            export_model,
            dummy_input,
            str(path),
            input_names=["image"],
            output_names=["anomaly_score", "anomaly_map"],
            dynamic_axes={
                "image": {0: "batch"},
                "anomaly_score": {0: "batch"},
                "anomaly_map": {0: "batch"},
            },
            opset_version=17,
            do_constant_folding=True,
            dynamo=False,
        )

    def save_state(self, path: Path) -> None:
        self.core_model.cpu()
        torch.save(
            {
                "model": "dinomaly",
                "encoder_name": self.encoder_name,
                "image_size": self.image_size,
                "bottleneck_dropout": self.bottleneck_dropout,
                "decoder_depth": self.decoder_depth,
                "max_steps": self.max_steps,
                "learning_rate": self.learning_rate,
                "seed": self.seed,
                "state_dict": self.module.state_dict(),
            },
            path,
        )

    def model_info(self) -> dict[str, Any]:
        return {
            "model": "dinomaly",
            "backbone": self.encoder_name,
            "pretrained": True,
            "architecture": "vit_encoder_mlp_bottleneck_transformer_decoder",
            "bottleneck_dropout": self.bottleneck_dropout,
            "decoder_depth": self.decoder_depth,
            "max_steps": self.max_steps,
            "learning_rate": self.learning_rate,
            "image_score": "top_1_percent_mean",
            "device": str(self.device),
        }
