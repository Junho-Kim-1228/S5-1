# coil-ai

YOLO detector training and anomaly training live in one Python project so the C# WPF Training UI can keep a stable execution contract while the internal code stays modular.

## Project Structure

```text
coil-ai/
  README.md
  requirements-train.txt
  .gitignore

  scripts/
    train_yolo.py
    train_anoma.py

  common/
    __init__.py
    cli.py
    io_utils.py
    logging_utils.py
    path_utils.py
    seed.py
    summary.py
    exceptions.py

  yolo/
    __init__.py
    config.py
    workspace.py
    trainer.py
    exporter.py
    metrics.py

  anoma/
    __init__.py
    config.py
    workspace.py
    trainer.py
    exporter.py
    adapters.py
    metrics.py

  assets/
    weights/
      .gitkeep
```

## Folder Roles

- `scripts/`
  - Thin entrypoints used directly by the C# Training UI.
  - Keep the external CLI contract stable.
- `common/`
  - Shared utilities for path handling, summary saving, logging, seed setup, and common exceptions.
- `yolo/`
  - YOLO-specific argument parsing, workspace validation, training, metric extraction, and ONNX export.
- `anoma/`
  - Anomaly-specific argument parsing, workspace validation, training orchestration, adapter-based model construction, and ONNX export.
- `assets/weights/`
  - Local pretrained weight storage.
  - Recommended place for `yolo11n.pt`, `yolov8n.pt`, and future local checkpoints that should not live at project root.

## External Contract With C# Training UI

The following commands must remain valid:

```bash
python scripts/train_yolo.py --workspace "<workspace>" --out "<out>"
python scripts/train_anoma.py --workspace "<workspace>" --out "<out>"
```

Expected behavior:

- Success creates:
  - `<out>/yolo.onnx`
  - `<out>/anoma.onnx`
  - `<out>/train_summary.json`
- Failure exits with non-zero exit code.
- Logs are written to stdout/stderr.

Workspace contract:

- YOLO workspace:
  - `images/train`
  - `images/val`
  - `labels/train`
  - `labels/val`
  - `data.yaml`
- Anoma workspace:
  - `train`
  - `val`

## Weight File Rules

YOLO base weight lookup order:

1. Explicit `--model`
2. `assets/weights/yolo11n.pt`
3. `assets/weights/yolov8n.pt`
4. Legacy root fallback `yolo11n.pt`, `yolov8n.pt` for compatibility

Recommended practice:

- Keep local weights in `assets/weights/`
- Do not commit them to git

## Runtime Artifacts

The project may contain local runtime artifacts during experimentation, but they should be treated as non-source data:

- `.venv_train/`
- `runs/`
- `datasets/`
- `runtime/`
- `__pycache__/`

These are ignored through `.gitignore`.

If you want a cleaner local layout, prefer:

```text
runtime/
  runs/
  datasets/
```

The code keeps the CLI contract unchanged, so the C# app can still pass any explicit `--workspace` and `--out` path it already uses.

## Setup

Python 3.10+ is required.

```bash
python -m venv .venv_train
source .venv_train/bin/activate
pip install -r requirements-train.txt
```

Windows CMD:

```bat
python -m venv .venv_train
.venv_train\Scripts\activate
pip install -r requirements-train.txt
```

## Notes

- `scripts/` intentionally contains very little logic.
- `anoma/` is adapter-based so the final anomaly model choice can change later.
- TODO markers remain around dataset transforms, model selection, threshold strategy, and export graph verification.
