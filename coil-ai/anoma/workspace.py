from __future__ import annotations

import json
import os
import random
import shutil
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
import torch
from torch.utils.data import DataLoader, Dataset

from common import WorkspaceValidationError, ensure_directory, get_project_root
from common.io_utils import IMAGE_EXTENSIONS

IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)


@dataclass(slots=True)
class RawSample:
    image_path: Path
    json_path: Path
    is_normal: bool
    review_status: str


@dataclass(slots=True)
class DatasetSample:
    image_path: Path
    label: int


class AnomaImageDataset(Dataset):
    def __init__(self, samples: list[DatasetSample], image_size: int) -> None:
        self.samples = samples
        self.image_size = image_size

    def __len__(self) -> int:
        return len(self.samples)

    def __getitem__(self, index: int) -> dict[str, object]:
        sample = self.samples[index]
        return {
            "image": load_image_tensor(sample.image_path, self.image_size),
            "label": sample.label,
            "path": str(sample.image_path),
        }


def _iter_image_files(workspace: Path) -> list[Path]:
    return sorted(
        path
        for path in workspace.rglob("*")
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def _read_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise WorkspaceValidationError(f"failed to parse json: {path}") from exc


def _resolve_json_path(image_path: Path, workspace: Path) -> Path | None:
    direct_path = image_path.with_suffix(".state.json")
    if direct_path.exists():
        return direct_path

    masked_fallback = image_path.with_name(f"{image_path.stem}_masked.state.json")
    if masked_fallback.exists():
        return masked_fallback

    workspace_name = workspace.name
    if workspace_name.endswith("_raw"):
        masked_workspace = workspace.parent / workspace_name[: -len("_raw")]
        relative_image = image_path.relative_to(workspace)
        sibling_direct = masked_workspace / relative_image.with_suffix(".state.json")
        if sibling_direct.exists():
            return sibling_direct

        sibling_masked = masked_workspace / relative_image.with_name(
            f"{relative_image.stem}_masked.state.json"
        )
        if sibling_masked.exists():
            return sibling_masked

    return None


def validate_workspace(workspace: Path) -> dict[str, object]:
    if not workspace.exists():
        raise WorkspaceValidationError(f"workspace does not exist: {workspace}")
    if not workspace.is_dir():
        raise WorkspaceValidationError(f"workspace is not a directory: {workspace}")

    image_paths = _iter_image_files(workspace)
    if not image_paths:
        raise WorkspaceValidationError(f"no image files were found under: {workspace}")

    samples: list[RawSample] = []
    missing_json: list[Path] = []
    excluded_review_needed = 0
    invalid_labels = 0

    for image_path in image_paths:
        json_path = _resolve_json_path(image_path, workspace)
        if json_path is None:
            missing_json.append(image_path)
            continue

        payload = _read_json(json_path)
        review_status = str(payload.get("ReviewStatus", ""))
        if review_status == "review_needed":
            excluded_review_needed += 1
            continue

        is_normal = payload.get("IsNormal")
        if not isinstance(is_normal, bool):
            invalid_labels += 1
            continue

        samples.append(
            RawSample(
                image_path=image_path,
                json_path=json_path,
                is_normal=is_normal,
                review_status=review_status,
            )
        )

    if missing_json:
        preview = ", ".join(path.name for path in missing_json[:5])
        raise WorkspaceValidationError(
            f"missing .state.json for {len(missing_json)} image(s). examples: {preview}"
        )
    if invalid_labels:
        raise WorkspaceValidationError(f"{invalid_labels} sample(s) are missing a valid IsNormal boolean.")
    if not samples:
        raise WorkspaceValidationError("no usable anomaly samples were found after filtering.")

    normal_count = sum(1 for sample in samples if sample.is_normal)
    defect_count = len(samples) - normal_count
    if normal_count < 2:
        raise WorkspaceValidationError("at least 2 normal samples are required for train/val splitting.")
    if defect_count == 0:
        raise WorkspaceValidationError("at least 1 defect sample is required for anomaly evaluation.")

    return {
        "workspace": workspace,
        "samples": samples,
        "counts": {
            "usable_total": len(samples),
            "normal": normal_count,
            "defect": defect_count,
            "excluded_review_needed": excluded_review_needed,
        },
    }


def _dataset_root(dataset_name: str) -> Path:
    return get_project_root() / "datasets" / "anoma" / dataset_name


def _reset_dataset_root(dataset_root: Path) -> None:
    if dataset_root.exists():
        shutil.rmtree(dataset_root)
    ensure_directory(dataset_root / "train" / "good")
    ensure_directory(dataset_root / "val" / "good")
    ensure_directory(dataset_root / "val" / "defect")
    ensure_directory(dataset_root / "meta")


def _target_name(workspace: Path, image_path: Path) -> str:
    relative = image_path.relative_to(workspace)
    stem = "__".join(relative.parts[:-1] + (relative.stem,))
    stem = stem.replace(" ", "_")
    return f"{stem}{image_path.suffix.lower()}"


def _copy_samples(samples: list[RawSample], workspace: Path, target_dir: Path) -> list[DatasetSample]:
    copied: list[DatasetSample] = []
    for sample in samples:
        target_path = target_dir / _target_name(workspace, sample.image_path)
        try:
            os.link(sample.image_path, target_path)
        except OSError:
            shutil.copy2(sample.image_path, target_path)
        copied.append(DatasetSample(image_path=target_path, label=0 if sample.is_normal else 1))
    return copied


def prepare_dataset(
    *,
    workspace: Path,
    samples: list[RawSample],
    dataset_name: str,
    val_ratio: float,
    seed: int,
) -> dict[str, object]:
    dataset_root = _dataset_root(dataset_name)
    _reset_dataset_root(dataset_root)

    good_samples = [sample for sample in samples if sample.is_normal]
    defect_samples = [sample for sample in samples if not sample.is_normal]

    rng = random.Random(seed)
    rng.shuffle(good_samples)

    if len(good_samples) == 1:
        val_good_count = 0
    else:
        val_good_count = int(round(len(good_samples) * val_ratio))
        val_good_count = max(1, min(len(good_samples) - 1, val_good_count))

    val_good_samples = good_samples[:val_good_count]
    train_good_samples = good_samples[val_good_count:]

    train_good = _copy_samples(train_good_samples, workspace, dataset_root / "train" / "good")
    val_good = _copy_samples(val_good_samples, workspace, dataset_root / "val" / "good")
    val_defect = _copy_samples(defect_samples, workspace, dataset_root / "val" / "defect")

    manifest = {
        "source_workspace": str(workspace),
        "dataset_root": str(dataset_root),
        "val_ratio": val_ratio,
        "counts": {
            "train_good": len(train_good),
            "val_good": len(val_good),
            "val_defect": len(val_defect),
        },
    }
    (dataset_root / "meta" / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    return {
        "dataset_root": dataset_root,
        "train_samples": train_good,
        "eval_samples": val_good + val_defect,
        "counts": manifest["counts"],
        "manifest_path": dataset_root / "meta" / "manifest.json",
    }


def build_dataloaders(
    *,
    train_samples: list[DatasetSample],
    eval_samples: list[DatasetSample],
    image_size: int,
    batch_size: int,
    num_workers: int,
) -> tuple[DataLoader, DataLoader]:
    train_dataset = AnomaImageDataset(train_samples, image_size=image_size)
    eval_dataset = AnomaImageDataset(eval_samples, image_size=image_size)
    pin_memory = torch.cuda.is_available()

    train_loader = DataLoader(
        train_dataset,
        batch_size=batch_size,
        shuffle=False,
        num_workers=num_workers,
        pin_memory=pin_memory,
    )
    eval_loader = DataLoader(
        eval_dataset,
        batch_size=batch_size,
        shuffle=False,
        num_workers=num_workers,
        pin_memory=pin_memory,
    )
    return train_loader, eval_loader


def _imread(path: Path) -> np.ndarray:
    encoded = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if image is None:
        raise WorkspaceValidationError(f"failed to read image: {path}")
    return image


def load_image_tensor(path: Path, image_size: int) -> torch.Tensor:
    image = _imread(path)
    image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    image = cv2.resize(image, (image_size, image_size), interpolation=cv2.INTER_AREA)
    image = image.astype(np.float32) / 255.0
    image = (image - IMAGENET_MEAN) / IMAGENET_STD
    image = np.transpose(image, (2, 0, 1))
    return torch.from_numpy(image)
