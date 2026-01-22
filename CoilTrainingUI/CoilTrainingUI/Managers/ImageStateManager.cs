using CoilTrainingUI.Models;
using System.Collections.Generic;

namespace CoilTrainingUI.Managers
{
    public class ImageStateManager
    {
        private readonly Dictionary<string, List<BoundingBox>> _labels = new();
        private readonly Dictionary<string, bool> _anomalyStates = new();

        private readonly Dictionary<string, RoiType> _roiTypes = new();

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

            if (!_roiTypes.ContainsKey(imagePath))
                _roiTypes[imagePath] = RoiType.None; // 기본: ROI 없음
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

        // ---------- Roi ----------
        public void SetRoiType(string imagePath, RoiType type)
        {
            EnsureImage(imagePath);
            _roiTypes[imagePath] = type;
        }

        public RoiType GetRoiType(string imagePath)
        {
            EnsureImage(imagePath);
            return _roiTypes.GetValueOrDefault(imagePath, RoiType.None);
        }

    }
}
