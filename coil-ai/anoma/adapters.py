from __future__ import annotations

import shutil
from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any

from common import TrainingError, ensure_directory, find_latest_file, log_info, log_progress, log_step


class AnomalyAdapter(ABC):
    @abstractmethod
    def create_datamodule(self, workspace: Path, args: Any) -> Any:
        raise NotImplementedError

    @abstractmethod
    def create_model(self, args: Any) -> Any:
        raise NotImplementedError

    @abstractmethod
    def export_onnx(
        self,
        *,
        model: Any,
        engine: Any,
        out_dir: Path,
        checkpoint_path: Path | None,
        args: Any,
    ) -> Path:
        raise NotImplementedError


class AnomalibPadimAdapter(AnomalyAdapter):
    def create_datamodule(self, workspace: Path, args: Any) -> Any:
        try:
            from anomalib.data import Folder
        except ImportError as exc:  # pragma: no cover - import guard
            raise TrainingError(
                "anomalib is required to build the anomaly datamodule. Install requirements-train.txt."
            ) from exc

        # TODO: dataset transform
        return Folder(
            name="coil-anoma",
            root=str(workspace),
            normal_dir="train",
            normal_test_dir="val",
            train_batch_size=args.batch,
            eval_batch_size=args.batch,
            num_workers=args.num_workers,
            image_size=(args.image_size, args.image_size),
        )

    def create_model(self, args: Any) -> Any:
        try:
            from anomalib.models import Padim
        except ImportError:
            try:
                from anomalib.models.image.padim import Padim
            except ImportError as exc:  # pragma: no cover - import guard
                raise TrainingError(
                    "Unable to import the Padim model from anomalib. Check anomalib version compatibility."
                ) from exc

        # TODO: model selection
        return Padim(
            backbone="resnet18",
            layers=["layer1", "layer2", "layer3"],
            pre_trained=False,
        )

    def export_onnx(
        self,
        *,
        model: Any,
        engine: Any,
        out_dir: Path,
        checkpoint_path: Path | None,
        args: Any,
    ) -> Path:
        try:
            from anomalib.deploy import ExportType
        except ImportError as exc:  # pragma: no cover - import guard
            raise TrainingError(
                "anomalib is required to export anomaly ONNX. Install requirements-train.txt."
            ) from exc

        log_step("export onnx")
        export_root = out_dir / "anoma_export"
        ensure_directory(export_root)

        try:
            exported = engine.export(
                model=model,
                export_type=ExportType.ONNX,
                export_root=str(export_root),
                model_file_name="anoma",
                input_size=(args.image_size, args.image_size),
                ckpt_path=str(checkpoint_path) if checkpoint_path else None,
            )
        except Exception as exc:
            raise TrainingError(f"Anomaly ONNX export failed: {exc}") from exc

        export_path = Path(str(exported)).resolve(strict=False)
        if not export_path.exists():
            fallback_export = find_latest_file(export_root, "*.onnx")
            if fallback_export is not None:
                export_path = fallback_export.resolve(strict=False)
        if not export_path.exists():
            raise TrainingError(f"Anomaly export reported success but file was not found: {export_path}")

        expected_path = out_dir / "anoma.onnx"
        if export_path != expected_path:
            shutil.copy2(export_path, expected_path)

        if not expected_path.exists():
            raise TrainingError(f"Expected ONNX output was not created: {expected_path}")

        # TODO: threshold strategy
        # TODO: export graph verification
        log_info(f"exported onnx: {expected_path}")
        log_progress(100)
        return expected_path


def build_adapter(model_key: str) -> AnomalyAdapter:
    if model_key.lower() == "padim":
        return AnomalibPadimAdapter()
    raise TrainingError(
        f"Unsupported anomaly model '{model_key}'. "
        "This skeleton currently enables padim and is structured for later extension."
    )
