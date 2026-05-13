using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void RequestSaveLabelsDebounced(string imagePath)
        {
            if (_isLoadingImage) return;
            if (string.IsNullOrEmpty(imagePath)) return;

            _pendingSaveImagePath = imagePath;

            _labelSaveDebounceTimer.Stop();
            _labelSaveDebounceTimer.Start();
        }

        private async void TrainAll_Click(object sender, RoutedEventArgs e)
        {
            await TrainImageInputsAsync(
                BuildTrainingInputsFromCurrentImageScope(),
                "Train All");
        }

        private async Task TrainSelectedBatchesAsync(IReadOnlyList<BatchLibraryItem> selectedBatches)
        {
            await TrainImageInputsAsync(
                BuildTrainingInputsFromBatchSelection(selectedBatches),
                "Selected Batch Train");
        }

        private async Task TrainImageInputsAsync(IReadOnlyList<TrainingImageInput> trainingInputs, string operationName)
        {
            var progressWindow = new OperationProgressWindow($"{operationName} 진행")
            {
                Owner = this
            };
            progressWindow.UpdateProgress(0, "작업 준비 중...");
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
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);

                string runRoot = ResolveRunRoot(trainingInputs);
                Directory.CreateDirectory(runRoot);

                var imagePaths = trainingInputs
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
                var validation = await Task.Run(() => _datasetValidator.Validate(trainingInputs));
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

                int totalImages = imagePaths.Count;
                var normalImagePaths = imagePaths
                    .Where(path => (_stateService.Load(path).IsNormal ?? true) == true)
                    .ToList();
                int normalImages = normalImagePaths.Count;

                EnsureEnoughWorkspaceDiskSpace(runRoot, imagePaths, normalImagePaths);

                progressWindow.UpdateProgress(15, "YOLO workspace 생성 중...");
                var yoloWsSvc = new YoloWorkspaceService(_stateService);
                var yoloWs = await Task.Run(() => yoloWsSvc.BuildYoloWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed
                ));

                progressWindow.UpdateProgress(30, "Anoma workspace 생성 중...");
                var anomaWsSvc = new AnomaWorkspaceService(_stateService);
                var anomaWs = await Task.Run(() => anomaWsSvc.BuildWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed
                ));

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string runDir = IOPath.Combine(runRoot, $"run_{stamp}_all");
                string logsDir = IOPath.Combine(runDir, "logs");
                Directory.CreateDirectory(logsDir);

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");
                Directory.CreateDirectory(yoloOut);
                Directory.CreateDirectory(anomaOut);

                string pythonExe = settings.PythonExe;
                string aiProjectRoot = settings.AiProjectRoot;
                string yoloScript = IOPath.Combine(aiProjectRoot, "scripts", "train_yolo.py");
                string anomaScript = IOPath.Combine(aiProjectRoot, "scripts", "train_anoma.py");

                if (!File.Exists(yoloScript) || !File.Exists(anomaScript))
                {
                    MessageBox.Show(
                        $"AI 학습 폴더가 올바르지 않습니다.\n" +
                        $"폴더: {aiProjectRoot}\n" +
                        $"필수 파일: scripts/train_yolo.py, scripts/train_anoma.py");
                    return;
                }

                var runner = new PythonRunner();
                using var cts = new CancellationTokenSource();

                progressWindow.UpdateProgress(45, "YOLO 학습 중...");
                progressWindow.AppendLog("[YOLO] START");
                int yoloCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: yoloScript,
                    args: $"--workspace \"{yoloWs.WorkspaceRoot}\" --out \"{yoloOut}\"",
                    workingDir: aiProjectRoot,
                    logPath: IOPath.Combine(logsDir, "yolo.log"),
                    ct: cts.Token,
                    onOutputLine: line =>
                    {
                        progressWindow.AppendLog("[YOLO] " + line);
                        int? logPercent = TryParseTrainingPercentFromLog(line);
                        if (logPercent.HasValue)
                        {
                            int mapped = MapStagePercent(logPercent.Value, 45, 20);
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

                progressWindow.UpdateProgress(68, "YOLO 학습 완료");
                progressWindow.UpdateProgress(72, "Anoma 학습 중...");
                progressWindow.AppendLog("[ANOMA] START");
                int anomaCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: anomaScript,
                    args: $"--workspace \"{anomaWs.WorkspaceRoot}\" --out \"{anomaOut}\"",
                    workingDir: aiProjectRoot,
                    logPath: IOPath.Combine(logsDir, "anoma.log"),
                    ct: cts.Token,
                    onOutputLine: line =>
                    {
                        progressWindow.AppendLog("[ANOMA] " + line);
                        int? logPercent = TryParseTrainingPercentFromLog(line);
                        if (logPercent.HasValue)
                        {
                            int mapped = MapStagePercent(logPercent.Value, 72, 18);
                            progressWindow.UpdateProgress(mapped, $"Anoma 학습 중... ({logPercent.Value}%)");
                        }
                    }
                );

                if (anomaCode != 0)
                {
                    MessageBox.Show($"Anomalib 학습 실패 (ExitCode={anomaCode})\nlogs/anoma.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                progressWindow.UpdateProgress(92, "패키지 생성 중...");
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");
                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if (!File.Exists(yoloOnnx) || !File.Exists(anomaOnnx))
                {
                    MessageBox.Show("ONNX 산출물이 없습니다. 스크립트가 yolo.onnx / anoma.onnx를 out 폴더에 생성해야 합니다.");
                    OpenFolder(runDir);
                    return;
                }

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                var pipelineObj = new
                {
                    schema_version = 1,
                    input = new
                    {
                        image_format = "bmp"
                    },
                    yolo = new
                    {
                        model = "models/yolo.onnx",
                        imgsz = settings.YoloInfer.ImgSz,
                        letterbox = settings.YoloInfer.Letterbox,
                        conf_thres = settings.YoloInfer.ConfThres,
                        iou_thres = settings.YoloInfer.IouThres,
                        max_det = settings.YoloInfer.MaxDet,
                        class_map = new { dent = 0, loose = 1 }
                    },
                    anoma = new
                    {
                        model = "models/anoma.onnx",
                        mode = settings.AnomaInfer.Mode,
                        input_size = settings.AnomaInfer.InputSize,
                        score_thres = settings.AnomaInfer.ScoreThres,
                        crop_padding_px = settings.AnomaInfer.CropPaddingPx
                    },
                    fusion = new
                    {
                        rule = settings.Fusion.Rule,
                        yolo_conf_thres = settings.Fusion.YoloThreshold,
                        anoma_score_thres = settings.Fusion.AnomaThreshold
                    },
                    output = new
                    {
                        format = "json",
                        schema = "detections_v1"
                    }
                };

                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");
                File.WriteAllText(
                    pipelinePath,
                    JsonSerializer.Serialize(
                        pipelineObj,
                        new JsonSerializerOptions { WriteIndented = true }
                    ),
                    System.Text.Encoding.UTF8
                );

                VerifyInferencePackageOrThrow(pkgDir);
                progressWindow.UpdateProgress(100, "학습 및 패키지 생성 완료");

                WriteRunManifest(
                    runDir: runDir,
                    projectRoot: projectRoot,
                    aiProjectRoot: aiProjectRoot,
                    pythonExe: pythonExe,
                    yoloScript: yoloScript,
                    anomaScript: anomaScript,
                    yoloWorkspaceRoot: yoloWs.WorkspaceRoot,
                    anomaWorkspaceRoot: anomaWs.WorkspaceRoot,
                    yoloOutDir: yoloOut,
                    anomaOutDir: anomaOut,
                    inferencePackageDir: pkgDir,
                    settings: settings,
                    totalImages: totalImages,
                    normalImages: normalImages,
                    sourceBatches: trainingInputs
                        .Select(input => input.BatchKey)
                        .Where(batchKey => !string.IsNullOrWhiteSpace(batchKey))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(batchKey => batchKey, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                );

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

        private void VerifyInferencePackageOrThrow(string pkgDir)
        {
            string modelsDir = IOPath.Combine(pkgDir, "models");
            string cfgDir = IOPath.Combine(pkgDir, "config");

            string yoloOnnx = IOPath.Combine(modelsDir, "yolo.onnx");
            string anomaOnnx = IOPath.Combine(modelsDir, "anoma.onnx");
            string pipeline = IOPath.Combine(cfgDir, "pipeline.json");

            if (!File.Exists(yoloOnnx))
                throw new FileNotFoundException("Missing yolo.onnx in inference_package/models", yoloOnnx);

            if (!File.Exists(anomaOnnx))
                throw new FileNotFoundException("Missing anoma.onnx in inference_package/models", anomaOnnx);

            if (!File.Exists(pipeline))
                throw new FileNotFoundException("Missing pipeline.json in inference_package/config", pipeline);

            var yoloSize = new FileInfo(yoloOnnx).Length;
            var anomaSize = new FileInfo(anomaOnnx).Length;

            if (yoloSize <= 0)
                throw new InvalidOperationException("yolo.onnx is empty (0 bytes).");

            if (anomaSize <= 0)
                throw new InvalidOperationException("anoma.onnx is empty (0 bytes).");

            using var doc = JsonDocument.Parse(File.ReadAllText(pipeline));
            var root = doc.RootElement;

            Require(root, "schema_version");
            Require(root, "yolo");
            Require(root, "anoma");
            Require(root, "fusion");

            var yolo = root.GetProperty("yolo");
            Require(yolo, "model");
            Require(yolo, "imgsz");
            Require(yolo, "conf_thres");
            Require(yolo, "iou_thres");
            Require(yolo, "max_det");
            Require(yolo, "class_map");

            var anoma = root.GetProperty("anoma");
            Require(anoma, "model");
            Require(anoma, "mode");
            Require(anoma, "input_size");
            Require(anoma, "score_thres");

            var fusion = root.GetProperty("fusion");
            Require(fusion, "rule");
            Require(fusion, "yolo_conf_thres");
            Require(fusion, "anoma_score_thres");
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
            string pythonExe,
            string yoloScript,
            string anomaScript,
            string yoloWorkspaceRoot,
            string anomaWorkspaceRoot,
            string yoloOutDir,
            string anomaOutDir,
            string inferencePackageDir,
            AppSettings settings,
            int totalImages,
            int normalImages,
            IReadOnlyList<string>? sourceBatches = null
        )
        {
            var manifest = new
            {
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProjectRoot = projectRoot,
                AiProjectRoot = aiProjectRoot,
                PythonExe = pythonExe,
                Scripts = new { Yolo = yoloScript, Anoma = anomaScript },
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
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);

                string runRoot = ResolveRunRoot(currentInputs);
                if (!Directory.Exists(runRoot))
                {
                    MessageBox.Show("_train_runs 폴더가 없습니다.");
                    return;
                }

                var latestRunDir = Directory.GetDirectories(runRoot, "run_*_all")
                                            .Select(dir => new DirectoryInfo(dir))
                                            .OrderByDescending(dir => dir.CreationTimeUtc)
                                            .FirstOrDefault();

                if (latestRunDir == null)
                {
                    MessageBox.Show("run_*_all 폴더가 없습니다.");
                    return;
                }

                string runDir = latestRunDir.FullName;

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");

                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if (!File.Exists(yoloOnnx) || !File.Exists(anomaOnnx))
                {
                    MessageBox.Show(
                        "필요한 ONNX가 없습니다.\n\n" +
                        $"yolo: {yoloOnnx}\n" +
                        $"anoma: {anomaOnnx}\n\n" +
                        "Train All을 먼저 수행했는지 확인하세요."
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

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");

                var pipelineObj = new
                {
                    schema_version = 1,
                    input = new
                    {
                        image_format = "bmp"
                    },
                    yolo = new
                    {
                        model = "models/yolo.onnx",
                        imgsz = settings.YoloInfer.ImgSz,
                        letterbox = settings.YoloInfer.Letterbox,
                        conf_thres = settings.YoloInfer.ConfThres,
                        iou_thres = settings.YoloInfer.IouThres,
                        max_det = settings.YoloInfer.MaxDet,
                        class_map = new { dent = 0, loose = 1 }
                    },
                    anoma = new
                    {
                        model = "models/anoma.onnx",
                        mode = settings.AnomaInfer.Mode,
                        input_size = settings.AnomaInfer.InputSize,
                        score_thres = settings.AnomaInfer.ScoreThres,
                        crop_padding_px = settings.AnomaInfer.CropPaddingPx
                    },
                    fusion = new
                    {
                        rule = settings.Fusion.Rule,
                        yolo_conf_thres = settings.Fusion.YoloThreshold,
                        anoma_score_thres = settings.Fusion.AnomaThreshold
                    },
                    output = new
                    {
                        format = "json",
                        schema = "detections_v1"
                    }
                };

                File.WriteAllText(
                    pipelinePath,
                    JsonSerializer.Serialize(
                        pipelineObj,
                        new JsonSerializerOptions { WriteIndented = true }),
                    System.Text.Encoding.UTF8
                );

                VerifyInferencePackageOrThrow(pkgDir);

                MessageBox.Show($"Package Only 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Build Package Only 실패:\n" + ex.Message);
            }
        }
    }
}
