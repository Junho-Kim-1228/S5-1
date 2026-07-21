using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoilTrainingUI.Models
{
    public class ImageItem : INotifyPropertyChanged
    {
        private string _fileName = "";
        private string _batchName = "";
        private string _batchKey = "";
        private string _processedPath = "";
        private string? _rawPath;
        private bool _hasLabel;
        private bool _isNormal = true;
        private bool _hasConfirmedDecision;
        private bool _hasAiInfer;
        private bool _aiYoloDefect;
        private bool _aiAnomaDefect;
        private bool _aiConsensusHighConfidence;
        private double _aiYoloMaxConf;
        private double _aiAnomaScore;
        private bool _requiresInfer;
        private bool _hasInferFile;
        private bool _inferParseFailed;
        private bool _hasStateFile;
        private int _gtDentCount;
        private int _gtLooseCount;
        private int _gtOtherCount;
        private int _aiDentCount;
        private int _aiLooseCount;
        private int _aiOtherCount;
        private string _reviewStatus = "none";
        private string _reviewReasonText = "";
        private string _decisionSource = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FileName
        {
            get => _fileName;
            set => SetField(ref _fileName, value);
        }

        public string BatchName
        {
            get => _batchName;
            set => SetField(ref _batchName, value);
        }

        public string ProcessedPath
        {
            get => _processedPath;
            set => SetField(ref _processedPath, value);
        }

        public string? RawPath
        {
            get => _rawPath;
            set => SetField(ref _rawPath, value);
        }

        public bool HasLabel
        {
            get => _hasLabel;
            set
            {
                if (!SetField(ref _hasLabel, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public bool IsNormal
        {
            get => _isNormal;
            set
            {
                if (!SetField(ref _isNormal, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public bool HasAiInfer
        {
            get => _hasAiInfer;
            set
            {
                if (!SetField(ref _hasAiInfer, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public bool AiYoloDefect
        {
            get => _aiYoloDefect;
            set
            {
                if (!SetField(ref _aiYoloDefect, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public bool AiAnomaDefect
        {
            get => _aiAnomaDefect;
            set
            {
                if (!SetField(ref _aiAnomaDefect, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public bool AiConsensusHighConfidence
        {
            get => _aiConsensusHighConfidence;
            set
            {
                if (!SetField(ref _aiConsensusHighConfidence, value))
                    return;
                OnPropertyChanged(nameof(ConfidenceStatusText));
            }
        }

        public double AiYoloMaxConf
        {
            get => _aiYoloMaxConf;
            set
            {
                if (!SetField(ref _aiYoloMaxConf, value))
                    return;
                OnPropertyChanged(nameof(AiEvidenceText));
            }
        }

        public double AiAnomaScore
        {
            get => _aiAnomaScore;
            set
            {
                if (!SetField(ref _aiAnomaScore, value))
                    return;
                OnPropertyChanged(nameof(AiEvidenceText));
            }
        }

        public bool RequiresInfer
        {
            get => _requiresInfer;
            set => SetField(ref _requiresInfer, value);
        }

        public bool HasInferFile
        {
            get => _hasInferFile;
            set => SetField(ref _hasInferFile, value);
        }

        public bool InferParseFailed
        {
            get => _inferParseFailed;
            set
            {
                if (!SetField(ref _inferParseFailed, value))
                    return;
                OnPropertyChanged(nameof(AutoApproveCandidate));
                OnPropertyChanged(nameof(NeedsReview));
            }
        }

        public bool HasStateFile
        {
            get => _hasStateFile;
            set => SetField(ref _hasStateFile, value);
        }

        public int GtDentCount
        {
            get => _gtDentCount;
            set => SetField(ref _gtDentCount, value);
        }

        public int GtLooseCount
        {
            get => _gtLooseCount;
            set => SetField(ref _gtLooseCount, value);
        }

        public int GtOtherCount
        {
            get => _gtOtherCount;
            set => SetField(ref _gtOtherCount, value);
        }

        public int AiDentCount
        {
            get => _aiDentCount;
            set => SetField(ref _aiDentCount, value);
        }

        public int AiLooseCount
        {
            get => _aiLooseCount;
            set => SetField(ref _aiLooseCount, value);
        }

        public int AiOtherCount
        {
            get => _aiOtherCount;
            set => SetField(ref _aiOtherCount, value);
        }

        public string ReviewStatus
        {
            get => _reviewStatus;
            set
            {
                if (!SetField(ref _reviewStatus, value))
                    return;
                OnPropertyChanged(nameof(ReviewDone));
                OnPropertyChanged(nameof(AutoApproveCandidate));
                OnPropertyChanged(nameof(NeedsReview));
                OnPropertyChanged(nameof(ReviewStatusText));
                OnPropertyChanged(nameof(DisplayDecisionText));
                OnPropertyChanged(nameof(DisplayDecisionStatus));
                OnPropertyChanged(nameof(DecisionSourceText));
                OnPropertyChanged(nameof(ReviewGuideText));
            }
        }

        public string ReviewReasonText
        {
            get => _reviewReasonText;
            set
            {
                if (!SetField(ref _reviewReasonText, value))
                    return;
                OnPropertyChanged(nameof(ReviewGuideText));
            }
        }

        public bool HasConfirmedDecision
        {
            get => _hasConfirmedDecision;
            set
            {
                if (!SetField(ref _hasConfirmedDecision, value))
                    return;
                OnStatusPropertiesChanged();
            }
        }

        public string DecisionSource
        {
            get => _decisionSource;
            set
            {
                if (!SetField(ref _decisionSource, value ?? ""))
                    return;
                OnPropertyChanged(nameof(DecisionSourceText));
            }
        }

        public bool HasRawFile => !string.IsNullOrWhiteSpace(RawPath);
        public bool IsConfirmedDefect => HasConfirmedDecision && (HasLabel || !IsNormal);
        public bool IsConfirmedNormal => HasConfirmedDecision && !HasLabel && IsNormal;
        public bool ReviewDone => string.Equals(ReviewStatus, "review_done", System.StringComparison.OrdinalIgnoreCase);
        public bool AutoApproveCandidate => string.Equals(ReviewStatus, "auto_candidate", System.StringComparison.OrdinalIgnoreCase);
        public bool NeedsReview => string.Equals(ReviewStatus, "review_needed", System.StringComparison.OrdinalIgnoreCase);
        public string ReviewStatusText => ReviewStatus switch
        {
            "review_needed" => "검수 필요",
            "auto_candidate" => "자동 확정 후보",
            "review_done" => "확정 완료",
            _ => "-"
        };

        public string ConfidenceStatusText => !HasAiInfer
            ? "판정 없음"
            : (AiConsensusHighConfidence ? "AI 신뢰도 높음" : "AI 신뢰도 낮음");

        public string DecisionSourceText => !ReviewDone
            ? "미확정"
            : DecisionSource switch
            {
                "auto" => "AI 자동 확정",
                "manual" => "사용자 확정",
                _ => "기존 확정"
            };

        public string DisplayDecisionText => ReviewDone
            ? $"최종 {GtImageStatusText}"
            : $"AI 후보 {AiFinalStatusText}";

        public string DisplayDecisionStatus => ReviewDone ? GtImageStatusText : AiFinalStatusText;

        public string AiEvidenceText
        {
            get
            {
                if (!HasAiInfer)
                    return "AI 추론 결과 없음";

                string yolo = AiDetectionCount > 0
                    ? $"YOLO {AiYoloMaxConf:0.00} / 박스 {AiDetectionCount}개"
                    : "YOLO 미검출";
                return $"Anoma {AiAnomaScore:0.000} / {yolo}";
            }
        }

        public string ReviewGuideText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ReviewReasonText))
                    return ReviewDone ? "추가 검수 불필요" : "판정 확인 대기";

                string translated = ReviewReasonText
                    .Replace("model_disagree", "모델 판정 불일치")
                    .Replace("yolo_low_conf", "YOLO 신뢰도 낮음")
                    .Replace("anoma_low_conf", "Anoma 신뢰도 낮음")
                    .Replace("infer_missing", "추론 결과 없음")
                    .Replace("infer_parse_failed", "추론 결과 해석 실패")
                    .Replace("model_agree_high_conf", "고신뢰 모델 일치")
                    .Replace("yolo_detection_exists", "YOLO 예측 박스 확인 필요")
                    .Replace("bbox_edited_pending_confirmation", "박스 수정됨, 이미지 판정 확인 필요")
                    .Replace("defect_predicted", "불량 후보 확인 필요");
                return translated;
            }
        }

        // AI 기준 불량 여부 (YOLO/Anoma 둘 중 하나라도 불량이면 true)
        public bool AiIsDefect => AiYoloDefect || AiAnomaDefect;

        public int AiDetectionCount => AiDentCount + AiLooseCount + AiOtherCount;

        // 기존 UI 표시용
        public string AiYoloStatusText => !HasAiInfer ? "미분류" : (AiYoloDefect ? "불량" : "정상");
        public string AiAnomaStatusText => !HasAiInfer ? "미분류" : (AiAnomaDefect ? "불량" : "정상");
        public string GtYoloStatusText => HasLabel ? "불량" : "정상";
        public string GtAnomaStatusText => IsNormal ? "정상" : "불량";

        // 2-stage 파이프라인 표시용
        public string AiStage1StatusText => !HasAiInfer ? "미분류" : (AiAnomaDefect ? "이상" : "정상");

        public string AiStage2StatusText
        {
            get
            {
                if (!HasAiInfer)
                    return "미분류";

                if (!AiAnomaDefect && AiDetectionCount == 0)
                    return "건너뜀";

                if (AiDetectionCount > 0)
                    return "검출";

                return "미검출";
            }
        }

        public string BatchKey
        {
            get => _batchKey;
            set => SetField(ref _batchKey, value);
        }

        public string AiFinalStatusText => !HasAiInfer ? "미분류" : (AiIsDefect ? "불량" : "정상");
        public string GtImageStatusText => IsNormal ? "정상" : "불량";
        public string GtBoxesStatusText => HasLabel ? "있음" : "없음";

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

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);

            if (propertyName == nameof(RawPath))
                OnPropertyChanged(nameof(HasRawFile));

            return true;
        }

        private void OnStatusPropertiesChanged()
        {
            OnPropertyChanged(nameof(AiIsDefect));
            OnPropertyChanged(nameof(AiDetectionCount));
            OnPropertyChanged(nameof(AiYoloStatusText));
            OnPropertyChanged(nameof(AiAnomaStatusText));
            OnPropertyChanged(nameof(GtYoloStatusText));
            OnPropertyChanged(nameof(GtAnomaStatusText));
            OnPropertyChanged(nameof(AiStage1StatusText));
            OnPropertyChanged(nameof(AiStage2StatusText));
            OnPropertyChanged(nameof(AiFinalStatusText));
            OnPropertyChanged(nameof(GtImageStatusText));
            OnPropertyChanged(nameof(GtBoxesStatusText));
            OnPropertyChanged(nameof(IsConfirmedDefect));
            OnPropertyChanged(nameof(IsConfirmedNormal));
            OnPropertyChanged(nameof(ReviewDone));
            OnPropertyChanged(nameof(AutoApproveCandidate));
            OnPropertyChanged(nameof(NeedsReview));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(string.Empty);
        }

        private void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
