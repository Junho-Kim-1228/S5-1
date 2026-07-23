from __future__ import annotations

import unittest

import numpy as np

from anoma.metrics import compute_target_recall_operating_point


class AnomaOperatingPointTests(unittest.TestCase):
    def test_highest_precision_threshold_meeting_target_recall_is_selected(self) -> None:
        labels = np.asarray([1, 0, 1, 0], dtype=np.int64)
        scores = np.asarray([0.9, 0.8, 0.7, 0.6], dtype=np.float64)

        result = compute_target_recall_operating_point(labels, scores, target_recall=0.9)

        self.assertAlmostEqual(result["deployment_threshold"], 0.7)
        self.assertAlmostEqual(result["deployment_precision"], 2 / 3)
        self.assertAlmostEqual(result["deployment_recall"], 1.0)


if __name__ == "__main__":
    unittest.main()
