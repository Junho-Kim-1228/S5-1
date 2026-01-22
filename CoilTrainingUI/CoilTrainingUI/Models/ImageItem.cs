using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoilTrainingUI.Models
{
    public class ImageItem
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public bool HasLabel { get; set; }           // YOLO 박스 존재 여부
        public bool IsNormal { get; set; } = true;  // Anomaly 기준 정상 여부

        // UI 표시용
        public string YoloStatusText => HasLabel ? "불량" : "정상";
        public string AnomalyStatusText => IsNormal ? "정상" : "불량";

        public RoiType RoiType { get; set; } = RoiType.None;
    }
}
