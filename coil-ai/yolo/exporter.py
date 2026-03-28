import shutil
from pathlib import Path

from common.exceptions import ExportError


def export_yolo_to_onnx(model, out_path: Path, *, imgsz: int = 640) -> Path:
    out_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        exported = model.export(format="onnx", imgsz=imgsz)
        exported_path = Path(str(exported)).resolve()

        if not exported_path.exists():
            raise ExportError(f"Exported ONNX file not found: {exported_path}")

        shutil.copy2(exported_path, out_path)

        if not out_path.exists():
            raise ExportError(f"Final ONNX file not created: {out_path}")

        return out_path
    except Exception as e:
        raise ExportError(f"YOLO ONNX export failed: {e}") from e
