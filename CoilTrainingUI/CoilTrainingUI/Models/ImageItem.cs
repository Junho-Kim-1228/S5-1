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
        public string ProcessedPath { get; set; } = "";
        public string? RawPath { get; set; }

        // Legacy compatibility: 기존 코드가 FullPath를 참조해도 processed 경로를 반환.
        public string FullPath
        {
            get => ProcessedPath;
            set => ProcessedPath = value;
        }
        public bool HasLabel { get; set; }           // YOLO 박스 존재 여부
        public bool IsNormal { get; set; } = true;  // Anomaly 기준 정상 여부
        public bool HasAiInfer { get; set; }        // infer.json 존재/파싱 성공 여부
        public bool AiIsDefect { get; set; }        // AI 기준 불량 여부
        public bool AiYoloDefect { get; set; }      // AI YOLO 기준 불량 여부
        public bool AiAnomaDefect { get; set; }     // AI Anoma 기준 불량 여부

        // UI 표시용
        public string AiYoloStatusText => !HasAiInfer ? "미분류" : (AiYoloDefect ? "불량" : "정상");
        public string AiAnomaStatusText => !HasAiInfer ? "미분류" : (AiAnomaDefect ? "불량" : "정상");
        public string GtYoloStatusText => HasLabel ? "불량" : "정상";
        public string GtAnomaStatusText => IsNormal ? "정상" : "불량";

        public string StatusText
        {
            get
            {
                if (HasLabel || !IsNormal)
                    return "불량";

                if (HasAiInfer)
                    return AiIsDefect ? "AI불량" : "AI정상";

                return "미분류";
            }
        }

        public RoiType RoiType { get; set; } = RoiType.None;
    }
}
