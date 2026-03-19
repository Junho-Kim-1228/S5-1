from __future__ import annotations

import argparse
from typing import Sequence

from common import add_workspace_out_args


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Train an anomaly model and export it to ONNX for the WPF training UI."
    )
    add_workspace_out_args(parser)
    parser.add_argument(
        "--model",
        default="padim",
        help="Anomaly model key. Defaults to padim and can be extended later.",
    )
    parser.add_argument("--epochs", type=int, default=10, help="Training epochs.")
    parser.add_argument("--batch", type=int, default=16, help="Batch size.")
    parser.add_argument("--image-size", type=int, default=256, help="Input image size.")
    parser.add_argument("--num-workers", type=int, default=4, help="Dataloader worker count.")
    parser.add_argument(
        "--device",
        default="auto",
        help="Training device. Examples: auto, cpu, cuda.",
    )
    parser.add_argument("--seed", type=int, default=42, help="Random seed.")
    parser.add_argument(
        "--project-name",
        default="anoma_train",
        help="Directory name used under the output folder for training artifacts.",
    )
    return parser.parse_args(argv)
