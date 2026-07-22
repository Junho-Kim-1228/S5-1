using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilTrainingUI.Services;

public sealed class TrainingEligibility
{
    public bool AnomaTraining { get; init; }
    public bool AnomaEvaluation { get; init; }
    public bool YoloBackground { get; init; }
    public bool YoloPositive { get; init; }
    public bool YoloExcludedDefectWithoutBoxes { get; init; }
    public string ExclusionReason { get; init; } = "";

    public bool AnyTrainingUse => AnomaTraining || AnomaEvaluation || YoloBackground || YoloPositive;

    public string SummaryText
    {
        get
        {
            var uses = new List<string>();
            if (AnomaTraining) uses.Add("Anoma 정상 학습");
            if (AnomaEvaluation) uses.Add("Anoma 평가");
            if (YoloBackground) uses.Add("YOLO 배경");
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
    public int ExcludedDefectWithoutBoxes { get; set; }
    public int ExcludedLegacyMigrationRequired { get; set; }
    public int ExcludedUnreviewedOrReviewing { get; set; }
    public int ExcludedByUser { get; set; }
}

public sealed class TrainingDatasetSelector
{
    private readonly ReviewRepository _repository;

    public TrainingDatasetSelector(ReviewRepository repository)
    {
        _repository = repository;
    }

    public TrainingEligibility Evaluate(ReviewStateLoadResult load)
    {
        if (load.ParseFailed)
            return Excluded("검수 상태 파일 해석 실패");
        if (load.IsLegacyProjection)
            return Excluded("기존 state.json 마이그레이션 필요");
        if (!load.HasReviewFile)
            return Excluded("미검수");

        ReviewState state = load.State;
        return state.Decision switch
        {
            ImageReviewDecision.Unreviewed => Excluded("미검수"),
            ImageReviewDecision.Reviewing => Excluded("검수 중"),
            ImageReviewDecision.Excluded => Excluded(
                string.IsNullOrWhiteSpace(state.ExclusionReason) ? "사용자 학습 제외" : state.ExclusionReason),
            ImageReviewDecision.ConfirmedNormal => new TrainingEligibility
            {
                AnomaTraining = true,
                YoloBackground = state.UseAsYoloBackground,
                ExclusionReason = state.UseAsYoloBackground ? "" : "YOLO 정상 배경 미선택"
            },
            ImageReviewDecision.ConfirmedDefect => new TrainingEligibility
            {
                AnomaEvaluation = true,
                YoloPositive = state.BoxReview == BoxReviewDecision.Confirmed && state.Boxes.Count > 0,
                YoloExcludedDefectWithoutBoxes = state.Boxes.Count == 0,
                ExclusionReason = state.Boxes.Count == 0
                    ? "불량 확정이지만 박스 없음: YOLO 제외"
                    : state.BoxReview != BoxReviewDecision.Confirmed
                        ? "박스 검수 미완료: YOLO 제외"
                        : ""
            },
            _ => Excluded("지원하지 않는 검수 상태")
        };
    }

    public TrainingDatasetSelection Select(IReadOnlyList<TrainingImageInput> candidates)
    {
        var selection = new TrainingDatasetSelection
        {
            TotalCandidates = candidates?.Count ?? 0
        };

        foreach (var input in candidates ?? Array.Empty<TrainingImageInput>())
        {
            ReviewStateLoadResult load = _repository.Load(input.ImagePath);
            TrainingEligibility eligibility = Evaluate(load);

            if (eligibility.AnomaTraining || eligibility.AnomaEvaluation)
                selection.AnomaInputs.Add(input);
            if (eligibility.YoloBackground || eligibility.YoloPositive)
                selection.YoloInputs.Add(input);
            if (eligibility.YoloExcludedDefectWithoutBoxes)
                selection.ExcludedDefectWithoutBoxes++;
            if (load.IsLegacyProjection)
                selection.ExcludedLegacyMigrationRequired++;
            if (load.State.Decision is ImageReviewDecision.Unreviewed or ImageReviewDecision.Reviewing)
                selection.ExcludedUnreviewedOrReviewing++;
            if (load.State.Decision == ImageReviewDecision.Excluded)
                selection.ExcludedByUser++;
        }

        Deduplicate(selection.AnomaInputs);
        Deduplicate(selection.YoloInputs);
        return selection;
    }

    private static TrainingEligibility Excluded(string reason)
        => new() { ExclusionReason = reason };

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
