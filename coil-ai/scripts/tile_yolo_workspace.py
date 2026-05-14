from __future__ import annotations

import argparse
import json
import random
import shutil
from pathlib import Path
from typing import Any

import cv2
import numpy as np
import yaml


VALID_IMAGE_EXTS = {".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".webp"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Create a tiled YOLO workspace from an existing YOLO workspace."
    )
    parser.add_argument("--workspace-in", required=True, help="Source YOLO workspace root.")
    parser.add_argument("--out-root", required=True, help="Output tiled YOLO workspace root.")
    parser.add_argument("--tile-size", type=int, default=1024, help="Tile size in pixels.")
    parser.add_argument("--stride", type=int, default=768, help="Tile stride in pixels.")
    parser.add_argument(
        "--min-box-area-ratio",
        type=float,
        default=0.3,
        help="Minimum clipped/original box area ratio to keep a box in a tile.",
    )
    parser.add_argument(
        "--background-tiles-per-image",
        type=int,
        default=1,
        help="How many empty tiles to keep for a background-only source image.",
    )
    parser.add_argument("--seed", type=int, default=42, help="Random seed for background tile sampling.")
    return parser.parse_args()


def ensure_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def clear_output_dirs(out_root: Path) -> None:
    for sub in ("images", "labels"):
        target = out_root / sub
        if target.exists():
            shutil.rmtree(target)
    for extra_file in ("data.yaml", "manifest.json"):
        target = out_root / extra_file
        if target.exists():
            target.unlink()


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        data = yaml.safe_load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"Invalid YAML file: {path}")
    return data


def save_yaml(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        yaml.safe_dump(payload, handle, sort_keys=False, allow_unicode=False)


def save_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def save_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")


def validate_workspace(workspace: Path) -> None:
    required = [
        workspace / "images" / "train",
        workspace / "images" / "val",
        workspace / "labels" / "train",
        workspace / "labels" / "val",
        workspace / "data.yaml",
    ]
    missing = [str(path) for path in required if not path.exists()]
    if missing:
        raise ValueError(f"Workspace is missing required paths: {missing}")


def find_image_files(images_dir: Path) -> list[Path]:
    return sorted(
        path for path in images_dir.iterdir() if path.is_file() and path.suffix.lower() in VALID_IMAGE_EXTS
    )


def read_image(path: Path) -> np.ndarray:
    file_bytes = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"Failed to read image: {path}")
    return image


def write_image(path: Path, image: np.ndarray) -> None:
    ext = path.suffix or ".bmp"
    ok, encoded = cv2.imencode(ext, image)
    if not ok:
        raise ValueError(f"Failed to encode image: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    encoded.tofile(str(path))


def label_path_for_image(image_path: Path, labels_dir: Path) -> Path:
    return labels_dir / f"{image_path.stem}.txt"


def parse_yolo_labels(label_path: Path, image_width: int, image_height: int) -> list[dict[str, Any]]:
    if not label_path.exists():
        raise ValueError(f"Missing label file for image: {label_path}")

    boxes: list[dict[str, Any]] = []
    for raw_line in label_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line:
            continue
        parts = line.split()
        if len(parts) != 5:
            raise ValueError(f"Invalid YOLO label line in {label_path}: {line}")
        class_id = int(parts[0])
        cx, cy, bw, bh = map(float, parts[1:])
        abs_w = bw * image_width
        abs_h = bh * image_height
        abs_cx = cx * image_width
        abs_cy = cy * image_height
        x1 = abs_cx - abs_w / 2.0
        y1 = abs_cy - abs_h / 2.0
        x2 = abs_cx + abs_w / 2.0
        y2 = abs_cy + abs_h / 2.0
        boxes.append(
            {
                "class_id": class_id,
                "x1": x1,
                "y1": y1,
                "x2": x2,
                "y2": y2,
                "area": max(abs_w * abs_h, 1e-9),
            }
        )
    return boxes


def compute_positions(full_size: int, tile_size: int, stride: int) -> list[int]:
    if full_size <= tile_size:
        return [0]

    positions = list(range(0, max(full_size - tile_size, 0) + 1, stride))
    last = full_size - tile_size
    if positions[-1] != last:
        positions.append(last)
    return sorted(set(positions))


def clip_box_to_tile(box: dict[str, Any], tile_x: int, tile_y: int, tile_w: int, tile_h: int) -> tuple[float, float, float, float] | None:
    x1 = max(box["x1"], tile_x)
    y1 = max(box["y1"], tile_y)
    x2 = min(box["x2"], tile_x + tile_w)
    y2 = min(box["y2"], tile_y + tile_h)
    if x2 <= x1 or y2 <= y1:
        return None
    return x1, y1, x2, y2


def build_tile_records(
    image_path: Path,
    label_path: Path,
    tile_size: int,
    stride: int,
    min_box_area_ratio: float,
    background_tiles_per_image: int,
    rng: random.Random,
) -> list[dict[str, Any]]:
    image = read_image(image_path)
    image_height, image_width = image.shape[:2]
    boxes = parse_yolo_labels(label_path, image_width=image_width, image_height=image_height)
    xs = compute_positions(image_width, tile_size=tile_size, stride=stride)
    ys = compute_positions(image_height, tile_size=tile_size, stride=stride)

    tile_records: list[dict[str, Any]] = []
    for tile_y in ys:
        for tile_x in xs:
            tile_w = min(tile_size, image_width - tile_x)
            tile_h = min(tile_size, image_height - tile_y)
            tile_boxes: list[str] = []

            for box in boxes:
                center_x = (box["x1"] + box["x2"]) / 2.0
                center_y = (box["y1"] + box["y2"]) / 2.0
                if not (tile_x <= center_x < tile_x + tile_w and tile_y <= center_y < tile_y + tile_h):
                    continue

                clipped = clip_box_to_tile(box, tile_x=tile_x, tile_y=tile_y, tile_w=tile_w, tile_h=tile_h)
                if clipped is None:
                    continue
                x1, y1, x2, y2 = clipped
                clipped_area = max((x2 - x1) * (y2 - y1), 1e-9)
                if clipped_area / box["area"] < min_box_area_ratio:
                    continue

                rel_x1 = x1 - tile_x
                rel_y1 = y1 - tile_y
                rel_x2 = x2 - tile_x
                rel_y2 = y2 - tile_y
                clipped_w = rel_x2 - rel_x1
                clipped_h = rel_y2 - rel_y1
                center_x_norm = (rel_x1 + clipped_w / 2.0) / tile_w
                center_y_norm = (rel_y1 + clipped_h / 2.0) / tile_h
                width_norm = clipped_w / tile_w
                height_norm = clipped_h / tile_h
                tile_boxes.append(
                    f"{box['class_id']} {center_x_norm:.10f} {center_y_norm:.10f} {width_norm:.10f} {height_norm:.10f}"
                )

            tile_records.append(
                {
                    "tile_x": tile_x,
                    "tile_y": tile_y,
                    "tile_w": tile_w,
                    "tile_h": tile_h,
                    "image": image,
                    "boxes": tile_boxes,
                    "source_image_name": image_path.name,
                    "is_positive": bool(tile_boxes),
                }
            )

    if boxes:
        return [record for record in tile_records if record["is_positive"]]

    if background_tiles_per_image <= 0:
        return []

    rng.shuffle(tile_records)
    return tile_records[:background_tiles_per_image]


def tile_name(source_image_name: str, tile_x: int, tile_y: int) -> str:
    source_path = Path(source_image_name)
    return f"{source_path.stem}__tile_x{tile_x}_y{tile_y}{source_path.suffix}"


def write_split(
    split: str,
    workspace_in: Path,
    out_root: Path,
    tile_size: int,
    stride: int,
    min_box_area_ratio: float,
    background_tiles_per_image: int,
    seed: int,
) -> dict[str, Any]:
    images_in = workspace_in / "images" / split
    labels_in = workspace_in / "labels" / split
    images_out = out_root / "images" / split
    labels_out = out_root / "labels" / split
    ensure_dir(images_out)
    ensure_dir(labels_out)

    rng = random.Random(seed if split == "train" else seed + 1)
    image_files = find_image_files(images_in)
    manifest_items: list[dict[str, Any]] = []
    positive_tiles = 0
    background_tiles = 0
    total_boxes = 0

    for image_path in image_files:
        label_path = label_path_for_image(image_path, labels_dir=labels_in)
        tile_records = build_tile_records(
            image_path=image_path,
            label_path=label_path,
            tile_size=tile_size,
            stride=stride,
            min_box_area_ratio=min_box_area_ratio,
            background_tiles_per_image=background_tiles_per_image,
            rng=rng,
        )

        for record in tile_records:
            image_name = tile_name(record["source_image_name"], record["tile_x"], record["tile_y"])
            label_name = f"{Path(image_name).stem}.txt"
            tile = record["image"][
                record["tile_y"] : record["tile_y"] + record["tile_h"],
                record["tile_x"] : record["tile_x"] + record["tile_w"],
            ]
            write_image(images_out / image_name, tile)

            label_text = "\n".join(record["boxes"])
            if label_text:
                label_text += "\n"
            save_text(labels_out / label_name, label_text)

            total_boxes += len(record["boxes"])
            if record["is_positive"]:
                positive_tiles += 1
            else:
                background_tiles += 1

            manifest_items.append(
                {
                    "image": image_name,
                    "label": label_name,
                    "source_image": record["source_image_name"],
                    "tile_x": record["tile_x"],
                    "tile_y": record["tile_y"],
                    "tile_w": record["tile_w"],
                    "tile_h": record["tile_h"],
                    "box_count": len(record["boxes"]),
                    "is_positive": record["is_positive"],
                }
            )

    return {
        "count": len(manifest_items),
        "positive_tiles": positive_tiles,
        "background_tiles": background_tiles,
        "bbox_count": total_boxes,
        "items": manifest_items,
    }


def main() -> int:
    args = parse_args()

    workspace_in = Path(args.workspace_in).expanduser().resolve()
    out_root = Path(args.out_root).expanduser().resolve()

    if args.tile_size <= 0:
        print(f"[ERROR] tile-size must be > 0: {args.tile_size}")
        return 1
    if args.stride <= 0:
        print(f"[ERROR] stride must be > 0: {args.stride}")
        return 1
    if not (0.0 <= args.min_box_area_ratio <= 1.0):
        print(f"[ERROR] min-box-area-ratio must be in [0, 1]: {args.min_box_area_ratio}")
        return 1
    if args.background_tiles_per_image < 0:
        print(f"[ERROR] background-tiles-per-image must be >= 0: {args.background_tiles_per_image}")
        return 1

    try:
        validate_workspace(workspace_in)
    except Exception as exc:
        print(f"[ERROR] {exc}")
        return 1

    ensure_dir(out_root)
    clear_output_dirs(out_root)

    source_yaml = load_yaml(workspace_in / "data.yaml")
    save_yaml(
        out_root / "data.yaml",
        {
            "train": "images/train",
            "val": "images/val",
            "nc": source_yaml.get("nc", len(source_yaml.get("names", []))),
            "names": source_yaml.get("names", []),
        },
    )

    train_info = write_split(
        split="train",
        workspace_in=workspace_in,
        out_root=out_root,
        tile_size=args.tile_size,
        stride=args.stride,
        min_box_area_ratio=args.min_box_area_ratio,
        background_tiles_per_image=args.background_tiles_per_image,
        seed=args.seed,
    )
    val_info = write_split(
        split="val",
        workspace_in=workspace_in,
        out_root=out_root,
        tile_size=args.tile_size,
        stride=args.stride,
        min_box_area_ratio=args.min_box_area_ratio,
        background_tiles_per_image=args.background_tiles_per_image,
        seed=args.seed,
    )

    manifest = {
        "meta": {
            "workspace_in": str(workspace_in),
            "tile_size": args.tile_size,
            "stride": args.stride,
            "min_box_area_ratio": args.min_box_area_ratio,
            "background_tiles_per_image": args.background_tiles_per_image,
            "seed": args.seed,
        },
        "summary": {
            "train_count": train_info["count"],
            "train_positive_tiles": train_info["positive_tiles"],
            "train_background_tiles": train_info["background_tiles"],
            "train_bbox_count": train_info["bbox_count"],
            "val_count": val_info["count"],
            "val_positive_tiles": val_info["positive_tiles"],
            "val_background_tiles": val_info["background_tiles"],
            "val_bbox_count": val_info["bbox_count"],
            "total_count": train_info["count"] + val_info["count"],
        },
        "train": train_info["items"],
        "val": val_info["items"],
    }
    save_json(out_root / "manifest.json", manifest)

    print("[INFO] Tiled YOLO workspace prepared successfully.")
    print(f"[INFO] workspace_in              : {workspace_in}")
    print(f"[INFO] out_root                  : {out_root}")
    print(f"[INFO] tile_size                 : {args.tile_size}")
    print(f"[INFO] stride                    : {args.stride}")
    print(f"[INFO] min_box_area_ratio        : {args.min_box_area_ratio}")
    print(f"[INFO] background_tiles_per_image: {args.background_tiles_per_image}")
    print(
        f"[INFO] train tiles               : {train_info['count']} "
        f"(positive={train_info['positive_tiles']}, background={train_info['background_tiles']})"
    )
    print(
        f"[INFO] val tiles                 : {val_info['count']} "
        f"(positive={val_info['positive_tiles']}, background={val_info['background_tiles']})"
    )
    print(
        f"[INFO] train/val bbox            : {train_info['bbox_count']}/{val_info['bbox_count']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
