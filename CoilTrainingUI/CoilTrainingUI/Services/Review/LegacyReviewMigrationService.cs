using CoilTrainingUI.Models.Review;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Services.Review;

public sealed class ReviewMigrationReport
{
    public int Requested { get; set; }
    public int LegacyFound { get; set; }
    public int Converted { get; set; }
    public int AlreadyMigrated { get; set; }
    public int Ambiguous { get; set; }
    public int Failed { get; set; }
    public List<string> Failures { get; } = new();
}

public sealed class LegacyReviewMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ReviewRepository _repository;

    public LegacyReviewMigrationService(ReviewRepository repository)
    {
        _repository = repository;
    }

    public ReviewMigrationReport Migrate(IEnumerable<string> imagePaths)
    {
        var distinctPaths = (imagePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var report = new ReviewMigrationReport { Requested = distinctPaths.Count };

        foreach (string imagePath in distinctPaths)
        {
            if (_repository.HasReviewFile(imagePath))
            {
                report.AlreadyMigrated++;
                continue;
            }

            string legacyPath = ImageStateService.GetStatePath(imagePath);
            if (!File.Exists(legacyPath))
                continue;

            report.LegacyFound++;
            try
            {
                var legacy = JsonSerializer.Deserialize<ImageStateDto>(File.ReadAllText(legacyPath), JsonOptions)
                             ?? throw new InvalidDataException("legacy state is empty.");
                string backupPath = GetLegacyBackupPath(imagePath);
                if (!File.Exists(backupPath))
                    File.Copy(legacyPath, backupPath, overwrite: false);

                var conversion = LegacyReviewConverter.Convert(legacy, legacyPath);
                conversion.State.Migration ??= new ReviewMigrationMetadata();
                conversion.State.Migration.SourcePath = legacyPath;
                conversion.State.Migration.BackupPath = backupPath;
                conversion.State.Migration.MigratedAtUtc = DateTime.UtcNow;
                conversion.State.Migration.Ambiguous = conversion.IsAmbiguous;
                conversion.State.Migration.Notes = new List<string>(conversion.Notes);
                _repository.Save(imagePath, conversion.State);

                report.Converted++;
                if (conversion.IsAmbiguous)
                    report.Ambiguous++;
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Failures.Add($"{Path.GetFileName(imagePath)}: {ex.Message}");
            }
        }

        return report;
    }

    public static string GetLegacyBackupPath(string imagePath)
        => Path.ChangeExtension(imagePath, ".state.v1.backup.json");
}
