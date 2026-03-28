# Experiments

Use this directory for experiment-specific notes, frozen configs, and comparison snapshots.

Suggested usage:

- `yolo/yolov8n_baseline/`
  - Baseline runs with standard `yolov8n.pt`
- `yolo/yolov8l_baseline/`
  - Baseline runs with standard `yolov8l.pt`
- `yolo/yolov8l_c2f_rvb/`
  - Research runs for the custom YAML and module variant
- `anoma/`
  - Anomaly experiment notes and config snapshots

Keep large runtime artifacts in `outputs/` instead of committing them here.
