from __future__ import annotations

import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from common.cli import build_train_parser
from common.exceptions import CoilAIError
from common.logging_utils import configure_logging, get_logger
from yolo.trainer import run_yolo_training


def main() -> int:
    parser = build_train_parser(task_name="yolo")
    args = parser.parse_args()

    configure_logging()
    logger = get_logger(__name__)

    try:
        run_yolo_training(
            workspace=args.workspace,
            out_dir=args.out,
            model=args.model,
            epochs=args.epochs,
            imgsz=args.imgsz,
            batch=args.batch,
            device=args.device,
            seed=args.seed,
            workers=args.workers,
            conf_val=args.conf_val,
        )
        return 0
    except CoilAIError as e:
        logger.exception("YOLO training failed: %s", e)
        return 1
    except Exception as e:
        logger.exception("Unexpected YOLO training error: %s", e)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
