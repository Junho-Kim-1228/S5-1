from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from common.io_utils import ensure_directory
from common.logging_utils import log_info, log_step


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def save_summary(
    *,
    out_dir: Path,
    model_type: str,
    workspace: Path,
    started_at: str,
    finished_at: str,
    success: bool,
    metrics: dict[str, Any],
    export_path: str | None,
    notes: list[str],
) -> Path:
    log_step("save summary")
    ensure_directory(out_dir)
    summary_path = out_dir / "train_summary.json"
    payload = {
        "model_type": model_type,
        "workspace": str(workspace),
        "out_dir": str(out_dir),
        "started_at": started_at,
        "finished_at": finished_at,
        "success": success,
        "metrics": metrics,
        "export_path": export_path,
        "notes": notes,
    }
    summary_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
    log_info(f"summary: {summary_path}")
    return summary_path
