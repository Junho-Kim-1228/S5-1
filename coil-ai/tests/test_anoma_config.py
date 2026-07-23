from __future__ import annotations

import unittest

from anoma.config import parse_args


class AnomaConfigTests(unittest.TestCase):
    def test_padim_keeps_640_default(self) -> None:
        config = parse_args(["--workspace", "raw", "--out", "out", "--model", "padim"])
        self.assertEqual(config.image_size, 640)

    def test_dinomaly_uses_model_specific_defaults(self) -> None:
        config = parse_args(["--workspace", "raw", "--out", "out", "--model", "dinomaly"])
        self.assertEqual(config.image_size, 448)
        self.assertEqual(config.dinomaly_encoder, "vit_base_patch14_reg4_dinov2")
        self.assertEqual(config.dinomaly_decoder_depth, 8)
        self.assertEqual(config.dinomaly_max_steps, 5000)

    def test_dinomaly_rejects_non_patch_aligned_size(self) -> None:
        with self.assertRaises(SystemExit):
            parse_args(
                [
                    "--workspace",
                    "raw",
                    "--out",
                    "out",
                    "--model",
                    "dinomaly",
                    "--image-size",
                    "640",
                ]
            )

    def test_dinomaly_rejects_decoder_too_shallow_for_default_fusion(self) -> None:
        with self.assertRaises(SystemExit):
            parse_args(
                [
                    "--workspace",
                    "raw",
                    "--out",
                    "out",
                    "--model",
                    "dinomaly",
                    "--dinomaly-decoder-depth",
                    "7",
                ]
            )

    def test_target_recall_is_configurable(self) -> None:
        config = parse_args(
            [
                "--workspace",
                "raw",
                "--out",
                "out",
                "--model",
                "dinomaly",
                "--target-recall",
                "0.9",
            ]
        )
        self.assertEqual(config.target_recall, 0.9)


if __name__ == "__main__":
    unittest.main()
