from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort


RUNTIME_ROOT = Path(__file__).resolve().parents[1]
SRC_ROOT = RUNTIME_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

import config_ai  # noqa: E402
from io_utils_ai import apply_mask_to_image, load_image_bgr, resize_with_padding, restore_from_padding  # noqa: E402
from postprocess_ai import PostprocessConfig, postprocess_probability_map  # noqa: E402


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the deployment Mask ONNX pipeline for parity checks.")
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, default=None)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    image = load_image_bgr(args.input)
    resized, meta = resize_with_padding(image, config_ai.INPUT_SIZE, is_mask=False, pad_value=0)
    rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    mean = np.asarray(config_ai.IMAGE_MEAN, dtype=np.float32).reshape(1, 1, 3)
    std = np.asarray(config_ai.IMAGE_STD, dtype=np.float32).reshape(1, 1, 3)
    tensor = np.transpose((rgb - mean) / std, (2, 0, 1))[None].astype(np.float32)

    session = ort.InferenceSession(str(args.model), providers=["CPUExecutionProvider"])
    input_name = session.get_inputs()[0].name
    probability = session.run(["probability"], {input_name: tensor})[0][0, 0]
    probability = restore_from_padding(probability, meta, is_mask=False)

    post_config = PostprocessConfig(
        confidence_threshold=config_ai.CONF_THRESHOLD,
        mask_threshold=config_ai.MASK_THRESHOLD,
        min_component_area=config_ai.MIN_COMPONENT_AREA,
        morph_open_kernel=config_ai.MORPH_OPEN_KERNEL,
        morph_close_kernel=config_ai.MORPH_CLOSE_KERNEL,
        outer_recover_kernel=config_ai.OUTER_RECOVER_KERNEL,
        keep_largest_component=config_ai.KEEP_LARGEST_COMPONENT,
        preserve_inner_holes=config_ai.PRESERVE_INNER_HOLES,
        min_hole_area=config_ai.MIN_HOLE_AREA,
    )
    mask = postprocess_probability_map(probability, post_config)
    masked = apply_mask_to_image(image, mask)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if not cv2.imwrite(str(args.output), masked):
        raise RuntimeError(f"failed to save output: {args.output}")

    report = {
        "input": str(args.input.resolve()),
        "model": str(args.model.resolve()),
        "output": str(args.output.resolve()),
        "probability_min": float(probability.min()),
        "probability_max": float(probability.max()),
        "prediction_score_p99_5": float(np.percentile(probability, 99.5)),
        "mask_pixels": int(np.count_nonzero(mask)),
        "image_pixels": int(mask.size),
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if args.report is not None:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
