from __future__ import annotations

from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any


class AnomalyModelBase(ABC):
    @abstractmethod
    def fit(self, train_loader) -> None:
        raise NotImplementedError

    @abstractmethod
    def predict_scores(self, eval_loader) -> dict[str, Any]:
        raise NotImplementedError

    @abstractmethod
    def export_onnx(self, path: Path, image_size: int) -> None:
        raise NotImplementedError

    @abstractmethod
    def save_state(self, path: Path) -> None:
        raise NotImplementedError

    @abstractmethod
    def model_info(self) -> dict[str, Any]:
        raise NotImplementedError
