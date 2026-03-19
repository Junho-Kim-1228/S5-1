from __future__ import annotations

import sys
from typing import TextIO


def log(message: str, *, stream: TextIO = sys.stdout) -> None:
    print(message, file=stream, flush=True)


def log_step(message: str) -> None:
    log(f"[STEP] {message}")


def log_info(message: str) -> None:
    log(f"[INFO] {message}")


def log_warn(message: str) -> None:
    log(f"[WARN] {message}", stream=sys.stderr)


def log_error(message: str) -> None:
    log(f"[ERROR] {message}", stream=sys.stderr)


def log_progress(value: int) -> None:
    log(f"PROGRESS: {value}")
