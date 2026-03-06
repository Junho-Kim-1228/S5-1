using CoilTrainingUI.Models;
using System.Collections.Generic;

namespace CoilTrainingUI.Managers
{
    public class ImageStateManager
    {
        private readonly Dictionary<string, List<BoundingBox>> _labels = new();
        private readonly Dictionary<string, bool> _anomalyStates = new();

        public List<BoundingBox> GetMutableLabels(string imagePath)
        {
            EnsureImage(imagePath);
            return _labels[imagePath];
        }

        public void EnsureImage(string imagePath)
        {
            if (!_labels.ContainsKey(imagePath))
                _labels[imagePath] = new List<BoundingBox>();

            if (!_anomalyStates.ContainsKey(imagePath))
                _anomalyStates[imagePath] = true; // 기본: 정상
        }


        // ---------- YOLO ----------
        public IReadOnlyList<BoundingBox> GetLabels(string imagePath)
        {
            EnsureImage(imagePath);
            return _labels[imagePath];
        }

        public void AddLabel(string imagePath, BoundingBox bbox)
        {
            EnsureImage(imagePath);
            _labels[imagePath].Add(bbox);
        }

        public void RemoveLabel(string imagePath, BoundingBox bbox)
        {
            if (_labels.ContainsKey(imagePath))
                _labels[imagePath].Remove(bbox);
        }

        public void ClearLabels(string imagePath)
        {
            if (_labels.ContainsKey(imagePath))
                _labels[imagePath].Clear();
        }

        public bool HasLabel(string imagePath)
        {
            return _labels.ContainsKey(imagePath)
                   && _labels[imagePath].Count > 0;
        }

        // ---------- Anomaly ----------
        public bool IsNormal(string imagePath)
        {
            EnsureImage(imagePath);
            return _anomalyStates[imagePath];
        }

        public void SetNormal(string imagePath, bool isNormal)
        {
            EnsureImage(imagePath);
            _anomalyStates[imagePath] = isNormal;
        }
    }
}
