using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoilTrainingUI.Services;

public static class TrainingSettingsValidator
{
    public static IReadOnlyList<string> Validate(
        AppSettings.YoloTrainingSection yolo,
        AppSettings.AnomaTrainingSection anoma)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(yolo.Model)) errors.Add("YOLO 기본 모델을 입력하세요.");
        if (yolo.Epochs <= 0) errors.Add("YOLO epoch는 1 이상이어야 합니다.");
        if (yolo.FineTuneEpochs <= 0) errors.Add("YOLO 파인튜닝 epoch는 1 이상이어야 합니다.");
        if (yolo.FineTuneLearningRate <= 0) errors.Add("YOLO 파인튜닝 학습률은 0보다 커야 합니다.");
        if (yolo.ImageSize <= 0) errors.Add("YOLO 이미지 크기는 1 이상이어야 합니다.");
        if (yolo.Batch <= 0) errors.Add("YOLO batch는 1 이상이어야 합니다.");
        if (string.IsNullOrWhiteSpace(yolo.Device)) errors.Add("YOLO device를 입력하세요.");
        if (yolo.Seed < 0) errors.Add("YOLO seed는 0 이상이어야 합니다.");

        if (!string.Equals(anoma.Model, "dinomaly", StringComparison.OrdinalIgnoreCase))
            errors.Add("현재 Anoma 학습 모델은 dinomaly만 지원합니다.");
        if (anoma.ImageSize <= 0) errors.Add("Anoma 이미지 크기는 1 이상이어야 합니다.");
        else if (anoma.ImageSize % 14 != 0) errors.Add("Dinomaly 이미지 크기는 14의 배수여야 합니다.");
        if (anoma.Batch <= 0) errors.Add("Anoma batch는 1 이상이어야 합니다.");
        if (string.IsNullOrWhiteSpace(anoma.Device)) errors.Add("Anoma device를 입력하세요.");
        if (anoma.Seed < 0) errors.Add("Anoma seed는 0 이상이어야 합니다.");
        if (string.IsNullOrWhiteSpace(anoma.Encoder)) errors.Add("Dinomaly encoder를 입력하세요.");
        if (anoma.Dropout < 0 || anoma.Dropout >= 1) errors.Add("Dinomaly dropout은 0 이상 1 미만이어야 합니다.");
        if (anoma.DecoderDepth < 8) errors.Add("Dinomaly decoder depth는 8 이상이어야 합니다.");
        if (anoma.MaxSteps <= 0) errors.Add("Dinomaly max steps는 1 이상이어야 합니다.");
        if (anoma.LearningRate <= 0) errors.Add("Dinomaly 학습률은 0보다 커야 합니다.");
        if (anoma.TargetRecall <= 0 || anoma.TargetRecall > 1) errors.Add("Anoma 목표 recall은 0보다 크고 1 이하여야 합니다.");

        return errors;
    }
}

public sealed class TrainingSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsRoot;

    public TrainingSettingsStore(string settingsRoot)
    {
        _settingsRoot = Path.GetFullPath(settingsRoot ?? throw new ArgumentNullException(nameof(settingsRoot)));
    }

    public string LocalSettingsPath => Path.Combine(_settingsRoot, "config", "appsettings.local.json");

    public AppSettings LoadEffectiveSettings() => AppSettingsLoader.LoadOrThrow(
        _settingsRoot,
        requireYoloPython: false,
        requireAnomaPython: false);

    public void SaveTrainingSections(
        AppSettings.YoloTrainingSection yolo,
        AppSettings.AnomaTrainingSection anoma)
    {
        ArgumentNullException.ThrowIfNull(yolo);
        ArgumentNullException.ThrowIfNull(anoma);

        IReadOnlyList<string> errors = TrainingSettingsValidator.Validate(yolo, anoma);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        JsonObject root = LoadLocalRoot();
        root[nameof(AppSettings.YoloTraining)] = JsonSerializer.SerializeToNode(yolo, JsonOptions);
        root[nameof(AppSettings.AnomaTraining)] = JsonSerializer.SerializeToNode(anoma, JsonOptions);
        WriteAtomic(root);
    }

    private JsonObject LoadLocalRoot()
    {
        if (!File.Exists(LocalSettingsPath))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(LocalSettingsPath)) as JsonObject
                   ?? throw new InvalidDataException("appsettings.local.json의 최상위 값은 JSON 객체여야 합니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"appsettings.local.json을 읽을 수 없습니다: {LocalSettingsPath}", ex);
        }
    }

    private void WriteAtomic(JsonObject root)
    {
        string? directory = Path.GetDirectoryName(LocalSettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("로컬 설정 폴더를 확인할 수 없습니다.");

        Directory.CreateDirectory(directory);
        string tempPath = LocalSettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, root.ToJsonString(JsonOptions));
            File.Move(tempPath, LocalSettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
