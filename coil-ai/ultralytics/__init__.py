from __future__ import annotations

import importlib.machinery
import importlib.util
import sys
from pathlib import Path


_PROJECT_ROOT = Path(__file__).resolve().parents[1]
_THIS_PACKAGE_DIR = Path(__file__).resolve().parent

_search_paths = []
for raw_path in sys.path:
    resolved = Path(raw_path or ".").resolve()
    if resolved in {_PROJECT_ROOT, _THIS_PACKAGE_DIR}:
        continue
    _search_paths.append(raw_path)

_spec = importlib.machinery.PathFinder.find_spec("ultralytics", _search_paths)
if _spec is None or _spec.loader is None:
    raise ImportError("Unable to locate the installed ultralytics package for proxy import.")

_real_module = importlib.util.module_from_spec(_spec)
sys.modules[__name__] = _real_module
_spec.loader.exec_module(_real_module)

from yolo.models.modules.c2f_rvb import install_ultralytics_c2f_rvb

install_ultralytics_c2f_rvb()
globals().update(_real_module.__dict__)
