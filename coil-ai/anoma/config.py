from __future__ import annotations

import argparse
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

from common import resolve_path


@dataclass(slots=True)
class AnomaConfig:
    workspace: Path
    out_dir: Path
    dataset_name: str
    image_size: int
    batch_size: int
    num_workers: int
    seed: int
    val_ratio: float
    device: str
    embedding_dim: int
    covariance_eps: float


def parse_args(argv: Sequence[str] | None = None) -> AnomaConfig:
    parser = argparse.ArgumentParser(
        description="Train a PaDiM anomaly detector and export it to ONNX."
    )
    parser.add_argument("--workspace", required=True, help="Raw anomaly data root.")
    parser.add_argument("--out", required=True, help="Output directory for artifacts.")
    parser.add_argument("--dataset-name", default="pcb_v1", help="Generated dataset name.")
    parser.add_argument("--image-size", type=int, default=256, help="Square input size.")
    parser.add_argument("--batch-size", type=int, default=16, help="Batch size for feature extraction.")
    parser.add_argument(
        "--num-workers",
        type=int,
        default=0 if os.name == "nt" else 4,
        help="Dataloader worker count.",
    )
    parser.add_argument("--seed", type=int, default=42, help="Random seed.")
    parser.add_argument("--val-ratio", type=float, default=0.2, help="Validation ratio for good images.")
    parser.add_argument("--device", default="auto", help="Device. Examples: auto, cpu, cuda.")
    parser.add_argument("--embedding-dim", type=int, default=100, help="PaDiM embedding dimension.")
    parser.add_argument(
        "--covariance-eps",
        type=float,
        default=0.01,
        help="Diagonal epsilon added before covariance inversion.",
    )
    args = parser.parse_args(argv)

    if not 0.0 < args.val_ratio < 1.0:
        raise SystemExit("--val-ratio must be between 0 and 1.")
    if args.image_size <= 0:
        raise SystemExit("--image-size must be positive.")
    if args.batch_size <= 0:
        raise SystemExit("--batch-size must be positive.")
    if args.embedding_dim <= 0:
        raise SystemExit("--embedding-dim must be positive.")
    if args.covariance_eps <= 0:
        raise SystemExit("--covariance-eps must be positive.")

    return AnomaConfig(
        workspace=resolve_path(args.workspace),
        out_dir=resolve_path(args.out),
        dataset_name=args.dataset_name,
        image_size=args.image_size,
        batch_size=args.batch_size,
        num_workers=args.num_workers,
        seed=args.seed,
        val_ratio=args.val_ratio,
        device=args.device,
        embedding_dim=args.embedding_dim,
        covariance_eps=args.covariance_eps,
    )
