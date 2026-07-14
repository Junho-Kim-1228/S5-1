using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services
{
    public sealed class BatchPredictionApplyTarget
    {
        public string ImagePath { get; set; } = "";
        public string InferJsonPath { get; set; } = "";
        public bool RequiresInfer { get; set; }
    }

    public sealed class BatchPredictionApplySummary
    {
        public int TotalTargets { get; set; }
        public int PreLabeled { get; set; }
        public int AutoApprovedNormals { get; set; }
        public int MarkedReviewNeeded { get; set; }
        public int MarkedAutoCandidate { get; set; }
        public int SkippedManual { get; set; }
        public int SkippedMissingInfer { get; set; }
        public int ParseFailed { get; set; }
    }

    public sealed class BatchPredictionReviewService
    {
        private readonly ImageStateService _stateService;

        public BatchPredictionReviewService(ImageStateService stateService)
        {
            _stateService = stateService;
        }

        public BatchPredictionApplySummary PreLabelBatch(
            IReadOnlyList<BatchPredictionApplyTarget> targets,
            bool overwriteExistingLabels)
        {
            var summary = new BatchPredictionApplySummary
            {
                TotalTargets = targets?.Count ?? 0
            };

            if (targets == null || targets.Count == 0)
                return summary;

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.ImagePath))
                    continue;

                var state = _stateService.Load(target.ImagePath);

                if ((state.HasManualYoloDecision && state.Labels.Count > 0) && !overwriteExistingLabels)
                {
                    summary.SkippedManual++;
                    continue;
                }

                if (state.HasManualAnomalyDecision)
                {
                    summary.SkippedManual++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.InferJsonPath) || !File.Exists(target.InferJsonPath))
                {
                    summary.SkippedMissingInfer++;
                    if (target.RequiresInfer)
                    {
                        SetReviewStatus(
                            state,
                            ReviewStatus.ReviewNeeded,
                            new[] { "infer_missing" });
                        _stateService.Save(target.ImagePath, state);
                        summary.MarkedReviewNeeded++;
                    }

                    continue;
                }

                InferResultDto infer;
                try
                {
                    infer = InferenceBatchSchemaParser.ParseInferResult(target.InferJsonPath);
                }
                catch
                {
                    summary.ParseFailed++;
                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        new[] { "infer_parse_failed" });
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                var labels = ConvertDetectionsToLabels(infer.Yolo?.Detections);
                var evaluation = PredictionConsensusPolicy.Evaluate(infer);

                if (evaluation.YoloDefect)
                {
                    state.Labels = labels;
                    state.HasManualYoloDecision = false;
                    state.AutoAppliedAt = DateTime.UtcNow;
                    summary.PreLabeled++;
                }

                if (evaluation.RequiresReview)
                {
                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        evaluation.Reasons);
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                SetReviewStatus(
                    state,
                    ReviewStatus.AutoCandidate,
                    new[] { "model_agree_high_conf" });
                _stateService.Save(target.ImagePath, state);
                summary.MarkedAutoCandidate++;
            }

            return summary;
        }

        public BatchPredictionApplySummary AutoApproveSafeNormals(
            IReadOnlyList<BatchPredictionApplyTarget> targets)
        {
            var summary = new BatchPredictionApplySummary
            {
                TotalTargets = targets?.Count ?? 0
            };

            if (targets == null || targets.Count == 0)
                return summary;

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.ImagePath))
                    continue;

                var state = _stateService.Load(target.ImagePath);
                if (state.HasManualYoloDecision || state.HasManualAnomalyDecision)
                {
                    summary.SkippedManual++;
                    continue;
                }

                if (state.Labels.Count > 0)
                {
                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        new[] { "yolo_detection_exists" });
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.InferJsonPath) || !File.Exists(target.InferJsonPath))
                {
                    summary.SkippedMissingInfer++;
                    if (target.RequiresInfer)
                    {
                        SetReviewStatus(
                            state,
                            ReviewStatus.ReviewNeeded,
                            new[] { "infer_missing" });
                        _stateService.Save(target.ImagePath, state);
                        summary.MarkedReviewNeeded++;
                    }

                    continue;
                }

                InferResultDto infer;
                try
                {
                    infer = InferenceBatchSchemaParser.ParseInferResult(target.InferJsonPath);
                }
                catch
                {
                    summary.ParseFailed++;
                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        new[] { "infer_parse_failed" });
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                var labels = ConvertDetectionsToLabels(infer.Yolo?.Detections);
                var evaluation = PredictionConsensusPolicy.Evaluate(infer);

                if (evaluation.YoloDefect)
                {
                    if (state.Labels.Count == 0 && labels.Count > 0)
                        state.Labels = labels;

                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        evaluation.Reasons.Count > 0
                            ? evaluation.Reasons
                            : new[] { "defect_predicted" });
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                if (evaluation.RequiresReview)
                {
                    SetReviewStatus(
                        state,
                        ReviewStatus.ReviewNeeded,
                        evaluation.Reasons);
                    _stateService.Save(target.ImagePath, state);
                    summary.MarkedReviewNeeded++;
                    continue;
                }

                state.IsNormal = true;
                state.HasManualAnomalyDecision = true;
                state.AutoAppliedAt = DateTime.UtcNow;
                state.ReviewedAt = state.AutoAppliedAt;
                state.DecisionSource = "auto";
                SetReviewStatus(
                    state,
                    ReviewStatus.ReviewDone,
                    Array.Empty<string>());
                _stateService.Save(target.ImagePath, state);
                summary.AutoApprovedNormals++;
            }

            return summary;
        }

        private static void SetReviewStatus(ImageStateDto state, string reviewStatus, IEnumerable<string> reasons)
        {
            state.ReviewStatus = string.IsNullOrWhiteSpace(reviewStatus) ? ReviewStatus.None : reviewStatus;
            state.ReviewReasons = reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (string.Equals(state.ReviewStatus, ReviewStatus.ReviewDone, StringComparison.OrdinalIgnoreCase))
                state.ReviewedAt ??= DateTime.UtcNow;
        }

        private static List<LabelDto> ConvertDetectionsToLabels(IReadOnlyList<DetectionDto>? detections)
        {
            var labels = new List<LabelDto>();
            if (detections == null)
                return labels;

            foreach (var detection in detections)
            {
                if (!PredictionConsensusPolicy.IsUsableDetectionForDecision(detection))
                    continue;

                double cx = detection.BboxXywhNorm[0];
                double cy = detection.BboxXywhNorm[1];
                double bw = detection.BboxXywhNorm[2];
                double bh = detection.BboxXywhNorm[3];

                double left = Math.Clamp(cx - (bw / 2.0), 0.0, 1.0);
                double right = Math.Clamp(cx + (bw / 2.0), 0.0, 1.0);
                double top = Math.Clamp(cy - (bh / 2.0), 0.0, 1.0);
                double bottom = Math.Clamp(cy + (bh / 2.0), 0.0, 1.0);

                double width = right - left;
                double height = bottom - top;
                if (width <= 0 || height <= 0)
                    continue;

                labels.Add(new LabelDto
                {
                    ClassName = NormalizeClassName(detection.ClassName),
                    X = (left + right) / 2.0,
                    Y = (top + bottom) / 2.0,
                    Width = width,
                    Height = height,
                    Source = "auto_infer",
                    InferConf = detection.Conf
                });
            }

            return labels;
        }

        private static string NormalizeClassName(string? className)
        {
            var normalized = (className ?? "").Trim().ToLowerInvariant();
            return normalized switch
            {
                "dent" => "dent",
                "loose" => "loose",
                _ => "dent"
            };
        }
    }
}
