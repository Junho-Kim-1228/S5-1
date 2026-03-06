using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private sealed class InferenceBatchValidationResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; } = "";

            public static InferenceBatchValidationResult Fail(string message)
                => new() { IsValid = false, Message = message };
        }

        private sealed class CreatedBatchInfo
        {
            public string BatchPath { get; set; } = "";
            public int ItemCount { get; set; }
        }

        private sealed class SeedManifestItem
        {
            public string Id { get; set; } = "";
            public string ProcessedImage { get; set; } = "";
            public string? RawImage { get; set; }
        }

        private sealed class SeedManifestItemJson
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("processed_image")]
            public string ProcessedImage { get; set; } = "";

            [JsonPropertyName("raw_image")]
            public string? RawImage { get; set; }
        }

        private string FindProjectRoot(string targetFolderName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (dir != null)
            {
                if (dir.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private string GetTrainingInboxRoot()
        {
            string projectRoot = FindProjectRoot("capstone_design");
            return IOPath.Combine(projectRoot, "training_inbox");
        }
    }
}
