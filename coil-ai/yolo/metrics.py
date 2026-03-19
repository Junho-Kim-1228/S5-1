from __future__ import annotations

from typing import Any


def safe_float(value: Any) -> Any:
    if isinstance(value, (int, float, bool)) or value is None:
        return value
    try:
        return float(value)
    except (TypeError, ValueError):
        return str(value)


def extract_metrics(result: Any) -> dict[str, Any]:
    metrics: dict[str, Any] = {}
    if result is None:
        return metrics

    candidate_dicts: list[dict[str, Any]] = []
    if isinstance(result, dict):
        candidate_dicts.append(result)
    for attribute_name in ("results_dict", "metrics"):
        attribute_value = getattr(result, attribute_name, None)
        if isinstance(attribute_value, dict):
            candidate_dicts.append(attribute_value)

    for candidate in candidate_dicts:
        for key, value in candidate.items():
            metrics[str(key)] = safe_float(value)

    return metrics
