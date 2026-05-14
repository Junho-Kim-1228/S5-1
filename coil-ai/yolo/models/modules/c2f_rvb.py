from __future__ import annotations

from copy import deepcopy

import torch
import torch.nn as nn
from ultralytics.nn.modules import Conv


class RVBBlock(nn.Module):
    """Simple residual depthwise block for experiment-safe C2f replacement."""

    def __init__(self, channels: int, shortcut: bool = True) -> None:
        super().__init__()
        self.cv1 = Conv(channels, channels, 3, 1)
        self.dw = Conv(channels, channels, 3, 1, g=channels)
        self.pw = Conv(channels, channels, 1, 1)
        self.shortcut = shortcut

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        y = self.cv1(x)
        y = self.dw(y)
        y = self.pw(y)
        return x + y if self.shortcut else y


class C2f_RVB(nn.Module):
    """C2f-compatible block that swaps Bottleneck for a simple RVB block."""

    def __init__(
        self,
        c1: int,
        c2: int,
        n: int = 1,
        shortcut: bool = False,
        g: int = 1,
        e: float = 0.5,
    ) -> None:
        super().__init__()
        _ = g  # kept for C2f signature compatibility
        self.c = int(c2 * e)
        self.cv1 = Conv(c1, 2 * self.c, 1, 1)
        self.cv2 = Conv((2 + n) * self.c, c2, 1, 1)
        self.m = nn.ModuleList(RVBBlock(self.c, shortcut=shortcut) for _ in range(n))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        y = list(self.cv1(x).chunk(2, 1))
        for block in self.m:
            y.append(block(y[-1]))
        return self.cv2(torch.cat(y, 1))

    def forward_split(self, x: torch.Tensor) -> torch.Tensor:
        y0, y1 = self.cv1(x).split((self.c, self.c), 1)
        y = [y0, y1]
        for block in self.m:
            y.append(block(y[-1]))
        return self.cv2(torch.cat(y, 1))


def _patch_parse_model() -> None:
    import ultralytics.nn.modules as modules_pkg
    import ultralytics.nn.modules.block as block_pkg
    import ultralytics.nn.tasks as tasks

    if getattr(tasks, "_coil_ai_c2f_rvb_installed", False):
        return

    original_parse_model = tasks.parse_model

    def parse_model_with_c2f_rvb(d, ch, verbose=True):
        model_dict = deepcopy(d)
        uses_c2f_rvb = False

        for layer in model_dict.get("backbone", []) + model_dict.get("head", []):
            if len(layer) >= 3 and layer[2] == "C2f_RVB":
                layer[2] = "C2f"
                uses_c2f_rvb = True

        if not uses_c2f_rvb:
            return original_parse_model(d, ch, verbose)

        original_tasks_c2f = tasks.C2f
        original_modules_c2f = getattr(modules_pkg, "C2f", None)
        original_block_c2f = getattr(block_pkg, "C2f", None)
        try:
            tasks.C2f = C2f_RVB
            modules_pkg.C2f = C2f_RVB
            block_pkg.C2f = C2f_RVB
            return original_parse_model(model_dict, ch, verbose)
        finally:
            tasks.C2f = original_tasks_c2f
            if original_modules_c2f is not None:
                modules_pkg.C2f = original_modules_c2f
            if original_block_c2f is not None:
                block_pkg.C2f = original_block_c2f

    tasks.C2f_RVB = C2f_RVB
    modules_pkg.C2f_RVB = C2f_RVB
    block_pkg.C2f_RVB = C2f_RVB
    tasks.parse_model = parse_model_with_c2f_rvb
    tasks._coil_ai_c2f_rvb_installed = True


def install_ultralytics_c2f_rvb() -> None:
    _patch_parse_model()
