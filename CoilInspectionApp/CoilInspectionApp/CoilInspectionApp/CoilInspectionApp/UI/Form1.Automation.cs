using CoilInspectionApp.Automation;
using CoilInspectionApp.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoilInspectionApp
{
    public partial class Form1
    {
        private readonly AutomationSettingsStore _automationSettingsStore = new AutomationSettingsStore();
        private AutomationSettings _automationSettings = new AutomationSettings();
        private ModelActivationReconciler _modelActivationReconciler;
        private FileSystemWatcher _activationRequestWatcher;
        private System.Threading.Timer _automationDebounceTimer;
        private readonly Timer _automationPeriodicTimer = new Timer();
        private bool _automationReconcileRunning;
        private bool _isApplyingModel;
        private string _lastClosedBatchPath = "";
        private StatusStrip _automationStatusStrip;
        private ToolStripStatusLabel _batchDeliveryStatusLabel;
        private ToolStripStatusLabel _modelActivationStatusLabel;
        private ToolStripDropDownButton _automationSettingsButton;
        private readonly List<ToolStripMenuItem> _automationToggleMenuItems = new List<ToolStripMenuItem>();

        private sealed class BatchReceipt
        {
            [JsonProperty("source_path")] public string SourcePath { get; set; }
            [JsonProperty("status")] public string Status { get; set; }
            [JsonProperty("message")] public string Message { get; set; }
            [JsonProperty("recorded_at_utc")] public DateTime RecordedAtUtc { get; set; }
        }

        private void InitializeAutomationSettingsAndUi()
        {
            _automationSettings = _automationSettingsStore.Load();
            try { _automationSettingsStore.Save(_automationSettings); }
            catch (Exception ex) { LogException(ex); }

            _automationStatusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _batchDeliveryStatusLabel = new ToolStripStatusLabel("배치 전달: 자동화 OFF")
            {
                Spring = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            _modelActivationStatusLabel = new ToolStripStatusLabel("모델: 새 요청 없음")
            {
                Spring = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            _automationStatusStrip.Items.Add(_batchDeliveryStatusLabel);
            _automationStatusStrip.Items.Add(new ToolStripSeparator());
            _automationStatusStrip.Items.Add(_modelActivationStatusLabel);
            _automationSettingsButton = new ToolStripDropDownButton
            {
                AutoSize = false,
                Width = 96,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ToolTipText = "새 모델 자동 적용과 공유 폴더를 설정합니다."
            };
            AddAutomationMenuItems(_automationSettingsButton.DropDownItems);
            _automationStatusStrip.Items.Add(_automationSettingsButton);
            Controls.Add(_automationStatusStrip);
            _automationStatusStrip.BringToFront();

            ContextMenuStrip menu = new ContextMenuStrip();
            AddAutomationMenuItems(menu.Items);
            _automationStatusStrip.ContextMenuStrip = menu;
            UpdateAutomationToggleUi();
            string accountHint =
                "기본 경로는 현재 Windows 사용자의 %LOCALAPPDATA%입니다. " +
                "다른 계정으로 실행하면 양쪽에서 접근 가능한 동일한 별도 로컬 경로를 설정하세요.";
            _batchDeliveryStatusLabel.ToolTipText = accountHint;
            _modelActivationStatusLabel.ToolTipText = accountHint;
        }

        private void AddAutomationMenuItems(ToolStripItemCollection items)
        {
            ToolStripMenuItem toggle = new ToolStripMenuItem
            {
                CheckOnClick = true
            };
            _automationToggleMenuItems.Add(toggle);
            toggle.CheckedChanged += delegate { ChangeAutomationEnabled(toggle.Checked); };
            ToolStripMenuItem choose = new ToolStripMenuItem("공유 데이터 폴더 선택...");
            choose.Click += delegate { SelectAutomationExchangeRoot(); };
            ToolStripMenuItem reset = new ToolStripMenuItem("기본 공유 데이터 폴더로 재설정");
            reset.Click += delegate
            {
                if (!CanChangeRuntimePath("공유 데이터 폴더")) return;
                _automationSettings.ExchangeRoot = AutomationPaths.DefaultExchangeRoot;
                _automationSettingsStore.Save(_automationSettings);
                ReconfigureAutomation();
            };
            ToolStripMenuItem reconcile = new ToolStripMenuItem("지금 동기화");
            reconcile.Click += delegate { ScheduleAutomationReconcile(0); };
            items.Add(toggle);
            items.Add(choose);
            items.Add(reset);
            items.Add(new ToolStripSeparator());
            items.Add(reconcile);
        }

        private void ChangeAutomationEnabled(bool enabled)
        {
            if (enabled == _automationSettings.Enabled)
            {
                UpdateAutomationToggleUi();
                return;
            }
            if (!CanChangeRuntimePath("새 모델 자동 적용 설정"))
            {
                UpdateAutomationToggleUi();
                return;
            }

            _automationSettings.Enabled = enabled;
            _automationSettingsStore.Save(_automationSettings);
            UpdateAutomationToggleUi();
            ReconfigureAutomation();
        }

        private void UpdateAutomationToggleUi()
        {
            foreach (ToolStripMenuItem toggle in _automationToggleMenuItems)
            {
                toggle.Checked = _automationSettings.Enabled;
                toggle.Text = "새 모델 자동 적용: " + (_automationSettings.Enabled ? "ON" : "OFF");
            }

            if (_automationSettingsButton != null)
            {
                _automationSettingsButton.Text = "자동 적용 " + (_automationSettings.Enabled ? "ON" : "OFF");
                _automationSettingsButton.ForeColor = _automationSettings.Enabled
                    ? ThemeSuccess
                    : ThemeTextMuted;
            }
        }

        private void InitializeAutomationRuntime()
        {
            _automationDebounceTimer = new System.Threading.Timer(
                delegate { PostToUi(ReconcileAutomationOnUiThread); },
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
            _automationPeriodicTimer.Interval = Math.Max(2000, _automationSettings.ReconcileIntervalSeconds * 1000);
            _automationPeriodicTimer.Tick += delegate { ScheduleAutomationReconcile(0); };
            _automationPeriodicTimer.Start();
            ConfigureActivationWatcher();
            UpdateAutomationConfigurationStatus();
        }

        private void ReconfigureAutomation()
        {
            try
            {
                ApplyAutomationOutputRoot();
                ConfigureActivationWatcher();
                _automationPeriodicTimer.Interval = Math.Max(2000, _automationSettings.ReconcileIntervalSeconds * 1000);
                UpdateAutomationConfigurationStatus();
                ScheduleAutomationReconcile(0);
            }
            catch (Exception ex)
            {
                LogException(ex);
                _modelActivationStatusLabel.Text = "자동화 설정 실패: " + ex.Message;
            }
        }

        private void ApplyAutomationOutputRoot()
        {
            string target = AutomationPaths.Outbox(_automationSettings.ExchangeRoot);
            target = Path.GetFullPath(target);
            if (string.Equals(target, _exportBasePath, StringComparison.OrdinalIgnoreCase)) return;

            Directory.CreateDirectory(target);
            BatchExporter candidate = null;
            if (_servicesInitialized)
            {
                candidate = CreateBatchExporter(target, _packagePath, _config);
                candidate.StartOrResumeBatch();
                if (candidate.HasCurrentItems)
                    throw new InvalidOperationException("대상 출력 경로에 마감되지 않은 배치가 있습니다.");
            }
            _exportBasePath = target;
            if (candidate != null) _batchExporter = candidate;
            if (_statisticsForm != null && !_statisticsForm.IsDisposed) _statisticsForm.Close();
            UpdateStaticUi();
        }

        private void ConfigureActivationWatcher()
        {
            if (_activationRequestWatcher != null)
            {
                _activationRequestWatcher.EnableRaisingEvents = false;
                _activationRequestWatcher.Dispose();
                _activationRequestWatcher = null;
            }
            _modelActivationReconciler = null;
            if (!_automationSettings.Enabled) return;

            AutomationPaths.EnsureLayout(_automationSettings.ExchangeRoot);
            _modelActivationReconciler = new ModelActivationReconciler(_automationSettings.ExchangeRoot);
            _activationRequestWatcher = new FileSystemWatcher(AutomationPaths.Control(_automationSettings.ExchangeRoot), "activation_request.json")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
            };
            _activationRequestWatcher.Created += AutomationWatcherChanged;
            _activationRequestWatcher.Changed += AutomationWatcherChanged;
            _activationRequestWatcher.Renamed += AutomationWatcherChanged;
            _activationRequestWatcher.EnableRaisingEvents = true;
        }

        private void AutomationWatcherChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleAutomationReconcile(500);
        }

        private void ScheduleAutomationReconcile(int delayMilliseconds)
        {
            if (_automationDebounceTimer == null || IsDisposed) return;
            _automationDebounceTimer.Change(Math.Max(0, delayMilliseconds), System.Threading.Timeout.Infinite);
        }

        private void ReconcileAutomationOnUiThread()
        {
            if (_automationReconcileRunning || IsDisposed) return;
            _automationReconcileRunning = true;
            try
            {
                UpdateLatestBatchReceipt();
                if (!_automationSettings.Enabled || !_automationSettings.AutoApplyApprovedModels || _modelActivationReconciler == null)
                    return;

                bool busy = IsRuntimeBusyForActivation();
                if (!busy) _modelActivationStatusLabel.Text = "모델 적용 확인 중...";
                ActivationResult result = _modelActivationReconciler.Reconcile(
                    busy,
                    _packagePath,
                    delegate(string packagePath)
                    {
                        _isApplyingModel = true;
                        try { return ApplyInferencePackageSafely(packagePath); }
                        finally { _isApplyingModel = false; }
                    });
                if (result == null)
                {
                    _modelActivationStatusLabel.Text = "모델: 새 모델 없음";
                }
                else if (string.Equals(result.Status, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    _modelActivationStatusLabel.Text = "모델: 현재 배치 마감 후 적용 예정 · " + result.ModelId;
                }
                else if (string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase))
                {
                    _modelActivationStatusLabel.Text = "모델 적용 완료 · " + result.ModelId;
                }
                else
                {
                    _modelActivationStatusLabel.Text = "모델 적용 실패, 기존 모델 유지 · " + result.Message;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                _modelActivationStatusLabel.Text = "자동화 확인 실패 · " + ex.Message;
            }
            finally
            {
                _automationReconcileRunning = false;
            }
        }

        private bool IsRuntimeBusyForActivation()
        {
            return _isApplyingModel || _isClosingBatch || _isPreprocessing || _isInferring ||
                   _autoInferenceScheduled || _results.Count > 0 || (_batchExporter != null && _batchExporter.HasCurrentItems);
        }

        private void UpdateLatestBatchReceipt()
        {
            string receipts = AutomationPaths.Receipts(_automationSettings.ExchangeRoot);
            if (!Directory.Exists(receipts))
            {
                _batchDeliveryStatusLabel.Text = "배치 전달 대기 · " + Path.GetFileName(_lastClosedBatchPath);
                return;
            }

            BatchReceipt match;
            if (string.IsNullOrWhiteSpace(_lastClosedBatchPath))
            {
                match = Directory.GetFiles(receipts, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(TryReadReceipt)
                    .Where(receipt => receipt != null)
                    .OrderByDescending(receipt => receipt.RecordedAtUtc)
                    .FirstOrDefault();
                if (match == null)
                {
                    _batchDeliveryStatusLabel.Text = "배치 전달 대기";
                    return;
                }
                _lastClosedBatchPath = match.SourcePath ?? "";
            }
            else
            {
                match = Directory.GetFiles(receipts, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(TryReadReceipt)
                    .Where(receipt => receipt != null && string.Equals(
                        Path.GetFullPath(receipt.SourcePath ?? ""),
                        Path.GetFullPath(_lastClosedBatchPath),
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(receipt => receipt.RecordedAtUtc)
                    .FirstOrDefault();
            }
            if (match == null)
                _batchDeliveryStatusLabel.Text = "배치 전달 대기 · " + Path.GetFileName(_lastClosedBatchPath);
            else if (match.Status == "imported" || match.Status == "duplicate")
                _batchDeliveryStatusLabel.Text = "학습 UI 전달 완료 · " + Path.GetFileName(_lastClosedBatchPath);
            else
                _batchDeliveryStatusLabel.Text = "학습 UI 전달 실패 · " + match.Message;
        }

        private static BatchReceipt TryReadReceipt(string path)
        {
            try { return JsonConvert.DeserializeObject<BatchReceipt>(File.ReadAllText(path)); }
            catch { return null; }
        }

        private void UpdateAutomationConfigurationStatus()
        {
            if (!_automationSettings.Enabled)
            {
                _batchDeliveryStatusLabel.Text = "배치 출력: 공유 outbox";
                _modelActivationStatusLabel.Text = "모델 자동 적용: OFF";
                return;
            }
            string trainingRoot = _automationSettingsStore.ReadTrainingExchangeRoot();
            if (!string.IsNullOrWhiteSpace(trainingRoot) &&
                !string.Equals(trainingRoot, _automationSettings.ExchangeRoot, StringComparison.OrdinalIgnoreCase))
            {
                _modelActivationStatusLabel.Text = "경고: 학습 UI ExchangeRoot 불일치 · " + trainingRoot;
            }
            else
            {
                _modelActivationStatusLabel.Text = "모델 자동 적용 대기";
            }
        }

        private void SelectAutomationExchangeRoot()
        {
            if (!CanChangeRuntimePath("공유 데이터 폴더")) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "추론 UI와 학습 UI가 함께 사용할 공유 데이터 폴더 선택",
                SelectedPath = Directory.Exists(_automationSettings.ExchangeRoot)
                    ? _automationSettings.ExchangeRoot
                    : AutomationPaths.DefaultExchangeRoot,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                _automationSettings.ExchangeRoot = Path.GetFullPath(dialog.SelectedPath);
                _automationSettingsStore.Save(_automationSettings);
                ReconfigureAutomation();
            }
        }

        private void DisposeAutomation()
        {
            _automationPeriodicTimer.Stop();
            _automationPeriodicTimer.Dispose();
            if (_activationRequestWatcher != null) _activationRequestWatcher.Dispose();
            if (_automationDebounceTimer != null) _automationDebounceTimer.Dispose();
            if (_automationStatusStrip != null && _automationStatusStrip.ContextMenuStrip != null)
                _automationStatusStrip.ContextMenuStrip.Dispose();
            if (_automationStatusStrip != null) _automationStatusStrip.Dispose();
        }
    }
}
