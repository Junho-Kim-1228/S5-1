import json
from datetime import datetime, UTC
from pathlib import Path


def utc_now_iso() -> str:
    return datetime.now(UTC).isoformat()


def save_train_summary(summary_path: Path, summary: dict) -> None:
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    with summary_path.open("w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, ensure_ascii=False)


def build_train_summary(
    model_type: str,
    workspace: str,
    out_dir: str,
    started_at: str,
    finished_at: str,
    success: bool,
    metrics: dict,
    export_path: str | None,
    notes: list[str],
    extras: dict | None = None,
) -> dict:
    summary = {
        "model_type": model_type,
        "workspace": workspace,
        "out_dir": out_dir,
        "started_at": started_at,
        "finished_at": finished_at,
        "success": success,
        "metrics": metrics,
        "export_path": export_path,
        "notes": notes,
        "task": model_type,
        "status": "success" if success else "failed",
        "onnx_file": export_path,
        "created_at_utc": finished_at,
    }
    if extras:
        summary["extras"] = extras
    return summary


def save_summary(
    *,
    out_dir: Path,
    model_type: str,
    workspace: Path,
    started_at: str,
    finished_at: str,
    success: bool,
    metrics: dict,
    export_path: str | None,
    notes: list[str],
    extras: dict | None = None,
) -> Path:
    summary_path = out_dir / "train_summary.json"
    save_train_summary(
        summary_path,
        build_train_summary(
            model_type=model_type,
            workspace=str(workspace),
            out_dir=str(out_dir),
            started_at=started_at,
            finished_at=finished_at,
            success=success,
            metrics=metrics,
            export_path=export_path,
            notes=notes,
            extras=extras,
        ),
    )
    return summary_path
