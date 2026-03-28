import logging
import sys


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(name)s | %(message)s",
        handlers=[logging.StreamHandler(sys.stdout)],
        force=True,
    )


def get_logger(name: str) -> logging.Logger:
    return logging.getLogger(name)


def log(message: str, *, stream=sys.stdout) -> None:
    print(message, file=stream, flush=True)


def log_error(message: str) -> None:
    log(f"[ERROR] {message}", stream=sys.stderr)


def _is_logger(value) -> bool:
    return isinstance(value, logging.Logger)


def log_step(logger_or_message, message: str | None = None) -> None:
    if _is_logger(logger_or_message):
        logger_or_message.info("[STEP] %s", message)
    else:
        log(f"[STEP] {logger_or_message}")


def log_info(logger_or_message, message: str | None = None, *args) -> None:
    if _is_logger(logger_or_message):
        logger_or_message.info("[INFO] " + str(message), *args)
    else:
        log(f"[INFO] {logger_or_message}")


def log_warn(logger_or_message, message: str | None = None, *args) -> None:
    if _is_logger(logger_or_message):
        logger_or_message.warning("[WARN] " + str(message), *args)
    else:
        log(f"[WARN] {logger_or_message}", stream=sys.stderr)


def log_progress(logger_or_value, value: int | None = None) -> None:
    if _is_logger(logger_or_value):
        logger_or_value.info("PROGRESS: %s", value)
    else:
        log(f"PROGRESS: {logger_or_value}")
