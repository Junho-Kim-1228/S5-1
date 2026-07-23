using CoilInspectionApp.Interface;
using CoilInspectionApp.Logging;
using CoilInspectionApp.Configuration;
using CoilInspectionApp.Preprocess;
using CoilInspectionApp.UI;
using CoilInspectionApp.Watcher;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CoilInspectionApp
{
    public partial class Form1 : Form
    {
        private DirectoryWatcher _dw;
        private readonly CsvLogger _logger = new CsvLogger();
        private OnnxModelTester _modelTester = new OnnxModelTester();
        private readonly List<InspectionResultViewModel> _results = new List<InspectionResultViewModel>();
        private readonly object _batchExporterLock = new object();
        private readonly ContextMenuStrip _resultContextMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _retryPreprocessMenuItem = new ToolStripMenuItem("전처리 재시도");
        private readonly ToolStripMenuItem _retryAllPreprocessMenuItem = new ToolStripMenuItem("전처리 실패 전체 재시도");
        private readonly ToolStripMenuItem _retryInferenceMenuItem = new ToolStripMenuItem("추론 재시도");
        private readonly RuntimePathSettingsStore _runtimePathSettingsStore = new RuntimePathSettingsStore();
        private StatisticsForm _statisticsForm;
        private BatchExporter _batchExporter;
        private MaskOnnxRunner _maskOnnxRunner;
        private PipelinePackageConfig _config;
        private string _inputPath = "";
        private string _packagePath = "";
        private string _exportBasePath = "";
        private RuntimePathSettings _runtimePathSettings = new RuntimePathSettings();
        private bool _servicesInitialized;
        private volatile bool _isPreprocessing;
        private volatile bool _preprocessAgainRequested;
        private bool _isClosingBatch;
        private bool _isAutoClosePaused;
        private volatile bool _isInferring;
        private bool _autoInferenceScheduled;
        private readonly System.Windows.Forms.Timer _autoCloseTimer = new System.Windows.Forms.Timer();
        private long _lastInputReceivedTicks;
        private System.Drawing.Image _displayImage;
        private double _imageScale = 1.0;
        private double _imageFitScale = 1.0;
        private System.Drawing.PointF _imageOffset = new System.Drawing.PointF(0f, 0f);
        private bool _isImagePanning;
        private System.Drawing.Point _lastPanPoint;

        private const double ImageZoomStep = 0.10;
        private const double ImageMaxScale = 10.0;
        private readonly int _autoCloseIdleSeconds = ResolveAutoCloseIdleSeconds();

        public Form1()
        {
            InitializeComponent();
            buttonZoomIn.BringToFront();
            buttonZoomOut.BringToFront();
            buttonZoomFit.BringToFront();
            InitializeResultContextMenu();
            InitSystem();
            InitializeAutoCloseTimer();
        }

        private void InitializeResultContextMenu()
        {
            var deleteMenuItem = new ToolStripMenuItem("이미지 삭제");
            var retrySeparator = new ToolStripSeparator();
            deleteMenuItem.Click += (sender, args) => DeleteSelectedResult();
            _retryPreprocessMenuItem.Click += (sender, args) => RetrySelectedPreprocess();
            _retryAllPreprocessMenuItem.Click += (sender, args) => RetryAllFailedPreprocess();
            _retryInferenceMenuItem.Click += (sender, args) => RetrySelectedInference();
            _resultContextMenu.Items.Add(_retryPreprocessMenuItem);
            _resultContextMenu.Items.Add(_retryAllPreprocessMenuItem);
            _resultContextMenu.Items.Add(_retryInferenceMenuItem);
            _resultContextMenu.Items.Add(retrySeparator);
            _resultContextMenu.Items.Add(deleteMenuItem);
            _resultContextMenu.Opening += (sender, args) =>
            {
                InspectionResultViewModel selectedResult = GetCurrentSelectedResult();
                args.Cancel = selectedResult == null;
                _retryPreprocessMenuItem.Visible = CanRetryPreprocess(selectedResult);
                int failedPreprocessCount = _results.Count(CanRetryPreprocess);
                _retryAllPreprocessMenuItem.Visible = failedPreprocessCount > 0;
                _retryAllPreprocessMenuItem.Text = $"전처리 실패 전체 재시도 ({failedPreprocessCount}장)";
                _retryInferenceMenuItem.Visible = selectedResult != null
                    && string.Equals(selectedResult.ReasonText, "inference_failed", StringComparison.OrdinalIgnoreCase);
                retrySeparator.Visible = _retryPreprocessMenuItem.Visible
                    || _retryAllPreprocessMenuItem.Visible
                    || _retryInferenceMenuItem.Visible;
            };
            listViewResults.ContextMenuStrip = _resultContextMenu;
        }

        private static bool CanRetryPreprocess(InspectionResultViewModel result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.SourceFilePath) || !File.Exists(result.SourceFilePath))
                return false;

            return string.Equals(result.ReasonText, "preprocess_failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.ReasonText, "mask_not_created", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.ReasonText, "masked_file_locked", StringComparison.OrdinalIgnoreCase);
        }

        private void RetrySelectedPreprocess()
        {
            InspectionResultViewModel result = GetCurrentSelectedResult();
            if (!CanRetryPreprocess(result))
                return;

            ResetForPreprocessRetry(result);
            RefreshResultList(selectFirst: false);
            SaveSessionState();
            StartPreprocessWorkerIfNeeded();
        }

        private void RetryAllFailedPreprocess()
        {
            List<InspectionResultViewModel> failedItems = _results.Where(CanRetryPreprocess).ToList();
            if (failedItems.Count == 0)
                return;

            foreach (InspectionResultViewModel item in failedItems)
                ResetForPreprocessRetry(item);

            RefreshResultList(selectFirst: false);
            SaveSessionState();
            StartPreprocessWorkerIfNeeded();
        }

        private static void ResetForPreprocessRetry(InspectionResultViewModel result)
        {
            result.Stage1 = "미전처리";
            result.Stage2 = "대기";
            result.Final = "수신완료";
            result.ScoreText = "-";
            result.DetectionCount = 0;
            result.Detections = new List<Detection>();
            result.RawImagePath = null;
            result.ProcessedImagePath = null;
            result.ReasonText = "received_waiting_preprocess";
            result.IsPreprocessPending = true;
            result.IsPending = false;
            result.IsInferenceCompleted = false;
        }

        private void RetrySelectedInference()
        {
            InspectionResultViewModel result = GetCurrentSelectedResult();
            if (result == null
                || !string.Equals(result.ReasonText, "inference_failed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result.Stage1 = "대기";
            result.Stage2 = "대기";
            result.Final = "추론대기";
            result.ScoreText = "-";
            result.IsPending = true;
            result.IsInferenceCompleted = false;
            RefreshResultList(selectFirst: false);
            SaveSessionState();
            StartAutomaticInferenceIfNeeded();
        }

        private void InitializeAutoCloseTimer()
        {
            _autoCloseTimer.Interval = 1000;
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            _autoCloseTimer.Start();
        }

        private void InitSystem()
        {
            try
            {
                _runtimePathSettings = _runtimePathSettingsStore.Load();
                _inputPath = ResolveSavedOrConfiguredPath(
                    _runtimePathSettings.InputDirectory,
                    ConfigurationManager.AppSettings["InputDir"],
                    @"C:\InspectionTest\input");
                _packagePath = ResolveSavedPackagePath(
                    _runtimePathSettings.InferencePackageDirectory,
                    ConfigurationManager.AppSettings["InferencePackagePath"],
                    @".\InferencePackage");
                // 사용자가 선택한 출력 경로가 있으면 복원하고,
                // 최초 실행에는 EXE 기준 TrainingBatches 폴더를 사용한다.
                _exportBasePath = ResolveSavedOrConfiguredPath(
                    _runtimePathSettings.ExportBaseDirectory,
                    ConfigurationManager.AppSettings["ExportBasePath"],
                    @".\TrainingBatches");
                Directory.CreateDirectory(_exportBasePath);

                _config = LoadPipelinePackageOrThrow(_packagePath);
                _maskOnnxRunner = LoadMaskOnnxRunnerOrThrow(_packagePath, _config);
                LoadRequiredModelsOrThrow(_modelTester, _packagePath, _config);
                InitializeOperationalServices();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"초기화 오류: {ex.Message}", "CoilInspectionApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateStaticUi();
            }
        }

        private void InitializeOperationalServices()
        {
            if (_servicesInitialized)
                return;

            Directory.CreateDirectory(_inputPath);

            _batchExporter = CreateBatchExporter(_exportBasePath, _packagePath, _config);
            _batchExporter.StartOrResumeBatch();
            RestoreSessionState();

            StartInputWatcher(_inputPath);
            _servicesInitialized = true;
            RegisterExistingInputFiles();

            if (_results.Count > 0 && System.Threading.Interlocked.Read(ref _lastInputReceivedTicks) <= 0)
                MarkInputReceived();
        }

        private void StartInputWatcher(string path)
        {
            var nextWatcher = new DirectoryWatcher();
            nextWatcher.OnFileCreated += filePath =>
            {
                MarkInputReceived();
                PostToUi(() => RegisterIncomingFile(filePath));
            };
            nextWatcher.OnFileDeleted += filePath => PostToUi(() => RemoveIncomingFile(filePath));
            nextWatcher.StartWatch(path);

            DirectoryWatcher previousWatcher = _dw;
            _dw = nextWatcher;
            previousWatcher?.Dispose();
        }

        private void PostToUi(Action action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // 종료 중 들어온 파일 시스템 이벤트는 무시한다.
            }
        }

        private void MarkInputReceived()
        {
            System.Threading.Interlocked.Exchange(ref _lastInputReceivedTicks, DateTime.Now.Ticks);
        }

        private void RegisterExistingInputFiles()
        {
            string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
            foreach (string filePath in Directory.GetFiles(_inputPath)
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                RegisterIncomingFile(filePath);
            }
        }

        private static string ResolveConfiguredPath(string configuredValue, string fallbackValue)
        {
            string value = string.IsNullOrWhiteSpace(configuredValue) ? fallbackValue : configuredValue;
            if (Path.IsPathRooted(value))
                return value;

            string normalized = value.Replace('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.StartupPath, normalized));
        }

        private static string ResolveSavedOrConfiguredPath(
            string savedValue,
            string configuredValue,
            string fallbackValue)
        {
            if (!string.IsNullOrWhiteSpace(savedValue))
                return ResolveConfiguredPath(savedValue, fallbackValue);

            return ResolveConfiguredPath(configuredValue, fallbackValue);
        }

        private static string ResolveSavedPackagePath(
            string savedValue,
            string configuredValue,
            string fallbackValue)
        {
            if (!string.IsNullOrWhiteSpace(savedValue))
                return ResolveConfiguredPath(savedValue, fallbackValue);

            return ResolvePackagePath(configuredValue, fallbackValue);
        }

        private static int ResolveAutoCloseIdleSeconds()
        {
            const int defaultSeconds = 300;
            int configuredSeconds;
            return int.TryParse(ConfigurationManager.AppSettings["AutoCloseIdleSeconds"], out configuredSeconds)
                && configuredSeconds > 0
                ? configuredSeconds
                : defaultSeconds;
        }

        private static string ResolvePackagePath(string configuredValue, string fallbackValue)
        {
            string primary = ResolveConfiguredPath(configuredValue, fallbackValue);
            if (Directory.Exists(primary))
                return primary;

            string startupPath = Application.StartupPath;
            string[] candidates =
            {
                primary,
                Path.Combine(startupPath, "InferencePackage"),
                Path.Combine(startupPath, "inference_package"),
                Path.Combine(startupPath, ".\\InferencePackage"),
                Path.Combine(startupPath, ".\\inference_package"),
            };

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return primary;
        }

        private PipelinePackageConfig LoadPipelinePackageOrThrow(string packagePath)
        {
            string configPath = Path.Combine(packagePath, "config", "pipeline.json");
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"pipeline.json not found. looked for: {configPath}", configPath);

            var config = JsonConvert.DeserializeObject<PipelinePackageConfig>(File.ReadAllText(configPath));
            if (config == null)
                throw new InvalidOperationException("pipeline.json deserialization failed.");

            if (config.pipeline == null)
                throw new InvalidOperationException("pipeline.json missing pipeline section.");

            string pipelineMode = (config.pipeline.mode ?? "").Trim();
            if (!string.Equals(pipelineMode, "anoma_then_yolo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("pipeline.mode must be anoma_then_yolo.");

            if (config.pipeline.skip_yolo_when_stage1_normal != true)
                throw new InvalidOperationException("pipeline.skip_yolo_when_stage1_normal must be true.");

            if (!config.RequiresMask || config.mask == null || string.IsNullOrWhiteSpace(config.mask.model))
                throw new InvalidOperationException("pipeline.json missing required mask.model.");

            return config;
        }

        private static MaskOnnxRunner LoadMaskOnnxRunnerOrThrow(
            string packagePath,
            PipelinePackageConfig config)
        {
            if (config == null || config.mask == null || string.IsNullOrWhiteSpace(config.mask.model))
                throw new InvalidOperationException("pipeline.json missing mask.model");

            string modelPath = Path.GetFullPath(Path.Combine(
                packagePath,
                config.mask.model.Replace('/', Path.DirectorySeparatorChar)));
            return new MaskOnnxRunner(modelPath, config.mask);
        }

        private static BatchExporter CreateBatchExporter(
            string exportBasePath,
            string packagePath,
            PipelinePackageConfig config)
        {
            InferenceContextInfo context = InferenceContextFactory.Create(packagePath, config);
            string pipelineSnapshot = InferenceContextFactory.ReadPipelineSnapshot(packagePath);
            return new BatchExporter(exportBasePath, context, pipelineSnapshot);
        }

        private static void LoadRequiredModelsOrThrow(
            OnnxModelTester modelTester,
            string packagePath,
            PipelinePackageConfig config)
        {
            if (modelTester == null)
                throw new ArgumentNullException(nameof(modelTester));

            if (config.RequiresAnoma)
            {
                if (config.anoma == null || string.IsNullOrWhiteSpace(config.anoma.model))
                    throw new InvalidOperationException("pipeline.json missing anoma.model");

                string anomaPath = Path.Combine(packagePath, config.anoma.model);
                if (!File.Exists(anomaPath))
                    throw new FileNotFoundException("anoma model not found.", anomaPath);

                modelTester.LoadAnomaModel(anomaPath);
            }

            if (config.RequiresYolo)
            {
                if (config.yolo == null || string.IsNullOrWhiteSpace(config.yolo.model))
                    throw new InvalidOperationException("pipeline.json missing yolo.model");

                string yoloPath = Path.Combine(packagePath, config.yolo.model);
                if (!File.Exists(yoloPath))
                    throw new FileNotFoundException("yolo model not found.", yoloPath);

                modelTester.LoadYoloModel(yoloPath);
            }
        }

        private void RegisterIncomingFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            if (_results.Any(item =>
                string.Equals(item.SourceFilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string imageId = Path.GetFileNameWithoutExtension(filePath);
            string fileName = Path.GetFileName(filePath);
            var viewModel = new InspectionResultViewModel
            {
                ImageId = imageId,
                FileName = fileName,
                Stage1 = "미전처리",
                Stage2 = "대기",
                Final = "수신완료",
                ScoreText = "-",
                DetectionCount = 0,
                Detections = new List<Detection>(),
                ReasonText = "received_waiting_preprocess",
                SourceFilePath = filePath,
                IsPreprocessPending = true,
                IsPending = false,
                IsInferenceCompleted = false,
            };

            _results.Insert(0, viewModel);
            MarkInputReceived();
            RefreshResultList(selectFirst: true);
            SaveSessionState();
            StartPreprocessWorkerIfNeeded();
        }

        private void RemoveIncomingFile(string filePath)
        {
            if (_isClosingBatch)
                return;

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            InspectionResultViewModel currentSelection = GetCurrentSelectedResult();
            bool removedCurrentSelection = currentSelection != null
                && string.Equals(currentSelection.SourceFilePath, filePath, StringComparison.OrdinalIgnoreCase);

            int removed = _results.RemoveAll(item =>
                string.Equals(item.SourceFilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return;

            RefreshResultList(selectFirst: !removedCurrentSelection);
            if (_results.Count == 0 || removedCurrentSelection)
                ClearSelectionView();
            SaveSessionState();
        }

        private void RefreshInputListFromFolder()
        {
            var existingPaths = new HashSet<string>(
                Directory.GetFiles(_inputPath)
                    .Where(IsSupportedImagePath),
                StringComparer.OrdinalIgnoreCase);

            _results.RemoveAll(item =>
                !string.IsNullOrWhiteSpace(item.SourceFilePath)
                && IsSupportedImagePath(item.SourceFilePath)
                && IsUnderDirectory(item.SourceFilePath, _inputPath)
                && !existingPaths.Contains(item.SourceFilePath));

            RegisterExistingInputFiles();
            RefreshResultList(selectFirst: true);
            if (_results.Count == 0)
                ClearSelectionView();
            SaveSessionState();
        }

        private static bool IsSupportedImagePath(string path)
        {
            string extension = Path.GetExtension(path) ?? "";
            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderDirectory(string path, string directory)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private void RunPendingPreprocess()
        {
            if (_isPreprocessing)
            {
                MessageBox.Show("자동 전처리 진행 중입니다. 완료 후 다시 시도하세요.");
                return;
            }

            RunPendingPreprocess(showNoPendingMessage: true, runInBackground: false);
        }

        private void RunPendingPreprocess(bool showNoPendingMessage, bool runInBackground)
        {
            List<InspectionResultViewModel> pendingItems = _results
                .Where(item => item.IsPreprocessPending)
                .ToList();

            if (pendingItems.Count == 0)
            {
                if (showNoPendingMessage)
                    MessageBox.Show("전처리 대기 중인 항목이 없습니다.");
                else if (runInBackground)
                {
                    _isPreprocessing = false;
                    StartAutomaticInferenceIfNeeded();
                }
                return;
            }

            var sourcePaths = new List<string>();
            foreach (InspectionResultViewModel item in pendingItems)
            {
                if (WaitForFile(item.SourceFilePath))
                    sourcePaths.Add(item.SourceFilePath);
            }

            if (sourcePaths.Count == 0)
            {
                if (showNoPendingMessage)
                    MessageBox.Show("전처리 가능한 입력 파일이 없습니다.");
                else if (runInBackground)
                {
                    _isPreprocessing = false;
                    if (_results.Any(item => item.IsPreprocessPending))
                        StartPreprocessWorkerIfNeeded();
                    else
                        StartAutomaticInferenceIfNeeded();
                }
                return;
            }

            foreach (InspectionResultViewModel item in pendingItems)
            {
                if (sourcePaths.Contains(item.SourceFilePath, StringComparer.OrdinalIgnoreCase))
                {
                    item.Stage1 = "전처리중";
                    item.Final = "전처리중";
                    item.ReasonText = "preprocessing";
                }
            }
            RefreshResultList(selectFirst: true);
            SaveSessionState();

            if (runInBackground)
            {
                string currentBatchDirectory = _batchExporter.CurrentBatchDirectory;
                Task.Run(() => RunPreprocessBatchInBackground(pendingItems, sourcePaths, currentBatchDirectory));
                return;
            }

            IReadOnlyDictionary<string, string> maskedBySource = RunMaskRuntimeBatch(sourcePaths, _batchExporter.CurrentBatchDirectory, null);
            ApplyPreprocessResults(pendingItems, maskedBySource);
        }

        private IReadOnlyDictionary<string, string> RunMaskRuntimeBatch(
            List<string> sourcePaths,
            string batchDirectory,
            Action<string, string> onMaskedImageReady)
        {
            string preprocessOutputDir = Path.Combine(batchDirectory, "preprocessed");
            return _maskOnnxRunner.RunBatch(sourcePaths, preprocessOutputDir, onMaskedImageReady);
        }

        private void RunPreprocessBatchInBackground(
            List<InspectionResultViewModel> pendingItems,
            List<string> sourcePaths,
            string batchDirectory)
        {
            try
            {
                var processedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyDictionary<string, string> maskedBySource = RunMaskRuntimeBatch(
                    sourcePaths,
                    batchDirectory,
                    (sourcePath, maskedPath) =>
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;

                        Invoke(new Action(() =>
                        {
                            InspectionResultViewModel item = pendingItems.FirstOrDefault(candidate =>
                                string.Equals(candidate.SourceFilePath, sourcePath, StringComparison.OrdinalIgnoreCase));

                            if (item == null || !item.IsPreprocessPending)
                                return;

                            ApplySinglePreprocessResult(item, maskedPath);
                            processedSources.Add(sourcePath);
                            RefreshResultList(selectFirst: false);
                        }));
                    });

                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        MarkMissingPreprocessResults(pendingItems, maskedBySource, processedSources);
                        RefreshResultList(selectFirst: true);
                    }));
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        LogException(ex);
                        foreach (InspectionResultViewModel item in pendingItems)
                            MarkPreprocessFailed(item, "preprocess_failed");
                        RefreshResultList(selectFirst: true);
                        SaveSessionState();
                        SetAutoCloseStatus("자동 전처리 오류: error_log.txt 확인");
                    }));
                }
            }
            finally
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _isPreprocessing = false;
                        if (_preprocessAgainRequested || _results.Any(item => item.IsPreprocessPending))
                            StartPreprocessWorkerIfNeeded();
                        else
                            StartAutomaticInferenceIfNeeded();
                    }));
                }
            }
        }

        private void ApplyPreprocessResults(
            List<InspectionResultViewModel> pendingItems,
            IReadOnlyDictionary<string, string> maskedBySource)
        {
            foreach (InspectionResultViewModel item in pendingItems)
            {
                string maskedPath;
                if (!maskedBySource.TryGetValue(item.SourceFilePath, out maskedPath))
                {
                    MarkPreprocessFailed(item, "mask_not_created");
                    continue;
                }

                ApplySinglePreprocessResult(item, maskedPath);
            }

            RefreshResultList(selectFirst: true);
            SaveSessionState();
            StartAutomaticInferenceIfNeeded();
        }

        private void MarkMissingPreprocessResults(
            List<InspectionResultViewModel> pendingItems,
            IReadOnlyDictionary<string, string> maskedBySource,
            HashSet<string> processedSources)
        {
            foreach (InspectionResultViewModel item in pendingItems)
            {
                if (!item.IsPreprocessPending)
                    continue;

                string maskedPath;
                if (maskedBySource.TryGetValue(item.SourceFilePath, out maskedPath)
                    && !processedSources.Contains(item.SourceFilePath))
                {
                    ApplySinglePreprocessResult(item, maskedPath);
                    processedSources.Add(item.SourceFilePath);
                    continue;
                }

                MarkPreprocessFailed(item, "mask_not_created");
            }
        }

        private void ApplySinglePreprocessResult(InspectionResultViewModel item, string maskedPath)
        {
            if (!WaitForFile(maskedPath))
            {
                MarkPreprocessFailed(item, "masked_file_locked");
                return;
            }

            SavePreparedItem(item, maskedPath);
            StartAutomaticInferenceIfNeeded();
        }

        private static void MarkPreprocessFailed(InspectionResultViewModel item, string reason)
        {
            item.Stage1 = "전처리실패";
            item.Final = "실패";
            item.ReasonText = reason;
            item.DetectionCount = 0;
            item.Detections = new List<Detection>();
            item.IsPreprocessPending = false;
            item.IsPending = false;
            item.IsInferenceCompleted = false;
        }

        private void StartPreprocessWorkerIfNeeded()
        {
            if (_isPreprocessing)
            {
                _preprocessAgainRequested = true;
                return;
            }

            _isPreprocessing = true;
            Task.Run(() => RunPreprocessWorkerLoop());
        }

        private void RunPreprocessWorkerLoop()
        {
            try
            {
                _preprocessAgainRequested = false;

                if (IsDisposed || !IsHandleCreated)
                    return;

                BeginInvoke(new Action(() => RunPendingPreprocess(showNoPendingMessage: false, runInBackground: true)));
            }
            catch (Exception ex)
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _isPreprocessing = false;
                        LogException(ex);
                        SetAutoCloseStatus("자동 전처리 오류: error_log.txt 확인");
                    }));
                }
            }
        }

        private void SavePreparedItem(InspectionResultViewModel item, string preparedImagePath)
        {
            using (Mat rawImg = Cv2.ImRead(item.SourceFilePath))
            using (Mat maskedImg = Cv2.ImRead(preparedImagePath))
            {
                if (rawImg.Empty())
                    throw new InvalidOperationException($"Failed to load image: {item.SourceFilePath}");
                if (maskedImg.Empty())
                    throw new InvalidOperationException("Masked image load failed.");

                var processor = new ImageProcessor();
                BatchExporter.PreparedImagePaths savedPaths;
                lock (_batchExporterLock)
                    savedPaths = _batchExporter.SavePreparedImages(item.ImageId, rawImg, maskedImg);

                int displaySize = ResolveDisplayInputSize();
                using (Mat displayImg = processor.PrepareExistingMaskedDisplayImage(preparedImagePath, displaySize, displaySize))
                {
                    if (displayImg == null || displayImg.Empty())
                        throw new InvalidOperationException("Image preprocessing failed.");

                    SetDisplayImage(BitmapConverter.ToBitmap(displayImg), resetView: true);
                }

                item.Stage1 = "대기";
                item.Stage2 = "대기";
                item.Final = "전처리완료";
                item.ScoreText = "-";
                item.DetectionCount = 0;
                item.Detections = new List<Detection>();
                item.ReasonText = "preprocessed_waiting_inference";
                item.RawImagePath = savedPaths.RawImagePath;
                item.ProcessedImagePath = savedPaths.ProcessedImagePath;
                item.IsPreprocessPending = false;
                item.IsPending = true;
                item.IsInferenceCompleted = false;
                SaveSessionState();
            }

            ShowResult(item);
            UpdateStaticUi();
        }

        private void StartAutomaticInferenceIfNeeded()
        {
            if (_isInferring || _autoInferenceScheduled || _isClosingBatch)
                return;

            if (!_results.Any(item => item.IsPending))
                return;

            if (IsDisposed || !IsHandleCreated)
                return;

            _autoInferenceScheduled = true;
            BeginInvoke(new Action(RunNextAutomaticInference));
        }

        private async void RunNextAutomaticInference()
        {
            _autoInferenceScheduled = false;
            if (_isInferring || _isClosingBatch)
                return;

            InspectionResultViewModel pendingItem = _results.FirstOrDefault(item => item.IsPending);
            if (pendingItem == null)
                return;

            int waitingCount = _results.Count(item => item.IsPending);
            _isInferring = true;
            SetInferenceProgress(0, 1, $"자동 추론 중: {pendingItem.FileName} / 대기 {waitingCount}개");
            try
            {
                await Task.Run(() => RunInferenceForPreparedItem(pendingItem));
                SaveSessionState();
                SetInferenceProgress(1, 1, $"자동 추론 완료: {pendingItem.FileName}");
            }
            finally
            {
                _isInferring = false;
            }

            RefreshResultList(selectFirst: false);
            RefreshSelectedResult(pendingItem);
            StartAutomaticInferenceIfNeeded();
        }

        private async void RunPendingInference(bool showNoPendingMessage = true)
        {
            if (_isInferring)
            {
                if (showNoPendingMessage)
                    MessageBox.Show("자동 추론이 진행 중입니다. 완료 후 다시 시도하세요.");
                return;
            }

            List<InspectionResultViewModel> pendingItems = _results
                .Where(item => item.IsPending
                    || (showNoPendingMessage
                        && string.Equals(item.ReasonText, "inference_failed", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (pendingItems.Count == 0)
            {
                if (showNoPendingMessage)
                    MessageBox.Show("추론 대기 또는 재시도할 항목이 없습니다.");
                return;
            }

            foreach (InspectionResultViewModel item in pendingItems)
                item.IsPending = true;

            SetInferenceProgress(0, pendingItems.Count, "추론 시작");
            _isInferring = true;
            try
            {
                for (int index = 0; index < pendingItems.Count; index++)
                {
                    InspectionResultViewModel pendingItem = pendingItems[index];
                    SetInferenceProgress(index, pendingItems.Count, $"추론 중: {pendingItem.FileName}");
                    await Task.Run(() => RunInferenceForPreparedItem(pendingItem));
                    SaveSessionState();
                    SetInferenceProgress(index + 1, pendingItems.Count, $"추론 완료: {index + 1}/{pendingItems.Count}");
                    RefreshResultList(selectFirst: false);
                    RefreshSelectedResult(pendingItem);
                }
            }
            finally
            {
                _isInferring = false;
            }

            RefreshResultList(selectFirst: true);
            StartAutomaticInferenceIfNeeded();
        }

        private void SetInferenceProgress(int completed, int total, string message)
        {
            UpdatePipelineProgress();
        }

        private void UpdatePipelineProgress()
        {
            int total = _results.Count;
            int preprocessed = _results.Count(item => !item.IsPreprocessPending);
            int inferred = _results.Count(item => item.IsInferenceCompleted);
            int progressMaximum = Math.Max(total, 1);

            labelInputProgress.Text = $"입력 {total}장";
            labelPreprocessProgress.Text = $"전처리 {preprocessed}/{total}" + (_isPreprocessing ? " · 진행" : "");
            labelInferenceProgress.Text = $"추론 {inferred}/{total}" + (_isInferring ? " · 진행" : "");

            progressBarPreprocess.Minimum = 0;
            progressBarPreprocess.Maximum = progressMaximum;
            progressBarPreprocess.Value = Math.Min(preprocessed, progressMaximum);
            progressBarInference.Minimum = 0;
            progressBarInference.Maximum = progressMaximum;
            progressBarInference.Value = Math.Min(inferred, progressMaximum);
        }

        private void RunInferenceForPreparedItem(InspectionResultViewModel result)
        {
            try
            {
                using (Mat rawImg = Cv2.ImRead(result.RawImagePath))
                using (Mat processedImg = Cv2.ImRead(result.ProcessedImagePath))
                {
                    if (rawImg.Empty())
                        throw new InvalidOperationException($"Failed to load raw image: {result.RawImagePath}");
                    if (processedImg.Empty())
                        throw new InvalidOperationException($"Failed to load prepared image: {result.ProcessedImagePath}");

                    var processor = new ImageProcessor();
                    bool anomaExecuted = false;
                    float anomaScore = 0f;
                    string anomaDecision = "not_run";
                    bool yoloExecuted = false;
                    string yoloSkippedReason = "";
                    var detections = new List<Detection>();
                    var reasons = new List<string>();
                    bool finalIsDefect = false;

                    int displaySize = ResolveDisplayInputSize();
                    using (Mat displayImg = processor.PrepareExistingMaskedDisplayImage(result.ProcessedImagePath, displaySize, displaySize))
                    {
                        if (displayImg == null || displayImg.Empty())
                            throw new InvalidOperationException("Image display preparation failed.");

                        if (_config.RequiresAnoma)
                        {
                            int anomaInputSize = _config.anoma?.input_size ?? displaySize;
                            using (Mat anomaImg = processor.PrepareExistingMaskedModelInput(result.ProcessedImagePath, anomaInputSize, anomaInputSize))
                            {
                                if (anomaImg == null || anomaImg.Empty())
                                    throw new InvalidOperationException("Anoma preprocessing failed.");

                                AnomaInferenceResult anomaResult = _modelTester.RunAnomaInference(
                                    anomaImg,
                                    _config.anoma?.score_thres ?? 0.5f);

                                anomaExecuted = true;
                                anomaScore = anomaResult.Score;
                                anomaDecision = anomaResult.Decision;
                            }
                        }

                        switch ((_config.pipeline?.mode ?? "").Trim().ToLowerInvariant())
                        {
                            case "yolo_only":
                                detections = RunYoloStage(processor, result.ProcessedImagePath, true);
                                yoloExecuted = true;
                                finalIsDefect = detections.Count > 0;
                                reasons.Add(finalIsDefect ? "yolo_detection" : "yolo_clear");
                                break;

                            case "anoma_only":
                                finalIsDefect = string.Equals(anomaDecision, "anomaly", StringComparison.OrdinalIgnoreCase);
                                reasons.Add(finalIsDefect ? "anoma_anomaly" : "anoma_normal");
                                break;

                            default:
                                bool stage1Abnormal = string.Equals(anomaDecision, "anomaly", StringComparison.OrdinalIgnoreCase);
                                if (!stage1Abnormal && _config.pipeline?.skip_yolo_when_stage1_normal == true)
                                {
                                    yoloExecuted = false;
                                    yoloSkippedReason = "stage1_normal";
                                    finalIsDefect = false;
                                    reasons.Add("stage1_normal_skip_yolo");
                                }
                                else
                                {
                                    detections = RunYoloStage(processor, result.ProcessedImagePath, true);
                                    yoloExecuted = true;
                                    finalIsDefect = stage1Abnormal;
                                    reasons.Add(stage1Abnormal ? "stage1_abnormal" : "stage1_normal");
                                    reasons.Add(detections.Count > 0 ? "yolo_detected" : "yolo_no_detections");
                                }
                                break;
                        }

                        if (yoloExecuted && detections.Count > 0)
                            DrawDetections(displayImg, detections);

                        _logger.SaveResult(result.FileName, finalIsDefect ? "NG" : "OK", anomaScore);
                        lock (_batchExporterLock)
                        {
                            _batchExporter.AddResult(
                                result.ImageId,
                                rawImg,
                                processedImg,
                                anomaExecuted,
                                anomaScore,
                                anomaDecision,
                                yoloExecuted,
                                yoloSkippedReason,
                                detections,
                                finalIsDefect,
                                reasons);
                        }
                    }

                    result.Stage1 = ToStage1Text(anomaExecuted, anomaDecision);
                    result.Stage2 = ToStage2Text(yoloExecuted, yoloSkippedReason, detections.Count);
                    result.Final = finalIsDefect ? "불량" : "정상";
                    result.ScoreText = anomaExecuted ? anomaScore.ToString("0.000") : "-";
                    result.DetectionCount = detections.Count;
                    result.Detections = detections;
                    result.ReasonText = reasons.Count > 0 ? string.Join(", ", reasons) : "-";
                    result.IsPending = false;
                    result.IsInferenceCompleted = true;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                result.Stage1 = "추론실패";
                result.Stage2 = "미실행";
                result.Final = "실패";
                result.ReasonText = "inference_failed";
                result.IsPending = false;
                result.IsInferenceCompleted = false;
            }
        }

        private void RefreshSelectedResult(InspectionResultViewModel completedResult)
        {
            if (ReferenceEquals(GetCurrentSelectedResult(), completedResult))
                ShowResult(completedResult);
        }

        private List<Detection> RunYoloStage(ImageProcessor processor, string imagePath, bool alreadyMasked)
        {
            if (!_config.RequiresYolo)
                return new List<Detection>();

            int yoloInputSize = _config.yolo?.imgsz ?? 640;
            using (Mat yoloImg = alreadyMasked
                ? processor.PrepareExistingMaskedModelInput(imagePath, yoloInputSize, yoloInputSize)
                : processor.PrepareModelInput(imagePath, yoloInputSize, yoloInputSize))
            {
                if (yoloImg == null || yoloImg.Empty())
                    throw new InvalidOperationException("YOLO preprocessing failed.");

                return _modelTester.RunYoloInference(
                    yoloImg,
                    _config.yolo?.conf_thres ?? 0.25f,
                    _config.yolo?.iou_thres ?? 0.45f,
                    _config.yolo?.max_det ?? 300,
                    _config.ClassNamesById);
            }
        }

        private static void DrawDetections(Mat image, List<Detection> detections)
        {
            if (image == null || image.Empty() || detections == null)
                return;

            foreach (Detection detection in detections)
            {
                if (detection?.bbox_xywh_norm == null || detection.bbox_xywh_norm.Length < 4)
                    continue;

                float cx = detection.bbox_xywh_norm[0];
                float cy = detection.bbox_xywh_norm[1];
                float w = detection.bbox_xywh_norm[2];
                float h = detection.bbox_xywh_norm[3];

                int left = ClampToImage((cx - w / 2f) * image.Width, image.Width);
                int top = ClampToImage((cy - h / 2f) * image.Height, image.Height);
                int right = ClampToImage((cx + w / 2f) * image.Width, image.Width);
                int bottom = ClampToImage((cy + h / 2f) * image.Height, image.Height);

                int boxWidth = Math.Max(1, right - left);
                int boxHeight = Math.Max(1, bottom - top);
                Scalar color = ResolveDetectionColor(detection.class_name);

                Cv2.Rectangle(image, new Rect(left, top, boxWidth, boxHeight), color, 3);

                string label = string.IsNullOrWhiteSpace(detection.class_name)
                    ? detection.conf.ToString("0.00")
                    : $"{detection.class_name} {detection.conf:0.00}";
                int labelY = Math.Max(18, top - 6);
                Cv2.PutText(image, label, new Point(left, labelY), HersheyFonts.HersheySimplex, 0.6, color, 2);
            }
        }

        private static Scalar ResolveDetectionColor(string className)
        {
            string normalized = (className ?? "").Trim().ToLowerInvariant();
            if (normalized.Contains("dent") || normalized.Contains("찍힘"))
                return new Scalar(0, 0, 255);
            if (normalized.Contains("loose") || normalized.Contains("풀림"))
                return new Scalar(255, 0, 0);
            return new Scalar(0, 255, 255);
        }

        private static int ClampToImage(float value, int size)
        {
            if (size <= 0)
                return 0;

            int rounded = (int)Math.Round(value);
            return Math.Max(0, Math.Min(size - 1, rounded));
        }

        private int ResolveDisplayInputSize()
        {
            if (_config.RequiresYolo)
                return _config.yolo?.imgsz ?? 640;
            if (_config.RequiresAnoma)
                return _config.anoma?.input_size ?? 640;
            return 640;
        }

        private bool WaitForFile(string path)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                        return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(300);
                }
            }
            return false;
        }

        private void LogException(Exception ex)
        {
            string logPath = Path.Combine(Application.StartupPath, "error_log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:O} {ex}\n");
        }

        private static void OpenFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show("폴더를 찾을 수 없습니다.");
                return;
            }

            Process.Start("explorer.exe", path);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            CloseCurrentBatch(isAutomatic: false);
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            if (_isAutoClosePaused)
            {
                SetAutoCloseStatus("자동 마감 일시정지");
                return;
            }

            long lastInputTicks = System.Threading.Interlocked.Read(ref _lastInputReceivedTicks);
            if (_isClosingBatch || _batchExporter == null || _results.Count == 0 || lastInputTicks <= 0)
                return;

            if (_isInferring)
                return;

            int failedCount = _results.Count(item =>
                string.Equals(item.Final, "실패", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Stage1, "전처리실패", StringComparison.OrdinalIgnoreCase));
            if (failedCount > 0)
            {
                SetAutoCloseStatus($"자동 마감 보류: 실패 {failedCount}개");
                return;
            }

            int incompleteCount = _results.Count(item => !item.IsInferenceCompleted);
            if (_isPreprocessing || incompleteCount > 0)
            {
                SetAutoCloseStatus($"자동 마감: 처리 완료 대기 ({incompleteCount}개)");
                return;
            }

            var lastInputReceivedAt = new DateTime(lastInputTicks);
            double idleSeconds = (DateTime.Now - lastInputReceivedAt).TotalSeconds;
            int remainingSeconds = Math.Max(0, _autoCloseIdleSeconds - (int)Math.Floor(idleSeconds));
            if (remainingSeconds > 0)
            {
                SetAutoCloseStatus($"자동 마감까지 {remainingSeconds}초");
                return;
            }

            CloseCurrentBatch(isAutomatic: true);
        }

        private void buttonToggleAutoClose_CheckedChanged(object sender, EventArgs e)
        {
            _isAutoClosePaused = !buttonToggleAutoClose.Checked;
            buttonToggleAutoClose.Text = _isAutoClosePaused ? "자동 마감 OFF" : "자동 마감 ON";
            buttonToggleAutoClose.BackColor = _isAutoClosePaused
                ? System.Drawing.Color.MistyRose
                : System.Drawing.Color.Honeydew;

            if (_isAutoClosePaused)
            {
                SetAutoCloseStatus("자동 마감 일시정지");
                return;
            }

            if (_results.Count > 0)
            {
                MarkInputReceived();
                SetAutoCloseStatus($"자동 마감까지 {_autoCloseIdleSeconds}초");
            }
            else
            {
                SetAutoCloseStatus("자동 마감 대기");
            }
        }

        private void SetAutoCloseStatus(string message)
        {
            labelAutoCloseProgress.Text = message;
        }

        private void CloseCurrentBatch(bool isAutomatic)
        {
            try
            {
                if (_results.Count == 0)
                {
                    if (!isAutomatic)
                        MessageBox.Show("마감할 항목이 없습니다.");
                    return;
                }

                InspectionResultViewModel incompleteItem = _results.FirstOrDefault(item => !item.IsInferenceCompleted);
                if (incompleteItem != null)
                {
                    if (!isAutomatic)
                    {
                        MessageBox.Show(
                            $"추론을 마치지 않은 이미지가 있습니다.\n먼저 추론을 완료하세요.\n\n파일: {incompleteItem.FileName}\n상태: {incompleteItem.Final}",
                            "CoilInspectionApp",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    return;
                }

                _isClosingBatch = true;
                try
                {
                    _batchExporter.CloseBatch();
                    string exportedBatchDirectory = _batchExporter.LastExportDirectory;
                    DeleteBatchInputFiles();
                    _batchExporter.StartNewBatch();
                    _results.Clear();
                    listViewResults.Items.Clear();
                    System.Threading.Interlocked.Exchange(ref _lastInputReceivedTicks, 0);
                    ClearSelectionView();
                    SaveSessionState();
                    UpdateStaticUi();

                    if (isAutomatic)
                    {
                        string batchName = string.IsNullOrWhiteSpace(exportedBatchDirectory)
                            ? "-"
                            : Path.GetFileName(exportedBatchDirectory);
                        SetAutoCloseStatus($"자동 배치 마감 완료: {batchName}");
                    }
                    else
                    {
                        MessageBox.Show("배치 마감 완료 (DONE.flag 생성됨)");
                    }
                }
                finally
                {
                    _isClosingBatch = false;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                if (isAutomatic)
                    SetAutoCloseStatus("자동 마감 오류: error_log.txt 확인");
                else
                    MessageBox.Show("마감 오류: " + ex.Message);
            }
        }

        private void DeleteBatchInputFiles()
        {
            foreach (InspectionResultViewModel item in _results.ToList())
            {
                if (string.IsNullOrWhiteSpace(item.SourceFilePath))
                    continue;

                if (!IsUnderDirectory(item.SourceFilePath, _inputPath))
                    continue;

                try
                {
                    if (File.Exists(item.SourceFilePath))
                        File.Delete(item.SourceFilePath);
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
            }
        }

        private bool CanChangeRuntimePath(string pathName)
        {
            bool hasCurrentBatchData = _results.Count > 0 || (_batchExporter?.HasCurrentItems ?? false);
            if (_isClosingBatch || _isPreprocessing || _isInferring || hasCurrentBatchData)
            {
                MessageBox.Show(
                    $"{pathName}은(는) 현재 배치가 비어 있을 때만 변경할 수 있습니다.\n" +
                    "진행 중인 처리를 완료하고 배치를 마감한 뒤 다시 시도하세요.",
                    "경로 변경",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private static string SelectFolderPath(string description, string currentPath, bool allowNewFolder)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = description,
                SelectedPath = Directory.Exists(currentPath) ? currentPath : Application.StartupPath,
                ShowNewFolderButton = allowNewFolder
            })
            {
                return dialog.ShowDialog() == DialogResult.OK
                    ? Path.GetFullPath(dialog.SelectedPath)
                    : "";
            }
        }

        private void PersistRuntimePathSettings()
        {
            try
            {
                _runtimePathSettings.InputDirectory = _inputPath;
                _runtimePathSettings.InferencePackageDirectory = _packagePath;
                _runtimePathSettings.ExportBaseDirectory = _exportBasePath;
                _runtimePathSettingsStore.Save(_runtimePathSettings);
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show(
                    $"현재 실행에는 경로가 적용됐지만 사용자 설정을 저장하지 못했습니다.\n{ex.Message}",
                    "경로 설정 저장",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void buttonSelectInput_Click(object sender, EventArgs e)
        {
            if (!CanChangeRuntimePath("입력 폴더"))
                return;

            string selectedPath = SelectFolderPath("입력 이미지 폴더 선택", _inputPath, allowNewFolder: true);
            if (string.IsNullOrWhiteSpace(selectedPath) ||
                string.Equals(selectedPath, _inputPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string previousPath = _inputPath;
            try
            {
                Directory.CreateDirectory(selectedPath);
                _inputPath = selectedPath;
                if (_servicesInitialized)
                    StartInputWatcher(_inputPath);

                System.Threading.Interlocked.Exchange(ref _lastInputReceivedTicks, 0);
                PersistRuntimePathSettings();
                UpdateStaticUi();

                if (_servicesInitialized)
                    RegisterExistingInputFiles();
            }
            catch (Exception ex)
            {
                _inputPath = previousPath;
                LogException(ex);
                MessageBox.Show(
                    $"입력 폴더를 변경하지 못했습니다.\n{ex.Message}",
                    "입력 폴더 변경",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateStaticUi();
            }
        }

        private void buttonSelectPackage_Click(object sender, EventArgs e)
        {
            if (!CanChangeRuntimePath("추론 패키지"))
                return;

            string selectedPath = SelectFolderPath("InferencePackage 폴더 선택", _packagePath, allowNewFolder: false);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            OnnxModelTester candidateTester = null;
            MaskOnnxRunner candidateMaskRunner = null;
            BatchExporter candidateExporter = null;
            bool packageApplied = false;
            try
            {
                PipelinePackageConfig candidateConfig = LoadPipelinePackageOrThrow(selectedPath);
                candidateTester = new OnnxModelTester();
                candidateMaskRunner = LoadMaskOnnxRunnerOrThrow(selectedPath, candidateConfig);
                LoadRequiredModelsOrThrow(candidateTester, selectedPath, candidateConfig);

                if (_servicesInitialized)
                {
                    candidateExporter = CreateBatchExporter(_exportBasePath, selectedPath, candidateConfig);
                    candidateExporter.StartOrResumeBatch();
                }

                OnnxModelTester previousTester = _modelTester;
                MaskOnnxRunner previousMaskRunner = _maskOnnxRunner;
                _modelTester = candidateTester;
                candidateTester = null;
                _maskOnnxRunner = candidateMaskRunner;
                candidateMaskRunner = null;
                _config = candidateConfig;
                _packagePath = selectedPath;
                if (candidateExporter != null)
                    _batchExporter = candidateExporter;
                previousTester?.Dispose();
                previousMaskRunner?.Dispose();
                packageApplied = true;

                PersistRuntimePathSettings();
                InitializeOperationalServices();
                UpdateStaticUi();

                MessageBox.Show(
                    "추론 패키지를 검증하고 적용했습니다.\n새 입력부터 선택한 모델을 사용합니다.",
                    "패키지 변경",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                candidateTester?.Dispose();
                candidateMaskRunner?.Dispose();
                LogException(ex);
                MessageBox.Show(
                    packageApplied
                        ? $"패키지는 적용됐지만 실행 서비스 초기화에 실패했습니다.\n{ex.Message}"
                        : $"패키지 검증에 실패해 기존 패키지를 유지합니다.\n{ex.Message}",
                    "패키지 변경",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateStaticUi();
            }
        }

        private void buttonSelectBatch_Click(object sender, EventArgs e)
        {
            if (!CanChangeRuntimePath("배치 출력 폴더"))
                return;

            string selectedPath = SelectFolderPath("배치 출력 폴더 선택", _exportBasePath, allowNewFolder: true);
            if (string.IsNullOrWhiteSpace(selectedPath) ||
                string.Equals(selectedPath, _exportBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(selectedPath);
                BatchExporter candidateExporter = null;
                if (_servicesInitialized)
                {
                    candidateExporter = CreateBatchExporter(selectedPath, _packagePath, _config);
                    candidateExporter.StartOrResumeBatch();

                    if (candidateExporter.HasCurrentItems)
                    {
                        DialogResult resume = MessageBox.Show(
                            "선택한 출력 폴더에 마감되지 않은 현재 배치가 있습니다.\n이 배치를 이어서 불러올까요?",
                            "배치 출력 변경",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (resume != DialogResult.Yes)
                            return;
                    }
                }

                _exportBasePath = selectedPath;
                if (candidateExporter != null)
                {
                    _batchExporter = candidateExporter;
                    RestoreSessionState();
                }

                if (_statisticsForm != null && !_statisticsForm.IsDisposed)
                    _statisticsForm.Close();

                PersistRuntimePathSettings();
                UpdateStaticUi();
                RefreshResultList(selectFirst: true);
                StartAutomaticInferenceIfNeeded();
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show(
                    $"배치 출력 폴더를 변경하지 못했습니다.\n{ex.Message}",
                    "배치 출력 변경",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateStaticUi();
            }
        }

        private void buttonRefreshInput_Click(object sender, EventArgs e)
        {
            if (!_servicesInitialized)
            {
                MessageBox.Show("유효한 추론 패키지를 먼저 선택하세요.");
                return;
            }

            RefreshInputListFromFolder();
        }

        private void buttonOpenInput_Click(object sender, EventArgs e)
        {
            OpenFolderPath(_inputPath);
        }

        private void buttonOpenPackage_Click(object sender, EventArgs e)
        {
            OpenFolderPath(_packagePath);
        }

        private void buttonOpenBatch_Click(object sender, EventArgs e)
        {
            OpenFolderPath(_exportBasePath);
        }

        private void buttonStatistics_Click(object sender, EventArgs e)
        {
            if (_batchExporter == null)
                return;

            if (_statisticsForm == null || _statisticsForm.IsDisposed)
            {
                _statisticsForm = new StatisticsForm(
                    _batchExporter.ExportBaseDirectory,
                    _batchExporter.CurrentBatchDirectory,
                    _config?.anoma?.score_thres);
                _statisticsForm.FormClosed += (closedSender, closedArgs) => _statisticsForm = null;
                _statisticsForm.Show(this);
                return;
            }

            if (_statisticsForm.WindowState == FormWindowState.Minimized)
                _statisticsForm.WindowState = FormWindowState.Normal;
            _statisticsForm.Activate();
        }

        private void buttonZoomIn_Click(object sender, EventArgs e)
        {
            ZoomImageBy(ImageZoomStep);
        }

        private void buttonZoomOut_Click(object sender, EventArgs e)
        {
            ZoomImageBy(-ImageZoomStep);
        }

        private void buttonZoomFit_Click(object sender, EventArgs e)
        {
            FitDisplayImageToView();
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(pictureBox1.BackColor);
            if (_displayImage == null)
                return;

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(_displayImage, GetDisplayImageRect());
        }

        private void pictureBox1_MouseEnter(object sender, EventArgs e)
        {
            pictureBox1.Focus();
        }

        private void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            ZoomImageBy(e.Delta > 0 ? ImageZoomStep : -ImageZoomStep, e.Location);
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (_displayImage == null || e.Button != MouseButtons.Left)
                return;

            _isImagePanning = true;
            _lastPanPoint = e.Location;
            pictureBox1.Cursor = Cursors.SizeAll;
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isImagePanning || _displayImage == null)
                return;

            _imageOffset = new System.Drawing.PointF(
                _imageOffset.X + e.X - _lastPanPoint.X,
                _imageOffset.Y + e.Y - _lastPanPoint.Y);
            _lastPanPoint = e.Location;
            ClampImageOffset();
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _isImagePanning = false;
            pictureBox1.Cursor = Cursors.Default;
        }

        private void pictureBox1_Resize(object sender, EventArgs e)
        {
            if (_displayImage == null)
                return;

            double previousFitScale = _imageFitScale;
            _imageFitScale = CalculateFitScale();
            if (Math.Abs(_imageScale - previousFitScale) < 0.001)
            {
                FitDisplayImageToView();
            }
            else
            {
                _imageScale = Math.Max(_imageFitScale, _imageScale);
                ClampImageOffset();
                pictureBox1.Invalidate();
            }
        }

        private void SelectAndQueueImage()
        {
            using (var ofd = new OpenFileDialog { Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    RegisterIncomingFile(ofd.FileName);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Dispose();
            _dw?.Dispose();
            _dw = null;
            _modelTester?.Dispose();
            _maskOnnxRunner?.Dispose();
            _resultContextMenu.Dispose();
            SaveSessionState();
            ClearDisplayImage();
            base.OnFormClosing(e);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateStaticUi();
            ClearSelectionDetails();
            StartAutomaticInferenceIfNeeded();
        }

        private void listViewResults_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewResults.SelectedIndices.Count == 0)
                return;

            InspectionResultViewModel result = listViewResults.SelectedItems[0].Tag as InspectionResultViewModel;
            if (result == null)
                return;

            ShowResult(result);
        }

        private void listViewResults_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            ListViewHitTestInfo hit = listViewResults.HitTest(e.Location);
            if (hit.Item == null)
            {
                listViewResults.SelectedItems.Clear();
                return;
            }

            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        private void DeleteSelectedResult()
        {
            if (listViewResults.SelectedItems.Count == 0)
                return;

            InspectionResultViewModel result = listViewResults.SelectedItems[0].Tag as InspectionResultViewModel;
            if (result == null)
                return;

            DialogResult confirm = MessageBox.Show(
                $"이 항목을 삭제할까요?\n\n{result.FileName}\n\ninput 폴더와 현재 배치 임시 파일에서도 삭제됩니다.",
                "항목 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            DeleteResultItem(result);
        }

        private void RefreshResultList(bool selectFirst)
        {
            UpdatePipelineProgress();
            InspectionResultViewModel selectedResult = GetCurrentSelectedResult();
            InspectionResultViewModel topResult = listViewResults.TopItem?.Tag as InspectionResultViewModel;
            bool updateInPlace = CanUpdateResultListInPlace();
            ListViewItem itemToSelect = null;
            ListViewItem itemToKeepAtTop = null;

            if (updateInPlace)
            {
                for (int index = 0; index < _results.Count; index++)
                    UpdateResultListItem(listViewResults.Items[index], _results[index], index);
                return;
            }

            listViewResults.BeginUpdate();
            try
            {
                listViewResults.Items.Clear();
                for (int index = 0; index < _results.Count; index++)
                {
                    ListViewItem item = CreateResultListItem(_results[index], index);
                    listViewResults.Items.Add(item);

                    if (ReferenceEquals(_results[index], selectedResult))
                        itemToSelect = item;
                    if (ReferenceEquals(_results[index], topResult))
                        itemToKeepAtTop = item;
                }

                if (itemToSelect == null && selectFirst && listViewResults.SelectedItems.Count == 0
                    && listViewResults.Items.Count > 0)
                {
                    itemToSelect = listViewResults.Items[0];
                }

                if (itemToSelect != null)
                {
                    itemToSelect.Selected = true;
                    itemToSelect.Focused = true;
                }
            }
            finally
            {
                listViewResults.EndUpdate();
            }

            if (itemToKeepAtTop != null)
                listViewResults.TopItem = itemToKeepAtTop;

            if (itemToSelect != null)
                listViewResults.Select();
        }

        private bool CanUpdateResultListInPlace()
        {
            if (listViewResults.Items.Count != _results.Count)
                return false;

            for (int index = 0; index < _results.Count; index++)
            {
                if (!ReferenceEquals(listViewResults.Items[index].Tag, _results[index]))
                    return false;
            }

            return true;
        }

        private static ListViewItem CreateResultListItem(InspectionResultViewModel result, int index)
        {
            var item = new ListViewItem();
            while (item.SubItems.Count < 7)
                item.SubItems.Add("");

            UpdateResultListItem(item, result, index);
            return item;
        }

        private static void UpdateResultListItem(ListViewItem item, InspectionResultViewModel result, int index)
        {
            while (item.SubItems.Count < 7)
                item.SubItems.Add("");

            SetSubItemText(item, 0, (index + 1).ToString());
            SetSubItemText(item, 1, result.FileName);
            SetSubItemText(item, 2, ResolvePreprocessStatus(result));
            SetSubItemText(item, 3, ResolveAnomalyStatus(result));
            SetSubItemText(item, 4, ResolveDetectionStatus(result));
            SetSubItemText(item, 5, result.Final);
            SetSubItemText(item, 6, result.ScoreText);
            ApplyResultListColors(item);
            item.Tag = result;
        }

        private static void ApplyResultListColors(ListViewItem item)
        {
            item.UseItemStyleForSubItems = false;
            for (int index = 0; index < item.SubItems.Count; index++)
                item.SubItems[index].ForeColor = System.Drawing.SystemColors.ControlText;

            item.SubItems[2].ForeColor = ResolveStatusColor(item.SubItems[2].Text);
            item.SubItems[3].ForeColor = ResolveStatusColor(item.SubItems[3].Text);
            item.SubItems[4].ForeColor = ResolveStatusColor(item.SubItems[4].Text);
            item.SubItems[5].ForeColor = ResolveStatusColor(item.SubItems[5].Text);
        }

        private static System.Drawing.Color ResolveStatusColor(string status)
        {
            if (string.Equals(status, "완료", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "정상", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "미검출", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Color.SeaGreen;
            }

            if (string.Equals(status, "이상", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "검출", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "진행 중", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Color.DarkOrange;
            }

            if (string.Equals(status, "불량", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "실패", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Color.Firebrick;
            }

            return System.Drawing.SystemColors.ControlText;
        }

        private static string ResolvePreprocessStatus(InspectionResultViewModel result)
        {
            if (string.Equals(result.Stage1, "전처리실패", StringComparison.OrdinalIgnoreCase))
                return "실패";
            if (!result.IsPreprocessPending)
                return "완료";
            if (string.Equals(result.Final, "전처리중", StringComparison.OrdinalIgnoreCase))
                return "진행 중";
            return "대기";
        }

        private static string ResolveAnomalyStatus(InspectionResultViewModel result)
        {
            if (result.IsPreprocessPending)
                return "-";
            if (result.IsPending)
                return "대기";
            if (string.Equals(result.Stage1, "추론실패", StringComparison.OrdinalIgnoreCase))
                return "실패";
            return result.Stage1;
        }

        private static string ResolveDetectionStatus(InspectionResultViewModel result)
        {
            if (result.IsPreprocessPending)
                return "-";
            if (result.IsPending)
                return "대기";
            return result.Stage2;
        }

        private static void SetSubItemText(ListViewItem item, int index, string value)
        {
            string safeValue = value ?? "";
            if (!string.Equals(item.SubItems[index].Text, safeValue, StringComparison.Ordinal))
                item.SubItems[index].Text = safeValue;
        }

        private void DeleteResultItem(InspectionResultViewModel result)
        {
            if (result == null)
                return;

            int removedIndex = _results.IndexOf(result);
            if (removedIndex < 0)
                return;

            _results.RemoveAt(removedIndex);
            DeleteFileIfExists(result.SourceFilePath);
            DeleteFileIfExists(result.RawImagePath);
            DeleteFileIfExists(result.ProcessedImagePath);
            _batchExporter?.RemoveItem(result.ImageId);

            RefreshResultList(selectFirst: false);
            if (_results.Count == 0)
            {
                ClearSelectionView();
            }
            else
            {
                int nextIndex = Math.Min(removedIndex, _results.Count - 1);
                SelectResultAt(nextIndex);
            }

            SaveSessionState();
            UpdateStaticUi();
        }

        private void SelectResultAt(int index)
        {
            if (index < 0 || index >= listViewResults.Items.Count)
                return;

            ListViewItem item = listViewResults.Items[index];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            listViewResults.Select();
            ShowResult(item.Tag as InspectionResultViewModel);
        }

        private void DeleteFileIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private string SessionStatePath =>
            _batchExporter == null || string.IsNullOrWhiteSpace(_batchExporter.CurrentBatchDirectory)
                ? ""
                : Path.Combine(_batchExporter.CurrentBatchDirectory, "meta", "session_state.json");

        private void SaveSessionState()
        {
            try
            {
                string statePath = SessionStatePath;
                if (string.IsNullOrWhiteSpace(statePath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                var state = new InspectionSessionState
                {
                    saved_at = DateTime.Now.ToString("O"),
                    items = _results.ToList()
                };
                File.WriteAllText(statePath, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void RestoreSessionState()
        {
            try
            {
                string statePath = SessionStatePath;
                if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
                    return;

                InspectionSessionState state =
                    JsonConvert.DeserializeObject<InspectionSessionState>(File.ReadAllText(statePath));
                if (state?.items == null)
                    return;

                _results.Clear();
                foreach (InspectionResultViewModel item in state.items)
                {
                    if (string.IsNullOrWhiteSpace(item.SourceFilePath))
                        continue;

                    if (IsUnderDirectory(item.SourceFilePath, _inputPath) && !File.Exists(item.SourceFilePath))
                        continue;

                    if (item.IsPreprocessPending)
                    {
                        item.Stage1 = "미전처리";
                        item.Stage2 = "대기";
                        item.Final = "수신완료";
                        item.ReasonText = "restored_waiting_preprocess";
                        item.IsInferenceCompleted = false;
                    }
                    else if (item.IsPending)
                    {
                        item.IsInferenceCompleted = false;
                    }
                    else if (!item.IsInferenceCompleted)
                    {
                        item.IsInferenceCompleted =
                            string.Equals(item.Final, "정상", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(item.Final, "불량", StringComparison.OrdinalIgnoreCase);
                    }

                    _results.Add(item);
                }

                RefreshResultList(selectFirst: true);
                if (_results.Any(item => item.IsPreprocessPending))
                    StartPreprocessWorkerIfNeeded();
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void ShowResult(InspectionResultViewModel result)
        {
            labelValueFile.Text = result.FileName;
            labelValueStage1.Text = result.Stage1;
            labelValueStage2.Text = result.Stage2;
            labelValueFinal.Text = result.Final;
            labelValueScore.Text = result.ScoreText;
            labelValueDetections.Text = result.DetectionCount.ToString();
            labelValueReasons.Text = result.ReasonText;

            string imagePath = File.Exists(result.ProcessedImagePath) ? result.ProcessedImagePath : result.RawImagePath;
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                using (var mat = Cv2.ImRead(imagePath))
                {
                    if (!mat.Empty())
                    {
                        if (result.Detections != null && result.Detections.Count > 0)
                            DrawDetections(mat, result.Detections);

                        SetDisplayImage(BitmapConverter.ToBitmap(mat), resetView: true);
                    }
                }
            }
        }

        private void UpdateStaticUi()
        {
            labelValuePipeline.Text = _config?.pipeline?.mode ?? "-";
            labelValueInput.Text = _inputPath;
            labelValuePackage.Text = _packagePath;
            if (_batchExporter == null)
            {
                labelValueBatch.Text = string.IsNullOrWhiteSpace(_exportBasePath)
                    ? "-"
                    : _exportBasePath + " (초기화 대기)";
            }
            else if (!string.IsNullOrWhiteSpace(_batchExporter.LastExportDirectory))
            {
                labelValueBatch.Text = _batchExporter.LastExportDirectory;
            }
            else
            {
                labelValueBatch.Text = _exportBasePath + " (배치 마감 시 생성)";
            }

            buttonOpenInput.Enabled = Directory.Exists(_inputPath);
            buttonOpenPackage.Enabled = Directory.Exists(_packagePath);
            buttonOpenBatch.Enabled = Directory.Exists(_exportBasePath);
            buttonRefreshInput.Enabled = _servicesInitialized;
            buttonStatistics.Enabled = _servicesInitialized;
            button2.Enabled = _servicesInitialized;
        }

        private void ClearSelectionDetails()
        {
            labelValueFile.Text = "-";
            labelValueStage1.Text = "-";
            labelValueStage2.Text = "-";
            labelValueFinal.Text = "-";
            labelValueScore.Text = "-";
            labelValueDetections.Text = "-";
            labelValueReasons.Text = "-";
            SetInferenceProgress(0, 0, "자동 검사 대기 중");
        }

        private void ClearSelectionView()
        {
            ClearSelectionDetails();
            ClearDisplayImage();
        }

        private void SetDisplayImage(System.Drawing.Image image, bool resetView)
        {
            ClearDisplayImage();
            _displayImage = image;
            pictureBox1.Image = null;

            if (resetView)
                FitDisplayImageToView();
            else
                pictureBox1.Invalidate();
        }

        private void ClearDisplayImage()
        {
            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
                pictureBox1.Image = null;
            }

            if (_displayImage != null)
            {
                _displayImage.Dispose();
                _displayImage = null;
            }

            _imageScale = 1.0;
            _imageFitScale = 1.0;
            _imageOffset = new System.Drawing.PointF(0f, 0f);
            _isImagePanning = false;
            pictureBox1.Cursor = Cursors.Default;
            pictureBox1.Invalidate();
        }

        private void FitDisplayImageToView()
        {
            if (_displayImage == null)
            {
                pictureBox1.Invalidate();
                return;
            }

            _imageFitScale = CalculateFitScale();
            _imageScale = _imageFitScale;
            CenterDisplayImage();
            pictureBox1.Invalidate();
        }

        private double CalculateFitScale()
        {
            if (_displayImage == null || pictureBox1.ClientSize.Width <= 0 || pictureBox1.ClientSize.Height <= 0)
                return 1.0;

            double scaleX = (double)pictureBox1.ClientSize.Width / _displayImage.Width;
            double scaleY = (double)pictureBox1.ClientSize.Height / _displayImage.Height;
            return Math.Max(0.001, Math.Min(scaleX, scaleY));
        }

        private void CenterDisplayImage()
        {
            if (_displayImage == null)
                return;

            float scaledWidth = (float)(_displayImage.Width * _imageScale);
            float scaledHeight = (float)(_displayImage.Height * _imageScale);
            _imageOffset = new System.Drawing.PointF(
                (pictureBox1.ClientSize.Width - scaledWidth) / 2f,
                (pictureBox1.ClientSize.Height - scaledHeight) / 2f);
        }

        private System.Drawing.RectangleF GetDisplayImageRect()
        {
            if (_displayImage == null)
                return System.Drawing.RectangleF.Empty;

            return new System.Drawing.RectangleF(
                _imageOffset.X,
                _imageOffset.Y,
                (float)(_displayImage.Width * _imageScale),
                (float)(_displayImage.Height * _imageScale));
        }

        private void ZoomImageBy(double deltaScale)
        {
            ZoomImageBy(deltaScale, new System.Drawing.Point(
                pictureBox1.ClientSize.Width / 2,
                pictureBox1.ClientSize.Height / 2));
        }

        private void ZoomImageBy(double deltaScale, System.Drawing.Point anchor)
        {
            if (_displayImage == null)
                return;

            ApplyImageZoom(_imageScale + deltaScale, anchor);
        }

        private void ApplyImageZoom(double requestedScale, System.Drawing.Point anchor)
        {
            if (_displayImage == null)
                return;

            _imageFitScale = CalculateFitScale();
            double minScale = _imageFitScale;
            double maxScale = Math.Max(ImageMaxScale, minScale);
            double nextScale = Math.Max(minScale, Math.Min(maxScale, requestedScale));

            double imageX = (anchor.X - _imageOffset.X) / _imageScale;
            double imageY = (anchor.Y - _imageOffset.Y) / _imageScale;
            _imageScale = nextScale;
            _imageOffset = new System.Drawing.PointF(
                (float)(anchor.X - imageX * _imageScale),
                (float)(anchor.Y - imageY * _imageScale));

            ClampImageOffset();
            pictureBox1.Invalidate();
        }

        private void ClampImageOffset()
        {
            if (_displayImage == null)
                return;

            float viewWidth = pictureBox1.ClientSize.Width;
            float viewHeight = pictureBox1.ClientSize.Height;
            float scaledWidth = (float)(_displayImage.Width * _imageScale);
            float scaledHeight = (float)(_displayImage.Height * _imageScale);

            float offsetX = _imageOffset.X;
            float offsetY = _imageOffset.Y;

            if (scaledWidth <= viewWidth)
                offsetX = (viewWidth - scaledWidth) / 2f;
            else
                offsetX = Math.Min(0f, Math.Max(offsetX, viewWidth - scaledWidth));

            if (scaledHeight <= viewHeight)
                offsetY = (viewHeight - scaledHeight) / 2f;
            else
                offsetY = Math.Min(0f, Math.Max(offsetY, viewHeight - scaledHeight));

            _imageOffset = new System.Drawing.PointF(offsetX, offsetY);
        }

        private InspectionResultViewModel GetCurrentSelectedResult()
        {
            if (listViewResults.SelectedItems.Count == 0)
                return null;

            return listViewResults.SelectedItems[0].Tag as InspectionResultViewModel;
        }

        private static string ToStage1Text(bool anomaExecuted, string anomaDecision)
        {
            if (!anomaExecuted)
                return "미실행";
            if (string.Equals(anomaDecision, "anomaly", StringComparison.OrdinalIgnoreCase))
                return "이상";
            if (string.Equals(anomaDecision, "normal", StringComparison.OrdinalIgnoreCase))
                return "정상";
            return anomaDecision ?? "-";
        }

        private static string ToStage2Text(bool yoloExecuted, string skippedReason, int detectionCount)
        {
            if (!yoloExecuted)
            {
                if (string.Equals(skippedReason, "stage1_normal", StringComparison.OrdinalIgnoreCase))
                    return "건너뜀";
                return "미실행";
            }

            return detectionCount > 0 ? "검출" : "미검출";
        }

        private sealed class InspectionResultViewModel
        {
            public string ImageId { get; set; }
            public string FileName { get; set; }
            public string Stage1 { get; set; }
            public string Stage2 { get; set; }
            public string Final { get; set; }
            public string ScoreText { get; set; }
            public int DetectionCount { get; set; }
            public List<Detection> Detections { get; set; } = new List<Detection>();
            public string ReasonText { get; set; }
            public string RawImagePath { get; set; }
            public string ProcessedImagePath { get; set; }
            public string SourceFilePath { get; set; }
            public bool IsPreprocessPending { get; set; }
            public bool IsPending { get; set; }
            public bool IsInferenceCompleted { get; set; }
        }

        private sealed class InspectionSessionState
        {
            public int schema_version { get; set; } = 1;
            public string saved_at { get; set; }
            public List<InspectionResultViewModel> items { get; set; } = new List<InspectionResultViewModel>();
        }
    }

}
