from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

import cv2
import numpy as np


IMAGE_EXTENSIONS = {".bmp", ".png", ".jpg", ".jpeg"}
CLASS_NAMES = {"dent", "loose"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Audit raw image + state.json pairs before anomaly and YOLO training."
    )
    parser.add_argument("--raw-root", required=True, help="Raw training-data directory.")
    parser.add_argument(
        "--report",
        default=None,
        help="Optional JSON report path. Parent directories are created automatically.",
    )
    return parser.parse_args()


def _read_json(path: Path) -> dict[str, Any]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("root JSON value must be an object")
    return payload


def _read_image_shape(path: Path) -> tuple[int, int]:
    encoded = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError("image decode failed")
    height, width = image.shape[:2]
    if height <= 0 or width <= 0:
        raise ValueError(f"invalid image shape: {width}x{height}")
    return width, height


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(float(value))


def _validate_label(
    label: Any,
    *,
    image_path: Path,
    label_index: int,
    errors: list[str],
) -> str | None:
    prefix = f"{image_path.name}: label[{label_index}]"
    if not isinstance(label, dict):
        errors.append(f"{prefix} must be an object")
        return None

    class_name = label.get("ClassName")
    if class_name not in CLASS_NAMES:
        errors.append(f"{prefix} has unknown ClassName={class_name!r}; expected {sorted(CLASS_NAMES)}")
        return None

    coordinates: dict[str, float] = {}
    for field in ("X", "Y", "Width", "Height"):
        value = label.get(field)
        if not _is_number(value):
            errors.append(f"{prefix} has invalid {field}={value!r}")
            return class_name
        coordinates[field] = float(value)

    x = coordinates["X"]
    y = coordinates["Y"]
    width = coordinates["Width"]
    height = coordinates["Height"]
    if width <= 0.0 or height <= 0.0 or width > 1.0 or height > 1.0:
        errors.append(f"{prefix} has invalid normalized size width={width:.6f}, height={height:.6f}")
    if not (0.0 <= x <= 1.0 and 0.0 <= y <= 1.0):
        errors.append(f"{prefix} has center outside [0,1]: x={x:.6f}, y={y:.6f}")
    if x - width / 2.0 < -1e-6 or x + width / 2.0 > 1.0 + 1e-6:
        errors.append(f"{prefix} extends past the left/right image boundary")
    if y - height / 2.0 < -1e-6 or y + height / 2.0 > 1.0 + 1e-6:
        errors.append(f"{prefix} extends past the top/bottom image boundary")
    return class_name


def audit(raw_root: Path) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    image_paths = sorted(
        path
        for path in raw_root.rglob("*")
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )

    status_counts: Counter[str] = Counter()
    class_instances: Counter[str] = Counter()
    class_images: Counter[str] = Counter()
    image_sizes: Counter[str] = Counter()
    hashes: defaultdict[str, list[str]] = defaultdict(list)
    usable_normal = 0
    usable_defect = 0
    review_needed = 0

    if not image_paths:
        errors.append(f"no supported images found under {raw_root}")

    for image_path in image_paths:
        json_path = image_path.with_name(f"{image_path.stem}.state.json")
        if not json_path.exists():
            errors.append(f"{image_path.name}: missing {json_path.name}")
            continue

        try:
            width_px, height_px = _read_image_shape(image_path)
            image_sizes[f"{width_px}x{height_px}"] += 1
            hashes[_sha256(image_path)].append(str(image_path.relative_to(raw_root)))
        except Exception as exc:
            errors.append(f"{image_path.name}: {exc}")
            continue

        try:
            payload = _read_json(json_path)
        except Exception as exc:
            errors.append(f"{json_path.name}: JSON parse failed: {exc}")
            continue

        review_status = str(payload.get("ReviewStatus", "")).strip()
        status_counts[review_status or "(empty)"] += 1
        if review_status == "review_needed":
            review_needed += 1
            continue

        is_normal = payload.get("IsNormal")
        if not isinstance(is_normal, bool):
            errors.append(f"{json_path.name}: IsNormal must be true or false")
            continue

        labels = payload.get("Labels", [])
        if not isinstance(labels, list):
            errors.append(f"{json_path.name}: Labels must be an array")
            continue

        classes_in_image: set[str] = set()
        for index, label in enumerate(labels):
            class_name = _validate_label(
                label,
                image_path=image_path,
                label_index=index,
                errors=errors,
            )
            if class_name in CLASS_NAMES:
                class_instances[class_name] += 1
                classes_in_image.add(class_name)
        for class_name in classes_in_image:
            class_images[class_name] += 1

        if is_normal:
            usable_normal += 1
            if labels:
                errors.append(f"{json_path.name}: IsNormal=true but Labels is not empty")
        else:
            usable_defect += 1
            if not labels:
                warnings.append(
                    f"{json_path.name}: defect image has no box; usable by Anoma but becomes YOLO background"
                )

    duplicates = [paths for paths in hashes.values() if len(paths) > 1]
    for paths in duplicates:
        warnings.append(f"duplicate image content: {', '.join(paths)}")

    if usable_normal < 2:
        errors.append(f"Anoma requires at least 2 usable normal images; found {usable_normal}")
    if usable_defect < 1:
        errors.append(f"Anoma evaluation requires at least 1 usable defect image; found {usable_defect}")
    for class_name in sorted(CLASS_NAMES):
        count = class_images[class_name]
        if count == 0:
            warnings.append(f"YOLO class {class_name!r} has no labeled images")
        elif count < 5:
            warnings.append(
                f"YOLO class {class_name!r} has only {count} labeled image(s); train/val metrics will be unstable"
            )

    return {
        "schema_version": 1,
        "raw_root": str(raw_root),
        "ok": not errors,
        "summary": {
            "image_count": len(image_paths),
            "usable_normal": usable_normal,
            "usable_defect": usable_defect,
            "review_needed": review_needed,
            "class_instances": dict(sorted(class_instances.items())),
            "class_images": dict(sorted(class_images.items())),
            "image_sizes": dict(sorted(image_sizes.items())),
            "review_statuses": dict(sorted(status_counts.items())),
            "duplicate_groups": len(duplicates),
        },
        "errors": errors,
        "warnings": warnings,
    }


def _print_report(report: dict[str, Any]) -> None:
    summary = report["summary"]
    print("[DATA AUDIT]")
    print(f"status             : {'PASS' if report['ok'] else 'FAIL'}")
    print(f"images             : {summary['image_count']}")
    print(f"usable normal      : {summary['usable_normal']}")
    print(f"usable defect      : {summary['usable_defect']}")
    print(f"review needed      : {summary['review_needed']}")
    print(f"class images       : {summary['class_images']}")
    print(f"class instances    : {summary['class_instances']}")
    print(f"image sizes        : {summary['image_sizes']}")
    print(f"duplicate groups   : {summary['duplicate_groups']}")
    for message in report["errors"]:
        print(f"[ERROR] {message}")
    for message in report["warnings"]:
        print(f"[WARN] {message}")


def main() -> int:
    args = parse_args()
    raw_root = Path(args.raw_root).expanduser().resolve()
    if not raw_root.exists() or not raw_root.is_dir():
        print(f"[ERROR] raw-root does not exist or is not a directory: {raw_root}")
        return 2

    report = audit(raw_root)
    _print_report(report)
    if args.report:
        report_path = Path(args.report).expanduser().resolve()
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"report             : {report_path}")
    return 0 if report["ok"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
