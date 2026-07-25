using System;
using System.Text.RegularExpressions;

namespace CoilTrainingUI.Services;

public sealed class TrainingProgressSnapshot
{
    public int CurrentUnit { get; init; }
    public int TotalUnits { get; init; }
    public int Percent { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan? EstimatedRemaining { get; init; }
}

public sealed class TrainingEtaEstimator
{
    private static readonly Regex DinomalyStepPattern = new(
        @"\bDinomaly\s+step\s+(\d+)\s*/\s*(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YoloEpochPattern = new(
        @"^(?:\[ERR\]\s*)?(?:\x1B\[[0-?]*[ -/]*[@-~])*\s*(\d{1,7})\s*/\s*(\d{1,7})\b",
        RegexOptions.Compiled);

    private int _baselineUnit;
    private DateTimeOffset? _baselineTime;
    private int _lastUnit;
    private int _totalUnits;

    public TrainingProgressSnapshot? ObserveDinomalyStep(
        string? line,
        DateTimeOffset? observedAt = null)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        Match match = DinomalyStepPattern.Match(line);
        return match.Success
            ? ObserveMatch(match, expectedTotal: null, observedAt)
            : null;
    }

    public TrainingProgressSnapshot? ObserveYoloEpoch(
        string? line,
        int expectedTotalEpochs,
        DateTimeOffset? observedAt = null)
    {
        if (string.IsNullOrWhiteSpace(line) || expectedTotalEpochs <= 0)
            return null;

        Match match = YoloEpochPattern.Match(line);
        return match.Success
            ? ObserveMatch(match, expectedTotalEpochs, observedAt)
            : null;
    }

    private TrainingProgressSnapshot? ObserveMatch(
        Match match,
        int? expectedTotal,
        DateTimeOffset? observedAt)
    {
        if (!int.TryParse(match.Groups[1].Value, out int currentUnit) ||
            !int.TryParse(match.Groups[2].Value, out int totalUnits) ||
            currentUnit <= 0 || totalUnits <= 0 || currentUnit > totalUnits ||
            (expectedTotal.HasValue && totalUnits != expectedTotal.Value))
        {
            return null;
        }

        DateTimeOffset now = observedAt ?? DateTimeOffset.UtcNow;
        if (!_baselineTime.HasValue || totalUnits != _totalUnits)
        {
            _baselineUnit = currentUnit;
            _baselineTime = now;
            _lastUnit = currentUnit;
            _totalUnits = totalUnits;
        }
        else if (currentUnit <= _lastUnit)
        {
            return null;
        }
        else
        {
            _lastUnit = currentUnit;
        }

        TimeSpan elapsed = now - _baselineTime.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        TimeSpan? remaining = null;
        int completedSinceBaseline = currentUnit - _baselineUnit;
        if (completedSinceBaseline > 0 && elapsed >= TimeSpan.FromSeconds(2))
        {
            double secondsPerUnit = elapsed.TotalSeconds / completedSinceBaseline;
            double remainingSeconds = secondsPerUnit * (totalUnits - currentUnit);
            if (double.IsFinite(remainingSeconds) && remainingSeconds >= 0)
                remaining = TimeSpan.FromSeconds(remainingSeconds);
        }

        return new TrainingProgressSnapshot
        {
            CurrentUnit = currentUnit,
            TotalUnits = totalUnits,
            Percent = Math.Clamp(
                (int)Math.Round(currentUnit * 100.0 / totalUnits),
                0,
                100),
            Elapsed = elapsed,
            EstimatedRemaining = remaining
        };
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        int totalHours = (int)Math.Floor(duration.TotalHours);
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
