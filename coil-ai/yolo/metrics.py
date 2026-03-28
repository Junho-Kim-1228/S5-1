from __future__ import annotations

from typing import Any


def _safe_float(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def _safe_int(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def _extract_per_class_metrics(results) -> list[dict[str, Any]]:
    summary_fn = getattr(results, "summary", None)
    if not callable(summary_fn):
        return []

    try:
        rows = summary_fn(normalize=True, decimals=6)
    except Exception:
        return []

    per_class: list[dict[str, Any]] = []
    for row in rows or []:
        if not isinstance(row, dict):
            continue
        per_class.append(
            {
                "class_name": str(row.get("Class", "")),
                "images": _safe_int(row.get("Images", 0)),
                "instances": _safe_int(row.get("Instances", 0)),
                "precision": _safe_float(row.get("Box-P", 0.0)),
                "recall": _safe_float(row.get("Box-R", 0.0)),
                "map50": _safe_float(row.get("mAP50", 0.0)),
                "map": _safe_float(row.get("mAP50-95", 0.0)),
            }
        )
    return per_class


def extract_yolo_metrics(results) -> dict:
    box = getattr(results, "box", None)

    if box is None:
        return {}

    return {
        "map50": float(getattr(box, "map50", 0.0)),
        "map": float(getattr(box, "map", 0.0)),
        "precision": float(getattr(box, "mp", 0.0)),
        "recall": float(getattr(box, "mr", 0.0)),
        "per_class": _extract_per_class_metrics(results),
    }
