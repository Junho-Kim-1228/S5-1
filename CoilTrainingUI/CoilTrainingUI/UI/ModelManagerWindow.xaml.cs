using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace CoilTrainingUI;

public partial class ModelManagerWindow : Window
{
    private readonly ModelRegistryService _registry;
    private readonly ObservableCollection<ModelRegistryEntry> _models = new();

    public ModelManagerWindow(ModelRegistryService registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        InitializeComponent();
        ModelsGrid.ItemsSource = _models;
        RegistryPathText.Text = _registry.RegistryPath;
        RefreshModels();
    }

    public ModelRegistryEntry? RequestedFineTuneModel { get; private set; }

    private ModelRegistryEntry? Selected => ModelsGrid.SelectedItem as ModelRegistryEntry;

    private void RefreshModels(string? selectedId = null)
    {
        selectedId ??= Selected?.Id;
        _models.Clear();
        foreach (ModelRegistryEntry model in _registry.Load())
            _models.Add(model);

        ModelsGrid.SelectedItem = _models.FirstOrDefault(model =>
            string.Equals(model.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (ModelsGrid.SelectedItem == null && _models.Count > 0)
            ModelsGrid.SelectedIndex = 0;
    }

    private ModelRegistryEntry RequireSelection()
    {
        return Selected ?? throw new InvalidOperationException("모델을 먼저 선택하세요.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() => RefreshModels());
    }

    private void SetReference_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            if (MessageBox.Show(
                    $"{selected.Id}\n\n이 모델을 성능 비교용 대표 모델로 지정할까요?\n추론 UI의 파일은 변경되지 않습니다.",
                    "대표 모델 지정",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _registry.SetReference(selected.Id);
            RefreshModels(selected.Id);
        });
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            _registry.Archive(selected.Id);
            RefreshModels(selected.Id);
        });
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            ModelRegistryEntry? reference = _models.FirstOrDefault(model =>
                string.Equals(model.Status, ModelLifecycleStatus.Reference, StringComparison.OrdinalIgnoreCase));
            if (reference == null)
            {
                MessageBox.Show("현재 대표 모델이 없습니다. 먼저 비교 기준 모델을 지정하세요.");
                return;
            }

            string message =
                $"선택: {selected.Id}\n대표: {reference.Id}\n\n" +
                $"YOLO mAP50 차이: {Difference(selected.YoloMap50, reference.YoloMap50)}\n" +
                $"YOLO mAP50-95 차이: {Difference(selected.YoloMap5095, reference.YoloMap5095)}\n" +
                $"YOLO Precision 차이: {Difference(selected.YoloPrecision, reference.YoloPrecision)}\n" +
                $"YOLO Recall 차이: {Difference(selected.YoloRecall, reference.YoloRecall)}\n\n" +
                $"Anoma AUROC 차이: {Difference(selected.AnomaAuroc, reference.AnomaAuroc)}\n" +
                $"Anoma F1 차이: {Difference(selected.AnomaF1, reference.AnomaF1)}";
            MessageBox.Show(message, "모델 성능 비교", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void Deploy_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            var dialog = new OpenFolderDialog
            {
                Title = "교체할 추론 UI의 InferencePackage 폴더 선택",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
                return;

            if (MessageBox.Show(
                    $"선택 모델: {selected.Id}\n배포 대상: {dialog.FolderName}\n\n" +
                    "기존 InferencePackage는 같은 위치에 백업한 뒤 교체합니다. 계속할까요?",
                    "추론 패키지 배포",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var deployment = new InferencePackageDeploymentService();
            InferencePackageDeploymentResult result = deployment.Deploy(
                selected.InferencePackageDirectory,
                dialog.FolderName);
            _registry.SetReference(selected.Id);
            RefreshModels(selected.Id);

            string backupText = string.IsNullOrWhiteSpace(result.BackupDirectory)
                ? "기존 패키지 없음"
                : result.BackupDirectory;
            MessageBox.Show(
                $"추론 패키지 배포를 완료했습니다.\n\n대상: {result.TargetDirectory}\n백업: {backupText}\n\n" +
                "추론 UI가 실행 중이었다면 재시작하세요.",
                "배포 완료",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private void FineTune_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            if (!selected.HasYoloCheckpoint)
                throw new FileNotFoundException("선택한 모델의 YOLO best.pt를 찾을 수 없습니다.", selected.YoloBestPtPath);

            RequestedFineTuneModel = selected;
            DialogResult = true;
            Close();
        });
    }

    private void OpenPackage_Click(object sender, RoutedEventArgs e) =>
        TryAction(() => OpenFolder(RequireSelection().InferencePackageDirectory));

    private void OpenRun_Click(object sender, RoutedEventArgs e) =>
        TryAction(() => OpenFolder(RequireSelection().RunDirectory));

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Difference(double? candidate, double? reference) =>
        candidate.HasValue && reference.HasValue
            ? (candidate.Value - reference.Value).ToString("+0.0000;-0.0000;0.0000")
            : "-";

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {path}");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static void TryAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "모델 관리", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
