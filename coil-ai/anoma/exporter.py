from __future__ import annotations

from pathlib import Path
from typing import Any


def export_onnx(*, train_result: dict[str, Any], out_dir: Path, args: Any) -> Path:
    adapter = train_result["adapter"]
    model = train_result["model"]
    engine = train_result["engine"]
    checkpoint_path = train_result.get("best_model_path")
    return adapter.export_onnx(
        model=model,
        engine=engine,
        out_dir=out_dir,
        checkpoint_path=checkpoint_path,
        args=args,
    )
