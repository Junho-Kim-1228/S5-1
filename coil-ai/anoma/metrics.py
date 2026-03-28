from __future__ import annotations

import numpy as np


def compute_score_distribution(labels: np.ndarray, scores: np.ndarray) -> dict[str, float]:
    y_true = np.asarray(labels, dtype=np.int64)
    y_score = np.asarray(scores, dtype=np.float64)

    good = y_score[y_true == 0]
    defect = y_score[y_true == 1]
    if good.size == 0 or defect.size == 0:
        return {
            "good_mean": 0.0,
            "good_std": 0.0,
            "defect_mean": 0.0,
            "defect_std": 0.0,
            "overlap_coefficient": 0.0,
            "defect_below_good_p95_ratio": 0.0,
        }

    bins = np.histogram_bin_edges(y_score, bins="fd")
    if bins.size < 2:
        bins = np.array([float(y_score.min()), float(y_score.max()) + 1e-6], dtype=np.float64)

    good_hist, _ = np.histogram(good, bins=bins, density=True)
    defect_hist, _ = np.histogram(defect, bins=bins, density=True)
    widths = np.diff(bins)
    overlap = float(np.sum(np.minimum(good_hist, defect_hist) * widths))
    good_p95 = float(np.percentile(good, 95))

    return {
        "good_mean": float(good.mean()),
        "good_std": float(good.std()),
        "good_min": float(good.min()),
        "good_max": float(good.max()),
        "defect_mean": float(defect.mean()),
        "defect_std": float(defect.std()),
        "defect_min": float(defect.min()),
        "defect_max": float(defect.max()),
        "overlap_coefficient": overlap,
        "defect_below_good_p95_ratio": float((defect <= good_p95).mean()),
    }


def _binary_curve(y_true: np.ndarray, scores: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    order = np.argsort(scores)[::-1]
    y_sorted = y_true[order]
    score_sorted = scores[order]
    tp = np.cumsum(y_sorted == 1)
    fp = np.cumsum(y_sorted == 0)
    distinct = np.where(np.diff(score_sorted))[0]
    indices = np.r_[distinct, len(score_sorted) - 1]
    return tp[indices], fp[indices], score_sorted[indices]


def compute_image_metrics(labels: np.ndarray, scores: np.ndarray) -> dict[str, float]:
    y_true = np.asarray(labels, dtype=np.int64)
    y_score = np.asarray(scores, dtype=np.float64)

    positives = int((y_true == 1).sum())
    negatives = int((y_true == 0).sum())
    if positives == 0 or negatives == 0 or y_true.size == 0:
        return {
            "image_auroc": 0.0,
            "image_ap": 0.0,
            "best_f1": 0.0,
            "best_threshold": 0.0,
        }

    tp, fp, thresholds = _binary_curve(y_true, y_score)
    recall = tp / positives
    precision = tp / np.maximum(tp + fp, 1)
    false_positive_rate = fp / negatives

    roc_x = np.r_[0.0, false_positive_rate, 1.0]
    roc_y = np.r_[0.0, recall, 1.0]
    image_auroc = float(np.trapezoid(roc_y, roc_x))

    pr_recall = np.r_[0.0, recall, 1.0]
    pr_precision = np.r_[1.0, precision, 0.0]
    for index in range(pr_precision.size - 1, 0, -1):
        pr_precision[index - 1] = max(pr_precision[index - 1], pr_precision[index])
    image_ap = float(np.sum((pr_recall[1:] - pr_recall[:-1]) * pr_precision[1:]))

    f1_scores = (2.0 * precision * recall) / np.maximum(precision + recall, 1e-12)
    best_index = int(np.argmax(f1_scores))
    return {
        "image_auroc": image_auroc,
        "image_ap": image_ap,
        "best_f1": float(f1_scores[best_index]),
        "best_threshold": float(thresholds[best_index]),
    }
