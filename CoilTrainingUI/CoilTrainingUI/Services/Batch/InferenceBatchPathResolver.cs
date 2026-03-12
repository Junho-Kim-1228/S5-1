using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services;

public static class InferenceBatchPathResolver
{
    public static bool DetermineBatchRequiresInfer(string batchFolder, ManifestDto manifest)
    {
        string batchType = (manifest.BatchType ?? "").Trim().ToLowerInvariant();
        if (batchType == "no_infer")
            return false;

        if (batchType == "inference")
            return true;

        bool hasInferReference = manifest.Items.Any(item => !string.IsNullOrWhiteSpace(item.InferJson));
        if (hasInferReference)
            return true;

        string inferenceDir = Path.Combine(batchFolder, "inference");
        if (Directory.Exists(inferenceDir) &&
            Directory.EnumerateFiles(inferenceDir, "*.json", SearchOption.TopDirectoryOnly).Any())
        {
            return true;
        }

        return false;
    }

    public static string ResolveBatchRelativePath(string batchFolder, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return string.Empty;

        if (Path.IsPathRooted(candidatePath))
            return candidatePath;

        return Path.Combine(batchFolder, candidatePath);
    }

    public static string ResolveBatchInferJsonPath(string batchFolder, ManifestItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.InferJson))
            return Path.Combine(batchFolder, "inference", $"{item.Id}.infer.json");

        return Path.IsPathRooted(item.InferJson)
            ? item.InferJson
            : Path.Combine(batchFolder, item.InferJson);
    }

    public static string? ResolveBatchRawImagePath(string batchFolder, ManifestItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.RawImage))
        {
            string configuredPath = Path.IsPathRooted(item.RawImage)
                ? item.RawImage
                : Path.Combine(batchFolder, item.RawImage);
            return File.Exists(configuredPath) ? configuredPath : null;
        }

        if (string.IsNullOrWhiteSpace(item.Id))
        {
            if (string.IsNullOrWhiteSpace(item.ProcessedImage))
                return null;

            string processedFileName = Path.GetFileName(item.ProcessedImage);
            if (string.IsNullOrWhiteSpace(processedFileName))
                return null;

            string byProcessedNamePath = Path.Combine(batchFolder, "raw", processedFileName);
            return File.Exists(byProcessedNamePath) ? byProcessedNamePath : null;
        }

        string byIdPath = Path.Combine(batchFolder, "raw", $"{item.Id}.bmp");
        if (File.Exists(byIdPath))
            return byIdPath;

        if (!string.IsNullOrWhiteSpace(item.ProcessedImage))
        {
            string processedFileName = Path.GetFileName(item.ProcessedImage);
            if (!string.IsNullOrWhiteSpace(processedFileName))
            {
                string byProcessedNamePath = Path.Combine(batchFolder, "raw", processedFileName);
                if (File.Exists(byProcessedNamePath))
                    return byProcessedNamePath;
            }
        }

        return null;
    }
}
