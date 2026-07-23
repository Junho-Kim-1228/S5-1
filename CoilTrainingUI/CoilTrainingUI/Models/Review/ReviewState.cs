using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Models.Review;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageReviewDecision
{
    Unreviewed,
    Reviewing,
    ConfirmedNormal,
    ConfirmedDefect,
    Excluded
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BoxReviewDecision
{
    NotApplicable,
    Predicted,
    Edited,
    Confirmed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewDecisionSource
{
    None,
    Manual,
    AcceptedAiPrediction,
    AutoAcceptedAiPrediction,
    LegacyManual,
    LegacyAuto,
    LegacyUnknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BoxReviewSource
{
    None,
    AiPrediction,
    AcceptedAiPrediction,
    Manual,
    AutoAcceptedAiPrediction,
    LegacyUnknown
}

public sealed class ReviewBox
{
    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "manual";

    [JsonPropertyName("prediction_confidence")]
    public double? PredictionConfidence { get; set; }

    public ReviewBox Clone() => new()
    {
        ClassName = ClassName,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Source = Source,
        PredictionConfidence = PredictionConfidence
    };
}

public sealed class ReviewState
{
    public const int CurrentSchemaVersion = 3;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("decision")]
    public ImageReviewDecision Decision { get; set; } = ImageReviewDecision.Unreviewed;

    [JsonPropertyName("decision_source")]
    public ReviewDecisionSource DecisionSource { get; set; } = ReviewDecisionSource.None;

    [JsonPropertyName("box_review")]
    public BoxReviewDecision BoxReview { get; set; } = BoxReviewDecision.NotApplicable;

    [JsonPropertyName("box_review_source")]
    public BoxReviewSource BoxReviewSource { get; set; } = BoxReviewSource.None;

    [JsonPropertyName("boxes")]
    public List<ReviewBox> Boxes { get; set; } = new();

    [JsonPropertyName("use_as_yolo_background")]
    public bool UseAsYoloBackground { get; set; }

    [JsonPropertyName("exclusion_reason")]
    public string ExclusionReason { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("decision_confirmed_at_utc")]
    public DateTime? DecisionConfirmedAtUtc { get; set; }

    [JsonPropertyName("boxes_confirmed_at_utc")]
    public DateTime? BoxesConfirmedAtUtc { get; set; }

    [JsonPropertyName("migration")]
    public ReviewMigrationMetadata? Migration { get; set; }

    [JsonPropertyName("auto_review")]
    public AutoReviewMetadata? AutoReview { get; set; }

    public ReviewState Clone()
    {
        return new ReviewState
        {
            SchemaVersion = SchemaVersion,
            Decision = Decision,
            DecisionSource = DecisionSource,
            BoxReview = BoxReview,
            BoxReviewSource = BoxReviewSource,
            Boxes = Boxes.ConvertAll(box => box.Clone()),
            UseAsYoloBackground = UseAsYoloBackground,
            ExclusionReason = ExclusionReason,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            DecisionConfirmedAtUtc = DecisionConfirmedAtUtc,
            BoxesConfirmedAtUtc = BoxesConfirmedAtUtc,
            Migration = Migration?.Clone(),
            AutoReview = AutoReview?.Clone()
        };
    }
}

public sealed class ReviewMigrationMetadata
{
    [JsonPropertyName("source_schema")]
    public string SourceSchema { get; set; } = "legacy_state_v1";

    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("backup_path")]
    public string BackupPath { get; set; } = "";

    [JsonPropertyName("migrated_at_utc")]
    public DateTime MigratedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("ambiguous")]
    public bool Ambiguous { get; set; }

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();

    public ReviewMigrationMetadata Clone() => new()
    {
        SourceSchema = SourceSchema,
        SourcePath = SourcePath,
        BackupPath = BackupPath,
        MigratedAtUtc = MigratedAtUtc,
        Ambiguous = Ambiguous,
        Notes = new List<string>(Notes)
    };
}

public sealed class ReviewStateLoadResult
{
    public ReviewState State { get; init; } = new();
    public bool HasReviewFile { get; init; }
    public bool IsLegacyProjection { get; init; }
    public bool ParseFailed { get; init; }
    public string Message { get; init; } = "";

    public bool IsPersistedCurrentState => HasReviewFile && !ParseFailed;
}
