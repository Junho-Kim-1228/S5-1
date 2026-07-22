# coil-ai

YOLO detector training and anomaly training live in one Python project. The goal is to keep the C# WPF Training UI contract stable while the Python code stays modular and easy to extend.

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
    exceptions.py
    io_utils.py
    logging_utils.py
    path_utils.py
    seed.py
    summary.py

  yolo/
    __init__.py
    config.py
    workspace.py
    trainer.py
    exporter.py
    metrics.py
    model_factory.py
    models/
      README.md
      yolov8l_c2f_rvb.yaml
      modules/
        __init__.py
        c2f_rvb.py

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
      yolov8n.pt
      yolov8l.pt

  experiments/
    README.md
    yolo/
      yolov8n_baseline/
      yolov8l_baseline/
      yolov8l_c2f_rvb/
    anoma/

  outputs/
    .gitkeep
    yolo/
    anoma/

  datasets/
    .gitkeep
```

## Folder Roles

- `scripts/`
  - Thin entrypoints called by the C# Training UI.
  - Keep the external CLI contract stable.
- `common/`
  - Shared utilities for paths, logging, seed setup, output directories, and training summaries.
- `yolo/`
  - YOLO-specific configuration, workspace validation, training, export, metrics, and model construction.
- `yolo/models/`
  - Research-only model YAMLs and custom modules for future experiments.
  - The default training flow still uses pretrained `.pt` weights unless `--model` points to a custom YAML.
- `anoma/`
  - Anomaly-specific configuration, adapter-based training, workspace validation, metrics, and export.
- `assets/weights/`
  - Local pretrained weight storage.
  - Keep `.pt` files here and do not commit real weights.
- `experiments/`
  - Human-managed experiment notes, configs, and scratch folders.
- `outputs/`
  - Runtime training outputs such as exported ONNX files and summaries.
- `datasets/`
  - Local dataset staging area. The C# UI can create workspaces under here if desired.

## C# Training UI Contract

These commands must remain valid:

```bash
python scripts/train_yolo.py --workspace "<workspace>" --out "<out>"
python scripts/train_anoma.py --workspace "<workspace>" --out "<out>"
```

Expected behavior:

- Success creates:
  - `<out>/yolo.onnx`
  - `<out>/anoma.onnx`
  - `<out>/train_summary.json`
- Failure exits with a non-zero exit code.
- Logs are written to stdout and stderr.

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

## Weight Rules

YOLO model resolution order:

1. Explicit `--model`
2. `assets/weights/yolov8n.pt`
3. `assets/weights/yolov8l.pt`
4. Legacy root fallback `yolov8n.pt`, `yolov8l.pt`

Notes:

- The `.pt` files under `assets/weights/` are intentionally local-only.
- `yolo/models/` is for custom YAML-based experiments.
- If you later use a custom model YAML, keep the CLI the same and pass the YAML path via `--model`.

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

### Dinomaly Research Environment

Dinomaly uses Anomalib 2.x and a newer CUDA build of PyTorch, so keep it in a
separate environment from the existing YOLO/PaDiM/PatchCore environment:

```powershell
.\.venv_train\Scripts\python.exe -m venv .venv_dinomaly
.\.venv_dinomaly\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r requirements-dinomaly.txt
```

The first Dinomaly run downloads the pretrained DINOv2 encoder weights. The
cache defaults to `assets/weights/huggingface` unless `HF_HOME` is already set.

## Audit Raw Training Data

Before training, validate image decoding, `*.state.json` pairing, anomaly labels,
YOLO boxes, class counts, and duplicate images:

```powershell
python scripts/audit_training_data.py `
  --raw-root "raw_data/coil_v1" `
  --report "outputs/data_audit_coil_v1.json"
```

The command exits with code `2` when blocking data errors are found. Warnings do
not block training, but should be reviewed before reporting model metrics.

## Direct Training From Raw Data (Windows PowerShell)

Anoma reads the audited raw folder directly. YOLO first converts the same raw
folder into a class-preserving train/validation workspace:

```powershell
.\.venv_train\Scripts\Activate.ps1

python scripts/train_anoma.py `
  --workspace "raw_data/coil_v1" `
  --out "outputs/anoma/coil_v1_padim" `
  --dataset-name "coil_v1" `
  --model padim --image-size 640 --batch-size 8 --device cuda

python scripts/prepare_yolo_workspace.py `
  --raw-root "raw_data/coil_v1" `
  --out-root "datasets/yolo/coil_v1" `
  --augment-class all --augment-factor 2.0

python scripts/train_yolo.py `
  --workspace "datasets/yolo/coil_v1" `
  --out "outputs/yolo/coil_v1_yolov8l" `
  --model "assets/weights/yolov8l.pt" `
  --epochs 150 --imgsz 1024 --batch 4 --device 0
```

Run the Dinomaly comparison directly from `coil-ai` after activating
`.venv_dinomaly`:

```powershell
python scripts/train_anoma.py `
  --workspace "raw_data/pcb" `
  --out "outputs/anoma/pcb_v1_dinomaly_b448" `
  --dataset-name "pcb_v1_dinomaly_b448" `
  --model dinomaly `
  --image-size 448 `
  --batch-size 8 `
  --device auto `
  --seed 42 `
  --dinomaly-encoder vit_base_patch14_reg4_dinov2 `
  --dinomaly-dropout 0.2 `
  --dinomaly-decoder-depth 8 `
  --dinomaly-max-steps 5000
```

Dinomaly input sizes must be divisible by the DINOv2 patch size (`14`). Use
`--skip-export` for metric-only experiments. A successful full run keeps the
same deployment contract as the other anomaly models: `anoma.onnx` outputs
`anomaly_score` and `anomaly_map`, and `inference_config.json` stores the
validation-calibrated threshold and preprocessing contract.

Run the audit first. If CUDA memory is insufficient, lower the YOLO batch to
`2`; if training is stable, try `8` for higher throughput.

## Notes

- `scripts/` intentionally contains very little logic.
- `anoma/` is adapter-based so the final anomaly framework can change later.
- `yolo/models/` is intentionally light. The custom YAML and module are placeholders for future paper-driven experiments.
- Runtime folders under `outputs/` and `datasets/` are kept separate from source code.
