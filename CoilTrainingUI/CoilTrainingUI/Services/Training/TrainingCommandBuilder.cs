using System;
using System.Globalization;

namespace CoilTrainingUI.Services;

public static class TrainingCommandBuilder
{
    public static string BuildYoloWorkspaceArgs(
        AppSettings settings,
        string rawRoot,
        string workspaceRoot)
    {
        string args =
            $"--raw-root \"{rawRoot}\" " +
            $"--out-root \"{workspaceRoot}\" " +
            $"--train-ratio {Invariant(settings.Workspace.TrainRatio)} " +
            $"--seed {settings.Workspace.Seed}";

        if (settings.Workspace.YoloMaxBackground.HasValue)
            args += $" --max-background {settings.Workspace.YoloMaxBackground.Value}";

        string oversampleClass = settings.Workspace.YoloOversampleClass?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(oversampleClass))
        {
            args += $" --oversample-class \"{oversampleClass}\"";
            args += $" --oversample-factor {Invariant(settings.Workspace.YoloOversampleFactor)}";
        }

        string augmentClass = settings.Workspace.YoloAugmentClass?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(augmentClass))
        {
            args += $" --augment-class \"{augmentClass}\"";
            args += $" --augment-factor {Invariant(settings.Workspace.YoloAugmentFactor)}";
        }

        return args;
    }

    public static string BuildAnomaArgs(
        AppSettings settings,
        string workspaceRoot,
        string outRoot,
        string datasetName)
    {
        var config = settings.AnomaTraining;
        return
            $"--workspace \"{workspaceRoot}\" " +
            $"--out \"{outRoot}\" " +
            $"--dataset-name \"{datasetName}\" " +
            $"--model \"{config.Model}\" " +
            $"--image-size {config.ImageSize} " +
            $"--batch-size {config.Batch} " +
            $"--device \"{config.Device}\" " +
            $"--seed {config.Seed} " +
            $"--dinomaly-encoder \"{config.Encoder}\" " +
            $"--dinomaly-dropout {Invariant(config.Dropout)} " +
            $"--dinomaly-decoder-depth {config.DecoderDepth} " +
            $"--dinomaly-max-steps {config.MaxSteps} " +
            $"--dinomaly-learning-rate {Invariant(config.LearningRate)} " +
            $"--target-recall {Invariant(config.TargetRecall)}";
    }

    public static string BuildYoloArgs(
        AppSettings settings,
        string workspaceRoot,
        string outRoot,
        bool fineTune,
        string? fineTuneWeightsPath)
    {
        var config = settings.YoloTraining;
        string model = fineTune
            ? fineTuneWeightsPath ?? throw new ArgumentException("Fine-tune weights are required.")
            : config.Model;
        int epochs = fineTune ? config.FineTuneEpochs : config.Epochs;

        string args =
            $"--workspace \"{workspaceRoot}\" " +
            $"--out \"{outRoot}\" " +
            $"--model \"{model}\" " +
            $"--epochs {epochs} " +
            $"--imgsz {config.ImageSize} " +
            $"--batch {config.Batch} " +
            $"--device \"{config.Device}\" " +
            $"--seed {config.Seed}";

        if (fineTune)
            args += $" --lr0 {Invariant(config.FineTuneLearningRate)}";

        return args;
    }

    private static string Invariant(double value) =>
        value.ToString("0.#################", CultureInfo.InvariantCulture);
}
