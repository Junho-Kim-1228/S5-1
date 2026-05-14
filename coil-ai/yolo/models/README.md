# YOLO Research Models

This directory is reserved for custom YOLO architecture experiments that go beyond the standard pretrained `.pt` flow.

Current contents:

- `yolov8l_c2f_rvb.yaml`
  - Placeholder research config for a YOLOv8l-style model variant that can reference `C2FRVB`.
- `modules/c2f_rvb.py`
  - Lightweight custom block placeholder for future paper-driven experiments.

Notes:

- The default project flow still expects `.pt` weights under `assets/weights/`.
- These files are intentionally light and should be treated as a starting point, not a locked final architecture.
- If you later train from a custom YAML, pass the YAML path through `--model`.
