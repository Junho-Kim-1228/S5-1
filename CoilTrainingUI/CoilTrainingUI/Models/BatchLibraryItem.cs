using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoilTrainingUI.Models;

public sealed class BatchLibraryItem : INotifyPropertyChanged
{
    private string _batchKey = "";
    private string _batchId = "";
    private string _batchRoot = "";
    private int _itemCount;
    private DateTime? _createdAt;
    private string _batchKind = "regular";
    private bool _isHidden;
    private string _hiddenReason = "";
    private bool _requiresInfer;
    private bool _hasAnyInfer;
    private List<string> _sourceBatches = new();
    private string _reviewStatus = "pending";
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BatchKey
    {
        get => _batchKey;
        set => SetField(ref _batchKey, value);
    }

    public string BatchId
    {
        get => _batchId;
        set => SetField(ref _batchId, value);
    }

    public string BatchRoot
    {
        get => _batchRoot;
        set => SetField(ref _batchRoot, value);
    }

    public int ItemCount
    {
        get => _itemCount;
        set => SetField(ref _itemCount, value);
    }

    public DateTime? CreatedAt
    {
        get => _createdAt;
        set
        {
            if (!SetField(ref _createdAt, value))
                return;
            OnPropertyChanged(nameof(CreatedAtText));
        }
    }

    public string BatchKind
    {
        get => _batchKind;
        set
        {
            if (!SetField(ref _batchKind, string.IsNullOrWhiteSpace(value) ? "regular" : value))
                return;
            OnPropertyChanged(nameof(BatchKindText));
        }
    }

    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (!SetField(ref _isHidden, value))
                return;
            OnPropertyChanged(nameof(HiddenReasonText));
        }
    }

    public string HiddenReason
    {
        get => _hiddenReason;
        set
        {
            if (!SetField(ref _hiddenReason, value))
                return;
            OnPropertyChanged(nameof(HiddenReasonText));
        }
    }

    public bool RequiresInfer
    {
        get => _requiresInfer;
        set => SetField(ref _requiresInfer, value);
    }

    public bool HasAnyInfer
    {
        get => _hasAnyInfer;
        set => SetField(ref _hasAnyInfer, value);
    }

    public List<string> SourceBatches
    {
        get => _sourceBatches;
        set
        {
            if (!SetField(ref _sourceBatches, value ?? new List<string>()))
                return;
            OnPropertyChanged(nameof(SourceBatchesText));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string ReviewStatus
    {
        get => _reviewStatus;
        set
        {
            if (!SetField(ref _reviewStatus, string.IsNullOrWhiteSpace(value) ? "pending" : value))
                return;
            OnPropertyChanged(nameof(ReviewStatusText));
        }
    }

    public string CreatedAtText => CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public string BatchKindText => (BatchKind ?? "").Trim().ToLowerInvariant() switch
    {
        "merged" => "병합",
        _ => "일반"
    };

    public string HiddenReasonText
    {
        get
        {
            if (!IsHidden)
                return "-";

            string normalized = (HiddenReason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return "숨김";

            if (string.Equals(normalized, "manual", StringComparison.OrdinalIgnoreCase))
                return "수동 숨김";

            if (normalized.StartsWith("merged:", StringComparison.OrdinalIgnoreCase))
            {
                string mergedBatchKey = normalized["merged:".Length..].Trim();
                return string.IsNullOrWhiteSpace(mergedBatchKey)
                    ? "병합 숨김"
                    : $"병합 숨김 ({mergedBatchKey})";
            }

            return normalized;
        }
    }

    public string SourceBatchesText => SourceBatches.Count == 0
        ? "-"
        : string.Join(", ", SourceBatches);

    public string ReviewStatusText => (ReviewStatus ?? "").Trim().ToLowerInvariant() switch
    {
        "reviewed" => "검수 완료",
        "review_needed" => "검수 필요",
        _ => "검수 대기"
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
