using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoilTrainingUI.Models
{
    public class ImageItem : INotifyPropertyChanged
    {
        private string _fileName = "";
        private string _batchName = "";
        private string _processedPath = "";
        private string? _rawPath;
        private bool _hasLabel;
        private bool _isNormal = true;
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
            set => SetField(ref _aiConsensusHighConfidence, value);
        }

        public double AiYoloMaxConf
        {
            get => _aiYoloMaxConf;
            set => SetField(ref _aiYoloMaxConf, value);
        }

        public double AiAnomaScore
        {
            get => _aiAnomaScore;
            set => SetField(ref _aiAnomaScore, value);
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
            }
        }

        public string ReviewReasonText
        {
            get => _reviewReasonText;
            set => SetField(ref _reviewReasonText, value);
        }

        public bool HasRawFile => !string.IsNullOrWhiteSpace(RawPath);
        public bool IsConfirmedDefect => HasLabel || !IsNormal;
        public bool IsConfirmedNormal => !HasLabel && IsNormal;
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
