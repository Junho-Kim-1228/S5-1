using CoilInspectionApp.Interface;
using CoilInspectionApp.Logging;
using CoilInspectionApp.Preprocess;
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
        private readonly OnnxModelTester _modelTester = new OnnxModelTester();
        private readonly List<InspectionResultViewModel> _results = new List<InspectionResultViewModel>();
        private BatchExporter _batchExporter;
        private MaskRuntimeRunner _maskRuntimeRunner;
        private PipelinePackageConfig _config;
        private string _inputPath = "";
        private string _packagePath = "";
        private string _maskPythonExe = "";
        private string _maskRuntimePath = "";
        private volatile bool _isPreprocessing;
        private volatile bool _preprocessAgainRequested;
        private bool _isClosingBatch;
        private System.Drawing.Image _displayImage;
        private double _imageScale = 1.0;
        private double _imageFitScale = 1.0;
        private System.Drawing.PointF _imageOffset = new System.Drawing.PointF(0f, 0f);
        private bool _isImagePanning;
        private System.Drawing.Point _lastPanPoint;

        private const double ImageZoomStep = 0.10;
        private const double ImageMaxScale = 10.0;

        public Form1()
        {
            InitializeComponent();
            buttonZoomIn.BringToFront();
            buttonZoomOut.BringToFront();
            buttonZoomFit.BringToFront();
            InitSystem();
        }

        private void InitSystem()
        {
            try
            {
                _inputPath = ResolveConfiguredPath(ConfigurationManager.AppSettings["InputDir"], @"C:\InspectionTest\input");
                _packagePath = ResolvePackagePath(ConfigurationManager.AppSettings["InferencePackagePath"], @".\InferencePackage");
                _maskPythonExe = ResolveConfiguredExecutable(ConfigurationManager.AppSettings["MaskPythonExe"], "python");
                _maskRuntimePath = ResolveMaskRuntimePath(ConfigurationManager.AppSettings["MaskRuntimePath"], @".\mask-runtime");
                string exportBasePath = ResolveConfiguredPath(ConfigurationManager.AppSettings["ExportBasePath"], @"C:\InspectionTest\TrainingBatches");

                _config = LoadPipelinePackageOrThrow(_packagePath);
                LoadRequiredModelsOrThrow(_packagePath, _config);
                _maskRuntimeRunner = new MaskRuntimeRunner(_maskPythonExe, _maskRuntimePath);

                Directory.CreateDirectory(_inputPath);

                _batchExporter = new BatchExporter(exportBasePath);
                _batchExporter.StartOrResumeBatch();
                RestoreSessionState();
                UpdateStaticUi();

                _dw = new DirectoryWatcher();
                _dw.OnFileCreated += filePath => Invoke(new Action(() => RegisterIncomingFile(filePath)));
                _dw.OnFileDeleted += filePath => Invoke(new Action(() => RemoveIncomingFile(filePath)));
                _dw.StartWatch(_inputPath);
                RegisterExistingInputFiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"초기화 오류: {ex.Message}", "CoilInspectionApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private static string ResolveConfiguredExecutable(string configuredValue, string fallbackValue)
        {
            string value = string.IsNullOrWhiteSpace(configuredValue) ? fallbackValue : configuredValue;
            if (Path.IsPathRooted(value))
                return value;

            if (value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0)
            {
                string normalized = value.Replace('/', '\\');
                return Path.GetFullPath(Path.Combine(Application.StartupPath, normalized));
            }

            return value;
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

        private static string ResolveMaskRuntimePath(string configuredValue, string fallbackValue)
        {
            string primary = ResolveConfiguredPath(configuredValue, fallbackValue);
            if (Directory.Exists(primary))
                return primary;

            string startupPath = Application.StartupPath;
            string[] candidates =
            {
                primary,
                Path.Combine(startupPath, "mask-runtime"),
                Path.Combine(startupPath, "..", "..", "mask-runtime"),
                Path.Combine(startupPath, "..", "..", "..", "mask-runtime"),
            };

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath))
                    return fullPath;
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

            return config;
        }

        private void LoadRequiredModelsOrThrow(string packagePath, PipelinePackageConfig config)
        {
            if (config.RequiresAnoma)
            {
                if (config.anoma == null || string.IsNullOrWhiteSpace(config.anoma.model))
                    throw new InvalidOperationException("pipeline.json missing anoma.model");

                string anomaPath = Path.Combine(packagePath, config.anoma.model);
                if (!File.Exists(anomaPath))
                    throw new FileNotFoundException("anoma model not found.", anomaPath);

                _modelTester.LoadAnomaModel(anomaPath);
            }

            if (config.RequiresYolo)
            {
                if (config.yolo == null || string.IsNullOrWhiteSpace(config.yolo.model))
                    throw new InvalidOperationException("pipeline.json missing yolo.model");

                string yoloPath = Path.Combine(packagePath, config.yolo.model);
                if (!File.Exists(yoloPath))
                    throw new FileNotFoundException("yolo model not found.", yoloPath);

                _modelTester.LoadYoloModel(yoloPath);
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
                    _isPreprocessing = false;
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
                    _isPreprocessing = false;
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
            return _maskRuntimeRunner.RunBatch(sourcePaths, preprocessOutputDir, onMaskedImageReady);
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
                        MessageBox.Show($"자동 전처리 오류: {ex.Message}", "CoilInspectionApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                System.Threading.Thread.Sleep(1500);

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
                        MessageBox.Show($"자동 전처리 오류: {ex.Message}", "CoilInspectionApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                BatchExporter.PreparedImagePaths savedPaths =
                    _batchExporter.SavePreparedImages(item.ImageId, rawImg, maskedImg);

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

        private void RunPendingInference()
        {
            List<InspectionResultViewModel> pendingItems = _results
                .Where(item => item.IsPending)
                .ToList();

            if (pendingItems.Count == 0)
            {
                MessageBox.Show("추론 대기 중인 항목이 없습니다.");
                return;
            }

            SetInferenceProgress(0, pendingItems.Count, "추론 시작");
            button3.Enabled = false;
            try
            {
                for (int index = 0; index < pendingItems.Count; index++)
                {
                    InspectionResultViewModel pendingItem = pendingItems[index];
                    SetInferenceProgress(index, pendingItems.Count, $"추론 중: {pendingItem.FileName}");
                    RunInferenceForPreparedItem(pendingItem);
                    SetInferenceProgress(index + 1, pendingItems.Count, $"추론 완료: {index + 1}/{pendingItems.Count}");
                    Application.DoEvents();
                }
            }
            finally
            {
                button3.Enabled = true;
            }

            RefreshResultList(selectFirst: true);
        }

        private void SetInferenceProgress(int completed, int total, string message)
        {
            int safeTotal = Math.Max(total, 1);
            int safeCompleted = Math.Max(0, Math.Min(completed, safeTotal));

            progressBarInference.Minimum = 0;
            progressBarInference.Maximum = safeTotal;
            progressBarInference.Value = safeCompleted;
            labelInferenceProgress.Text = total <= 0
                ? message
                : $"{message} ({safeCompleted}/{total})";
            labelInferenceProgress.Refresh();
            progressBarInference.Refresh();
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

                        SetDisplayImage(BitmapConverter.ToBitmap(displayImg), resetView: true);

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

                        SetDisplayImage(BitmapConverter.ToBitmap(displayImg), resetView: true);

                        _logger.SaveResult(result.FileName, finalIsDefect ? "NG" : "OK", anomaScore);
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

                    result.Stage1 = ToStage1Text(anomaExecuted, anomaDecision);
                    result.Stage2 = ToStage2Text(yoloExecuted, yoloSkippedReason, detections.Count);
                    result.Final = finalIsDefect ? "불량" : "정상";
                    result.ScoreText = anomaExecuted ? anomaScore.ToString("0.000") : "-";
                    result.DetectionCount = detections.Count;
                    result.Detections = detections;
                    result.ReasonText = reasons.Count > 0 ? string.Join(", ", reasons) : "-";
                    result.IsPending = false;
                    result.IsInferenceCompleted = true;
                    SaveSessionState();
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show($"추론 오류: {ex.Message}", "CoilInspectionApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private void button1_Click(object sender, EventArgs e) => SelectAndQueueImage();

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                if (_results.Count == 0)
                {
                    MessageBox.Show("마감할 항목이 없습니다.");
                    return;
                }

                InspectionResultViewModel incompleteItem = _results.FirstOrDefault(item => !item.IsInferenceCompleted);
                if (incompleteItem != null)
                {
                    MessageBox.Show(
                        $"추론을 마치지 않은 이미지가 있습니다.\n먼저 추론을 완료하세요.\n\n파일: {incompleteItem.FileName}\n상태: {incompleteItem.Final}",
                        "CoilInspectionApp",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _isClosingBatch = true;
                try
                {
                    _batchExporter.CloseBatch();
                    DeleteBatchInputFiles();
                    MessageBox.Show("배치 마감 완료 (DONE.flag 생성됨)");
                    _batchExporter.StartNewBatch();
                    _results.Clear();
                    listViewResults.Items.Clear();
                    ClearSelectionView();
                    SaveSessionState();
                    UpdateStaticUi();
                }
                finally
                {
                    _isClosingBatch = false;
                }
            }
            catch (Exception ex)
            {
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

        private void button3_Click(object sender, EventArgs e)
        {
            RunPendingInference();
        }

        private void buttonRefreshInput_Click(object sender, EventArgs e)
        {
            RefreshInputListFromFolder();
        }

        private void buttonOpenInput_Click(object sender, EventArgs e)
        {
            OpenFolderPath(_inputPath);
        }

        private void buttonOpenBatch_Click(object sender, EventArgs e)
        {
            OpenFolderPath(_batchExporter?.ExportBaseDirectory);
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
            SaveSessionState();
            ClearDisplayImage();
            base.OnFormClosing(e);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateStaticUi();
            ClearSelectionDetails();
        }

        private void listViewResults_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewResults.SelectedIndices.Count == 0)
                return;

            int index = listViewResults.SelectedIndices[0];
            if (index < 0 || index >= _results.Count)
                return;

            ShowResult(_results[index]);
        }

        private void RefreshResultList(bool selectFirst)
        {
            listViewResults.BeginUpdate();
            try
            {
                listViewResults.Items.Clear();
                foreach (InspectionResultViewModel result in _results)
                {
                    var item = new ListViewItem(result.FileName);
                    item.SubItems.Add(result.Stage1);
                    item.SubItems.Add(result.Stage2);
                    item.SubItems.Add(result.Final);
                    item.SubItems.Add(result.ScoreText);
                    item.Tag = result;
                    listViewResults.Items.Add(item);
                }

                if (selectFirst && listViewResults.Items.Count > 0)
                {
                    listViewResults.Items[0].Selected = true;
                    listViewResults.Select();
                    ShowResult(_results[0]);
                }
            }
            finally
            {
                listViewResults.EndUpdate();
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
                labelValueBatch.Text = "-";
            }
            else if (!string.IsNullOrWhiteSpace(_batchExporter.LastExportDirectory))
            {
                labelValueBatch.Text = _batchExporter.LastExportDirectory;
            }
            else
            {
                labelValueBatch.Text = _batchExporter.ExportBaseDirectory + " (배치 마감 시 생성)";
            }
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
            SetInferenceProgress(0, 0, "추론 대기 중");
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
            if (listViewResults.SelectedIndices.Count == 0)
                return null;

            int index = listViewResults.SelectedIndices[0];
            if (index < 0 || index >= _results.Count)
                return null;

            return _results[index];
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

    public sealed class PipelinePackageConfig
    {
        public int schema_version { get; set; }
        public PipelineSection pipeline { get; set; } = new PipelineSection();
        public YoloSection yolo { get; set; }
        public AnomaSection anoma { get; set; }

        public bool RequiresYolo =>
            (pipeline.required_models?.Any(model => string.Equals(model, "yolo", StringComparison.OrdinalIgnoreCase)) == true)
            || yolo != null;

        public bool RequiresAnoma =>
            (pipeline.required_models?.Any(model => string.Equals(model, "anoma", StringComparison.OrdinalIgnoreCase)) == true)
            || anoma != null;

        public IReadOnlyDictionary<int, string> ClassNamesById
        {
            get
            {
                if (yolo?.class_map == null)
                    return new Dictionary<int, string>();

                return yolo.class_map.ToDictionary(kv => kv.Value, kv => kv.Key);
            }
        }
    }

    public sealed class PipelineSection
    {
        public string mode { get; set; } = "anoma_then_yolo";
        public string stage1 { get; set; } = "anoma";
        public string stage2 { get; set; } = "";
        public bool skip_yolo_when_stage1_normal { get; set; }
        public List<string> required_models { get; set; } = new List<string>();
    }

    public sealed class YoloSection
    {
        public string model { get; set; } = "";
        public int imgsz { get; set; } = 640;
        public bool letterbox { get; set; } = true;
        public float conf_thres { get; set; } = 0.25f;
        public float iou_thres { get; set; } = 0.45f;
        public int max_det { get; set; } = 300;
        public Dictionary<string, int> class_map { get; set; } = new Dictionary<string, int>();
    }

    public sealed class AnomaSection
    {
        public string model { get; set; } = "";
        public string mode { get; set; } = "crop";
        public int input_size { get; set; } = 640;
        public float score_thres { get; set; } = 0.5f;
        public int crop_padding_px { get; set; }
    }
}
