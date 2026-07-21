using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Services
{
    public static class ReviewStatus
    {
        public const string None = "none";
        public const string ReviewNeeded = "review_needed";
        public const string AutoCandidate = "auto_candidate";
        public const string ReviewDone = "review_done";
    }

    public class ImageStateService
    {
        public ImageStateDto Load(string imagePath)
        {
            var path = GetStatePath(imagePath);
            if (!File.Exists(path))
                return new ImageStateDto(); // 기본값

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<ImageStateDto>(json);
                return data ?? new ImageStateDto();
            }
            catch
            {
                // 깨진 파일이어도 UI 죽이면 안 됨
                return new ImageStateDto();
            }
        }

        public void Save(string imagePath, ImageStateDto state)
        {
            var path = GetStatePath(imagePath);

            state.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        public bool HasState(string imagePath) => File.Exists(GetStatePath(imagePath));

        public static string GetStatePath(string imagePath)
            => Path.ChangeExtension(imagePath, ".state.json");
    }

    public class ImageStateDto
    {
        public bool? IsNormal { get; set; } = null;
        public bool HasManualAnomalyDecision { get; set; } = false;
        public bool HasManualYoloDecision { get; set; } = false;
        public string ReviewStatus { get; set; } = global::CoilTrainingUI.Services.ReviewStatus.None;
        public List<string> ReviewReasons { get; set; } = new();
        public DateTime? AutoAppliedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string DecisionSource { get; set; } = "";

        // ✅ 라벨을 클래스 이름으로 저장
        public List<LabelDto> Labels { get; set; } = new();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public bool HasConfirmedAnomalyDecision =>
            IsNormal.HasValue &&
            (HasManualAnomalyDecision ||
             string.Equals(
                 ReviewStatus,
                 global::CoilTrainingUI.Services.ReviewStatus.ReviewDone,
                 StringComparison.OrdinalIgnoreCase));

        [JsonIgnore]
        public bool IsManualAnomalyDecision =>
            HasManualAnomalyDecision &&
            !string.Equals(DecisionSource, "auto", StringComparison.OrdinalIgnoreCase);
    }

    public class LabelDto
    {
        public string ClassName { get; set; } = ""; // "dent" / "loose"
        public double X { get; set; }               // normalized
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Source { get; set; } = "manual"; // manual / auto_infer
        public double? InferConf { get; set; }
    }

}
