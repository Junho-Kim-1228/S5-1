from __future__ import annotations

import sys
import traceback
from typing import Sequence

from anoma.config import parse_args
from anoma.trainer import run_training
from common import (
    configure_logging,
    ensure_directory,
    log,
    log_error,
    save_train_summary,
    set_seed,
)


def main(argv: Sequence[str] | None = None) -> int:
    configure_logging()
    args = parse_args(argv)
    ensure_directory(args.out_dir)

    summary_path = args.out_dir / "train_summary.json"

    try:
        set_seed(args.seed)
        result = run_training(args)
        model_name = str(result["model"].model_info().get("model", args.model))
        save_train_summary(
            summary_path,
            {
                "task": "anomaly_detection",
                "model": model_name,
                "status": "success",
                "metrics": result["metrics"],
                "dataset": result["dataset"],
                "artifacts": {
                    "onnx": result["artifacts"]["onnx"].name
                    if result["artifacts"]["onnx"] is not None
                    else None
                },
            },
        )
        log("[DONE] anomaly training finished successfully.")
        return 0
    except Exception as exc:
        error_message = str(exc) if str(exc) else exc.__class__.__name__
        log_error(error_message)
        log(traceback.format_exc(), stream=sys.stderr)
        try:
            save_train_summary(
                summary_path,
                {
                    "task": "anomaly_detection",
                    "model": args.model,
                    "status": "failed",
                    "metrics": {
                        "image_auroc": 0.0,
                        "image_ap": 0.0,
                        "best_f1": 0.0,
                        "best_threshold": 0.0,
                    },
                    "dataset": {
                        "train_good": 0,
                        "val_good": 0,
                        "val_defect": 0,
                    },
                    "artifacts": {"onnx": "anoma.onnx"},
                },
            )
        except Exception as summary_exc:  # pragma: no cover - disk errors
            log_error(f"failed to save summary: {summary_exc}")
        return 1
