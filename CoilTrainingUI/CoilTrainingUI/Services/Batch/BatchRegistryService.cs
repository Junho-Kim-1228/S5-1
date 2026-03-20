using CoilTrainingUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public static class BatchRegistryService
{
    private const string RegistryFileName = ".batch_registry.json";

    public static string GetRegistryPath(string inboxRoot)
        => Path.Combine(inboxRoot, RegistryFileName);

    public static BatchLibraryRegistryDto Load(string inboxRoot)
    {
        Directory.CreateDirectory(inboxRoot);
        string path = GetRegistryPath(inboxRoot);
        if (!File.Exists(path))
            return new BatchLibraryRegistryDto();

        try
        {
            var loaded = JsonSerializer.Deserialize<BatchLibraryRegistryDto>(File.ReadAllText(path))
                         ?? new BatchLibraryRegistryDto();

            return Normalize(loaded);
        }
        catch
        {
            return new BatchLibraryRegistryDto();
        }
    }

    public static void Save(string inboxRoot, BatchLibraryRegistryDto registry)
    {
        Directory.CreateDirectory(inboxRoot);
        string path = GetRegistryPath(inboxRoot);
        string json = JsonSerializer.Serialize(
            Normalize(registry),
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(path, json);
    }

    public static void SetHidden(string inboxRoot, IEnumerable<string> batchKeys, bool hidden, string reason)
    {
        var registry = Load(inboxRoot);
        foreach (string batchKey in DistinctBatchKeys(batchKeys))
        {
            var entry = GetOrCreateEntry(registry, batchKey);
            entry.Hidden = hidden;
            entry.HiddenReason = hidden ? (string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim()) : "";
            entry.UpdatedAt = DateTime.UtcNow;
        }

        Save(inboxRoot, registry);
    }

    public static void MarkMergedBatch(string inboxRoot, string mergedBatchKey, IEnumerable<string> sourceBatchKeys)
    {
        var registry = Load(inboxRoot);
        var normalizedSources = DistinctBatchKeys(sourceBatchKeys).ToList();
        if (normalizedSources.Count == 0)
            throw new InvalidOperationException("병합 원본 배치가 없습니다.");

        var mergedEntry = GetOrCreateEntry(registry, mergedBatchKey);
        mergedEntry.Hidden = false;
        mergedEntry.HiddenReason = "";
        mergedEntry.BatchKind = "merged";
        mergedEntry.SourceBatches = normalizedSources;
        mergedEntry.UpdatedAt = DateTime.UtcNow;

        foreach (string sourceKey in normalizedSources)
        {
            var sourceEntry = GetOrCreateEntry(registry, sourceKey);
            sourceEntry.Hidden = true;
            sourceEntry.HiddenReason = $"merged:{mergedBatchKey}";
            sourceEntry.UpdatedAt = DateTime.UtcNow;

            if (!sourceEntry.MergedInto.Any(item => string.Equals(item, mergedBatchKey, StringComparison.OrdinalIgnoreCase)))
                sourceEntry.MergedInto.Add(mergedBatchKey);
        }

        Save(inboxRoot, registry);
    }

    public static void DeleteBatches(string inboxRoot, IEnumerable<string> batchKeys)
    {
        var normalizedKeys = DistinctBatchKeys(batchKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedKeys.Count == 0)
            return;

        var registry = Load(inboxRoot);
        bool changed = false;

        foreach (string batchKey in normalizedKeys)
        {
            if (registry.Batches.Remove(batchKey))
                changed = true;
        }

        foreach (var entry in registry.Batches.Values)
        {
            bool entryChanged = false;

            int sourceCountBefore = entry.SourceBatches.Count;
            entry.SourceBatches = entry.SourceBatches
                .Where(source => !normalizedKeys.Contains(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            entryChanged |= sourceCountBefore != entry.SourceBatches.Count;

            int mergedIntoBefore = entry.MergedInto.Count;
            entry.MergedInto = entry.MergedInto
                .Where(target => !normalizedKeys.Contains(target))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            entryChanged |= mergedIntoBefore != entry.MergedInto.Count;

            string normalizedReason = (entry.HiddenReason ?? string.Empty).Trim();
            if (normalizedReason.StartsWith("merged:", StringComparison.OrdinalIgnoreCase))
            {
                string mergedBatchKey = normalizedReason["merged:".Length..].Trim();
                if (normalizedKeys.Contains(mergedBatchKey))
                {
                    if (entry.MergedInto.Count > 0)
                    {
                        entry.Hidden = true;
                        entry.HiddenReason = $"merged:{entry.MergedInto[0]}";
                    }
                    else
                    {
                        entry.Hidden = false;
                        entry.HiddenReason = "";
                    }

                    entryChanged = true;
                }
            }

            if (entryChanged)
            {
                entry.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
            Save(inboxRoot, registry);
    }

    private static BatchRegistryEntryDto GetOrCreateEntry(BatchLibraryRegistryDto registry, string batchKey)
    {
        if (!registry.Batches.TryGetValue(batchKey, out var entry))
        {
            entry = new BatchRegistryEntryDto();
            registry.Batches[batchKey] = entry;
        }

        if (entry.SourceBatches == null)
            entry.SourceBatches = new List<string>();
        if (entry.MergedInto == null)
            entry.MergedInto = new List<string>();

        return entry;
    }

    private static BatchLibraryRegistryDto Normalize(BatchLibraryRegistryDto registry)
    {
        var normalized = new BatchLibraryRegistryDto
        {
            Batches = new Dictionary<string, BatchRegistryEntryDto>(StringComparer.OrdinalIgnoreCase)
        };

        if (registry?.Batches == null)
            return normalized;

        foreach (var pair in registry.Batches)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            normalized.Batches[pair.Key.Trim()] = pair.Value ?? new BatchRegistryEntryDto();
        }

        return normalized;
    }

    private static IEnumerable<string> DistinctBatchKeys(IEnumerable<string> batchKeys)
    {
        return (batchKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
