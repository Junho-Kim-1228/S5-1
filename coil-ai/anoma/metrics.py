from __future__ import annotations

from typing import Any


def safe_float(value: Any) -> Any:
    if isinstance(value, (int, float, bool)) or value is None:
        return value
    try:
        return float(value)
    except (TypeError, ValueError):
        return str(value)


def extract_test_metrics(test_result: Any) -> dict[str, Any]:
    if not isinstance(test_result, list) or not test_result:
        return {}
    first_result = test_result[0]
    if not isinstance(first_result, dict):
        return {}
    return {str(key): safe_float(value) for key, value in first_result.items()}
