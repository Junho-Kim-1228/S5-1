using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.IO;

namespace CoilTrainingUI.Services;

public static class InferenceContextValidationService
{
    public static string GetExpectedContextId(ManifestDto manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        InferenceContextDto? context = manifest.InferenceContext;
        if (!string.Equals(context?.Status, "recorded", StringComparison.OrdinalIgnoreCase))
            return "";

        string contextId = (context?.ContextId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(contextId))
            throw new InvalidDataException("Recorded inference context has an empty context_id.");

        return contextId;
    }

    public static void ValidateInferContext(
        InferResultDto infer,
        string? expectedContextId,
        string? sourcePath = null)
    {
        if (infer == null)
            throw new ArgumentNullException(nameof(infer));

        string expected = (expectedContextId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(expected))
            return;

        string actual = (infer.InferenceContextId ?? "").Trim();
        string source = string.IsNullOrWhiteSpace(sourcePath)
            ? "infer.json"
            : Path.GetFileName(sourcePath);

        if (string.IsNullOrWhiteSpace(actual))
        {
            throw new InvalidDataException(
                $"Inference context is missing in {source}; expected '{expected}'.");
        }

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Inference context mismatch in {source}: expected '{expected}', actual '{actual}'.");
        }
    }
}
