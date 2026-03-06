using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CoilTrainingUI.Models;
using IOPath = System.IO.Path;

namespace CoilTrainingUI.Services
{
    public class YoloLabelService
    {
        private readonly Dictionary<string, int> _classToId;

        public YoloLabelService(Dictionary<string, int> classToId)
        {
            _classToId = classToId ?? throw new ArgumentNullException(nameof(classToId));
        }

        // =========================
        // Load: txt → 메모리
        // =========================
        public void Load(string imagePath, List<BoundingBox> targetList)
        {
            if (targetList == null)
                throw new ArgumentNullException(nameof(targetList));

            string labelPath = IOPath.ChangeExtension(imagePath, ".txt");

            targetList.Clear();

            if (!File.Exists(labelPath))
                return;

            foreach (var line in File.ReadAllLines(labelPath))
            {
                var p = line.Split(' ');
                if (p.Length != 5)
                    continue;

                if (!int.TryParse(p[0], out int classId))
                    continue;

                string? className = _classToId
                    .FirstOrDefault(kv => kv.Value == classId)
                    .Key;

                if (string.IsNullOrEmpty(className))
                    continue; // 정의되지 않은 클래스 ID 방어

                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                    !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ||
                    !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                    continue;

                var bbox = new BoundingBox
                {
                    ClassName = className,
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h
                };

                targetList.Add(bbox);
            }
        }

        // =========================
        // Save: 메모리 → txt
        // =========================
        public void Save(string imagePath, IEnumerable<BoundingBox> boxes)
        {
            if (boxes == null)
                throw new ArgumentNullException(nameof(boxes));

            string labelPath = IOPath.ChangeExtension(imagePath, ".txt");

            using var writer = new StreamWriter(labelPath);

            foreach (var bbox in boxes)
            {
                if (!_classToId.TryGetValue(bbox.ClassName, out int classId))
                    continue; // 정의되지 않은 클래스 방어

                writer.WriteLine(
                    $"{classId} " +
                    $"{bbox.X.ToString("F6", CultureInfo.InvariantCulture)} " +
                    $"{bbox.Y.ToString("F6", CultureInfo.InvariantCulture)} " +
                    $"{bbox.Width.ToString("F6", CultureInfo.InvariantCulture)} " +
                    $"{bbox.Height.ToString("F6", CultureInfo.InvariantCulture)}"
                );
            }
        }
    }
}
