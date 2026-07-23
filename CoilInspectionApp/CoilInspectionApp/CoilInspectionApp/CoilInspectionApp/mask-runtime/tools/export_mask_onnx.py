from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np


RUNTIME_ROOT = Path(__file__).resolve().parents[1]
SRC_ROOT = RUNTIME_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

import config_ai  # noqa: E402
from segment_model import build_segmenter  # noqa: E402


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export the coil segmentation checkpoint as a deployment ONNX model."
    )
    parser.add_argument("--checkpoint", type=Path, default=config_ai.MODEL_PATH)
    parser.add_argument(
        "--output",
        type=Path,
        default=RUNTIME_ROOT / "models" / "mask.onnx",
    )
    parser.add_argument("--input-size", type=int, default=config_ai.INPUT_SIZE)
    parser.add_argument("--opset", type=int, default=17)
    parser.add_argument(
        "--verification-json",
        type=Path,
        default=None,
        help="Optional JSON path for export and ONNX Runtime verification metadata.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.input_size <= 0:
        raise ValueError("--input-size must be positive")

    segmenter = build_segmenter(
        model_path=args.checkpoint,
        device="cpu",
        input_size=args.input_size,
        encoder_name=config_ai.ENCODER_NAME,
    )
    torch = segmenter.torch

    class ProbabilityModel(torch.nn.Module):
        def __init__(self, model):
            super().__init__()
            self.model = model

        def forward(self, images):
            logits = self.model(images)
            if isinstance(logits, (tuple, list)):
                logits = logits[0]
            return torch.sigmoid(logits)

    model = ProbabilityModel(segmenter.model).cpu().eval()
    sample = torch.randn(1, 3, args.input_size, args.input_size, dtype=torch.float32)
    args.output.parent.mkdir(parents=True, exist_ok=True)

    with torch.inference_mode():
        torch.onnx.export(
            model,
            sample,
            str(args.output),
            input_names=["images"],
            output_names=["probability"],
            dynamic_axes={"images": {0: "batch"}, "probability": {0: "batch"}},
            opset_version=args.opset,
            do_constant_folding=True,
            dynamo=False,
        )

    import onnxruntime as ort

    session = ort.InferenceSession(str(args.output), providers=["CPUExecutionProvider"])
    with torch.inference_mode():
        expected = model(sample).cpu().numpy()
    actual = session.run(["probability"], {"images": sample.cpu().numpy()})[0]
    absolute_error = np.abs(expected - actual)

    metadata = {
        "schema_version": 1,
        "checkpoint": str(args.checkpoint.resolve()),
        "output": str(args.output.resolve()),
        "onnx_bytes": args.output.stat().st_size,
        "input": {"name": "images", "shape": ["N", 3, args.input_size, args.input_size]},
        "output_tensor": {
            "name": "probability",
            "shape": ["N", 1, args.input_size, args.input_size],
            "sigmoid_applied": True,
        },
        "encoder_name": segmenter.encoder_name,
        "checkpoint_load": segmenter.load_meta,
        "verification": {
            "max_abs_error": float(absolute_error.max()),
            "mean_abs_error": float(absolute_error.mean()),
        },
    }
    print(json.dumps(metadata, ensure_ascii=False, indent=2))

    if args.verification_json is not None:
        args.verification_json.parent.mkdir(parents=True, exist_ok=True)
        args.verification_json.write_text(
            json.dumps(metadata, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )

    if metadata["verification"]["max_abs_error"] > 1e-4:
        raise RuntimeError("ONNX verification failed: max_abs_error is greater than 1e-4")


if __name__ == "__main__":
    main()
