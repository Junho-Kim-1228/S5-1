from __future__ import annotations

import math
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from torchvision.models import ResNet50_Weights, resnet50

from anoma.models.base import AnomalyModelBase
from common import TrainingError, log_warn


class ResNet50FeatureExtractor(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        backbone = resnet50(weights=ResNet50_Weights.DEFAULT)
        self.conv1 = backbone.conv1
        self.bn1 = backbone.bn1
        self.relu = backbone.relu
        self.maxpool = backbone.maxpool
        self.layer1 = backbone.layer1
        self.layer2 = backbone.layer2
        self.layer3 = backbone.layer3

    def forward(self, x: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        x = self.conv1(x)
        x = self.bn1(x)
        x = self.relu(x)
        x = self.maxpool(x)
        x = self.layer1(x)
        layer2 = self.layer2(x)
        layer3 = self.layer3(layer2)
        return layer2, layer3


class PadimInferenceModel(nn.Module):
    def __init__(
        self,
        feature_extractor: ResNet50FeatureExtractor,
        selected_indices: torch.Tensor,
        mean: torch.Tensor,
        inv_covariance: torch.Tensor,
        output_size: int,
    ) -> None:
        super().__init__()
        self.feature_extractor = feature_extractor
        self.register_buffer("selected_indices", selected_indices)
        self.register_buffer("mean", mean)
        self.register_buffer("inv_covariance", inv_covariance)
        self.output_size = output_size
        total_pixels = output_size * output_size
        self.topk_count = max(1, math.ceil(0.01 * total_pixels))

    def _aggregate_score(self, anomaly_map: torch.Tensor) -> torch.Tensor:
        flat = anomaly_map.flatten(1)
        top_values, _ = torch.topk(flat, k=self.topk_count, dim=1)
        return top_values.mean(dim=1, keepdim=True)

    def forward(self, x: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        layer2, layer3 = self.feature_extractor(x)
        target_size = layer2.shape[-2:]
        layer2 = F.interpolate(layer2, size=target_size, mode="bilinear", align_corners=False)
        layer3 = F.interpolate(layer3, size=target_size, mode="bilinear", align_corners=False)
        embedding = torch.cat([layer2, layer3], dim=1)
        embedding = torch.index_select(embedding, 1, self.selected_indices)

        batch_size, channels, height, width = embedding.shape
        diff = embedding - self.mean.unsqueeze(0)
        diff = diff.permute(0, 2, 3, 1).reshape(batch_size, height * width, channels)

        left = torch.matmul(diff.unsqueeze(-2), self.inv_covariance.unsqueeze(0))
        distance = torch.matmul(left, diff.unsqueeze(-1)).squeeze(-1).squeeze(-1)
        anomaly_map = distance.clamp_min(0.0).sqrt().reshape(batch_size, 1, height, width)
        anomaly_map = F.interpolate(
            anomaly_map,
            size=(self.output_size, self.output_size),
            mode="bilinear",
            align_corners=False,
        )
        anomaly_score = self._aggregate_score(anomaly_map)
        return anomaly_score, anomaly_map


class PadimModel(AnomalyModelBase):
    def __init__(
        self,
        *,
        image_size: int,
        device: str,
        embedding_dim: int,
        covariance_eps: float,
        seed: int,
    ) -> None:
        self.image_size = image_size
        self.device = torch.device(device)
        self.embedding_dim = embedding_dim
        self.covariance_eps = covariance_eps
        self.seed = seed

        self.feature_extractor = ResNet50FeatureExtractor().to(self.device)
        self.feature_extractor.eval()
        for parameter in self.feature_extractor.parameters():
            parameter.requires_grad_(False)

        self.selected_indices: torch.Tensor | None = None
        self.mean: torch.Tensor | None = None
        self.inv_covariance: torch.Tensor | None = None
        self._warned_cpu_fallback = False

    def _invert_covariance_matrices(
        self,
        covariance: torch.Tensor,
        *,
        chunk_size: int = 256,
    ) -> torch.Tensor:
        inverse_chunks: list[torch.Tensor] = []

        for start in range(0, covariance.shape[0], chunk_size):
            end = min(start + chunk_size, covariance.shape[0])
            chunk = covariance[start:end]

            try:
                chol = torch.linalg.cholesky(chunk)
                inv_chunk = torch.cholesky_inverse(chol)
            except RuntimeError:
                try:
                    inv_chunk = torch.linalg.inv(chunk)
                except RuntimeError:
                    inv_chunk = torch.linalg.pinv(chunk)

            inverse_chunks.append(inv_chunk)

        return torch.cat(inverse_chunks, dim=0)

    @staticmethod
    def _score_from_maps_numpy(anomaly_maps: np.ndarray) -> np.ndarray:
        if anomaly_maps.ndim != 3:
            raise TrainingError("expected anomaly maps with shape (N, H, W).")
        flat = anomaly_maps.reshape(anomaly_maps.shape[0], -1)
        topk_count = max(1, math.ceil(0.01 * flat.shape[1]))
        partition_index = flat.shape[1] - topk_count
        topk = np.partition(flat, partition_index, axis=1)[:, partition_index:]
        return topk.mean(axis=1).astype(np.float32)

    def _ensure_selected_indices(self, total_channels: int) -> torch.Tensor:
        if self.selected_indices is None:
            if self.embedding_dim > total_channels:
                raise TrainingError(
                    f"embedding_dim={self.embedding_dim} exceeds feature width {total_channels}."
                )
            generator = torch.Generator(device="cpu").manual_seed(self.seed)
            indices = torch.randperm(total_channels, generator=generator)[: self.embedding_dim]
            self.selected_indices = indices.to(dtype=torch.long)
        return self.selected_indices

    def _embed(self, images: torch.Tensor) -> torch.Tensor:
        with torch.no_grad():
            layer2, layer3 = self.feature_extractor(images)
            target_size = layer2.shape[-2:]
            layer2 = F.interpolate(layer2, size=target_size, mode="bilinear", align_corners=False)
            layer3 = F.interpolate(layer3, size=target_size, mode="bilinear", align_corners=False)
            embedding = torch.cat([layer2, layer3], dim=1)
            selected = self._ensure_selected_indices(embedding.shape[1]).to(embedding.device)
            return torch.index_select(embedding, 1, selected)

    def fit(self, train_loader) -> None:
        sample_count = 0
        sum_embeddings: torch.Tensor | None = None
        sum_outer: torch.Tensor | None = None
        feature_height = 0
        feature_width = 0

        for batch in train_loader:
            images = batch["image"].to(self.device, non_blocking=True)
            embedding = self._embed(images).cpu().to(dtype=torch.float64)
            batch_size, channels, height, width = embedding.shape
            feature_height, feature_width = height, width
            embedding = embedding.reshape(batch_size, channels, height * width)

            if sum_embeddings is None:
                sum_embeddings = torch.zeros((channels, height * width), dtype=torch.float64)
                sum_outer = torch.zeros((height * width, channels, channels), dtype=torch.float64)

            sum_embeddings += embedding.sum(dim=0)
            sum_outer += torch.einsum("bcl,bdl->lcd", embedding, embedding)
            sample_count += batch_size

        if sample_count == 0 or sum_embeddings is None or sum_outer is None:
            raise TrainingError("train_loader is empty. at least one good sample is required.")

        mean = sum_embeddings / sample_count
        mean_vectors = mean.transpose(0, 1)
        covariance = (sum_outer / sample_count) - (
            mean_vectors.unsqueeze(-1) * mean_vectors.unsqueeze(-2)
        )
        eye = torch.eye(self.embedding_dim, dtype=torch.float64).unsqueeze(0)
        covariance = covariance + (self.covariance_eps * eye)

        inv_covariance = self._invert_covariance_matrices(covariance)

        self.mean = mean.reshape(self.embedding_dim, feature_height, feature_width).to(dtype=torch.float32)
        self.inv_covariance = inv_covariance.to(dtype=torch.float32)

    def _ensure_fitted(self) -> None:
        if self.selected_indices is None or self.mean is None or self.inv_covariance is None:
            raise TrainingError("PaDiM model is not fitted yet.")

    def _mahalanobis_distance_chunked(
        self,
        diff: torch.Tensor,
        inv_covariance: torch.Tensor,
        *,
        chunk_size: int = 1024,
    ) -> torch.Tensor:
        distances: list[torch.Tensor] = []
        num_locations = diff.shape[1]
        for start in range(0, num_locations, chunk_size):
            end = min(start + chunk_size, num_locations)
            diff_chunk = diff[:, start:end, :]
            inv_chunk = inv_covariance[start:end, :, :]
            projected = torch.einsum("blc,lcd->bld", diff_chunk, inv_chunk)
            distances.append((projected * diff_chunk).sum(dim=-1))
        return torch.cat(distances, dim=1)

    def _compute_anomaly_map(self, images: torch.Tensor) -> torch.Tensor:
        self._ensure_fitted()
        embedding = self._embed(images)
        batch_size, channels, height, width = embedding.shape
        diff = embedding - self.mean.to(images.device).unsqueeze(0)
        diff = diff.permute(0, 2, 3, 1).reshape(batch_size, height * width, channels)

        try:
            distance = self._mahalanobis_distance_chunked(
                diff,
                self.inv_covariance.to(images.device),
            )
        except RuntimeError as exc:
            message = str(exc).lower()
            if "cuda" not in message and "cublas" not in message and "out of memory" not in message:
                raise
            if not self._warned_cpu_fallback:
                log_warn("GPU Mahalanobis distance failed; retrying anomaly scoring on CPU.")
                self._warned_cpu_fallback = True
            diff_cpu = diff.cpu()
            distance = self._mahalanobis_distance_chunked(diff_cpu, self.inv_covariance.cpu())

        anomaly_map = distance.clamp_min(0.0).sqrt().reshape(batch_size, 1, height, width)
        return F.interpolate(
            anomaly_map,
            size=(self.image_size, self.image_size),
            mode="bilinear",
            align_corners=False,
        )

    def predict_scores(self, eval_loader) -> dict[str, object]:
        self._ensure_fitted()
        scores: list[float] = []
        labels: list[int] = []
        paths: list[str] = []
        heatmaps: list[np.ndarray] = []

        with torch.no_grad():
            for batch in eval_loader:
                images = batch["image"].to(self.device, non_blocking=True)
                anomaly_map = self._compute_anomaly_map(images)
                anomaly_maps_np = anomaly_map.squeeze(1).cpu().numpy()
                image_scores = self._score_from_maps_numpy(anomaly_maps_np)

                scores.extend(image_scores.tolist())
                labels.extend(int(label) for label in batch["label"])
                paths.extend(batch["path"])
                heatmaps.extend(anomaly_maps_np)

        return {
            "scores": np.asarray(scores, dtype=np.float32),
            "labels": np.asarray(labels, dtype=np.int64),
            "paths": paths,
            "heatmaps": np.asarray(heatmaps, dtype=np.float32),
        }

    def export_onnx(self, path: Path, image_size: int) -> None:
        self._ensure_fitted()
        export_model = PadimInferenceModel(
            feature_extractor=self.feature_extractor.cpu(),
            selected_indices=self.selected_indices.cpu(),
            mean=self.mean.cpu(),
            inv_covariance=self.inv_covariance.cpu(),
            output_size=image_size,
        ).eval()
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
        )

    def save_state(self, path: Path) -> None:
        self._ensure_fitted()
        torch.save(
            {
                "backbone": "resnet50",
                "feature_layers": ["layer2", "layer3"],
                "embedding_dim": self.embedding_dim,
                "image_size": self.image_size,
                "covariance_eps": self.covariance_eps,
                "seed": self.seed,
                "selected_indices": self.selected_indices.cpu(),
                "mean": self.mean.cpu(),
                "inv_covariance": self.inv_covariance.cpu(),
                "feature_extractor": self.feature_extractor.cpu().state_dict(),
            },
            path,
        )

    def model_info(self) -> dict[str, object]:
        return {
            "model": "padim",
            "backbone": "resnet50",
            "pretrained": True,
            "feature_layers": ["layer2", "layer3"],
            "embedding_dim": self.embedding_dim,
            "device": str(self.device),
        }
