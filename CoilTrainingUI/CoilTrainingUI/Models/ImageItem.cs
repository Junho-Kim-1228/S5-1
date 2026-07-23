using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoilTrainingUI.Models;

/// <summary>
/// Read-only projection used by the image list. Persisted review decisions live in
/// ReviewState and are never inferred back from these display properties.
/// </summary>
public sealed class ImageItem : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _batchName = "";
    private string _batchKey = "";
    private string _processedPath = "";
    private string? _rawPath;
    private bool _requiresInfer;
    private bool _hasInferFile;
    private bool _inferParseFailed;
    private bool _hasStateFile;
    private bool _hasAiInfer;
    private bool _aiAnomaDefect;
    private int _gtDentCount;
    private int _gtLooseCount;
    private int _gtOtherCount;
    private string _decisionStatusKey = "Unreviewed";
    private string _userDecisionText = "미검수";
    private string _userDecisionSourceText = "-";
    private string _boxReviewStatusText = "해당 없음";
    private string _aiAnomaSummaryText = "Anoma 판정 없음";
    private string _aiYoloSummaryText = "YOLO 0개";
    private string _trainingEligibilityText = "학습 제외";
    private string _trainingExclusionReasonText = "";
    private string _statusColorMeaningText = "노란색: 아직 검수하지 않은 이미지입니다.";
    private bool _needsLegacyMigration;
    private bool _isReviewUnreviewed = true;
    private bool _isReviewing;
    private bool _isReviewConfirmedNormal;
    private bool _isReviewConfirmedDefect;
    private bool _isBoxReviewConfirmed;
    private bool _isReviewExcluded;
    private bool _isAutoAccepted;
    private bool _isAutoReviewAudit;
    private bool _anomaTrainingEligible;
    private bool _anomaEvaluationEligible;
    private bool _yoloPositiveEligible;
    private bool _yoloBackgroundEligible;
    private bool _yoloExcludedNoBoxDefect;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get => _fileName; set => SetField(ref _fileName, value); }
    public string BatchName { get => _batchName; set => SetField(ref _batchName, value); }
    public string BatchKey { get => _batchKey; set => SetField(ref _batchKey, value); }
    public string ProcessedPath { get => _processedPath; set => SetField(ref _processedPath, value); }
    public string? RawPath { get => _rawPath; set => SetField(ref _rawPath, value); }
    public bool RequiresInfer { get => _requiresInfer; set => SetField(ref _requiresInfer, value); }
    public bool HasInferFile { get => _hasInferFile; set => SetField(ref _hasInferFile, value); }
    public bool InferParseFailed { get => _inferParseFailed; set => SetField(ref _inferParseFailed, value); }
    public bool HasStateFile { get => _hasStateFile; set => SetField(ref _hasStateFile, value); }
    public bool HasAiInfer { get => _hasAiInfer; set => SetField(ref _hasAiInfer, value); }
    public bool AiAnomaDefect { get => _aiAnomaDefect; set => SetField(ref _aiAnomaDefect, value); }
    public int GtDentCount { get => _gtDentCount; set => SetField(ref _gtDentCount, value); }
    public int GtLooseCount { get => _gtLooseCount; set => SetField(ref _gtLooseCount, value); }
    public int GtOtherCount { get => _gtOtherCount; set => SetField(ref _gtOtherCount, value); }
    public string DecisionStatusKey { get => _decisionStatusKey; set => SetField(ref _decisionStatusKey, value); }
    public string UserDecisionText { get => _userDecisionText; set => SetField(ref _userDecisionText, value); }
    public string UserDecisionSourceText { get => _userDecisionSourceText; set => SetField(ref _userDecisionSourceText, value); }
    public string BoxReviewStatusText { get => _boxReviewStatusText; set => SetField(ref _boxReviewStatusText, value); }
    public string AiAnomaSummaryText { get => _aiAnomaSummaryText; set => SetField(ref _aiAnomaSummaryText, value); }
    public string AiYoloSummaryText { get => _aiYoloSummaryText; set => SetField(ref _aiYoloSummaryText, value); }
    public string TrainingEligibilityText { get => _trainingEligibilityText; set => SetField(ref _trainingEligibilityText, value); }
    public string TrainingExclusionReasonText { get => _trainingExclusionReasonText; set => SetField(ref _trainingExclusionReasonText, value); }
    public string StatusColorMeaningText { get => _statusColorMeaningText; set => SetField(ref _statusColorMeaningText, value); }
    public bool NeedsLegacyMigration { get => _needsLegacyMigration; set => SetField(ref _needsLegacyMigration, value); }
    public bool IsReviewUnreviewed { get => _isReviewUnreviewed; set => SetField(ref _isReviewUnreviewed, value); }
    public bool IsReviewing { get => _isReviewing; set => SetField(ref _isReviewing, value); }
    public bool IsReviewConfirmedNormal { get => _isReviewConfirmedNormal; set => SetField(ref _isReviewConfirmedNormal, value); }
    public bool IsReviewConfirmedDefect { get => _isReviewConfirmedDefect; set => SetField(ref _isReviewConfirmedDefect, value); }
    public bool IsBoxReviewConfirmed { get => _isBoxReviewConfirmed; set => SetField(ref _isBoxReviewConfirmed, value); }
    public bool IsReviewExcluded { get => _isReviewExcluded; set => SetField(ref _isReviewExcluded, value); }
    public bool IsAutoAccepted { get => _isAutoAccepted; set => SetField(ref _isAutoAccepted, value); }
    public bool IsAutoReviewAudit { get => _isAutoReviewAudit; set => SetField(ref _isAutoReviewAudit, value); }
    public bool AnomaTrainingEligible { get => _anomaTrainingEligible; set => SetField(ref _anomaTrainingEligible, value); }
    public bool AnomaEvaluationEligible { get => _anomaEvaluationEligible; set => SetField(ref _anomaEvaluationEligible, value); }
    public bool YoloPositiveEligible { get => _yoloPositiveEligible; set => SetField(ref _yoloPositiveEligible, value); }
    public bool YoloBackgroundEligible { get => _yoloBackgroundEligible; set => SetField(ref _yoloBackgroundEligible, value); }
    public bool YoloExcludedNoBoxDefect { get => _yoloExcludedNoBoxDefect; set => SetField(ref _yoloExcludedNoBoxDefect, value); }

    public bool HasRawFile => !string.IsNullOrWhiteSpace(RawPath);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(RawPath))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRawFile)));
    }
}
