from __future__ import annotations

import os
import random

from common.exceptions import TrainingError
from common.logging_utils import log_info


def set_seed(seed: int) -> None:
    try:
        import numpy as np
        import torch
    except ImportError as exc:  # pragma: no cover - import guard
        raise TrainingError(
            "numpy and torch are required to set the training seed. Install requirements-train.txt."
        ) from exc

    log_info(f"set seed: {seed}")
    os.environ["PYTHONHASHSEED"] = str(seed)
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed(seed)
        torch.cuda.manual_seed_all(seed)
