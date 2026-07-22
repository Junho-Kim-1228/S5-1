from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scripts.prepare_yolo_workspace import CLASS_MAP, build_sample_records


class PrepareYoloWorkspaceReviewRulesTests(unittest.TestCase):
    def test_defect_without_boxes_is_not_background(self) -> None:
        with tempfile.TemporaryDirectory(prefix="coil-yolo-review-") as temp_dir:
            root = Path(temp_dir)
            self._write_sample(root, "normal", is_normal=True, labels=[])
            self._write_sample(
                root,
                "defect_with_box",
                is_normal=False,
                labels=[
                    {
                        "ClassName": "dent",
                        "X": 0.5,
                        "Y": 0.5,
                        "Width": 0.2,
                        "Height": 0.2,
                    }
                ],
            )
            self._write_sample(root, "defect_without_box", is_normal=False, labels=[])
            self._write_sample(
                root,
                "unreviewed_normal",
                is_normal=True,
                labels=[],
                review_status="review_needed",
            )

            records, stats = build_sample_records(root, CLASS_MAP)

            by_stem = {record["image_path"].stem: record for record in records}
            self.assertEqual({"normal", "defect_with_box"}, set(by_stem))
            self.assertFalse(by_stem["normal"]["is_defect"])
            self.assertTrue(by_stem["defect_with_box"]["is_defect"])
            self.assertEqual(1, stats["excluded_defect_without_boxes"])
            self.assertEqual(1, stats["excluded_review_status"])

    @staticmethod
    def _write_sample(
        root: Path,
        stem: str,
        *,
        is_normal: bool,
        labels: list[dict],
        review_status: str = "review_done",
    ) -> None:
        (root / f"{stem}.bmp").write_bytes(b"BM")
        (root / f"{stem}.state.json").write_text(
            json.dumps(
                {
                    "IsNormal": is_normal,
                    "ReviewStatus": review_status,
                    "Labels": labels,
                }
            ),
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
