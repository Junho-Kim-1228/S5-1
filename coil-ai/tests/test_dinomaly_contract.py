from __future__ import annotations

import unittest
from types import SimpleNamespace

import torch
import torch.nn as nn

from anoma.models.dinomaly import DinomalyInferenceModel


class _FakeDinomalyCore(nn.Module):
    def forward(self, image: torch.Tensor) -> SimpleNamespace:
        score = image.mean(dim=(1, 2, 3))
        anomaly_map = image.mean(dim=1, keepdim=True)
        return SimpleNamespace(pred_score=score, anomaly_map=anomaly_map)


class DinomalyContractTests(unittest.TestCase):
    def test_export_wrapper_returns_score_then_map(self) -> None:
        model = DinomalyInferenceModel(_FakeDinomalyCore())
        score, anomaly_map = model(torch.ones(2, 3, 14, 14))
        self.assertEqual(tuple(score.shape), (2, 1))
        self.assertEqual(tuple(anomaly_map.shape), (2, 1, 14, 14))
        self.assertTrue(torch.allclose(score, torch.ones_like(score)))


if __name__ == "__main__":
    unittest.main()
