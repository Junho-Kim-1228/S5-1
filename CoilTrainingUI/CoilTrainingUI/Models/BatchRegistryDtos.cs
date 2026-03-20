using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Models;

public sealed class BatchLibraryRegistryDto
{
    [JsonPropertyName("batches")]
    public Dictionary<string, BatchRegistryEntryDto> Batches { get; set; } = new();
}

public sealed class BatchRegistryEntryDto
{
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    [JsonPropertyName("hidden_reason")]
    public string HiddenReason { get; set; } = "";

    [JsonPropertyName("batch_kind")]
    public string BatchKind { get; set; } = "regular";

    [JsonPropertyName("source_batches")]
    public List<string> SourceBatches { get; set; } = new();

    [JsonPropertyName("merged_into")]
    public List<string> MergedInto { get; set; } = new();

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
