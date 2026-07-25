using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Automation;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace CoilTrainingUI;

public partial class MainWindow
{
    private readonly TrainingAutomationSettingsStore _automationSettingsStore = new();
    private AutomationSettings _automationSettings = new();
    private TrainingAutomationCoordinator? _automationCoordinator;
    private BatchManagerWindow? _openBatchManager;
    private string _lastModelPublishStatus = "발행 이력 없음";

    private void InitializeAutomation()
    {
        try
        {
            AppSettings appSettings = AppSettingsLoader.LoadOrThrow(
                FindProjectRoot("capstone_design"),
                requireYoloPython: false,
                requireAnomaPython: false);
            _automationSettings = _automationSettingsStore.Load(appSettings.Automation);
            _automationSettingsStore.Save(_automationSettings);
            UpdateAutomationMenuAndStatus();
            LoadLatestModelPublishStatus();
            RestartAutomationCoordinator();
        }
        catch (Exception ex)
        {
            AutomationStatusText.Text = "자동화 설정 오류: " + ex.Message;
            AutomationStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void RestartAutomationCoordinator()
    {
        _automationCoordinator?.Dispose();
        _automationCoordinator = null;
        if (!_automationSettings.Enabled)
        {
            AutomationStatusText.Text = "배치 자동 가져오기 OFF";
            return;
        }

        AutomationPaths.EnsureLayout(_automationSettings.ExchangeRoot);
        string libraryRoot = GetTrainingInboxRoot();
        ModelRegistryService registry = CreateModelRegistryService();
        var requests = new ActivationRequestService(_automationSettings.ExchangeRoot);
        _automationCoordinator = new TrainingAutomationCoordinator(
            _automationSettings,
            new BatchInboxReconciler(_automationSettings.ExchangeRoot, libraryRoot),
            new ActivationResultSynchronizer(requests, registry));
        _automationCoordinator.Updated += AutomationCoordinator_Updated;
        _automationCoordinator.Start();
        AutomationStatusText.Text = "배치 자동 가져오기 ON · 검색 중";
    }

    private void AutomationCoordinator_Updated(object? sender, TrainingAutomationUpdate update)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (update.Error != null)
            {
                AutomationStatusText.Text = "자동화 오류: " + update.Error.Message;
                AutomationStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            AutomationStatusText.Foreground = System.Windows.Media.Brushes.White;
            BatchReconcileResult? batch = update.BatchResult;
            if (batch != null)
            {
                string time = batch.CheckedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                AutomationStatusText.Text = batch.ImportedCount > 0
                    ? $"새 배치 {batch.ImportedCount}개 도착 · {time}"
                    : $"배치 검색 {time} · {batch.LastMessage}";
                if (batch.ImportedCount > 0)
                {
                    string? selectedPath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
                    string? selectedBatchRoot = _currentBatchRoot;
                    RefreshAllImagesFromTrainingInbox(selectedPath, selectedBatchRoot);
                    _openBatchManager?.RefreshFromAutomation();
                }
            }

            ActivationResult? activation = update.ActivationResult;
            if (activation != null)
            {
                ModelActivationStatusText.Text = activation.Status switch
                {
                    "applied" => $"모델 적용 완료: {activation.ModelId}",
                    "pending" => $"모델 적용 대기: {activation.ModelId}",
                    "failed" => $"모델 적용 실패: {activation.Message}",
                    _ => $"모델 적용 상태: {activation.Status}"
                };
            }
            else if (_automationSettings.Enabled)
            {
                try
                {
                    ActivationRequest? pendingRequest = new ActivationRequestService(
                        _automationSettings.ExchangeRoot).TryReadRequest();
                    ModelActivationStatusText.Text = pendingRequest == null
                        ? "모델 적용 요청 없음"
                        : $"모델 적용 요청 대기: {pendingRequest.ModelId}";
                }
                catch (Exception ex)
                {
                    ModelActivationStatusText.Text = "모델 적용 상태 확인 실패: " + ex.Message;
                }
            }
        });
    }

    private async Task ReconcileAutomationNowAsync()
    {
        if (_automationCoordinator != null)
            await _automationCoordinator.ReconcileNowAsync();
    }

    private async void AutomationReconcileNow_Click(object sender, RoutedEventArgs e)
        => await ReconcileAutomationNowAsync();

    private void AutomationEnabled_Click(object sender, RoutedEventArgs e)
    {
        _automationSettings.Enabled = AutomationEnabledMenuItem.IsChecked;
        _automationSettingsStore.Save(_automationSettings);
        UpdateAutomationMenuAndStatus();
        RestartAutomationCoordinator();
    }

    private void SelectExchangeRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "추론 UI와 함께 사용할 공유 데이터 폴더 선택",
            InitialDirectory = Directory.Exists(_automationSettings.ExchangeRoot)
                ? _automationSettings.ExchangeRoot
                : AutomationPaths.DefaultExchangeRoot,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _automationSettings.ExchangeRoot = Path.GetFullPath(dialog.FolderName);
        _automationSettingsStore.Save(_automationSettings);
        UpdateAutomationMenuAndStatus();
        RestartAutomationCoordinator();
    }

    private void ResetExchangeRoot_Click(object sender, RoutedEventArgs e)
    {
        _automationSettings.ExchangeRoot = AutomationPaths.DefaultExchangeRoot;
        _automationSettingsStore.Save(_automationSettings);
        UpdateAutomationMenuAndStatus();
        RestartAutomationCoordinator();
    }

    private void UpdateAutomationMenuAndStatus()
    {
        AutomationEnabledMenuItem.IsChecked = _automationSettings.Enabled;
        ExchangeRootMenuItem.Header = "공유 데이터 폴더: " + _automationSettings.ExchangeRoot;
        AutomationStatusText.ToolTip =
            "기본 ExchangeRoot는 현재 Windows 사용자 계정의 %LOCALAPPDATA%입니다. " +
            "두 앱은 같은 계정으로 실행하거나, 양쪽에서 접근 가능한 동일한 별도 로컬 경로를 설정해야 합니다.";

        string? inspectionRoot = _automationSettingsStore.ReadInspectionExchangeRoot();
        bool mismatch = !string.IsNullOrWhiteSpace(inspectionRoot) &&
                        !string.Equals(inspectionRoot, _automationSettings.ExchangeRoot, StringComparison.OrdinalIgnoreCase);
        AutomationPathWarningText.Text = mismatch
            ? $"경고: 추론 UI ExchangeRoot 불일치 ({inspectionRoot})"
            : "";
    }

    private ModelPublishResult? TryAutoPublishModel(ModelRegistryEntry entry, AutomationSection configured)
    {
        _automationSettings = _automationSettingsStore.Load(configured);
        UpdateAutomationMenuAndStatus();
        if (!_automationSettings.Enabled || !_automationSettings.AutoPublishModels ||
            !string.Equals(entry.PipelineMode, InferencePipelineConfigBuilder.AnomaThenYolo, StringComparison.OrdinalIgnoreCase))
        {
            _lastModelPublishStatus = "자동 발행 제외: 전체 파이프라인 또는 자동화 설정 확인";
            ModelPublishStatusText.Text = _lastModelPublishStatus;
            return null;
        }

        try
        {
            ModelPublishResult published = new ModelReleasePublisher(_automationSettings.ExchangeRoot).Publish(entry);
            _lastModelPublishStatus = published.AlreadyPublished
                ? $"모델 발행 확인: {entry.Id}"
                : $"모델 발행 완료: {entry.Id}";
            ModelPublishStatusText.Text = _lastModelPublishStatus;
            return published;
        }
        catch (Exception ex)
        {
            _lastModelPublishStatus = "모델 발행 실패: " + ex.Message;
            ModelPublishStatusText.Text = _lastModelPublishStatus;
            return null;
        }
    }

    private void LoadLatestModelPublishStatus()
    {
        try
        {
            string releasesRoot = AutomationPaths.Releases(_automationSettings.ExchangeRoot);
            string? latest = Directory.Exists(releasesRoot)
                ? Directory.GetFiles(releasesRoot, "release.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            ModelPublishStatusText.Text = latest == null
                ? "모델 발행 이력 없음"
                : "마지막 모델 발행: " + Path.GetFileName(Path.GetDirectoryName(latest));
        }
        catch (Exception ex)
        {
            ModelPublishStatusText.Text = "모델 발행 상태 확인 실패: " + ex.Message;
        }
    }
}
