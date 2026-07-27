using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Automation;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace CoilTrainingUI;

public partial class ModelManagerWindow : Window
{
    private readonly ModelRegistryService _registry;
    private readonly ObservableCollection<ModelRegistryEntry> _models = new();
    private readonly AutomationSettings _automationSettings;
    private readonly ActivationRequestService _activationRequests;

    public ModelManagerWindow(ModelRegistryService registry, AutomationSettings? automationSettings = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _automationSettings = (automationSettings ?? new AutomationSettings()).Normalize();
        _activationRequests = new ActivationRequestService(_automationSettings.ExchangeRoot);
        InitializeComponent();
        ModelsGrid.ItemsSource = _models;
        RegistryPathText.Text = _registry.RegistryPath;
        RefreshModels();
        RefreshActivationStatus();
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
        ModelRegistryEntry? active = _models.FirstOrDefault(model => model.IsActive);
        CurrentModelText.Text = active == null
            ? "현재 적용 모델: 등록되지 않음"
            : $"현재 적용 모델: {active.ModelsText}  ·  {active.Id}";
        CurrentModelText.ToolTip = active?.InferencePackageDirectory;
        UpdateActionStates();
    }

    private ModelRegistryEntry RequireSelection()
    {
        return Selected ?? throw new InvalidOperationException("모델을 먼저 선택하세요.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            RefreshModels();
            RefreshActivationStatus();
        });
    }

    private void ModelsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionStates();

    private void UpdateActionStates()
    {
        if (!IsInitialized)
            return;

        ModelRegistryEntry? selected = Selected;
        bool hasSelection = selected != null;
        bool hasReference = _models.Any(model =>
            string.Equals(model.Status, ModelLifecycleStatus.Reference, StringComparison.OrdinalIgnoreCase));
        bool canApply = selected != null &&
                        selected.HasInferencePackage &&
                        string.Equals(
                            selected.PipelineMode,
                            InferencePipelineConfigBuilder.AnomaThenYolo,
                            StringComparison.OrdinalIgnoreCase);

        CompareModelButton.IsEnabled = hasSelection && hasReference;
        CompareModelButton.ToolTip = hasReference
            ? "선택 모델을 비교 기준 모델과 비교합니다."
            : "더보기에서 비교 기준 모델을 먼저 지정하세요.";
        ApplyModelButton.IsEnabled = canApply;
        ApplyModelButton.ToolTip = canApply
            ? "릴리스 발행·검증 후 추론 UI에 적용을 요청합니다."
            : selected?.HasInferencePackage == false
                ? "모델 파일을 찾을 수 없습니다."
                : "Anoma → YOLO 전체 파이프라인 모델만 운영 적용할 수 있습니다.";
        // 가져오기는 모델이 하나도 없을 때도 사용할 수 있어야 합니다.
        MoreActionsButton.IsEnabled = true;
    }

    private void MoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsButton.ContextMenu == null)
            return;

        MoreActionsButton.ContextMenu.PlacementTarget = MoreActionsButton;
        MoreActionsButton.ContextMenu.Placement = PlacementMode.Bottom;
        MoreActionsButton.ContextMenu.IsOpen = true;
    }

    private async void ImportExistingPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "현재 추론 UI가 사용하는 InferencePackage 폴더 선택",
            Multiselect = false
        };
        string likelyPackage = FindLikelyCurrentInferencePackage();
        if (Directory.Exists(likelyPackage))
            dialog.InitialDirectory = likelyPackage;
        if (dialog.ShowDialog(this) != true)
            return;

        if (MessageBox.Show(
                $"현재 운영 패키지로 등록할 폴더:\n{dialog.FolderName}\n\n" +
                "패키지를 검증한 뒤 학습 모델 관리 폴더에 복사하고 현재 적용 모델로 표시합니다. 계속할까요?",
                "기존 운영 패키지 가져오기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var progress = new OperationProgressWindow("기존 운영 패키지 가져오기") { Owner = this };
        progress.UpdateProgress(0, "패키지 검증 및 복사 중...", isIndeterminate: true,
            detail: "대용량 Anoma 모델 때문에 몇 분 걸릴 수 있습니다.");
        IsEnabled = false;
        progress.Show();
        try
        {
            string importedRoot = GetImportedModelsRoot();
            ExistingPackageImportResult result = await Task.Run(() =>
                new ExistingInferencePackageImportService(importedRoot, _registry)
                    .ImportCurrentOperationalPackage(dialog.FolderName));
            progress.Close();
            RefreshModels(result.Model.Id);
            MessageBox.Show(
                $"기존 운영 패키지를 {(result.AlreadyImported ? "확인" : "가져오기")}했습니다.\n\n" +
                $"모델: {result.Model.Id}\n" +
                "이 모델은 현재 적용으로 표시되며 이후 운영 적용 버튼으로 다시 롤백할 수 있습니다.",
                "운영 모델 등록 완료",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            progress.Close();
            MessageBox.Show(
                "기존 운영 패키지를 가져오지 못했습니다.\n" + ex.Message,
                "운영 모델 등록",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            Activate();
        }
    }

    private string GetImportedModelsRoot()
    {
        string registryDirectory = Path.GetDirectoryName(_registry.RegistryPath)
                                   ?? throw new InvalidOperationException("모델 레지스트리 폴더를 확인할 수 없습니다.");
        string trainingInbox = Directory.GetParent(registryDirectory)?.FullName
                               ?? throw new InvalidOperationException("training_inbox 폴더를 확인할 수 없습니다.");
        return Path.Combine(trainingInbox, "_imported_models");
    }

    private string FindLikelyCurrentInferencePackage()
    {
        string importedRoot = GetImportedModelsRoot();
        DirectoryInfo? trainingUiRoot = Directory.GetParent(Directory.GetParent(importedRoot)?.FullName ?? "");
        string repositoryRoot = trainingUiRoot?.Parent?.FullName ?? "";
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return "";

        string inspectionProject = Path.Combine(
            repositoryRoot,
            "CoilInspectionApp", "CoilInspectionApp", "CoilInspectionApp", "CoilInspectionApp");
        string[] candidates =
        {
            Path.Combine(inspectionProject, "bin", "x64", "Debug", "InferencePackage"),
            Path.Combine(inspectionProject, "bin", "x64", "Release", "InferencePackage"),
            Path.Combine(inspectionProject, "InferencePackage")
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? "";
    }

    private void RequestActivation_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            if (!_automationSettings.Enabled)
                throw new InvalidOperationException("Automation 메뉴에서 로컬 자동화를 먼저 켜세요.");
            ModelRegistryEntry selected = RequireSelection();
            if (!string.Equals(selected.PipelineMode, InferencePipelineConfigBuilder.AnomaThenYolo, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("전체 Anoma → YOLO 파이프라인 모델만 운영 적용할 수 있습니다.");
            if (!selected.HasInferencePackage)
                throw new DirectoryNotFoundException("선택한 모델의 추론 패키지를 찾을 수 없습니다.");
            if (MessageBox.Show(
                    $"모델 {selected.Id}를 운영에 적용할까요?\n\n" +
                    "릴리스를 발행·검증한 뒤 추론 UI에 적용을 요청합니다. " +
                    "추론 UI의 현재 배치가 비어 있을 때만 적용되며, 실패하면 기존 모델을 유지합니다.",
                    "운영 적용",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            ModelPublishResult publish = new ModelReleasePublisher(
                _automationSettings.ExchangeRoot).Publish(selected);
            ActivationRequest request = _activationRequests.Create(selected.Id);
            ActivationStatusText.Text =
                $"{(publish.AlreadyPublished ? "릴리스 검증" : "릴리스 발행")} 완료 · " +
                $"적용 요청 대기: {request.ModelId}";
            CancelActivationButton.Visibility = Visibility.Visible;
        });
    }

    private void PublishRelease_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            if (!_automationSettings.Enabled)
                throw new InvalidOperationException("Automation 메뉴에서 로컬 자동화를 먼저 켜세요.");
            ModelRegistryEntry selected = RequireSelection();
            ModelPublishResult result = new ModelReleasePublisher(_automationSettings.ExchangeRoot).Publish(selected);
            ActivationStatusText.Text = result.AlreadyPublished
                ? $"릴리스 검증 완료: {selected.Id}"
                : $"릴리스 발행 완료: {selected.Id}";
        });
    }

    private void CancelActivation_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            if (MessageBox.Show(
                    "현재 대기 중인 운영 적용 요청을 취소할까요?",
                    "적용 요청 취소",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _activationRequests.CancelPending(out string message);
            ActivationStatusText.Text = message;
            RefreshActivationStatus();
        });
    }

    private void RefreshActivationStatus()
    {
        try
        {
            ActivationRequest? request = _activationRequests.TryReadRequest();
            ActivationResult? result = _activationRequests.TryReadResult();
            if (request == null)
            {
                ActivationStatusText.Text = $"운영 적용 요청 없음 · ExchangeRoot: {_automationSettings.ExchangeRoot}";
                CancelActivationButton.Visibility = Visibility.Collapsed;
                return;
            }
            bool matchingResult = result != null &&
                                  string.Equals(result.RequestId, request.RequestId, StringComparison.OrdinalIgnoreCase);
            bool pending = !matchingResult ||
                           string.Equals(result!.Status, "pending", StringComparison.OrdinalIgnoreCase) ||
                           (!string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase));
            CancelActivationButton.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            if (matchingResult && result != null)
                ActivationStatusText.Text = $"{request.ModelId} · {result.Status}: {result.Message}";
            else
                ActivationStatusText.Text = $"{request.ModelId} · 적용 요청 대기";
        }
        catch (Exception ex)
        {
            CancelActivationButton.Visibility = Visibility.Collapsed;
            ActivationStatusText.Text = "적용 상태 확인 실패: " + ex.Message;
        }
    }

    private void SetReference_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() =>
        {
            ModelRegistryEntry selected = RequireSelection();
            if (MessageBox.Show(
                    $"{selected.Id}\n\n이 모델을 성능 비교 기준으로 지정할까요?\n추론 UI의 운영 모델은 변경되지 않습니다.",
                    "비교 기준 지정",
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
                MessageBox.Show("현재 비교 기준 모델이 없습니다. 더보기에서 비교 기준을 먼저 지정하세요.");
                return;
            }

            string message =
                $"선택: {selected.Id}\n비교 기준: {reference.Id}\n\n" +
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
            string backupText = string.IsNullOrWhiteSpace(result.BackupDirectory)
                ? "기존 패키지 없음"
                : result.BackupDirectory;
            MessageBox.Show(
                $"추론 패키지 배포를 완료했습니다.\n\n대상: {result.TargetDirectory}\n백업: {backupText}\n\n" +
                "이 기능은 장애 복구용 직접 배포이며 자동 적용 상태와는 별개입니다.",
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
