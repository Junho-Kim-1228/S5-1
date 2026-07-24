using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoilTrainingUI.Services;

public sealed class DatasetValidationResult
{
    public int TotalCandidates { get; set; }
    public int AnomaImages { get; set; }
    public int AnomaNormalImages { get; set; }
    public int AnomaDefectImages { get; set; }
    public int YoloImages { get; set; }
    public int YoloPositiveImages { get; set; }
    public int YoloBackgroundImages { get; set; }
    public int YoloExcludedDefectWithoutBoxes { get; set; }
    public int YoloExcludedLowConfidencePredictedBoxes { get; set; }
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;

    public string ToErrorMessage()
    {
        var sb = new StringBuilder()
            .AppendLine("학습 데이터 검증 실패")
            .AppendLine($"전체 후보: {TotalCandidates}")
            .AppendLine($"Anoma: 정상 {AnomaNormalImages}, 불량 평가 {AnomaDefectImages}")
            .AppendLine($"YOLO: 양성 {YoloPositiveImages}, 정상 배경 {YoloBackgroundImages}")
            .AppendLine($"YOLO 제외(박스 없는 불량): {YoloExcludedDefectWithoutBoxes}")
            .AppendLine($"오류: {Errors.Count}")
            .AppendLine($"YOLO 제외(저신뢰 AI 박스 검수 필요): {YoloExcludedLowConfidencePredictedBoxes}")
            .AppendLine();

        const int maxShow = 80;
        foreach (string error in Errors.Take(maxShow))
            sb.AppendLine("- " + error);
        if (Errors.Count > maxShow)
            sb.AppendLine($"... 외 {Errors.Count - maxShow}건");
        return sb.ToString().TrimEnd();
    }
}

public sealed class TrainingDatasetValidator
{
    private readonly ReviewRepository _repository;
    private readonly TrainingDatasetSelector _selector;
    private readonly PredictionReader _predictionReader = new();

    public TrainingDatasetValidator(ReviewRepository repository, TrainingDatasetSelector selector)
    {
        _repository = repository;
        _selector = selector;
    }

    public DatasetValidationResult Validate(
        TrainingDatasetSelection selection,
        bool trainAnoma,
        bool trainYolo)
    {
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));

        var result = new DatasetValidationResult
        {
            TotalCandidates = selection.TotalCandidates,
            AnomaImages = selection.AnomaInputs.Count,
            YoloImages = selection.YoloInputs.Count,
            YoloExcludedDefectWithoutBoxes = selection.ExcludedDefectWithoutBoxes,
            YoloExcludedLowConfidencePredictedBoxes = selection.ExcludedLowConfidencePredictedBoxes
        };

        if (trainAnoma)
            ValidateAnomaInputs(selection.AnomaInputs, result);
        if (trainYolo)
            ValidateYoloInputs(selection.YoloInputs, result);
        return result;
    }

    private void ValidateAnomaInputs(
        IReadOnlyList<TrainingImageInput> inputs,
        DatasetValidationResult result)
    {
        foreach (TrainingImageInput input in inputs)
        {
            if (!ValidateCommonInput(input, result))
                continue;

            ReviewStateLoadResult load = _repository.Load(input.ImagePath);
            if (!RequireCurrentReview(load, input.ImagePath, result))
                continue;

            switch (load.State.Decision)
            {
                case ImageReviewDecision.ConfirmedNormal:
                    result.AnomaNormalImages++;
                    break;
                case ImageReviewDecision.ConfirmedDefect:
                    result.AnomaDefectImages++;
                    break;
                default:
                    result.Errors.Add($"Anoma 입력에 미확정 상태가 포함됨: {Path.GetFileName(input.ImagePath)}");
                    break;
            }
        }

        if (result.AnomaNormalImages < 2)
            result.Errors.Add("Anoma 학습에는 정상 확정 이미지가 최소 2개 필요합니다.");
        if (result.AnomaDefectImages < 1)
            result.Errors.Add("Anoma 평가에는 불량 확정 이미지가 최소 1개 필요합니다.");
    }

    private void ValidateYoloInputs(
        IReadOnlyList<TrainingImageInput> inputs,
        DatasetValidationResult result)
    {
        foreach (TrainingImageInput input in inputs)
        {
            if (!ValidateCommonInput(input, result))
                continue;

            ReviewStateLoadResult load = _repository.Load(input.ImagePath);
            if (!RequireCurrentReview(load, input.ImagePath, result))
                continue;

            ReviewState state = load.State;
            TrainingEligibility eligibility = _selector.Evaluate(load);
            if (state.Decision == ImageReviewDecision.ConfirmedNormal && eligibility.YoloBackground)
            {
                if (state.Boxes.Count != 0)
                    result.Errors.Add($"YOLO 정상 배경에 박스가 있음: {Path.GetFileName(input.ImagePath)}");
                else
                    result.YoloBackgroundImages++;
                continue;
            }

            if (state.Decision == ImageReviewDecision.ConfirmedDefect && eligibility.YoloPositive)
            {
                bool labelsValid = true;
                for (int index = 0; index < state.Boxes.Count; index++)
                {
                    if (!TryValidateBox(state.Boxes[index], out string reason))
                    {
                        labelsValid = false;
                        result.Errors.Add(
                            $"YOLO 박스 오류: {Path.GetFileName(input.ImagePath)} box[{index}] {reason}");
                    }
                }
                if (labelsValid)
                    result.YoloPositiveImages++;
                continue;
            }

            result.Errors.Add($"YOLO 선택 규칙에 맞지 않는 입력: {Path.GetFileName(input.ImagePath)}");
        }

        if (result.YoloPositiveImages < 1)
            result.Errors.Add("YOLO 학습에는 박스가 확정된 불량 이미지가 최소 1개 필요합니다.");
    }

    private bool ValidateCommonInput(TrainingImageInput input, DatasetValidationResult result)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.ImagePath))
        {
            result.Errors.Add("빈 이미지 경로가 포함되어 있습니다.");
            return false;
        }
        if (!File.Exists(input.ImagePath))
        {
            result.Errors.Add("processed image 없음: " + input.ImagePath);
            return false;
        }
        if (input.RequiresInfer && (string.IsNullOrWhiteSpace(input.InferJsonPath) || !File.Exists(input.InferJsonPath)))
        {
            result.Errors.Add($"필수 infer.json 없음: {Path.GetFileName(input.ImagePath)}");
            return false;
        }
        if (input.RequiresInfer && !string.IsNullOrWhiteSpace(input.ExpectedInferenceContextId))
        {
            PredictionSnapshot prediction = _predictionReader.Read(
                input.InferJsonPath,
                input.ExpectedInferenceContextId);
            if (prediction.ParseFailed)
            {
                result.Errors.Add(
                    $"infer.json 추론 컨텍스트 오류: {Path.GetFileName(input.ImagePath)} ({prediction.Error})");
                return false;
            }
        }
        return true;
    }

    private static bool RequireCurrentReview(
        ReviewStateLoadResult load,
        string imagePath,
        DatasetValidationResult result)
    {
        if (load.ParseFailed)
        {
            result.Errors.Add($"검수 상태 해석 실패: {Path.GetFileName(imagePath)} ({load.Message})");
            return false;
        }
        if (load.IsLegacyProjection || !load.HasReviewFile)
        {
            result.Errors.Add($"새 검수 상태가 없음: {Path.GetFileName(imagePath)}");
            return false;
        }
        return true;
    }

    private static bool TryValidateBox(ReviewBox? box, out string reason)
    {
        if (box == null)
        {
            reason = "박스가 null임";
            return false;
        }

        string className = (box.ClassName ?? "").Trim().ToLowerInvariant();
        if (className is not ("dent" or "loose"))
        {
            reason = $"지원하지 않는 클래스 '{className}'";
            return false;
        }
        if (!IsFinite01(box.X) || !IsFinite01(box.Y) ||
            !IsFinite01(box.Width) || !IsFinite01(box.Height) ||
            box.Width <= 0 || box.Height <= 0)
        {
            reason = "정규화 좌표가 0~1 범위의 유효한 값이 아님";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IsFinite01(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0 && value <= 1.0;
}
