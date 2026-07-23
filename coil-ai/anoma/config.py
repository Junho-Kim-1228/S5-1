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
    model: str
    image_size: int
    batch_size: int
    num_workers: int
    seed: int
    val_ratio: float
    device: str
    embedding_dim: int
    covariance_eps: float
    memory_bank_size: int
    dinomaly_encoder: str
    dinomaly_dropout: float
    dinomaly_decoder_depth: int
    dinomaly_max_steps: int
    dinomaly_learning_rate: float
    target_recall: float | None
    skip_export: bool


def parse_args(argv: Sequence[str] | None = None) -> AnomaConfig:
    parser = argparse.ArgumentParser(
        description="Train an anomaly detector and optionally export it to ONNX."
    )
    parser.add_argument("--workspace", required=True, help="Raw anomaly data root.")
    parser.add_argument("--out", required=True, help="Output directory for artifacts.")
    parser.add_argument("--dataset-name", default="pcb_v1", help="Generated dataset name.")
    parser.add_argument(
        "--model",
        default="padim",
        choices=["padim", "patchcore", "dinomaly"],
        help="Anomaly model to run.",
    )
    parser.add_argument(
        "--image-size",
        type=int,
        default=None,
        help="Square input size. Defaults to 448 for Dinomaly and 640 otherwise.",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=8,
        help="Batch size for feature extraction. Kept conservative for 640px inputs.",
    )
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
    parser.add_argument(
        "--memory-bank-size",
        type=int,
        default=50000,
        help="PatchCore memory bank cap. Ignored by PaDiM.",
    )
    parser.add_argument(
        "--dinomaly-encoder",
        default="vit_base_patch14_reg4_dinov2",
        help="Timm DINO encoder used by Dinomaly.",
    )
    parser.add_argument(
        "--dinomaly-dropout",
        type=float,
        default=0.2,
        help="Dinomaly bottleneck dropout.",
    )
    parser.add_argument(
        "--dinomaly-decoder-depth",
        type=int,
        default=8,
        help="Number of Dinomaly Transformer decoder blocks.",
    )
    parser.add_argument(
        "--dinomaly-max-steps",
        type=int,
        default=5000,
        help="Number of Dinomaly optimization steps.",
    )
    parser.add_argument(
        "--dinomaly-learning-rate",
        type=float,
        default=0.002,
        help="Dinomaly StableAdamW base learning rate.",
    )
    parser.add_argument(
        "--target-recall",
        type=float,
        default=None,
        help="Use the highest-precision validation threshold meeting this recall target.",
    )
    parser.add_argument(
        "--skip-export",
        action="store_true",
        help="Skip ONNX/state export and keep only metrics/debug outputs.",
    )
    args = parser.parse_args(argv)
    image_size = args.image_size if args.image_size is not None else (448 if args.model == "dinomaly" else 640)

    if not 0.0 < args.val_ratio < 1.0:
        raise SystemExit("--val-ratio must be between 0 and 1.")
    if image_size <= 0:
        raise SystemExit("--image-size must be positive.")
    if args.batch_size <= 0:
        raise SystemExit("--batch-size must be positive.")
    if args.embedding_dim <= 0:
        raise SystemExit("--embedding-dim must be positive.")
    if args.covariance_eps <= 0:
        raise SystemExit("--covariance-eps must be positive.")
    if args.memory_bank_size <= 0:
        raise SystemExit("--memory-bank-size must be positive.")
    if not 0.0 <= args.dinomaly_dropout < 1.0:
        raise SystemExit("--dinomaly-dropout must be between 0 (inclusive) and 1 (exclusive).")
    if args.dinomaly_decoder_depth < 8:
        raise SystemExit(
            "--dinomaly-decoder-depth must be at least 8 for the default two-group feature fusion."
        )
    if args.dinomaly_max_steps <= 0:
        raise SystemExit("--dinomaly-max-steps must be positive.")
    if args.dinomaly_learning_rate <= 0:
        raise SystemExit("--dinomaly-learning-rate must be positive.")
    if args.target_recall is not None and not 0.0 < args.target_recall <= 1.0:
        raise SystemExit("--target-recall must be greater than 0 and at most 1.")
    if args.model == "dinomaly" and image_size % 14 != 0:
        raise SystemExit("Dinomaly --image-size must be divisible by the DINOv2 patch size (14).")

    return AnomaConfig(
        workspace=resolve_path(args.workspace),
        out_dir=resolve_path(args.out),
        dataset_name=args.dataset_name,
        model=args.model,
        image_size=image_size,
        batch_size=args.batch_size,
        num_workers=args.num_workers,
        seed=args.seed,
        val_ratio=args.val_ratio,
        device=args.device,
        embedding_dim=args.embedding_dim,
        covariance_eps=args.covariance_eps,
        memory_bank_size=args.memory_bank_size,
        dinomaly_encoder=args.dinomaly_encoder,
        dinomaly_dropout=args.dinomaly_dropout,
        dinomaly_decoder_depth=args.dinomaly_decoder_depth,
        dinomaly_max_steps=args.dinomaly_max_steps,
        dinomaly_learning_rate=args.dinomaly_learning_rate,
        target_recall=args.target_recall,
        skip_export=args.skip_export,
    )
