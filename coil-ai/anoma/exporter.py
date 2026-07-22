from __future__ import annotations

import csv
import json
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from common import ExportError, ensure_directory, log_info, log_step


def _write_json(path: Path, payload: Any) -> None:
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")


def _read_image(path: Path) -> np.ndarray:
    encoded = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if image is None:
        raise ExportError(f"failed to read image for debug output: {path}")
    return image


def _write_image(path: Path, image: np.ndarray) -> None:
    success, encoded = cv2.imencode(path.suffix or ".png", image)
    if not success:
        raise ExportError(f"failed to encode debug image: {path}")
    encoded.tofile(str(path))


def _build_debug_records(*, predictions: dict[str, object], threshold: float) -> list[dict[str, object]]:
    paths = list(predictions["paths"])
    labels = np.asarray(predictions["labels"], dtype=np.int64)
    scores = np.asarray(predictions["scores"], dtype=np.float64)
    heatmaps = np.asarray(predictions["heatmaps"], dtype=np.float32)

    records: list[dict[str, object]] = []
    for path, label, score, heatmap in zip(paths, labels, scores, heatmaps):
        predicted = int(score >= threshold)
        records.append(
            {
                "path": str(path),
                "label": "defect" if int(label) == 1 else "good",
                "score": float(score),
                "predicted_label": "defect" if predicted == 1 else "good",
                "is_false_positive": bool(int(label) == 0 and predicted == 1),
                "is_false_negative": bool(int(label) == 1 and predicted == 0),
                "heatmap": heatmap,
            }
        )
    return records


def _save_scores_csv(path: Path, records: list[dict[str, object]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=[
                "path",
                "label",
                "score",
                "predicted_label",
                "is_false_positive",
                "is_false_negative",
            ],
        )
        writer.writeheader()
        for record in records:
            writer.writerow(
                {
                    "path": record["path"],
                    "label": record["label"],
                    "score": f"{float(record['score']):.8f}",
                    "predicted_label": record["predicted_label"],
                    "is_false_positive": record["is_false_positive"],
                    "is_false_negative": record["is_false_negative"],
                }
            )


def _top_false_positive_records(records: list[dict[str, object]], limit: int) -> list[dict[str, object]]:
    items = [record for record in records if record["is_false_positive"]]
    items.sort(key=lambda item: float(item["score"]), reverse=True)
    return items[:limit]


def _top_false_negative_records(records: list[dict[str, object]], limit: int) -> list[dict[str, object]]:
    items = [record for record in records if record["is_false_negative"]]
    items.sort(key=lambda item: float(item["score"]))
    return items[:limit]


def _serialize_debug_record(record: dict[str, object]) -> dict[str, object]:
    return {
        "path": record["path"],
        "label": record["label"],
        "score": float(record["score"]),
        "predicted_label": record["predicted_label"],
    }


def _render_anomaly_map(record: dict[str, object], output_path: Path) -> None:
    image = _read_image(Path(str(record["path"])))
    heatmap = np.asarray(record["heatmap"], dtype=np.float32)
    heatmap = cv2.resize(heatmap, (image.shape[1], image.shape[0]), interpolation=cv2.INTER_LINEAR)
    heatmap_u8 = cv2.normalize(heatmap, None, 0, 255, cv2.NORM_MINMAX).astype(np.uint8)
    colored = cv2.applyColorMap(heatmap_u8, cv2.COLORMAP_JET)
    overlay = cv2.addWeighted(image, 0.6, colored, 0.4, 0.0)

    text = (
        f"label={record['label']} pred={record['predicted_label']} "
        f"score={float(record['score']):.3f}"
    )
    cv2.putText(
        overlay,
        text,
        (16, 32),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.8,
        (255, 255, 255),
        2,
        cv2.LINE_AA,
    )
    panel = np.hstack([image, colored, overlay])
    _write_image(output_path, panel)


def save_debug_artifacts(
    *,
    out_dir: Path,
    predictions: dict[str, object],
    metrics: dict[str, float],
    distribution: dict[str, float],
) -> dict[str, Path]:
    debug_dir = out_dir / "debug"
    map_dir = debug_dir / "anomaly_maps"
    ensure_directory(debug_dir)
    ensure_directory(map_dir)

    threshold = float(metrics["best_threshold"])
    records = _build_debug_records(predictions=predictions, threshold=threshold)
    score_csv_path = debug_dir / "scores.csv"
    score_stats_path = debug_dir / "score_stats.json"
    fp_path = debug_dir / "false_positives.json"
    fn_path = debug_dir / "false_negatives.json"

    _save_scores_csv(score_csv_path, records)
    _write_json(
        score_stats_path,
        {
            **distribution,
            "threshold": threshold,
            "sample_count": len(records),
        },
    )

    false_positives = _top_false_positive_records(records, limit=10)
    false_negatives = _top_false_negative_records(records, limit=10)
    _write_json(fp_path, [_serialize_debug_record(record) for record in false_positives])
    _write_json(fn_path, [_serialize_debug_record(record) for record in false_negatives])

    selected_for_maps: list[dict[str, object]] = []
    seen_paths: set[str] = set()
    for record in false_positives[:3] + false_negatives[:3]:
        record_path = str(record["path"])
        if record_path not in seen_paths:
            seen_paths.add(record_path)
            selected_for_maps.append(record)

    if len(selected_for_maps) < 6:
        records_by_score = sorted(records, key=lambda item: float(item["score"]), reverse=True)
        for record in records_by_score:
            record_path = str(record["path"])
            if record_path in seen_paths:
                continue
            seen_paths.add(record_path)
            selected_for_maps.append(record)
            if len(selected_for_maps) >= 6:
                break

    map_paths: list[Path] = []
    for index, record in enumerate(selected_for_maps, start=1):
        filename = (
            f"{index:02d}_{record['label']}_{record['predicted_label']}_"
            f"{float(record['score']):.3f}.png"
        )
        output_path = map_dir / filename
        _render_anomaly_map(record, output_path)
        map_paths.append(output_path)

    log_info(f"scores csv: {score_csv_path}")
    log_info(f"score stats: {score_stats_path}")
    log_info(f"false positives: {fp_path}")
    log_info(f"false negatives: {fn_path}")
    log_info(f"anomaly maps: {map_dir}")
    return {
        "scores_csv": score_csv_path,
        "score_stats": score_stats_path,
        "false_positives": fp_path,
        "false_negatives": fn_path,
        "anomaly_maps_dir": map_dir,
    }


def save_inference_config(
    *,
    out_dir: Path,
    model,
    image_size: int,
    metrics: dict[str, float],
) -> Path:
    """Persist the exact preprocessing and decision settings for deployment.

    Anomaly scores are not probabilities, so a static value such as 0.5 is not
    portable between fitted models.  The package builder consumes this file to
    keep deployed inference aligned with the validation-calibrated model.
    """
    ensure_directory(out_dir)
    path = out_dir / "inference_config.json"
    _write_json(
        path,
        {
            "schema_version": 2,
            "model": str(model.model_info().get("model", "anoma")),
            "input_size": int(image_size),
            "input_name": "image",
            "outputs": {
                "score": "anomaly_score",
                "map": "anomaly_map",
            },
            "preprocessing": {
                "color_space": "RGB",
                "resize": "stretch",
                "value_scale": "0_to_1",
                "mean": [0.485, 0.456, 0.406],
                "std": [0.229, 0.224, 0.225],
            },
            "score_threshold": float(metrics["best_threshold"]),
            "threshold_policy": "best_f1_on_validation",
            "validation_metrics": {
                "image_auroc": float(metrics["image_auroc"]),
                "image_ap": float(metrics["image_ap"]),
                "best_f1": float(metrics["best_f1"]),
                "best_precision": float(metrics["best_precision"]),
                "best_recall": float(metrics["best_recall"]),
            },
        },
    )
    log_info(f"inference config: {path}")
    return path


def export_artifacts(*, model, out_dir: Path, image_size: int) -> dict[str, Path]:
    ensure_directory(out_dir)
    onnx_path = out_dir / "anoma.onnx"
    state_path = out_dir / f"{model.model_info().get('model', 'anoma')}_state.pt"

    log_step("export onnx")
    try:
        model.export_onnx(onnx_path, image_size=image_size)
        model.save_state(state_path)
    except Exception as exc:  # pragma: no cover - export runtime dependent
        raise ExportError(f"failed to export anomaly artifacts: {exc}") from exc

    log_info(f"onnx: {onnx_path}")
    log_info(f"state: {state_path}")
    return {"onnx": onnx_path, "state": state_path}
