from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from yolo.config import build_yolo_train_config
from yolo.exporter import export_yolo_to_onnx


class _FakeYoloModel:
    def __init__(self, exported_path: Path) -> None:
        self.exported_path = exported_path
        self.export_kwargs: dict[str, object] = {}

    def export(self, **kwargs):
        self.export_kwargs = kwargs
        self.exported_path.parent.mkdir(parents=True, exist_ok=True)
        self.exported_path.write_bytes(b"fake-onnx")
        return self.exported_path


class Yolo26CompatibilityTests(unittest.TestCase):
    def test_official_yolo26_identifier_maps_to_local_weight_cache(self) -> None:
        config = build_yolo_train_config(
            model="yolo26m.pt",
            epochs=100,
            imgsz=1280,
            batch=4,
            device="cpu",
            seed=42,
            workers=0,
            conf_val=None,
            lr0=None,
        )

        self.assertEqual(config.weights.name, "yolo26m.pt")
        self.assertEqual(config.weights.parent.name, "weights")
        self.assertEqual(config.variant, "yolo26m")

    def test_fine_tune_learning_rate_is_preserved(self) -> None:
        config = build_yolo_train_config(
            model="yolo26n.pt",
            epochs=40,
            imgsz=1280,
            batch=4,
            device="cpu",
            seed=42,
            workers=0,
            conf_val=None,
            lr0=0.001,
        )
        self.assertEqual(config.lr0, 0.001)

    def test_onnx_export_uses_legacy_one_to_many_head(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            model = _FakeYoloModel(root / "ultralytics" / "model.onnx")
            destination = root / "output" / "yolo.onnx"

            result = export_yolo_to_onnx(model, destination, imgsz=1280)

            self.assertEqual(result, destination)
            self.assertEqual(destination.read_bytes(), b"fake-onnx")
            self.assertEqual(
                model.export_kwargs,
                {"format": "onnx", "imgsz": 1280, "end2end": False},
            )


if __name__ == "__main__":
    unittest.main()
