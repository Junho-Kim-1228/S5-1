from __future__ import annotations

import argparse
from pathlib import Path

from common.path_utils import resolve_path


def add_workspace_out_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--workspace", required=True, help="Workspace directory.")
    parser.add_argument("--out", required=True, help="Output directory for artifacts.")


def resolve_workspace_and_out(args: argparse.Namespace) -> tuple[Path, Path]:
    return resolve_path(args.workspace), resolve_path(args.out)
