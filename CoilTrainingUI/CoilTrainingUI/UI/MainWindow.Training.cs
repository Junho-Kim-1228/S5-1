using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private enum TrainingPipelineMode
        {
            AnomaThenYolo,
            AnomaOnly,
            YoloOnly
        }

        private enum YoloTrainingMode
        {
            Fresh,
            FineTune
        }

        private sealed class RunManifestMetadata
        {
            public TrainingPipelineMode PipelineMode { get; set; } = TrainingPipelineMode.AnomaThenYolo;
        }

        private sealed class AnomaInferenceCalibration
        {
            public int InputSize { get; set; }
            public double ScoreThreshold { get; set; }
        }

        private TrainingPipelineMode _trainingPipelineMode = TrainingPipelineMode.AnomaThenYolo;
        private YoloTrainingMode _yoloTrainingMode = YoloTrainingMode.Fresh;
        private string _yoloFineTuneWeightsPath = "";
        private string _yoloFineTuneParentModelId = "";
        private IReadOnlyList<string> _yoloFineTuneReplayBatchKeys = Array.Empty<string>();

        private void TrainPipelineAnomaThenYolo_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingPipelineMode(TrainingPipelineMode.AnomaThenYolo);
        }

        private void TrainPipelineAnomaOnly_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingPipelineMode(TrainingPipelineMode.AnomaOnly);
        }

        private void TrainPipelineYoloOnly_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingPipelineMode(TrainingPipelineMode.YoloOnly);
        }

        private void YoloTrainFresh_Click(object sender, RoutedEventArgs e)
        {
            _yoloTrainingMode = YoloTrainingMode.Fresh;
            _yoloFineTuneWeightsPath = "";
            _yoloFineTuneParentModelId = "";
            _yoloFineTuneReplayBatchKeys = Array.Empty<string>();
            UpdateYoloTrainingModeMenu();
        }

        private void YoloTrainFineTune_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "파인튜닝에 사용할 YOLO best.pt 선택",
                Filter = "PyTorch checkpoint (*.pt)|*.pt",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                UpdateYoloTrainingModeMenu();
                return;
            }

            _yoloTrainingMode = YoloTrainingMode.FineTune;
            _yoloFineTuneWeightsPath = dialog.FileName;
            _yoloFineTuneParentModelId = "";
            _yoloFineTuneReplayBatchKeys = Array.Empty<string>();
            UpdateYoloTrainingModeMenu();
            MessageBox.Show(
                "외부 best.pt는 부모 학습 데이터 계보를 알 수 없어 현재 선택 데이터만 사용합니다.\n" +
                "기존 데이터까지 자동 누적하려면 Train > 모델 관리에서 모델을 선택하세요.",
                "외부 YOLO 체크포인트",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ModelManagement_Click(object sender, RoutedEventArgs e)
        {
            var registry = CreateModelRegistryService();
            var window = new ModelManagerWindow(registry) { Owner = this };
            if (window.ShowDialog() != true || window.RequestedFineTuneModel == null)
                return;

            ModelRegistryEntry selected = window.RequestedFineTuneModel;
            _yoloTrainingMode = YoloTrainingMode.FineTune;
            _yoloFineTuneWeightsPath = selected.YoloBestPtPath;
            _yoloFineTuneParentModelId = selected.Id;
            _yoloFineTuneReplayBatchKeys = selected.SourceBatches.ToList();
            UpdateYoloTrainingModeMenu();
            MessageBox.Show(
                $"파인튜닝 부모 모델: {selected.Id}\n" +
                $"기존 배치 {selected.SourceBatches.Count}개를 새 검수 데이터와 함께 재사용합니다.",
                "YOLO 파인튜닝",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void UpdateYoloTrainingModeMenu()
        {
            YoloTrainFreshMenuItem.IsChecked = _yoloTrainingMode == YoloTrainingMode.Fresh;
            YoloTrainFineTuneMenuItem.IsChecked = _yoloTrainingMode == YoloTrainingMode.FineTune;
            YoloTrainFineTuneMenuItem.Header = _yoloTrainingMode == YoloTrainingMode.FineTune
                ? $"파인튜닝: {IOPath.GetFileName(_yoloFineTuneWeightsPath)}..."
                : "기존 best.pt로 파인튜닝...";
        }

        private void SetTrainingPipelineMode(TrainingPipelineMode mode)
        {
            _trainingPipelineMode = mode;

            TrainPipelineAnomaThenYoloMenuItem.IsChecked = mode == TrainingPipelineMode.AnomaThenYolo;
            TrainPipelineAnomaOnlyMenuItem.IsChecked = mode == TrainingPipelineMode.AnomaOnly;
            TrainPipelineYoloOnlyMenuItem.IsChecked = mode == TrainingPipelineMode.YoloOnly;
        }

        private static string GetTrainingPipelineModeToken(TrainingPipelineMode mode)
        {
            return mode switch
            {
                TrainingPipelineMode.AnomaThenYolo => "anoma_then_yolo",
                TrainingPipelineMode.AnomaOnly => "anoma_only",
                TrainingPipelineMode.YoloOnly => "yolo_only",
                _ => "anoma_then_yolo"
            };
        }

        private static string GetTrainingPipelineDisplayName(TrainingPipelineMode mode)
        {
            return mode switch
            {
                TrainingPipelineMode.AnomaThenYolo => "2단계 (Anoma -> YOLO)",
                TrainingPipelineMode.AnomaOnly => "Anoma만",
                TrainingPipelineMode.YoloOnly => "YOLO만",
                _ => "2단계 (Anoma -> YOLO)"
            };
        }

        private static bool RequiresYoloTraining(TrainingPipelineMode mode)
        {
            return mode is TrainingPipelineMode.AnomaThenYolo or TrainingPipelineMode.YoloOnly;
        }

        private static bool RequiresAnomaTraining(TrainingPipelineMode mode)
        {
            return mode is TrainingPipelineMode.AnomaThenYolo or TrainingPipelineMode.AnomaOnly;
        }

        private async void TrainAll_Click(object sender, RoutedEventArgs e)
        {
            await TrainImageInputsAsync(
                BuildTrainingInputsFromCurrentImageScope(),
                "Train All",
                _trainingPipelineMode);
        }

        private async Task TrainSelectedBatchesAsync(IReadOnlyList<BatchLibraryItem> selectedBatches)
        {
            await TrainImageInputsAsync(
                BuildTrainingInputsFromBatchSelection(selectedBatches),
                "Selected Batch Train",
                _trainingPipelineMode);
        }

        private async Task TrainImageInputsAsync(
            IReadOnlyList<TrainingImageInput> trainingInputs,
            string operationName,
            TrainingPipelineMode pipelineMode)
        {
            string pipelineDisplayName = GetTrainingPipelineDisplayName(pipelineMode);
            bool trainYolo = RequiresYoloTraining(pipelineMode);
            bool trainAnoma = RequiresAnomaTraining(pipelineMode);

            if (trainYolo && _yoloTrainingMode == YoloTrainingMode.FineTune
                && (string.IsNullOrWhiteSpace(_yoloFineTuneWeightsPath)
                    || !File.Exists(_yoloFineTuneWeightsPath)))
            {
                MessageBox.Show(
                    "YOLO 파인튜닝용 best.pt 파일을 찾을 수 없습니다.\nTrain > YOLO 학습 방식에서 다시 선택하세요.",
                    "YOLO Fine-tune",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var progressWindow = new OperationProgressWindow($"{operationName} 진행")
            {
                Owner = this
            };
            progressWindow.UpdateProgress(0, $"작업 준비 중... ({pipelineDisplayName})");
            progressWindow.Show();

            try
            {
                await Task.Delay(50);

                if (trainingInputs == null || trainingInputs.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }

                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(
                    projectRoot,
                    requireYoloPython: trainYolo,
                    requireAnomaPython: trainAnoma);
                string maskOnnxSource = ResolveMaskOnnxPathOrThrow(settings);

                var scopedTrainingInputs = trainingInputs.ToList();
                if (trainYolo
                    && _yoloTrainingMode == YoloTrainingMode.FineTune
                    && _yoloFineTuneReplayBatchKeys.Count > 0)
                {
                    IReadOnlyList<TrainingImageInput> replayInputs = BuildTrainingInputsForBatchKeys(
                        _yoloFineTuneReplayBatchKeys);
                    var replayedBatchKeys = replayInputs
                        .Select(input => input.BatchKey)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var missingReplayBatches = _yoloFineTuneReplayBatchKeys
                        .Where(batchKey => !replayedBatchKeys.Contains(batchKey))
                        .ToList();
                    if (missingReplayBatches.Count > 0)
                    {
                        throw new InvalidOperationException(
                            "부모 모델의 학습 배치를 찾을 수 없어 파인튜닝을 중단했습니다.\n" +
                            string.Join("\n", missingReplayBatches));
                    }
                    scopedTrainingInputs = scopedTrainingInputs
                        .Concat(replayInputs)
                        .GroupBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .ToList();
                    progressWindow.AppendLog(
                        $"[FINE-TUNE] parent={_yoloFineTuneParentModelId}, " +
                        $"replay_batches={_yoloFineTuneReplayBatchKeys.Count}, " +
                        $"replay_candidates={replayInputs.Count}");
                }

                string runRoot = ResolveRunRoot(scopedTrainingInputs);
                Directory.CreateDirectory(runRoot);

                TrainingDatasetSelection selection = _trainingDatasetSelector.Select(scopedTrainingInputs);
                var anomaTrainingInputs = trainAnoma
                    ? selection.AnomaInputs.ToList()
                    : new List<TrainingImageInput>();
                var yoloTrainingInputs = trainYolo
                    ? selection.YoloInputs.ToList()
                    : new List<TrainingImageInput>();
                var effectiveTrainingInputs = anomaTrainingInputs
                    .Concat(yoloTrainingInputs)
                    .GroupBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                var imagePaths = effectiveTrainingInputs
                    .Select(input => input.ImagePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (imagePaths.Count == 0)
                {
                    MessageBox.Show("학습에 사용할 이미지 경로가 없습니다.");
                    return;
                }

                progressWindow.UpdateProgress(5, "학습 데이터 검증 중...");
                var validation = await Task.Run(() =>
                    _datasetValidator.Validate(selection, trainAnoma, trainYolo));
                if (!validation.IsValid)
                {
                    MessageBox.Show(
                        validation.ToErrorMessage(),
                        "Train Dataset Validation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                progressWindow.AppendLog(
                    $"[DATASET] candidates={selection.TotalCandidates}, " +
                    $"anoma={anomaTrainingInputs.Count}, yolo={yoloTrainingInputs.Count}, " +
                    $"yolo_excluded_defect_without_boxes={selection.ExcludedDefectWithoutBoxes}, " +
                    $"unreviewed_or_reviewing={selection.ExcludedUnreviewedOrReviewing}, " +
                    $"user_excluded={selection.ExcludedByUser}, " +
                    $"legacy_migration_required={selection.ExcludedLegacyMigrationRequired}");

                var anomaImagePaths = anomaTrainingInputs
                    .Select(input => input.ImagePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int totalImages = imagePaths.Count;
                var normalImagePaths = effectiveTrainingInputs
                    .Select(input => input.ImagePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(path => _reviewRepository.Load(path).State.Decision == ImageReviewDecision.ConfirmedNormal)
                    .ToList();
                int normalImages = normalImagePaths.Count;

                EnsureEnoughWorkspaceDiskSpace(
                    runRoot,
                    trainAnoma ? anomaImagePaths : imagePaths,
                    trainAnoma ? normalImagePaths : Array.Empty<string>());

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string runDir = IOPath.Combine(runRoot, $"run_{stamp}_{GetTrainingPipelineModeToken(pipelineMode)}");
                string logsDir = IOPath.Combine(runDir, "logs");
                Directory.CreateDirectory(logsDir);

                string stagedRawRoot = IOPath.Combine(runDir, "staged_raw");
                string anomaStagedRawRoot = IOPath.Combine(runDir, "anoma_staged_raw");
                progressWindow.UpdateProgress(15, "학습 raw 입력 staging 중...");
                int stagedImageCount = trainYolo
                    ? await Task.Run(() => StageTrainingInputsForPython(yoloTrainingInputs, stagedRawRoot))
                    : 0;

                if (trainYolo && stagedImageCount == 0)
                {
                    MessageBox.Show("YOLO staged raw 입력 생성 결과가 비었습니다.");
                    return;
                }

                if (trainAnoma)
                {
                    int stagedAnomaImageCount = await Task.Run(() => StageTrainingInputsForPython(
                        anomaTrainingInputs,
                        anomaStagedRawRoot));
                    if (stagedAnomaImageCount == 0)
                    {
                        MessageBox.Show("Anoma 누적 학습 입력 생성 결과가 비었습니다.");
                        return;
                    }
                }

                string yoloWorkspaceRoot = IOPath.Combine(runDir, "yolo_workspace");
                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");
                string yoloPrepLog = IOPath.Combine(logsDir, "yolo_workspace.log");
                Directory.CreateDirectory(yoloOut);
                Directory.CreateDirectory(anomaOut);

                string yoloPythonExe = settings.YoloPythonExe;
                string anomaPythonExe = settings.AnomaPythonExe;
                string aiProjectRoot = settings.AiProjectRoot;
                string yoloPrepScript = IOPath.Combine(aiProjectRoot, "scripts", "prepare_yolo_workspace.py");
                string yoloScript = IOPath.Combine(aiProjectRoot, "scripts", "train_yolo.py");
                string anomaScript = IOPath.Combine(aiProjectRoot, "scripts", "train_anoma.py");

                if ((trainYolo && (!File.Exists(yoloPrepScript) || !File.Exists(yoloScript))) ||
                    (trainAnoma && !File.Exists(anomaScript)))
                {
                    MessageBox.Show(
                        $"AI 학습 폴더가 올바르지 않습니다.\n" +
                        $"폴더: {aiProjectRoot}\n" +
                        $"필수 파일: {(trainAnoma ? "scripts/train_anoma.py" : "")}" +
                        $"{(trainAnoma && trainYolo ? ", " : "")}" +
                        $"{(trainYolo ? "scripts/prepare_yolo_workspace.py, scripts/train_yolo.py" : "")}");
                    return;
                }

                var runner = new PythonRunner();
                using var cts = new CancellationTokenSource();

                if (trainYolo)
                {
                    progressWindow.UpdateProgress(trainAnoma ? 25 : 30, "YOLO workspace 생성 중...");
                    progressWindow.AppendLog("[YOLO-WS] START");

                    string yoloPrepArgs = TrainingCommandBuilder.BuildYoloWorkspaceArgs(
                        settings,
                        stagedRawRoot,
                        yoloWorkspaceRoot);

                    int yoloPrepCode = await runner.RunAsync(
                        pythonExe: yoloPythonExe,
                        scriptPath: yoloPrepScript,
                        args: yoloPrepArgs,
                        workingDir: aiProjectRoot,
                        logPath: yoloPrepLog,
                        ct: cts.Token,
                        onOutputLine: line => progressWindow.AppendLog("[YOLO-WS] " + line)
                    );

                    if (yoloPrepCode != 0)
                    {
                        MessageBox.Show($"YOLO workspace 생성 실패 (ExitCode={yoloPrepCode})\nlogs/yolo_workspace.log 확인");
                        OpenFolder(logsDir);
                        return;
                    }
                }

                if (trainAnoma)
                {
                    int stageStart = trainYolo ? 40 : 40;
                    int stageSpan = trainYolo ? 20 : 45;

                    progressWindow.UpdateProgress(stageStart, "Anoma 학습 중...");
                    progressWindow.AppendLog("[ANOMA] START");
                    int anomaCode = await runner.RunAsync(
                        pythonExe: anomaPythonExe,
                        scriptPath: anomaScript,
                        args: TrainingCommandBuilder.BuildAnomaArgs(
                            settings,
                            anomaStagedRawRoot,
                            anomaOut,
                            IOPath.GetFileName(runDir)),
                        workingDir: aiProjectRoot,
                        logPath: IOPath.Combine(logsDir, "anoma.log"),
                        ct: cts.Token,
                        onOutputLine: line =>
                        {
                            progressWindow.AppendLog("[ANOMA] " + line);
                            int? logPercent = TryParseTrainingPercentFromLog(line);
                            if (logPercent.HasValue)
                            {
                                int mapped = MapStagePercent(logPercent.Value, stageStart, stageSpan);
                                progressWindow.UpdateProgress(mapped, $"Anoma 학습 중... ({logPercent.Value}%)");
                            }
                        }
                    );

                    if (anomaCode != 0)
                    {
                        MessageBox.Show($"Anoma 학습 실패 (ExitCode={anomaCode})\nlogs/anoma.log 확인");
                        OpenFolder(logsDir);
                        return;
                    }
                }

                if (trainYolo)
                {
                    int stageStart = trainAnoma ? 65 : 40;
                    int stageSpan = trainAnoma ? 20 : 45;

                    progressWindow.UpdateProgress(stageStart, "YOLO 학습 중...");
                    progressWindow.AppendLog("[YOLO] START");
                    int yoloCode = await runner.RunAsync(
                        pythonExe: yoloPythonExe,
                        scriptPath: yoloScript,
                        args: TrainingCommandBuilder.BuildYoloArgs(
                            settings,
                            yoloWorkspaceRoot,
                            yoloOut,
                            _yoloTrainingMode == YoloTrainingMode.FineTune,
                            _yoloFineTuneWeightsPath),
                        workingDir: aiProjectRoot,
                        logPath: IOPath.Combine(logsDir, "yolo.log"),
                        ct: cts.Token,
                        onOutputLine: line =>
                        {
                            progressWindow.AppendLog("[YOLO] " + line);
                            int? logPercent = TryParseTrainingPercentFromLog(line);
                            if (logPercent.HasValue)
                            {
                                int mapped = MapStagePercent(logPercent.Value, stageStart, stageSpan);
                                progressWindow.UpdateProgress(mapped, $"YOLO 학습 중... ({logPercent.Value}%)");
                            }
                        }
                    );

                    if (yoloCode != 0)
                    {
                        MessageBox.Show($"YOLO 학습 실패 (ExitCode={yoloCode})\nlogs/yolo.log 확인");
                        OpenFolder(logsDir);
                        return;
                    }
                }

                progressWindow.UpdateProgress(92, $"패키지 생성 중... ({pipelineDisplayName})");
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");
                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if ((trainYolo && !File.Exists(yoloOnnx)) || (trainAnoma && !File.Exists(anomaOnnx)))
                {
                    MessageBox.Show("필수 ONNX 산출물이 없습니다. 스크립트가 필요한 모델 파일을 out 폴더에 생성해야 합니다.");
                    OpenFolder(runDir);
                    return;
                }

                if (trainYolo)
                    File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);

                string yoloBestPt = IOPath.Combine(yoloOut, "best.pt");
                if (trainYolo && File.Exists(yoloBestPt))
                {
                    string trainingAssetsDir = IOPath.Combine(pkgDir, "training");
                    Directory.CreateDirectory(trainingAssetsDir);
                    File.Copy(yoloBestPt, IOPath.Combine(trainingAssetsDir, "yolo_best.pt"), true);
                }

                if (trainAnoma)
                    File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                File.Copy(maskOnnxSource, IOPath.Combine(modelsDir, "mask.onnx"), true);

                var anomaCalibration = trainAnoma
                    ? TryLoadAnomaInferenceCalibration(anomaOut)
                    : null;
                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");
                File.WriteAllText(
                    pipelinePath,
                    JsonSerializer.Serialize(
                        BuildPipelineConfig(settings, pipelineMode, anomaCalibration),
                        new JsonSerializerOptions { WriteIndented = true }
                    ),
                    System.Text.Encoding.UTF8
                );

                VerifyInferencePackageOrThrow(pkgDir, pipelineMode);
                progressWindow.UpdateProgress(100, "학습 및 패키지 생성 완료");

                var sourceBatches = effectiveTrainingInputs
                    .Select(input => input.BatchKey)
                    .Where(batchKey => !string.IsNullOrWhiteSpace(batchKey))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(batchKey => batchKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                bool isFineTuneRun = trainYolo && _yoloTrainingMode == YoloTrainingMode.FineTune;
                string parentModelId = isFineTuneRun ? _yoloFineTuneParentModelId : "";
                string parentWeightsPath = isFineTuneRun ? _yoloFineTuneWeightsPath : "";
                string parentWeightsSha256 = ComputeSha256IfExists(parentWeightsPath);

                WriteRunManifest(
                    runDir: runDir,
                    projectRoot: projectRoot,
                    aiProjectRoot: aiProjectRoot,
                    yoloPythonExe: yoloPythonExe,
                    anomaPythonExe: anomaPythonExe,
                    yoloScript: yoloScript,
                    anomaScript: anomaScript,
                    pipelineMode: pipelineMode,
                    yoloWorkspaceRoot: trainYolo ? yoloWorkspaceRoot : "",
                    anomaWorkspaceRoot: trainAnoma ? anomaStagedRawRoot : "",
                    yoloOutDir: yoloOut,
                    anomaOutDir: anomaOut,
                    inferencePackageDir: pkgDir,
                    settings: settings,
                    totalImages: totalImages,
                    normalImages: normalImages,
                    yoloTrainingMode: isFineTuneRun ? "fine_tune" : "fresh",
                    parentModelId: parentModelId,
                    parentWeightsPath: parentWeightsPath,
                    parentWeightsSha256: parentWeightsSha256,
                    sourceBatches: sourceBatches
                );

                CreateModelRegistryService().Register(new ModelRegistrationContext
                {
                    RunDirectory = runDir,
                    InferencePackageDirectory = pkgDir,
                    PipelineMode = GetTrainingPipelineModeToken(pipelineMode),
                    TrainingMode = isFineTuneRun ? "fine_tune" : "fresh",
                    ParentModelId = parentModelId,
                    ParentWeightsPath = parentWeightsPath,
                    ParentWeightsSha256 = parentWeightsSha256,
                    SourceBatches = sourceBatches,
                    TotalImages = totalImages,
                    NormalImages = normalImages,
                    YoloModel = trainYolo ? settings.YoloTraining.Model : "",
                    AnomaModel = trainAnoma ? settings.AnomaTraining.Model : "",
                    YoloOutDirectory = trainYolo ? yoloOut : "",
                    AnomaOutDirectory = trainAnoma ? anomaOut : ""
                });

                MessageBox.Show($"{operationName} 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{operationName} 중 예외 발생:\n" + ex.Message);
            }
            finally
            {
                progressWindow.Close();
            }
        }

        private List<TrainingImageInput> BuildTrainingInputsFromCurrentImageScope()
        {
            return _images
                .Where(item => !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .GroupBy(item => item.ProcessedPath, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var item = group.First();
                    _inferJsonByImagePath.TryGetValue(item.ProcessedPath, out var inferJsonPath);

                    string batchRoot = GetBatchRootFromImagePath(item.ProcessedPath);
                    return new TrainingImageInput
                    {
                        ImagePath = item.ProcessedPath,
                        InferJsonPath = inferJsonPath ?? "",
                        RequiresInfer = item.RequiresInfer,
                        ExpectedInferenceContextId = _expectedInferenceContextByImagePath.TryGetValue(
                            item.ProcessedPath,
                            out var expectedContextId)
                                ? expectedContextId
                                : "",
                        BatchRoot = batchRoot,
                        BatchKey = BatchLibraryService.GetBatchKey(batchRoot)
                    };
                })
                .ToList();
        }

        private List<TrainingImageInput> BuildTrainingInputsFromBatchSelection(IReadOnlyList<BatchLibraryItem> selectedBatches)
        {
            var inputsByImagePath = new Dictionary<string, TrainingImageInput>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in selectedBatches ?? Array.Empty<BatchLibraryItem>())
            {
                if (batch == null || string.IsNullOrWhiteSpace(batch.BatchRoot) || !Directory.Exists(batch.BatchRoot))
                    continue;

                string manifestPath = IOPath.Combine(batch.BatchRoot, "meta", "manifest.json");
                var manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
                string expectedContextId = InferenceContextValidationService.GetExpectedContextId(manifest);

                foreach (var item in manifest.Items)
                {
                    string imagePath = InferenceBatchPathResolver.ResolveBatchProcessedImagePath(batch.BatchRoot, item);
                    if (inputsByImagePath.ContainsKey(imagePath))
                        continue;

                    inputsByImagePath[imagePath] = new TrainingImageInput
                    {
                        ImagePath = imagePath,
                        InferJsonPath = InferenceBatchPathResolver.ResolveBatchInferJsonPath(batch.BatchRoot, item),
                        RequiresInfer = InferenceBatchPathResolver.DetermineItemRequiresInfer(batch.BatchRoot, manifest, item),
                        ExpectedInferenceContextId = expectedContextId,
                        BatchRoot = batch.BatchRoot,
                        BatchKey = batch.BatchKey
                    };
                }
            }

            return inputsByImagePath.Values
                .OrderBy(input => input.BatchKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IReadOnlyList<TrainingImageInput> BuildTrainingInputsForBatchKeys(
            IReadOnlyList<string> batchKeys)
        {
            if (batchKeys == null || batchKeys.Count == 0)
                return Array.Empty<TrainingImageInput>();

            var requested = new HashSet<string>(batchKeys, StringComparer.OrdinalIgnoreCase);
            BatchImportLoadResult library = _batchImportService.LoadLibrary(
                GetTrainingInboxRoot(),
                includeHidden: true);
            return library.Images
                .Where(record => requested.Contains(record.BatchKey))
                .Select(record => new TrainingImageInput
                {
                    ImagePath = record.ProcessedPath,
                    InferJsonPath = record.InferJsonPath,
                    RequiresInfer = record.RequiresInfer,
                    ExpectedInferenceContextId = record.ExpectedInferenceContextId,
                    BatchRoot = record.BatchRoot,
                    BatchKey = record.BatchKey
                })
                .GroupBy(input => input.ImagePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private string ResolveRunRoot(IReadOnlyList<TrainingImageInput> trainingInputs)
        {
            var batchRoots = (trainingInputs ?? Array.Empty<TrainingImageInput>())
                .Select(input => input.BatchRoot)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (batchRoots.Count == 1)
            {
                string imagesDir = IOPath.Combine(batchRoots[0], "images");
                return Directory.Exists(imagesDir)
                    ? IOPath.Combine(imagesDir, "_train_runs")
                    : IOPath.Combine(batchRoots[0], "_train_runs");
            }

            return IOPath.Combine(GetTrainingInboxRoot(), "_train_runs");
        }

        private static string GetBatchRootFromImagePath(string imagePath)
        {
            string? imageDir = IOPath.GetDirectoryName(imagePath);
            if (string.IsNullOrWhiteSpace(imageDir))
                return "";

            string dirName = IOPath.GetFileName(imageDir.TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar));
            if (string.Equals(dirName, "images", StringComparison.OrdinalIgnoreCase))
                return Directory.GetParent(imageDir)?.FullName ?? imageDir;

            return imageDir;
        }

        private int StageTrainingInputsForPython(
            IReadOnlyList<TrainingImageInput> trainingInputs,
            string stagedRawRoot)
        {
            if (trainingInputs == null)
                throw new ArgumentNullException(nameof(trainingInputs));

            if (Directory.Exists(stagedRawRoot))
                Directory.Delete(stagedRawRoot, recursive: true);

            Directory.CreateDirectory(stagedRawRoot);

            var usedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stagedStateWriter = new ImageStateService();
            int stagedCount = 0;

            foreach (var input in trainingInputs)
            {
                if (input == null || string.IsNullOrWhiteSpace(input.ImagePath) || !File.Exists(input.ImagePath))
                    continue;

                string batchKey = SanitizePathSegment(
                    string.IsNullOrWhiteSpace(input.BatchKey) ? "batch_unknown" : input.BatchKey
                );

                string originalName = IOPath.GetFileNameWithoutExtension(input.ImagePath);
                string originalExt = IOPath.GetExtension(input.ImagePath);
                string safeStem = SanitizePathSegment(string.IsNullOrWhiteSpace(originalName) ? "image" : originalName);
                string safeExt = string.IsNullOrWhiteSpace(originalExt) ? ".bmp" : originalExt;

                int copyIndex = 0;
                string relativeImagePath;
                do
                {
                    string indexedStem = copyIndex == 0 ? safeStem : $"{safeStem}__{copyIndex:000}";
                    relativeImagePath = IOPath.Combine(batchKey, indexedStem + safeExt);
                    copyIndex++;
                }
                while (!usedRelativePaths.Add(relativeImagePath));

                string destImagePath = IOPath.Combine(stagedRawRoot, relativeImagePath);
                Directory.CreateDirectory(IOPath.GetDirectoryName(destImagePath)!);

                File.Copy(input.ImagePath, destImagePath, overwrite: true);

                ReviewStateLoadResult load = _reviewRepository.Load(input.ImagePath);
                if (!load.HasReviewFile || load.IsLegacyProjection || load.ParseFailed ||
                    load.State.Decision is not (ImageReviewDecision.ConfirmedNormal or ImageReviewDecision.ConfirmedDefect))
                {
                    throw new InvalidDataException(
                        $"확정된 새 검수 상태만 staging할 수 있습니다: {IOPath.GetFileName(input.ImagePath)}");
                }

                ReviewState review = load.State;
                var stagedState = new ImageStateDto
                {
                    IsNormal = review.Decision == ImageReviewDecision.ConfirmedNormal,
                    HasManualAnomalyDecision = true,
                    HasManualYoloDecision = review.BoxReview is BoxReviewDecision.Confirmed or BoxReviewDecision.NotApplicable,
                    ReviewStatus = ReviewStatus.ReviewDone,
                    DecisionSource = review.DecisionSource.ToString(),
                    ReviewedAt = review.DecisionConfirmedAtUtc,
                    Labels = review.Boxes.Select(box => new LabelDto
                    {
                        ClassName = box.ClassName,
                        X = box.X,
                        Y = box.Y,
                        Width = box.Width,
                        Height = box.Height,
                        Source = box.Source,
                        InferConf = box.PredictionConfidence
                    }).ToList()
                };
                stagedStateWriter.Save(destImagePath, stagedState);
                stagedCount++;
            }

            return stagedCount;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "item";

            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char ch in value)
            {
                sb.Append(ch switch
                {
                    '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' => '_',
                    _ => ch
                });
            }

            string sanitized = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
        }

        private void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private object BuildPipelineConfig(
            AppSettings settings,
            TrainingPipelineMode pipelineMode,
            AnomaInferenceCalibration? anomaCalibration = null)
        {
            return InferencePipelineConfigBuilder.Build(
                settings,
                GetTrainingPipelineModeToken(pipelineMode),
                GetTrainingPipelineDisplayName(pipelineMode),
                anomaCalibration?.InputSize,
                anomaCalibration?.ScoreThreshold);
        }

        private static string ResolveMaskOnnxPathOrThrow(AppSettings settings)
        {
            if (settings?.MaskInfer == null || string.IsNullOrWhiteSpace(settings.MaskInfer.ModelPath))
                throw new InvalidOperationException("MaskInfer.ModelPath가 설정되지 않았습니다.");

            string configured = settings.MaskInfer.ModelPath.Trim();
            string resolved = IOPath.IsPathRooted(configured)
                ? IOPath.GetFullPath(configured)
                : IOPath.GetFullPath(IOPath.Combine(
                    settings.AiProjectRoot,
                    configured.Replace('/', IOPath.DirectorySeparatorChar)));

            if (!File.Exists(resolved) || new FileInfo(resolved).Length == 0)
            {
                throw new FileNotFoundException(
                    "Mask ONNX 모델을 찾을 수 없습니다. 개발 환경에서 Mask 체크포인트를 ONNX로 먼저 내보내세요.",
                    resolved);
            }
            return resolved;
        }

        private RunManifestMetadata? TryLoadRunManifestMetadata(string runDir)
        {
            string manifestPath = IOPath.Combine(runDir, "run_manifest.json");
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                if (!root.TryGetProperty("Pipeline", out var pipelineElement))
                    return null;

                if (!pipelineElement.TryGetProperty("Mode", out var modeElement))
                    return null;

                string? modeToken = modeElement.GetString();
                return new RunManifestMetadata
                {
                    PipelineMode = TryParseTrainingPipelineMode(modeToken)
                };
            }
            catch
            {
                return null;
            }
        }

        private static AnomaInferenceCalibration? TryLoadAnomaInferenceCalibration(string anomaOutDir)
        {
            string configPath = IOPath.Combine(anomaOutDir, "inference_config.json");
            if (!File.Exists(configPath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("input_size", out JsonElement inputSizeElement)
                    || !inputSizeElement.TryGetInt32(out int inputSize)
                    || inputSize <= 0
                    || !root.TryGetProperty("score_threshold", out JsonElement thresholdElement)
                    || !thresholdElement.TryGetDouble(out double scoreThreshold)
                    || double.IsNaN(scoreThreshold)
                    || double.IsInfinity(scoreThreshold))
                {
                    return null;
                }

                return new AnomaInferenceCalibration
                {
                    InputSize = inputSize,
                    ScoreThreshold = scoreThreshold
                };
            }
            catch
            {
                return null;
            }
        }

        private static TrainingPipelineMode TryParseTrainingPipelineMode(string? modeToken)
        {
            return (modeToken ?? "").Trim().ToLowerInvariant() switch
            {
                "anoma_only" => TrainingPipelineMode.AnomaOnly,
                "yolo_only" => TrainingPipelineMode.YoloOnly,
                _ => TrainingPipelineMode.AnomaThenYolo
            };
        }

        private void VerifyInferencePackageOrThrow(string pkgDir, TrainingPipelineMode pipelineMode)
        {
            string modelsDir = IOPath.Combine(pkgDir, "models");
            string cfgDir = IOPath.Combine(pkgDir, "config");

            string yoloOnnx = IOPath.Combine(modelsDir, "yolo.onnx");
            string anomaOnnx = IOPath.Combine(modelsDir, "anoma.onnx");
            string maskOnnx = IOPath.Combine(modelsDir, "mask.onnx");
            string pipeline = IOPath.Combine(cfgDir, "pipeline.json");

            if (!File.Exists(maskOnnx))
                throw new FileNotFoundException("Missing mask.onnx in inference_package/models", maskOnnx);

            if (RequiresYoloTraining(pipelineMode) && !File.Exists(yoloOnnx))
                throw new FileNotFoundException("Missing yolo.onnx in inference_package/models", yoloOnnx);

            if (RequiresAnomaTraining(pipelineMode) && !File.Exists(anomaOnnx))
                throw new FileNotFoundException("Missing anoma.onnx in inference_package/models", anomaOnnx);

            if (!File.Exists(pipeline))
                throw new FileNotFoundException("Missing pipeline.json in inference_package/config", pipeline);

            if (RequiresYoloTraining(pipelineMode) && new FileInfo(yoloOnnx).Length <= 0)
                throw new InvalidOperationException("yolo.onnx is empty (0 bytes).");

            if (RequiresAnomaTraining(pipelineMode) && new FileInfo(anomaOnnx).Length <= 0)
                throw new InvalidOperationException("anoma.onnx is empty (0 bytes).");

            if (new FileInfo(maskOnnx).Length <= 0)
                throw new InvalidOperationException("mask.onnx is empty (0 bytes).");

            using var doc = JsonDocument.Parse(File.ReadAllText(pipeline));
            var root = doc.RootElement;

            Require(root, "schema_version");
            Require(root, "pipeline");
            Require(root, "input");
            Require(root, "output");
            Require(root, "mask");
            Require(root, "auto_review");

            var autoReview = root.GetProperty("auto_review");
            Require(autoReview, "enabled");
            Require(autoReview, "policy_version");
            Require(autoReview, "anoma_normal_threshold_multiplier");
            Require(autoReview, "anoma_defect_threshold_multiplier");
            Require(autoReview, "yolo_box_min_confidence");
            Require(autoReview, "audit_sample_rate");

            var mask = root.GetProperty("mask");
            Require(mask, "model");
            Require(mask, "input_size");
            Require(mask, "resize_mode");
            Require(mask, "image_mean");
            Require(mask, "image_std");
            Require(mask, "confidence_percentile");
            Require(mask, "confidence_threshold");
            Require(mask, "mask_threshold");
            Require(mask, "min_component_area");
            Require(mask, "keep_largest_component");
            Require(mask, "preserve_inner_holes");
            Require(mask, "min_hole_area");

            var pipelineSection = root.GetProperty("pipeline");
            Require(pipelineSection, "mode");
            Require(pipelineSection, "stage1");
            Require(pipelineSection, "required_models");

            if (RequiresYoloTraining(pipelineMode))
            {
                Require(root, "yolo");
                var yolo = root.GetProperty("yolo");
                Require(yolo, "model");
                Require(yolo, "imgsz");
                Require(yolo, "conf_thres");
                Require(yolo, "iou_thres");
                Require(yolo, "max_det");
                Require(yolo, "class_map");
            }

            if (RequiresAnomaTraining(pipelineMode))
            {
                Require(root, "anoma");
                var anoma = root.GetProperty("anoma");
                Require(anoma, "model");
                Require(anoma, "mode");
                Require(anoma, "input_size");
                Require(anoma, "score_thres");
            }

            if (pipelineMode == TrainingPipelineMode.AnomaThenYolo)
            {
                Require(pipelineSection, "stage2");
                Require(pipelineSection, "skip_yolo_when_stage1_normal");
                if (!string.Equals(
                        pipelineSection.GetProperty("mode").GetString(),
                        "anoma_then_yolo",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("pipeline.json mode must be anoma_then_yolo.");
                }
                if (pipelineSection.GetProperty("skip_yolo_when_stage1_normal").ValueKind != JsonValueKind.True)
                {
                    throw new InvalidOperationException(
                        "pipeline.json skip_yolo_when_stage1_normal must be true.");
                }
            }
        }

        private void Require(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out _))
                throw new InvalidOperationException($"pipeline.json missing required field: {prop}");
        }

        private static int? TryParseTrainingPercentFromLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var progressMatch = Regex.Match(line, @"PROGRESS\s*:\s*(\d{1,3})", RegexOptions.IgnoreCase);
            if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out int progressPercent))
                return Math.Clamp(progressPercent, 0, 100);

            var epochMatch = Regex.Match(line, @"(?:epoch|Epoch)\s+(\d+)\s*/\s*(\d+)");
            if (epochMatch.Success &&
                int.TryParse(epochMatch.Groups[1].Value, out int currentEpoch) &&
                int.TryParse(epochMatch.Groups[2].Value, out int totalEpoch) &&
                totalEpoch > 0)
            {
                return Math.Clamp((int)Math.Round((currentEpoch * 100.0) / totalEpoch), 0, 100);
            }

            var ratioMatch = Regex.Match(line, @"\b(\d+)\s*/\s*(\d+)\b");
            if (ratioMatch.Success &&
                int.TryParse(ratioMatch.Groups[1].Value, out int current) &&
                int.TryParse(ratioMatch.Groups[2].Value, out int total) &&
                total > 0 &&
                current <= total)
            {
                return Math.Clamp((int)Math.Round((current * 100.0) / total), 0, 100);
            }

            return null;
        }

        private static int MapStagePercent(int stagePercent, int stageStart, int stageSpan)
        {
            return stageStart + (int)Math.Round(Math.Clamp(stagePercent, 0, 100) * stageSpan / 100.0);
        }

        private static void EnsureEnoughWorkspaceDiskSpace(
            string runRoot,
            IReadOnlyList<string> allImagePaths,
            IReadOnlyList<string> normalImagePaths)
        {
            string fullRunRoot = IOPath.GetFullPath(runRoot);
            string? driveRoot = IOPath.GetPathRoot(fullRunRoot);
            if (string.IsNullOrWhiteSpace(driveRoot))
                return;

            var drive = new DriveInfo(driveRoot);
            long totalCopyBytes = SumFileSizes(allImagePaths) + SumFileSizes(normalImagePaths);

            long requiredBytes = (long)Math.Ceiling(totalCopyBytes * 1.15) + (256L * 1024L * 1024L);
            long freeBytes = drive.AvailableFreeSpace;

            if (freeBytes >= requiredBytes)
                return;

            throw new InvalidOperationException(
                "학습용 workspace를 만들 디스크 여유 공간이 부족합니다.\n" +
                $"대상 드라이브: {drive.Name}\n" +
                $"예상 필요 공간: {FormatBytes(requiredBytes)}\n" +
                $"현재 여유 공간: {FormatBytes(freeBytes)}\n\n" +
                "_train_runs 폴더를 정리하거나 여유 공간이 더 큰 드라이브를 사용하세요.");
        }

        private static long SumFileSizes(IEnumerable<string> paths)
        {
            long total = 0;
            foreach (var path in paths ?? Array.Empty<string>())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        total += new FileInfo(path).Length;
                }
                catch
                {
                }
            }

            return total;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = Math.Max(bytes, 0);
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        private void WriteRunManifest(
            string runDir,
            string projectRoot,
            string aiProjectRoot,
            string yoloPythonExe,
            string anomaPythonExe,
            string yoloScript,
            string anomaScript,
            TrainingPipelineMode pipelineMode,
            string yoloWorkspaceRoot,
            string anomaWorkspaceRoot,
            string yoloOutDir,
            string anomaOutDir,
            string inferencePackageDir,
            AppSettings settings,
            int totalImages,
            int normalImages,
            string yoloTrainingMode,
            string parentModelId,
            string parentWeightsPath,
            string parentWeightsSha256,
            IReadOnlyList<string>? sourceBatches = null
        )
        {
            var manifest = new
            {
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProjectRoot = projectRoot,
                AiProjectRoot = aiProjectRoot,
                Python = new
                {
                    Yolo = yoloPythonExe,
                    Anoma = anomaPythonExe
                },
                Scripts = new { Yolo = yoloScript, Anoma = anomaScript },
                Pipeline = new
                {
                    Mode = GetTrainingPipelineModeToken(pipelineMode),
                    DisplayName = GetTrainingPipelineDisplayName(pipelineMode)
                },
                YoloTraining = new
                {
                    Mode = yoloTrainingMode,
                    ParentModelId = parentModelId,
                    ParentWeightsPath = parentWeightsPath,
                    ParentWeightsSha256 = parentWeightsSha256
                },
                Workspaces = new { Yolo = yoloWorkspaceRoot, Anoma = anomaWorkspaceRoot },
                Outputs = new { YoloOut = yoloOutDir, AnomaOut = anomaOutDir, InferencePackage = inferencePackageDir },
                Dataset = new { TotalImages = totalImages, NormalImages = normalImages },
                SourceBatches = sourceBatches ?? Array.Empty<string>(),
                Settings = settings
            };

            string path = IOPath.Combine(runDir, "run_manifest.json");
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        private ModelRegistryService CreateModelRegistryService()
        {
            string registryDirectory = IOPath.Combine(GetTrainingInboxRoot(), "_model_registry");
            return new ModelRegistryService(registryDirectory);
        }

        private static string ComputeSha256IfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private void BuildPackageOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentInputs = BuildTrainingInputsFromCurrentImageScope();
                if (currentInputs.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }

                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(
                    projectRoot,
                    requireYoloPython: false,
                    requireAnomaPython: false);
                string maskOnnxSource = ResolveMaskOnnxPathOrThrow(settings);

                string runRoot = ResolveRunRoot(currentInputs);
                if (!Directory.Exists(runRoot))
                {
                    MessageBox.Show("_train_runs 폴더가 없습니다.");
                    return;
                }

                var latestRunDir = Directory.GetDirectories(runRoot, "run_*")
                                            .Select(dir => new DirectoryInfo(dir))
                                            .OrderByDescending(dir => dir.CreationTimeUtc)
                                            .FirstOrDefault();

                if (latestRunDir == null)
                {
                    MessageBox.Show("run_* 폴더가 없습니다.");
                    return;
                }

                string runDir = latestRunDir.FullName;
                var runMetadata = TryLoadRunManifestMetadata(runDir);
                var pipelineMode = runMetadata?.PipelineMode ?? TrainingPipelineMode.AnomaThenYolo;

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");

                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if ((RequiresYoloTraining(pipelineMode) && !File.Exists(yoloOnnx)) ||
                    (RequiresAnomaTraining(pipelineMode) && !File.Exists(anomaOnnx)))
                {
                    MessageBox.Show(
                        $"필요한 ONNX가 없습니다. (파이프라인: {GetTrainingPipelineDisplayName(pipelineMode)})\n\n" +
                        $"yolo: {yoloOnnx}\n" +
                        $"anoma: {anomaOnnx}\n\n" +
                        "현재 파이프라인 학습을 먼저 수행했는지 확인하세요."
                    );
                    OpenFolder(runDir);
                    return;
                }

                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");

                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                if (RequiresYoloTraining(pipelineMode))
                    File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);

                string yoloBestPt = IOPath.Combine(yoloOut, "best.pt");
                if (RequiresYoloTraining(pipelineMode) && File.Exists(yoloBestPt))
                {
                    string trainingAssetsDir = IOPath.Combine(pkgDir, "training");
                    Directory.CreateDirectory(trainingAssetsDir);
                    File.Copy(yoloBestPt, IOPath.Combine(trainingAssetsDir, "yolo_best.pt"), true);
                }

                if (RequiresAnomaTraining(pipelineMode))
                    File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                File.Copy(maskOnnxSource, IOPath.Combine(modelsDir, "mask.onnx"), true);

                var anomaCalibration = RequiresAnomaTraining(pipelineMode)
                    ? TryLoadAnomaInferenceCalibration(anomaOut)
                    : null;
                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");

                File.WriteAllText(
                    pipelinePath,
                    JsonSerializer.Serialize(
                        BuildPipelineConfig(settings, pipelineMode, anomaCalibration),
                        new JsonSerializerOptions { WriteIndented = true }),
                    System.Text.Encoding.UTF8
                );

                VerifyInferencePackageOrThrow(pkgDir, pipelineMode);

                MessageBox.Show($"Package Only 완료 ({GetTrainingPipelineDisplayName(pipelineMode)})\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Build Package Only 실패:\n" + ex.Message);
            }
        }
    }
}
