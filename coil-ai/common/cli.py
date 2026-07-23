import argparse

from common.path_utils import resolve_path


def build_train_parser(task_name: str) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=f"Train {task_name} model"
    )
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--out", required=True)
    if task_name == "yolo":
        parser.add_argument("--model", default=None)
        parser.add_argument("--epochs", type=int, default=150)
        parser.add_argument("--imgsz", type=int, default=1024)
        parser.add_argument("--batch", type=int, default=4)
        parser.add_argument("--device", default="auto")
        parser.add_argument("--seed", type=int, default=42)
        parser.add_argument("--workers", type=int, default=None)
        parser.add_argument("--conf-val", type=float, default=None)
        parser.add_argument("--lr0", type=float, default=None)
    return parser


def resolve_workspace_and_out(args) -> tuple:
    return resolve_path(args.workspace), resolve_path(args.out)
