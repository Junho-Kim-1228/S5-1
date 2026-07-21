import argparse
import hashlib
import json
import random
import shutil
from pathlib import Path
from typing import Dict, List, Tuple

import cv2
import numpy as np


CLASS_MAP: Dict[str, int] = {
    "dent": 0,
    "loose": 1,
}
ALL_DEFECTS = "all"

VALID_IMAGE_EXTS = {".bmp", ".png", ".jpg", ".jpeg"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Prepare YOLO workspace from raw image + state.json pairs."
    )
    parser.add_argument(
        "--raw-root",
        required=True,
        help="Root folder containing raw images and *.state.json files",
    )
    parser.add_argument(
        "--out-root",
        required=True,
        help="Output YOLO workspace root (e.g. datasets/yolo/pcb_v1)",
    )
    parser.add_argument(
        "--train-ratio",
        type=float,
        default=0.8,
        help="Train split ratio. Default: 0.8",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Random seed. Default: 42",
    )
    parser.add_argument(
        "--copy",
        action="store_true",
        help="Copy image files instead of hardlinking. Default: try hardlink first.",
    )
    parser.add_argument(
        "--max-background",
        type=int,
        default=None,
        help="Maximum number of empty-label background images to keep before splitting.",
    )
    parser.add_argument(
        "--oversample-class",
        default=None,
        help="Class name to oversample in train split only. Example: dent",
    )
    parser.add_argument(
        "--oversample-factor",
        type=float,
        default=1.0,
        help="Train-only oversampling factor for --oversample-class. Example: 1.5",
    )
    parser.add_argument(
        "--augment-class",
        default=None,
        help="Class name to augment in train split only. Example: dent",
    )
    parser.add_argument(
        "--augment-factor",
        type=float,
        default=1.0,
        help="Train-only augmentation factor for --augment-class. Example: 2.0",
    )
    return parser.parse_args()


def ensure_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def save_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def save_json(path: Path, data: dict | list) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)


def save_data_yaml(path: Path, class_map: Dict[str, int]) -> None:
    names = [None] * len(class_map)
    for class_name, class_id in class_map.items():
        names[class_id] = class_name

    yaml_text = (
        "train: images/train\n"
        "val: images/val\n\n"
        f"nc: {len(names)}\n"
        f"names: {names}\n"
    )
    save_text(path, yaml_text)


def image_to_state_json_path(image_path: Path) -> Path:
    return image_path.with_name(f"{image_path.stem}.state.json")


def build_unique_output_stem(relative_source: Path) -> str:
    raw_stem = "__".join(relative_source.with_suffix("").parts)
    safe_stem = "".join(
        character if character.isalnum() or character in {"-", "_", "."} else "_"
        for character in raw_stem
    ).strip("._") or "image"
    path_hash = hashlib.sha1(relative_source.as_posix().encode("utf-8")).hexdigest()[:8]
    return f"{safe_stem}__{path_hash}"


def find_image_files(raw_root: Path) -> List[Path]:
    image_files: List[Path] = []
    for path in raw_root.rglob("*"):
        if path.is_file() and path.suffix.lower() in VALID_IMAGE_EXTS:
            image_files.append(path)
    return sorted(image_files)


def should_use_sample(meta: dict) -> bool:
    review_status = meta.get("ReviewStatus", "")
    return review_status != "review_needed"


def convert_labels_to_yolo_lines(meta: dict, class_map: Dict[str, int]) -> List[str]:
    labels = meta.get("Labels", [])
    yolo_lines: List[str] = []

    for label in labels:
        class_name = label.get("ClassName")
        if class_name not in class_map:
            raise ValueError(f"Unknown class name: {class_name}")

        class_id = class_map[class_name]
        x = label.get("X")
        y = label.get("Y")
        width = label.get("Width")
        height = label.get("Height")

        if None in (x, y, width, height):
            raise ValueError(f"Missing bbox field in label: {label}")

        # 이미 0~1 정규화된 YOLO 형식이라고 가정
        yolo_lines.append(f"{class_id} {x} {y} {width} {height}")

    return yolo_lines


def copy_or_link(src: Path, dst: Path, use_copy: bool) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)

    if dst.exists():
        dst.unlink()

    if use_copy:
        shutil.copy2(src, dst)
        return

    try:
        dst.hardlink_to(src)
    except Exception:
        shutil.copy2(src, dst)


def build_sample_records(raw_root: Path, class_map: Dict[str, int]) -> List[dict]:
    records: List[dict] = []
    image_files = find_image_files(raw_root)

    for image_path in image_files:
        json_path = image_to_state_json_path(image_path)

        if not json_path.exists():
            print(f"[WARN] Missing metadata JSON for image: {image_path}")
            continue

        try:
            meta = load_json(json_path)
        except Exception as e:
            print(f"[WARN] Failed to read JSON: {json_path} | {e}")
            continue

        if not should_use_sample(meta):
            continue

        try:
            yolo_lines = convert_labels_to_yolo_lines(meta, class_map)
        except Exception as e:
            print(f"[WARN] Failed to convert labels: {json_path} | {e}")
            continue

        label_count = len(meta.get("Labels", []))
        is_defect = label_count > 0

        relative_source = image_path.relative_to(raw_root)
        output_stem = build_unique_output_stem(relative_source)

        records.append(
            {
                "image_path": image_path,
                "json_path": json_path,
                "relative_source": str(relative_source),
                "file_stem": output_stem,
                "output_image_name": f"{output_stem}{image_path.suffix.lower()}",
                "output_label_name": f"{output_stem}.txt",
                "yolo_lines": yolo_lines,
                "review_status": meta.get("ReviewStatus"),
                "label_count": label_count,
                "is_defect": is_defect,
                "class_names": sorted(
                    {
                        label.get("ClassName")
                        for label in meta.get("Labels", [])
                        if label.get("ClassName") in class_map
                    }
                ),
                "is_oversampled": False,
                "oversample_copy_index": 0,
                "is_augmented": False,
                "augmentation_copy_index": 0,
                "augmentation_profile": None,
            }
        )

    return records


def limit_background_records(records: List[dict], max_background: int | None, seed: int) -> List[dict]:
    if max_background is None:
        return records

    if max_background < 0:
        raise ValueError(f"max-background must be >= 0, got {max_background}")

    defect_records = [r for r in records if r["is_defect"]]
    background_records = [r for r in records if not r["is_defect"]]

    if len(background_records) <= max_background:
        return records

    rng = random.Random(seed + 20260324)
    sampled_background = background_records[:]
    rng.shuffle(sampled_background)
    sampled_background = sampled_background[:max_background]

    limited_records = defect_records + sampled_background
    rng = random.Random(seed + 20260325)
    rng.shuffle(limited_records)
    return limited_records


def simple_split(records: List[dict], train_ratio: float, seed: int) -> Tuple[List[dict], List[dict]]:
    if not records:
        return [], []

    # A singleton cannot represent both splits; keep it in training so the
    # corresponding class is learnable and let the data audit warn about the
    # insufficient validation count.
    if len(records) == 1:
        return records[:], []

    rng = random.Random(seed)
    shuffled = records[:]
    rng.shuffle(shuffled)

    split_index = max(1, min(len(shuffled) - 1, int(len(shuffled) * train_ratio)))
    train_records = shuffled[:split_index]
    val_records = shuffled[split_index:]

    return train_records, val_records


def stratified_split(
    records: List[dict],
    train_ratio: float,
    seed: int,
) -> Tuple[List[dict], List[dict]]:
    groups: Dict[tuple[str, ...], List[dict]] = {}
    for record in records:
        class_signature = tuple(sorted(record.get("class_names", [])))
        group_key = class_signature or ("__background__",)
        groups.setdefault(group_key, []).append(record)

    train_records: List[dict] = []
    val_records: List[dict] = []
    for group_index, group_key in enumerate(sorted(groups)):
        group_train, group_val = simple_split(
            groups[group_key],
            train_ratio,
            seed + group_index,
        )
        train_records.extend(group_train)
        val_records.extend(group_val)

    # Keep output order deterministic after splitting each label signature.
    rng = random.Random(seed + 999)
    rng.shuffle(train_records)
    rng.shuffle(val_records)

    return train_records, val_records


def _clone_oversampled_record(record: dict, copy_index: int) -> dict:
    suffix = f"__os{copy_index:02d}"
    output_image = Path(record["output_image_name"])
    cloned = dict(record)
    cloned["output_image_name"] = f"{output_image.stem}{suffix}{output_image.suffix}"
    cloned["output_label_name"] = f"{output_image.stem}{suffix}.txt"
    cloned["is_oversampled"] = True
    cloned["oversample_copy_index"] = copy_index
    return cloned


def oversample_train_records(
    records: List[dict],
    target_class: str | None,
    oversample_factor: float,
    seed: int,
) -> List[dict]:
    if not target_class or oversample_factor <= 1.0:
        return records

    eligible_records = _select_target_records(records, target_class)
    if not eligible_records:
        print(f"[WARN] No train samples contain oversample class: {target_class}")
        return records

    if oversample_factor < 1.0:
        raise ValueError(f"oversample-factor must be >= 1.0, got {oversample_factor}")

    rng = random.Random(seed + 20260326)
    oversampled_records = records[:]

    full_repeats = int(oversample_factor) - 1
    fractional_part = oversample_factor - int(oversample_factor)
    copy_index_map: Dict[str, int] = {}

    for _ in range(full_repeats):
        for record in eligible_records:
            source_key = str(record["image_path"])
            copy_index_map[source_key] = copy_index_map.get(source_key, 0) + 1
            oversampled_records.append(_clone_oversampled_record(record, copy_index_map[source_key]))

    extra_count = int(round(len(eligible_records) * fractional_part))
    if extra_count > 0:
        sampled_records = eligible_records[:]
        rng.shuffle(sampled_records)
        for record in sampled_records[:extra_count]:
            source_key = str(record["image_path"])
            copy_index_map[source_key] = copy_index_map.get(source_key, 0) + 1
            oversampled_records.append(_clone_oversampled_record(record, copy_index_map[source_key]))

    rng.shuffle(oversampled_records)
    return oversampled_records


def _parse_yolo_line(line: str) -> tuple[int, float, float, float, float]:
    class_id, x, y, width, height = line.split()
    return int(class_id), float(x), float(y), float(width), float(height)


def _format_yolo_line(class_id: int, x: float, y: float, width: float, height: float) -> str:
    return f"{class_id} {x:.10f} {y:.10f} {width:.10f} {height:.10f}"


def _clamp_center(value: float, size: float) -> float:
    half = size / 2.0
    return max(half, min(1.0 - half, value))


def _apply_profile_to_yolo_lines(lines: List[str], profile: dict) -> List[str]:
    translated_lines: List[str] = []
    translate_x = float(profile["translate_x"])
    translate_y = float(profile["translate_y"])

    for line in lines:
        class_id, x, y, width, height = _parse_yolo_line(line)
        x = 1.0 - x
        x = _clamp_center(x + translate_x, width)
        y = _clamp_center(y + translate_y, height)
        translated_lines.append(_format_yolo_line(class_id, x, y, width, height))

    return translated_lines


def _build_augmentation_profile(rng: random.Random) -> dict:
    return {
        "flip_lr": True,
        "brightness_scale": round(rng.uniform(0.94, 1.06), 6),
        "translate_x": round(rng.uniform(-0.03, 0.03), 6),
        "translate_y": round(rng.uniform(-0.03, 0.03), 6),
    }


def _clone_augmented_record(record: dict, copy_index: int, profile: dict) -> dict:
    suffix = f"__aug{copy_index:02d}"
    output_image = Path(record["output_image_name"])
    cloned = dict(record)
    cloned["output_image_name"] = f"{output_image.stem}{suffix}{output_image.suffix}"
    cloned["output_label_name"] = f"{output_image.stem}{suffix}.txt"
    cloned["yolo_lines"] = _apply_profile_to_yolo_lines(record["yolo_lines"], profile)
    cloned["is_augmented"] = True
    cloned["augmentation_copy_index"] = copy_index
    cloned["augmentation_profile"] = profile
    return cloned


def augment_train_records(
    records: List[dict],
    target_class: str | None,
    augment_factor: float,
    seed: int,
) -> List[dict]:
    if not target_class or augment_factor <= 1.0:
        return records

    if augment_factor < 1.0:
        raise ValueError(f"augment-factor must be >= 1.0, got {augment_factor}")

    eligible_records = _select_target_records(records, target_class)
    if not eligible_records:
        print(f"[WARN] No train samples contain augment class: {target_class}")
        return records

    rng = random.Random(seed + 20260327)
    augmented_records = records[:]
    full_repeats = int(augment_factor) - 1
    fractional_part = augment_factor - int(augment_factor)
    copy_index_map: Dict[str, int] = {}

    for _ in range(full_repeats):
        for record in eligible_records:
            source_key = str(record["image_path"])
            copy_index_map[source_key] = copy_index_map.get(source_key, 0) + 1
            profile = _build_augmentation_profile(rng)
            augmented_records.append(
                _clone_augmented_record(record, copy_index_map[source_key], profile)
            )

    extra_count = int(round(len(eligible_records) * fractional_part))
    if extra_count > 0:
        sampled_records = eligible_records[:]
        rng.shuffle(sampled_records)
        for record in sampled_records[:extra_count]:
            source_key = str(record["image_path"])
            copy_index_map[source_key] = copy_index_map.get(source_key, 0) + 1
            profile = _build_augmentation_profile(rng)
            augmented_records.append(
                _clone_augmented_record(record, copy_index_map[source_key], profile)
            )

    rng.shuffle(augmented_records)
    return augmented_records


def _select_target_records(records: List[dict], target_class: str) -> List[dict]:
    if target_class == ALL_DEFECTS:
        return [record for record in records if record.get("is_defect", False)]
    return [record for record in records if target_class in record.get("class_names", [])]


def _read_image(path: Path) -> np.ndarray:
    file_bytes = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"Failed to read image: {path}")
    return image


def _write_image(path: Path, image: np.ndarray) -> None:
    ext = path.suffix or ".bmp"
    ok, encoded = cv2.imencode(ext, image)
    if not ok:
        raise ValueError(f"Failed to encode image for output: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    encoded.tofile(str(path))


def _apply_augmentation_to_image(src_image: Path, dst_image: Path, profile: dict) -> None:
    image = _read_image(src_image)
    if profile.get("flip_lr", False):
        image = cv2.flip(image, 1)

    height, width = image.shape[:2]
    tx = int(round(float(profile["translate_x"]) * width))
    ty = int(round(float(profile["translate_y"]) * height))
    matrix = np.float32([[1, 0, tx], [0, 1, ty]])
    image = cv2.warpAffine(
        image,
        matrix,
        (width, height),
        flags=cv2.INTER_LINEAR,
        borderMode=cv2.BORDER_REFLECT_101,
    )

    brightness_scale = float(profile["brightness_scale"])
    image = np.clip(image.astype(np.float32) * brightness_scale, 0, 255).astype(np.uint8)
    _write_image(dst_image, image)


def write_split(
    records: List[dict],
    split: str,
    out_root: Path,
    use_copy: bool,
) -> None:
    images_dir = out_root / "images" / split
    labels_dir = out_root / "labels" / split

    ensure_dir(images_dir)
    ensure_dir(labels_dir)

    for record in records:
        src_image = record["image_path"]
        dst_image = images_dir / record["output_image_name"]
        dst_label = labels_dir / record["output_label_name"]

        if record.get("augmentation_profile"):
            _apply_augmentation_to_image(src_image, dst_image, record["augmentation_profile"])
        else:
            copy_or_link(src_image, dst_image, use_copy=use_copy)

        lines = record["yolo_lines"]
        label_text = "\n".join(lines)
        if label_text:
            label_text += "\n"

        save_text(dst_label, label_text)


def write_manifest(
    train_records: List[dict],
    val_records: List[dict],
    out_root: Path,
    raw_root: Path,
    train_ratio: float,
    seed: int,
    max_background: int | None,
    source_total_count: int,
    source_normal_count: int,
    source_defect_count: int,
    oversample_class: str | None,
    oversample_factor: float,
    augment_class: str | None,
    augment_factor: float,
) -> None:
    manifest = {
        "meta": {
            "raw_root": str(raw_root),
            "train_ratio": train_ratio,
            "seed": seed,
            "max_background": max_background,
            "oversample_class": oversample_class,
            "oversample_factor": oversample_factor,
            "augment_class": augment_class,
            "augment_factor": augment_factor,
            "class_map": CLASS_MAP,
        },
        "summary": {
            "source_total_count": source_total_count,
            "source_normal_count": source_normal_count,
            "source_defect_count": source_defect_count,
            "train_count": len(train_records),
            "val_count": len(val_records),
            "total_count": len(train_records) + len(val_records),
            "train_oversampled_count": sum(1 for r in train_records if r.get("is_oversampled")),
            "train_augmented_count": sum(1 for r in train_records if r.get("is_augmented")),
            "train_normal_count": sum(1 for r in train_records if not r["is_defect"]),
            "train_defect_count": sum(1 for r in train_records if r["is_defect"]),
            "val_normal_count": sum(1 for r in val_records if not r["is_defect"]),
            "val_defect_count": sum(1 for r in val_records if r["is_defect"]),
        },
        "train": [
            {
                "image": rec["output_image_name"],
                "source_image": str(rec["image_path"]),
                "source_json": str(rec["json_path"]),
                "review_status": rec["review_status"],
                "label_count": rec["label_count"],
                "is_defect": rec["is_defect"],
                "class_names": rec.get("class_names", []),
                "is_oversampled": rec.get("is_oversampled", False),
                "oversample_copy_index": rec.get("oversample_copy_index", 0),
                "is_augmented": rec.get("is_augmented", False),
                "augmentation_copy_index": rec.get("augmentation_copy_index", 0),
                "augmentation_profile": rec.get("augmentation_profile"),
            }
            for rec in train_records
        ],
        "val": [
            {
                "image": rec["output_image_name"],
                "source_image": str(rec["image_path"]),
                "source_json": str(rec["json_path"]),
                "review_status": rec["review_status"],
                "label_count": rec["label_count"],
                "is_defect": rec["is_defect"],
                "class_names": rec.get("class_names", []),
                "is_oversampled": rec.get("is_oversampled", False),
                "oversample_copy_index": rec.get("oversample_copy_index", 0),
                "is_augmented": rec.get("is_augmented", False),
                "augmentation_copy_index": rec.get("augmentation_copy_index", 0),
                "augmentation_profile": rec.get("augmentation_profile"),
            }
            for rec in val_records
        ],
    }

    save_json(out_root / "manifest.json", manifest)


def clear_output_dirs(out_root: Path) -> None:
    for sub in ["images", "labels"]:
        target = out_root / sub
        if target.exists():
            shutil.rmtree(target)

    for extra_file in ["data.yaml", "manifest.json"]:
        target = out_root / extra_file
        if target.exists():
            target.unlink()


def main() -> int:
    args = parse_args()

    raw_root = Path(args.raw_root).expanduser().resolve()
    out_root = Path(args.out_root).expanduser().resolve()

    if not raw_root.exists() or not raw_root.is_dir():
        print(f"[ERROR] raw-root does not exist or is not a directory: {raw_root}")
        return 1

    if not (0.0 < args.train_ratio < 1.0):
        print(f"[ERROR] train-ratio must be between 0 and 1: {args.train_ratio}")
        return 1

    if args.oversample_factor < 1.0:
        print(f"[ERROR] oversample-factor must be >= 1.0: {args.oversample_factor}")
        return 1

    valid_balance_targets = {*CLASS_MAP, ALL_DEFECTS}
    if args.oversample_class is not None and args.oversample_class not in valid_balance_targets:
        print(
            f"[ERROR] oversample-class must be one of {sorted(valid_balance_targets)}: {args.oversample_class}"
        )
        return 1

    if args.augment_factor < 1.0:
        print(f"[ERROR] augment-factor must be >= 1.0: {args.augment_factor}")
        return 1

    if args.augment_class is not None and args.augment_class not in valid_balance_targets:
        print(
            f"[ERROR] augment-class must be one of {sorted(valid_balance_targets)}: {args.augment_class}"
        )
        return 1

    ensure_dir(out_root)
    clear_output_dirs(out_root)

    records = build_sample_records(raw_root, CLASS_MAP)
    source_total_count = len(records)
    source_normal_count = sum(1 for r in records if not r["is_defect"])
    source_defect_count = sum(1 for r in records if r["is_defect"])

    if not records:
        print("[ERROR] No usable samples found after filtering.")
        return 1

    records = limit_background_records(
        records=records,
        max_background=args.max_background,
        seed=args.seed,
    )

    train_records, val_records = stratified_split(
        records=records,
        train_ratio=args.train_ratio,
        seed=args.seed,
    )

    train_records = oversample_train_records(
        records=train_records,
        target_class=args.oversample_class,
        oversample_factor=args.oversample_factor,
        seed=args.seed,
    )

    train_records = augment_train_records(
        records=train_records,
        target_class=args.augment_class,
        augment_factor=args.augment_factor,
        seed=args.seed,
    )

    if len(train_records) == 0 or len(val_records) == 0:
        print("[ERROR] Invalid split result. One of train/val is empty.")
        return 1

    write_split(train_records, "train", out_root, use_copy=args.copy)
    write_split(val_records, "val", out_root, use_copy=args.copy)

    save_data_yaml(out_root / "data.yaml", CLASS_MAP)
    write_manifest(
        train_records=train_records,
        val_records=val_records,
        out_root=out_root,
        raw_root=raw_root,
        train_ratio=args.train_ratio,
        seed=args.seed,
        max_background=args.max_background,
        source_total_count=source_total_count,
        source_normal_count=source_normal_count,
        source_defect_count=source_defect_count,
        oversample_class=args.oversample_class,
        oversample_factor=args.oversample_factor,
        augment_class=args.augment_class,
        augment_factor=args.augment_factor,
    )

    total_count = len(records)
    total_normal = sum(1 for r in records if not r["is_defect"])
    total_defect = sum(1 for r in records if r["is_defect"])
    train_oversampled = sum(1 for r in train_records if r.get("is_oversampled"))
    train_augmented = sum(1 for r in train_records if r.get("is_augmented"))

    print("[INFO] YOLO workspace prepared successfully.")
    print(f"[INFO] raw_root            : {raw_root}")
    print(f"[INFO] out_root            : {out_root}")
    print(f"[INFO] source total        : {source_total_count}")
    print(f"[INFO] source normal       : {source_normal_count}")
    print(f"[INFO] source defect       : {source_defect_count}")
    print(f"[INFO] max background      : {args.max_background}")
    print(f"[INFO] oversample class    : {args.oversample_class}")
    print(f"[INFO] oversample factor   : {args.oversample_factor}")
    print(f"[INFO] augment class       : {args.augment_class}")
    print(f"[INFO] augment factor      : {args.augment_factor}")
    print(f"[INFO] total samples       : {total_count}")
    print(f"[INFO] total normal        : {total_normal}")
    print(f"[INFO] total defect        : {total_defect}")
    print(f"[INFO] train samples       : {len(train_records)}")
    print(f"[INFO] val samples         : {len(val_records)}")
    print(f"[INFO] train oversampled   : {train_oversampled}")
    print(f"[INFO] train augmented     : {train_augmented}")
    print(f"[INFO] train normal/defect : "
          f"{sum(1 for r in train_records if not r['is_defect'])}/"
          f"{sum(1 for r in train_records if r['is_defect'])}")
    print(f"[INFO] val normal/defect   : "
          f"{sum(1 for r in val_records if not r['is_defect'])}/"
          f"{sum(1 for r in val_records if r['is_defect'])}")
    print(f"[INFO] classes             : {CLASS_MAP}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
