using CoilTrainingUI.Models.Review;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Services.Review;

public sealed class ReviewRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ReviewStateLoadResult Load(string imagePath)
    {
        string reviewPath = GetReviewPath(imagePath);
        if (File.Exists(reviewPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<ReviewState>(File.ReadAllText(reviewPath), JsonOptions)
                            ?? throw new InvalidDataException("review state is empty.");
                Normalize(state);
                Validate(state);
                return new ReviewStateLoadResult
                {
                    State = state,
                    HasReviewFile = true
                };
            }
            catch (Exception ex)
            {
                return new ReviewStateLoadResult
                {
                    State = new ReviewState(),
                    HasReviewFile = true,
                    ParseFailed = true,
                    Message = ex.Message
                };
            }
        }

        string legacyPath = ImageStateService.GetStatePath(imagePath);
        if (!File.Exists(legacyPath))
            return new ReviewStateLoadResult { State = new ReviewState() };

        try
        {
            var legacy = JsonSerializer.Deserialize<ImageStateDto>(File.ReadAllText(legacyPath), JsonOptions)
                         ?? throw new InvalidDataException("legacy state is empty.");
            var conversion = LegacyReviewConverter.Convert(legacy, legacyPath);
            return new ReviewStateLoadResult
            {
                State = conversion.State,
                IsLegacyProjection = true,
                Message = conversion.IsAmbiguous
                    ? string.Join(", ", conversion.Notes)
                    : "legacy_migration_required"
            };
        }
        catch (Exception ex)
        {
            return new ReviewStateLoadResult
            {
                State = new ReviewState(),
                IsLegacyProjection = true,
                ParseFailed = true,
                Message = ex.Message
            };
        }
    }

    public void Save(string imagePath, ReviewState state)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("imagePath is empty.", nameof(imagePath));
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var normalized = state.Clone();
        Normalize(normalized);
        normalized.SchemaVersion = ReviewState.CurrentSchemaVersion;
        normalized.UpdatedAtUtc = DateTime.UtcNow;
        Validate(normalized);

        string path = GetReviewPath(imagePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(path))
        {
            string previousPath = path + ".previous";
            File.Copy(path, previousPath, overwrite: true);
            File.Move(tempPath, path, overwrite: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    public bool HasReviewFile(string imagePath) => File.Exists(GetReviewPath(imagePath));

    public static string GetReviewPath(string imagePath)
        => Path.ChangeExtension(imagePath, ".review.json");

    public static string GetPreviousReviewPath(string imagePath)
        => GetReviewPath(imagePath) + ".previous";

    private static void Normalize(ReviewState state)
    {
        state.Boxes ??= new();
        state.ExclusionReason ??= "";
        foreach (var box in state.Boxes)
        {
            box.ClassName = (box.ClassName ?? "").Trim().ToLowerInvariant();
            box.Source = string.IsNullOrWhiteSpace(box.Source) ? "manual" : box.Source.Trim();
        }

        if (state.Decision == ImageReviewDecision.ConfirmedNormal)
        {
            state.BoxReview = BoxReviewDecision.NotApplicable;
            state.BoxReviewSource = BoxReviewSource.None;
            state.Boxes.Clear();
        }

        if (state.BoxReview == BoxReviewDecision.NotApplicable)
            state.BoxReviewSource = BoxReviewSource.None;

        if (state.Decision != ImageReviewDecision.ConfirmedNormal)
            state.UseAsYoloBackground = false;

        if (state.Decision != ImageReviewDecision.Excluded)
            state.ExclusionReason = "";
    }

    private static void Validate(ReviewState state)
    {
        if (state.SchemaVersion <= 0 || state.SchemaVersion > ReviewState.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported review schema version: {state.SchemaVersion}");

        foreach (var box in state.Boxes)
        {
            if (box.ClassName is not ("dent" or "loose"))
                throw new InvalidDataException($"Unsupported box class: {box.ClassName}");
            if (!IsFinite01(box.X) || !IsFinite01(box.Y) ||
                !IsFinite01(box.Width) || !IsFinite01(box.Height) ||
                box.Width <= 0 || box.Height <= 0)
            {
                throw new InvalidDataException("Review box coordinates must be finite normalized values.");
            }
        }

        if (state.DecisionSource == ReviewDecisionSource.AutoAcceptedAiPrediction &&
            state.AutoReview == null)
        {
            throw new InvalidDataException("Auto-accepted decision is missing auto_review metadata.");
        }

        if (state.AutoReview != null)
        {
            AutoReviewMetadata metadata = state.AutoReview;
            if (string.IsNullOrWhiteSpace(metadata.PolicyVersion) ||
                string.IsNullOrWhiteSpace(metadata.InferenceContextId) ||
                !IsFinite(metadata.AnomaScore) ||
                !IsFinite(metadata.AnomaScoreThreshold) || metadata.AnomaScoreThreshold <= 0 ||
                !IsFinite(metadata.NormalAutoMaxScore) ||
                !IsFinite(metadata.DefectAutoMinScore) ||
                !IsFinite01(metadata.YoloBoxMinConfidence) ||
                !IsFinite01(metadata.AuditSampleRate))
            {
                throw new InvalidDataException("Invalid auto_review metadata.");
            }
        }
    }

    private static bool IsFinite01(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
