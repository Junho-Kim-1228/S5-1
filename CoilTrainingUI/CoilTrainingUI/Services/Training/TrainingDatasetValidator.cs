using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Services
{
    public sealed class DatasetValidationResult
    {
        public int TotalImages { get; set; }
        public int NormalizedIsNormalCount { get; set; }
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;

        public string ToErrorMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("학습 데이터 검증 실패");
            sb.AppendLine($"총 이미지 수: {TotalImages}");
            if (NormalizedIsNormalCount > 0)
                sb.AppendLine($"IsNormal null -> true 보정: {NormalizedIsNormalCount}");
            sb.AppendLine($"오류 개수: {Errors.Count}");
            sb.AppendLine();

            const int maxShow = 80;
            foreach (var err in Errors.Take(maxShow))
                sb.AppendLine($"- {err}");

            if (Errors.Count > maxShow)
                sb.AppendLine($"... 외 {Errors.Count - maxShow}건");

            return sb.ToString().TrimEnd();
        }
    }

    public sealed class TrainingDatasetValidator
    {
        private readonly ImageStateService _stateService;

        public TrainingDatasetValidator(ImageStateService stateService)
        {
            _stateService = stateService;
        }

        public DatasetValidationResult Validate(
            IReadOnlyList<string> imagePaths,
            bool requiresInfer,
            IReadOnlyDictionary<string, string> inferJsonByImagePath)
        {
            var inputs = (imagePaths ?? Array.Empty<string>())
                .Select(imagePath => new TrainingImageInput
                {
                    ImagePath = imagePath,
                    InferJsonPath = inferJsonByImagePath != null &&
                                    inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath)
                        ? inferJsonPath
                        : "",
                    RequiresInfer = requiresInfer
                })
                .ToList();

            return Validate(inputs, requireAnomaNormals: true);
        }

        public DatasetValidationResult Validate(
            IReadOnlyList<TrainingImageInput> inputs,
            bool requireAnomaNormals = true)
        {
            var result = new DatasetValidationResult
            {
                TotalImages = inputs?.Count ?? 0
            };

            if (inputs == null || inputs.Count == 0)
            {
                result.Errors.Add("검증할 이미지 경로가 없습니다.");
                return result;
            }

            var normalCandidates = new List<string>();

            foreach (var input in inputs)
            {
                string imagePath = input?.ImagePath ?? "";
                bool requiresInfer = input?.RequiresInfer == true;
                string inferJsonPath = input?.InferJsonPath ?? "";
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    result.Errors.Add("빈 이미지 경로가 포함되어 있습니다.");
                    continue;
                }

                if (!File.Exists(imagePath))
                {
                    result.Errors.Add($"processed image 없음: {imagePath}");
                    continue;
                }

                if (requiresInfer)
                {
                    if (string.IsNullOrWhiteSpace(inferJsonPath))
                    {
                        result.Errors.Add($"infer.json 경로 매핑이 없습니다: {imagePath}");
                    }
                    else if (!File.Exists(inferJsonPath))
                    {
                        result.Errors.Add($"infer.json 없음: {inferJsonPath}");
                    }
                }

                var state = _stateService.Load(imagePath);
                if (!state.HasConfirmedAnomalyDecision)
                {
                    result.Errors.Add($"최종 정상/불량 판정이 확정되지 않았습니다: {Path.GetFileName(imagePath)}");
                }

                if (string.Equals(state.ReviewStatus, ReviewStatus.ReviewNeeded, StringComparison.OrdinalIgnoreCase))
                {
                    string reasons = (state.ReviewReasons != null && state.ReviewReasons.Count > 0)
                        ? string.Join(", ", state.ReviewReasons.Take(3))
                        : "reason_unspecified";
                    result.Errors.Add(
                        $"검수 필요 상태가 남아 있습니다: {Path.GetFileName(imagePath)} ({reasons})");
                }

                var labels = state.Labels ?? new List<LabelDto>();
                for (int i = 0; i < labels.Count; i++)
                {
                    if (!TryValidateGtLabel(labels[i], out var reason))
                    {
                        result.Errors.Add(
                            $"GT 좌표 오류: {Path.GetFileName(imagePath)} label[{i}] {reason}");
                    }
                }

                if (state.HasConfirmedAnomalyDecision && state.IsNormal == true)
                    normalCandidates.Add(imagePath);
            }

            var abnormalMixedInNormalSet = normalCandidates
                .Where(path => _stateService.Load(path).IsNormal == false)
                .ToList();

            if (abnormalMixedInNormalSet.Count > 0)
            {
                foreach (var mixed in abnormalMixedInNormalSet)
                    result.Errors.Add($"정상 학습셋에 IsNormal=false가 섞여 있습니다: {mixed}");
            }

            if (requireAnomaNormals && normalCandidates.Count < 2)
                result.Errors.Add("anoma 학습용 정상 이미지가 2장 미만입니다. (최소 2장 필요)");

            return result;
        }

        private static bool TryValidateGtLabel(LabelDto label, out string reason)
        {
            if (label == null)
            {
                reason = "label 객체가 null입니다.";
                return false;
            }

            if (!IsFinite01(label.X))
            {
                reason = $"X={label.X} (0~1 범위 아님)";
                return false;
            }

            if (!IsFinite01(label.Y))
            {
                reason = $"Y={label.Y} (0~1 범위 아님)";
                return false;
            }

            if (!IsFinite01(label.Width) || label.Width <= 0)
            {
                reason = $"Width={label.Width} (0~1 범위의 양수 아님)";
                return false;
            }

            if (!IsFinite01(label.Height) || label.Height <= 0)
            {
                reason = $"Height={label.Height} (0~1 범위의 양수 아님)";
                return false;
            }

            reason = "";
            return true;
        }

        private static bool IsFinite01(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0 && value <= 1.0;
    }
}
