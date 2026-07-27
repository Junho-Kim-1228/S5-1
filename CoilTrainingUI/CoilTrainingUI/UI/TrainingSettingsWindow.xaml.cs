using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CoilTrainingUI;

public partial class TrainingSettingsWindow : Window
{
    private readonly TrainingSettingsStore _store;

    public TrainingSettingsWindow(TrainingSettingsStore store, AppSettings settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        Populate(settings.YoloTraining, settings.AnomaTraining);
    }

    private void Populate(
        AppSettings.YoloTrainingSection yolo,
        AppSettings.AnomaTrainingSection anoma)
    {
        YoloModelTextBox.Text = yolo.Model;
        YoloEpochsTextBox.Text = Format(yolo.Epochs);
        YoloFineTuneEpochsTextBox.Text = Format(yolo.FineTuneEpochs);
        YoloFineTuneLearningRateTextBox.Text = Format(yolo.FineTuneLearningRate);
        YoloImageSizeTextBox.Text = Format(yolo.ImageSize);
        YoloBatchTextBox.Text = Format(yolo.Batch);
        SetComboText(YoloDeviceComboBox, yolo.Device);
        YoloSeedTextBox.Text = Format(yolo.Seed);

        AnomaImageSizeTextBox.Text = Format(anoma.ImageSize);
        AnomaBatchTextBox.Text = Format(anoma.Batch);
        SetComboText(AnomaDeviceComboBox, anoma.Device);
        AnomaSeedTextBox.Text = Format(anoma.Seed);
        SetComboText(AnomaEncoderComboBox, anoma.Encoder);
        AnomaDropoutTextBox.Text = Format(anoma.Dropout);
        AnomaDecoderDepthTextBox.Text = Format(anoma.DecoderDepth);
        AnomaMaxStepsTextBox.Text = Format(anoma.MaxSteps);
        AnomaLearningRateTextBox.Text = Format(anoma.LearningRate);
        AnomaTargetRecallTextBox.Text = Format(anoma.TargetRecall);
    }

    private void ApplyDemoPreset_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.YoloTrainingSection yolo = ReadYoloOrDefaults();
        AppSettings.AnomaTrainingSection anoma = ReadAnomaOrDefaults();
        yolo.Epochs = 1;
        yolo.FineTuneEpochs = 1;
        anoma.ImageSize = 224;
        anoma.Batch = 1;
        anoma.DecoderDepth = 8;
        anoma.MaxSteps = 5;
        Populate(yolo, anoma);
        PresetStatusTextBlock.Text = "시연용: YOLO 1 epoch · Anoma 224px / batch 1 / 5 steps (성능 평가용 아님)";
    }

    private void ApplyProductionPreset_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings();
        Populate(settings.YoloTraining, settings.AnomaTraining);
        PresetStatusTextBlock.Text = "운영 기본값: YOLO 100 epoch · Anoma 448px / batch 4 / 5000 steps";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppSettings.YoloTrainingSection yolo = ReadYolo();
            AppSettings.AnomaTrainingSection anoma = ReadAnoma();
            IReadOnlyList<string> errors = TrainingSettingsValidator.Validate(yolo, anoma);
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, errors.Select(error => "• " + error)),
                    "학습 설정 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _store.SaveTrainingSections(yolo, anoma);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(ex.Message, "학습 설정 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "학습 설정을 저장하지 못했습니다.\n" + ex.Message,
                "학습 설정",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private AppSettings.YoloTrainingSection ReadYoloOrDefaults()
    {
        try { return ReadYolo(); }
        catch (FormatException) { return new AppSettings.YoloTrainingSection(); }
    }

    private AppSettings.AnomaTrainingSection ReadAnomaOrDefaults()
    {
        try { return ReadAnoma(); }
        catch (FormatException) { return new AppSettings.AnomaTrainingSection(); }
    }

    private AppSettings.YoloTrainingSection ReadYolo() => new()
    {
        Model = YoloModelTextBox.Text.Trim(),
        Epochs = ParseInt(YoloEpochsTextBox, "YOLO epoch"),
        FineTuneEpochs = ParseInt(YoloFineTuneEpochsTextBox, "YOLO 파인튜닝 epoch"),
        FineTuneLearningRate = ParseDouble(YoloFineTuneLearningRateTextBox, "YOLO 파인튜닝 학습률"),
        ImageSize = ParseInt(YoloImageSizeTextBox, "YOLO 이미지 크기"),
        Batch = ParseInt(YoloBatchTextBox, "YOLO batch"),
        Device = GetComboText(YoloDeviceComboBox),
        Seed = ParseInt(YoloSeedTextBox, "YOLO seed")
    };

    private AppSettings.AnomaTrainingSection ReadAnoma() => new()
    {
        Model = "dinomaly",
        ImageSize = ParseInt(AnomaImageSizeTextBox, "Anoma 이미지 크기"),
        Batch = ParseInt(AnomaBatchTextBox, "Anoma batch"),
        Device = GetComboText(AnomaDeviceComboBox),
        Seed = ParseInt(AnomaSeedTextBox, "Anoma seed"),
        Encoder = GetComboText(AnomaEncoderComboBox),
        Dropout = ParseDouble(AnomaDropoutTextBox, "Anoma dropout"),
        DecoderDepth = ParseInt(AnomaDecoderDepthTextBox, "Anoma decoder depth"),
        MaxSteps = ParseInt(AnomaMaxStepsTextBox, "Anoma max steps"),
        LearningRate = ParseDouble(AnomaLearningRateTextBox, "Anoma 학습률"),
        TargetRecall = ParseDouble(AnomaTargetRecallTextBox, "Anoma 목표 recall")
    };

    private static int ParseInt(TextBox textBox, string label)
    {
        if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value;
        throw new FormatException($"{label}에 정수를 입력하세요.");
    }

    private static double ParseDouble(TextBox textBox, string label)
    {
        string text = textBox.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return value;
        throw new FormatException($"{label}에 숫자를 입력하세요.");
    }

    private static string GetComboText(ComboBox comboBox) =>
        (comboBox.Text ?? "").Trim();

    private static void SetComboText(ComboBox comboBox, string value) =>
        comboBox.Text = value ?? "";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);
}
