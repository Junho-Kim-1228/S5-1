using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoilTrainingUI.Services;

public sealed class TrainingEligibility
{
    public bool AnomaTraining { get; init; }
    public bool AnomaEvaluation { get; init; }
    public bool YoloBackground { get; init; }
    public bool YoloPositive { get; init; }
    public bool YoloExcludedDefectWithoutBoxes { get; init; }
    public bool YoloLowConfidencePredictionReviewRequired { get; init; }
    public string ExclusionReason { get; init; } = "";

    public bool AnyTrainingUse => AnomaTraining || AnomaEvaluation || YoloBackground || YoloPositive;

    public string SummaryText
    {
        get
        {
            var uses = new List<string>();
            if (AnomaTraining) uses.Add("Anoma 정상 학습");
            if (AnomaEvaluation) uses.Add("Anoma 평가");
            if (YoloBackground) uses.Add("YOLO 배경 후보");
            if (YoloPositive) uses.Add("YOLO 양성");
            return uses.Count > 0 ? string.Join(", ", uses) : "학습 제외";
        }
    }
}

public sealed class TrainingDatasetSelection
{
    public List<TrainingImageInput> AnomaInputs { get; } = new();
    public List<TrainingImageInput> YoloInputs { get; } = new();
    public int TotalCandidates { get; set; }
    public double YoloBackgroundToPositiveRatio { get; set; }
    public int YoloPositiveInputCount { get; set; }
    public int YoloBackgroundCandidateCount { get; set; }
    public int YoloBackgroundSelectedCount { get; set; }
    public int ExcludedNormalBackgroundByBalance { get; set; }
    public int ExcludedDefectWithoutBoxes { get; set; }
    public int ExcludedLowConfidencePredictedBoxes { get; set; }
    public int ExcludedLegacyMigrationRequired { get; set; }
    public int ExcludedUnreviewedOrReviewing { get; set; }
    public int ExcludedByUser { get; set; }
}

public sealed class TrainingDatasetSelector
{
    public const double DefaultYoloBackgroundToPositiveRatio = 1.0;

    private readonly ReviewRepository _repository;
    private readonly PredictionReader _predictionReader;

    public TrainingDatasetSelector(ReviewRepository repository, PredictionReader? predictionReader = null)
    {
        _repository = repository;
        _predictionReader = predictionReader ?? new PredictionReader();
    }

    public TrainingEligibility Evaluate(
        ReviewStateLoadResult load,
        PredictionSnapshot? prediction = null)
    {
        if (load.ParseFailed)
            return Excluded("검수 상태 파일 해석 실패");
        if (load.IsLegacyProjection)
            return Excluded("기존 state.json 마이그레이션 필요");
        if (!load.HasReviewFile)
            return Excluded("미검수");

        ReviewState state = load.State;
        if (!state.IncludeInTraining)
            return Excluded("학습 사용 OFF");

        return state.Decision switch
        {
            ImageReviewDecision.Unreviewed => Excluded("미검수"),
            ImageReviewDecision.Reviewing => Excluded("검수 중"),
            ImageReviewDecision.Excluded => Excluded(
                string.IsNullOrWhiteSpace(state.ExclusionReason) ? "사용자 학습 제외" : state.ExclusionReason),
            ImageReviewDecision.ConfirmedNormal => new TrainingEligibility
            {
                AnomaTraining = true,
                YoloBackground = true
            },
            ImageReviewDecision.ConfirmedDefect => EvaluateConfirmedDefect(state, prediction),
            _ => Excluded("지원하지 않는 검수 상태")
        };
    }

    public TrainingDatasetSelection Select(
        IReadOnlyList<TrainingImageInput> candidates,
        double yoloBackgroundToPositiveRatio = DefaultYoloBackgroundToPositiveRatio)
    {
        if (double.IsNaN(yoloBackgroundToPositiveRatio) ||
            double.IsInfinity(yoloBackgroundToPositiveRatio) ||
            yoloBackgroundToPositiveRatio < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yoloBackgroundToPositiveRatio),
                "YOLO background-to-positive ratio must be a finite value greater than or equal to zero.");
        }

        var selection = new TrainingDatasetSelection
        {
            TotalCandidates = candidates?.Count ?? 0,
            YoloBackgroundToPositiveRatio = yoloBackgroundToPositiveRatio
        };
        var yoloPositiveInputs = new List<TrainingImageInput>();
        var yoloBackgroundCandidates = new List<TrainingImageInput>();

        foreach (var input in candidates ?? Array.Empty<TrainingImageInput>())
        {
            ReviewStateLoadResult load = _repository.Load(input.ImagePath);
            PredictionSnapshot? prediction = string.IsNullOrWhiteSpace(input.InferJsonPath)
                ? null
                : _predictionReader.Read(input.InferJsonPath, input.ExpectedInferenceContextId);
            TrainingEligibility eligibility = Evaluate(load, prediction);

            if (eligibility.AnomaTraining || eligibility.AnomaEvaluation)
                selection.AnomaInputs.Add(input);
            if (eligibility.YoloPositive)
                yoloPositiveInputs.Add(input);
            if (eligibility.YoloBackground)
                yoloBackgroundCandidates.Add(input);
            if (eligibility.YoloExcludedDefectWithoutBoxes)
                selection.ExcludedDefectWithoutBoxes++;
            if (eligibility.YoloLowConfidencePredictionReviewRequired)
                selection.ExcludedLowConfidencePredictedBoxes++;
            if (load.IsLegacyProjection)
                selection.ExcludedLegacyMigrationRequired++;
            if (load.State.Decision is ImageReviewDecision.Unreviewed or ImageReviewDecision.Reviewing)
                selection.ExcludedUnreviewedOrReviewing++;
            if (!load.State.IncludeInTraining ||
                load.State.Decision == ImageReviewDecision.Excluded)
                selection.ExcludedByUser++;
        }

        Deduplicate(selection.AnomaInputs);
        Deduplicate(yoloPositiveInputs);
        Deduplicate(yoloBackgroundCandidates);

        int requestedBackgroundCount = CalculateBackgroundLimit(
            yoloPositiveInputs.Count,
            yoloBackgroundCandidates.Count,
            yoloBackgroundToPositiveRatio);
        List<TrainingImageInput> selectedBackgrounds = yoloBackgroundCandidates
            .OrderBy(BuildStableBackgroundSelectionKey, StringComparer.Ordinal)
            .ThenBy(input => input.BatchKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
            .Take(requestedBackgroundCount)
            .ToList();

        selection.YoloPositiveInputCount = yoloPositiveInputs.Count;
        selection.YoloBackgroundCandidateCount = yoloBackgroundCandidates.Count;
        selection.YoloBackgroundSelectedCount = selectedBackgrounds.Count;
        selection.ExcludedNormalBackgroundByBalance =
            yoloBackgroundCandidates.Count - selectedBackgrounds.Count;
        selection.YoloInputs.AddRange(yoloPositiveInputs);
        selection.YoloInputs.AddRange(selectedBackgrounds);
        Deduplicate(selection.YoloInputs);
        return selection;
    }

    private static int CalculateBackgroundLimit(
        int positiveCount,
        int candidateCount,
        double ratio)
    {
        if (positiveCount <= 0 || candidateCount <= 0 || ratio <= 0)
            return 0;

        double requested = Math.Ceiling(positiveCount * ratio);
        return requested >= candidateCount
            ? candidateCount
            : (int)requested;
    }

    private static string BuildStableBackgroundSelectionKey(TrainingImageInput input)
    {
        string material = $"{input.BatchKey.Trim()}|{Path.GetFileName(input.ImagePath).ToLowerInvariant()}";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest);
    }

    private static TrainingEligibility Excluded(string reason)
        => new() { ExclusionReason = reason };

    private static TrainingEligibility EvaluateConfirmedDefect(
        ReviewState state,
        PredictionSnapshot? prediction)
    {
        bool yoloPositive = state.BoxReview == BoxReviewDecision.Confirmed && state.Boxes.Count > 0;
        if (yoloPositive)
        {
            return new TrainingEligibility
            {
                AnomaEvaluation = true,
                YoloPositive = true
            };
        }

        bool predictionOnly = ReviewBoxLayerPolicy.IsPredictionOnly(state);
        int predictedBoxCount = prediction?.HasFile == true
            ? prediction.YoloDetectionCount
            : predictionOnly
                ? state.Boxes.Count
                : 0;
        bool lowConfidencePrediction = predictionOnly && predictedBoxCount > 0;
        bool noBoxes = state.Boxes.Count == 0 && !lowConfidencePrediction;

        return new TrainingEligibility
        {
            AnomaEvaluation = true,
            YoloExcludedDefectWithoutBoxes = noBoxes,
            YoloLowConfidencePredictionReviewRequired = lowConfidencePrediction,
            ExclusionReason = lowConfidencePrediction
                ? BuildLowConfidenceReason(prediction, state, predictedBoxCount)
                : noBoxes
                    ? BuildNoBoxReason(state, prediction)
                    : "박스 검수 미완료: YOLO 제외"
        };
    }

    private static string BuildLowConfidenceReason(
        PredictionSnapshot? prediction,
        ReviewState state,
        int predictedBoxCount)
    {
        double? threshold = state.AutoReview?.YoloBoxMinConfidence;
        var confidences = (prediction?.YoloBoxes ?? state.Boxes)
            .Select(box => box.PredictionConfidence)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (confidences.Count < predictedBoxCount)
            return $"AI 박스 {predictedBoxCount}개 중 신뢰도 미기록 박스 있음: 검수 필요, YOLO 제외";

        double minimum = confidences.Min();
        if (threshold.HasValue && IsFinite01(threshold.Value) && minimum < threshold.Value)
        {
            return $"AI 박스 저신뢰 (최저 {FormatConfidence(minimum)} < 자동확정 {FormatConfidence(threshold.Value)}): 검수 필요, YOLO 제외";
        }

        return $"AI 예측 박스 {predictedBoxCount}개 미확정: 검수 필요, YOLO 제외";
    }

    private static string BuildNoBoxReason(ReviewState state, PredictionSnapshot? prediction)
    {
        if (state.BoxReview == BoxReviewDecision.Confirmed)
            return "박스 0개로 검수 완료: YOLO 제외";
        if (prediction?.HasFile == true && prediction.YoloDetectionCount == 0)
            return "AI YOLO 미검출 및 확정 박스 없음: YOLO 제외";
        return "불량 확정이지만 확정 박스 없음: YOLO 제외";
    }

    private static string FormatConfidence(double value)
        => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static bool IsFinite01(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 1;

    private static void Deduplicate(List<TrainingImageInput> inputs)
    {
        var unique = inputs
            .GroupBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        inputs.Clear();
        inputs.AddRange(unique);
    }
}
