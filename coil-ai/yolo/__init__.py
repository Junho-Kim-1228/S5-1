from __future__ import annotations

import sys
import traceback
from typing import Sequence

from common import (
    ensure_directory,
    log,
    log_error,
    resolve_workspace_and_out,
    save_summary,
    set_seed,
    utc_now_iso,
)
from yolo.config import parse_args, resolve_base_model
from yolo.exporter import export_onnx
from yolo.trainer import train_model
from yolo.workspace import validate_workspace


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    workspace, out_dir = resolve_workspace_and_out(args)
    ensure_directory(out_dir)

    started_at = utc_now_iso()
    notes: list[str] = []
    metrics: dict[str, object] = {}
    export_path = None

    try:
        set_seed(args.seed)
        validation_info = validate_workspace(workspace)
        base_model_path = resolve_base_model(args.model)
        train_info = train_model(
            args=args,
            out_dir=out_dir,
            data_yaml=validation_info["data_yaml"],
            base_model_path=base_model_path,
        )
        metrics = train_info.get("metrics", {})
        notes.append(f"base_model={base_model_path}")
        notes.append(f"train_dir={train_info['train_dir']}")
        export_path = export_onnx(
            best_checkpoint=train_info["best_checkpoint"],
            out_dir=out_dir,
            imgsz=args.imgsz,
        )
        save_summary(
            out_dir=out_dir,
            model_type="yolo",
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
                model_type="yolo",
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
