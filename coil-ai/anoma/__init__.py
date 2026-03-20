from __future__ import annotations

import sys
import traceback
from typing import Sequence

from anoma.config import parse_args
from anoma.exporter import export_onnx
from anoma.trainer import train_model
from anoma.workspace import validate_workspace
from common import (
    ensure_directory,
    log,
    log_error,
    resolve_workspace_and_out,
    save_summary,
    set_seed,
    utc_now_iso,
)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    workspace, out_dir = resolve_workspace_and_out(args)
    ensure_directory(out_dir)

    started_at = utc_now_iso()
    notes: list[str] = [
        "val directory is currently treated as normal-image validation/test data.",
        "metrics can be sparse until the final anomaly evaluation policy is fixed.",
    ]
    metrics: dict[str, object] = {}
    export_path = None

    try:
        set_seed(args.seed)
        validate_workspace(workspace)
        train_result = train_model(args=args, workspace=workspace, out_dir=out_dir)
        metrics = train_result.get("metrics", {})
        if train_result.get("best_model_path"):
            notes.append(f"best_model_path={train_result['best_model_path']}")
        export_path = export_onnx(train_result=train_result, out_dir=out_dir, args=args)
        save_summary(
            out_dir=out_dir,
            model_type="anoma",
            workspace=workspace,
            started_at=started_at,
            finished_at=utc_now_iso(),
            success=True,
            metrics=metrics,
            export_path=str(export_path),
            notes=notes,
        )
        return 0
    except Exception as exc:
        error_message = str(exc) if str(exc) else exc.__class__.__name__
        log_error(error_message)
        log(traceback.format_exc(), stream=sys.stderr)
        notes.append(f"error={error_message}")
        try:
            save_summary(
                out_dir=out_dir,
                model_type="anoma",
                workspace=workspace,
                started_at=started_at,
                finished_at=utc_now_iso(),
                success=False,
                metrics=metrics,
                export_path=str(export_path) if export_path else None,
                notes=notes,
            )
        except Exception as summary_exc:
            log_error(f"failed to save summary: {summary_exc}")
        return 1
