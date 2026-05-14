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


class PatchcoreInferenceModel(nn.Module):
    def __init__(
        self,
        feature_extractor: ResNet50FeatureExtractor,
        selected_indices: torch.Tensor,
        memory_bank: torch.Tensor,
        output_size: int,
    ) -> None:
        super().__init__()
        self.feature_extractor = feature_extractor
        self.register_buffer("selected_indices", selected_indices)
        self.register_buffer("memory_bank", memory_bank)
        self.output_size = output_size
        total_pixels = output_size * output_size
        self.topk_count = max(1, math.ceil(0.01 * total_pixels))

    def _aggregate_score(self, anomaly_map: torch.Tensor) -> torch.Tensor:
        flat = anomaly_map.flatten(1)
        top_values, _ = torch.topk(flat, k=self.topk_count, dim=1)
        return top_values.mean(dim=1, keepdim=True)

    def forward(self, x: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        layer2, layer3 = self.feature_extractor(x)
        layer3 = F.interpolate(layer3, size=layer2.shape[-2:], mode="bilinear", align_corners=False)
        embedding = torch.cat([layer2, layer3], dim=1)
        embedding = torch.index_select(embedding, 1, self.selected_indices)

        batch_size, channels, height, width = embedding.shape
        patches = embedding.permute(0, 2, 3, 1).reshape(batch_size * height * width, channels)
        patch_norm = (patches * patches).sum(dim=1, keepdim=True)
        bank_norm = (self.memory_bank * self.memory_bank).sum(dim=1).unsqueeze(0)
        distance_sq = (patch_norm + bank_norm - (2.0 * (patches @ self.memory_bank.transpose(0, 1)))).clamp_min(0.0)
        anomaly_map = distance_sq.min(dim=1).values.sqrt().reshape(batch_size, 1, height, width)
        anomaly_map = F.interpolate(
            anomaly_map,
            size=(self.output_size, self.output_size),
            mode="bilinear",
            align_corners=False,
        )
        anomaly_score = self._aggregate_score(anomaly_map)
        return anomaly_score, anomaly_map


class PatchcoreModel(AnomalyModelBase):
    def __init__(
        self,
        *,
        image_size: int,
        device: str,
        embedding_dim: int,
        memory_bank_size: int,
        seed: int,
    ) -> None:
        self.image_size = image_size
        self.device = torch.device(device)
        self.embedding_dim = embedding_dim
        self.memory_bank_size = memory_bank_size
        self.seed = seed

        self.feature_extractor = ResNet50FeatureExtractor().to(self.device)
        self.feature_extractor.eval()
        for parameter in self.feature_extractor.parameters():
            parameter.requires_grad_(False)

        self.selected_indices: torch.Tensor | None = None
        self.memory_bank: torch.Tensor | None = None
        self._warned_cpu_fallback = False

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
            layer3 = F.interpolate(layer3, size=layer2.shape[-2:], mode="bilinear", align_corners=False)
            embedding = torch.cat([layer2, layer3], dim=1)
            selected = self._ensure_selected_indices(embedding.shape[1]).to(embedding.device)
            return torch.index_select(embedding, 1, selected)

    def fit(self, train_loader) -> None:
        generator = torch.Generator(device="cpu").manual_seed(self.seed)
        memory_bank: torch.Tensor | None = None
        priorities: torch.Tensor | None = None

        for batch in train_loader:
            images = batch["image"].to(self.device, non_blocking=True)
            embedding = self._embed(images).cpu().to(dtype=torch.float32)
            patches = embedding.permute(0, 2, 3, 1).reshape(-1, embedding.shape[1])
            patch_priorities = torch.rand(patches.shape[0], generator=generator)

            if memory_bank is None:
                memory_bank = patches
                priorities = patch_priorities
            else:
                memory_bank = torch.cat([memory_bank, patches], dim=0)
                priorities = torch.cat([priorities, patch_priorities], dim=0)

            if memory_bank.shape[0] > self.memory_bank_size:
                keep = torch.topk(priorities, k=self.memory_bank_size, largest=True).indices
                memory_bank = memory_bank.index_select(0, keep)
                priorities = priorities.index_select(0, keep)

        if memory_bank is None or memory_bank.shape[0] == 0:
            raise TrainingError("train_loader is empty. at least one good sample is required.")

        self.memory_bank = memory_bank.contiguous()

    def _ensure_fitted(self) -> None:
        if self.selected_indices is None or self.memory_bank is None:
            raise TrainingError("PatchCore model is not fitted yet.")

    def _pairwise_min_distance(
        self,
        query: torch.Tensor,
        *,
        query_chunk_size: int = 1024,
        bank_chunk_size: int = 8192,
    ) -> torch.Tensor:
        self._ensure_fitted()
        assert self.memory_bank is not None

        distances: list[torch.Tensor] = []
        for query_start in range(0, query.shape[0], query_chunk_size):
            query_end = min(query_start + query_chunk_size, query.shape[0])
            query_chunk = query[query_start:query_end]
            query_norm = (query_chunk * query_chunk).sum(dim=1, keepdim=True)
            min_distance_sq: torch.Tensor | None = None

            for bank_start in range(0, self.memory_bank.shape[0], bank_chunk_size):
                bank_end = min(bank_start + bank_chunk_size, self.memory_bank.shape[0])
                bank_chunk = self.memory_bank[bank_start:bank_end].to(query_chunk.device)
                bank_norm = (bank_chunk * bank_chunk).sum(dim=1).unsqueeze(0)
                distance_sq = (
                    query_norm + bank_norm - (2.0 * (query_chunk @ bank_chunk.transpose(0, 1)))
                ).clamp_min(0.0)
                chunk_min = distance_sq.min(dim=1).values
                min_distance_sq = chunk_min if min_distance_sq is None else torch.minimum(min_distance_sq, chunk_min)

            assert min_distance_sq is not None
            distances.append(min_distance_sq)

        return torch.cat(distances, dim=0).sqrt()

    def _compute_anomaly_map(self, images: torch.Tensor) -> torch.Tensor:
        self._ensure_fitted()
        embedding = self._embed(images)
        batch_size, channels, height, width = embedding.shape
        query = embedding.permute(0, 2, 3, 1).reshape(batch_size * height * width, channels)

        try:
            distance = self._pairwise_min_distance(query)
        except RuntimeError as exc:
            message = str(exc).lower()
            if "cuda" not in message and "cublas" not in message and "out of memory" not in message:
                raise
            if not self._warned_cpu_fallback:
                log_warn("GPU PatchCore distance failed; retrying anomaly scoring on CPU.")
                self._warned_cpu_fallback = True
            distance = self._pairwise_min_distance(query.cpu())

        anomaly_map = distance.reshape(batch_size, 1, height, width)
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
        assert self.memory_bank is not None
        export_model = PatchcoreInferenceModel(
            feature_extractor=self.feature_extractor.cpu(),
            selected_indices=self.selected_indices.cpu(),
            memory_bank=self.memory_bank.cpu(),
            output_size=image_size,
        ).eval()
        dummy_input = torch.randn(1, 3, image_size, image_size, dtype=torch.float32)
        export_kwargs = {
            "input_names": ["image"],
            "output_names": ["anomaly_score", "anomaly_map"],
            "dynamic_axes": {
                "image": {0: "batch"},
                "anomaly_score": {0: "batch"},
                "anomaly_map": {0: "batch"},
            },
            "opset_version": 17,
            "do_constant_folding": True,
        }

        try:
            torch.onnx.export(
                export_model,
                dummy_input,
                str(path),
                **export_kwargs,
            )
        except RuntimeError as exc:
            message = str(exc)
            if "2GiB limit" not in message:
                raise
            torch.onnx.export(
                export_model,
                dummy_input,
                str(path),
                dynamo=True,
                external_data=True,
                optimize=False,
                artifacts_dir=str(path.parent),
                **export_kwargs,
            )

    def save_state(self, path: Path) -> None:
        self._ensure_fitted()
        assert self.memory_bank is not None
        torch.save(
            {
                "backbone": "resnet50",
                "feature_layers": ["layer2", "layer3"],
                "embedding_dim": self.embedding_dim,
                "image_size": self.image_size,
                "memory_bank_size": self.memory_bank_size,
                "seed": self.seed,
                "selected_indices": self.selected_indices.cpu(),
                "memory_bank": self.memory_bank.cpu(),
                "feature_extractor": self.feature_extractor.cpu().state_dict(),
            },
            path,
        )

    def model_info(self) -> dict[str, object]:
        return {
            "model": "patchcore",
            "backbone": "resnet50",
            "pretrained": True,
            "feature_layers": ["layer2", "layer3"],
            "embedding_dim": self.embedding_dim,
            "memory_bank_size": self.memory_bank_size,
            "device": str(self.device),
        }
