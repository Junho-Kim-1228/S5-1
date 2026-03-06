using CoilTrainingUI.Services;
using CoilTrainingUI.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void RequestSaveLabelsDebounced(string imagePath) //디바운스 트리거
        {
            if (_isLoadingImage) return;
            if (string.IsNullOrEmpty(imagePath)) return;

            _pendingSaveImagePath = imagePath;

            _labelSaveDebounceTimer.Stop();
            _labelSaveDebounceTimer.Start();
        }

        private async void TrainAll_Click(object sender, RoutedEventArgs e)
        {
            // 학습은 오래 걸리니 UI 멈춤 방지
            try
            {
                if (_images.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }


                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);


                // 1) runRoot
                string inputDir = IOPath.GetDirectoryName(_images[0].ProcessedPath)!;
                string runRoot = IOPath.Combine(inputDir, "_train_runs");
                Directory.CreateDirectory(runRoot);

                // 2) 현재 이미지 경로
                var imagePaths = _images.Select(x => x.ProcessedPath)
                                        .Where(p => !string.IsNullOrWhiteSpace(p))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToList();

                if (imagePaths.Count == 0)
                {
                    MessageBox.Show("학습에 사용할 이미지 경로가 없습니다.");
                    return;
                }

                var validation = _datasetValidator.Validate(
                    imagePaths,
                    _currentBatchRequiresInfer,
                    _inferJsonByImagePath);
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
                int normalImages = imagePaths.Count(p => (_stateService.Load(p).IsNormal ?? true) == true);

                // 3) YOLO workspace 생성 (라벨 txt는 여기서 workspace에만 생성됨)
                var yoloWsSvc = new YoloWorkspaceService(_stateService);
                var yoloWs = yoloWsSvc.BuildYoloWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed
                );

                // 4) Anoma workspace 생성 (정상만)
                var anomaWsSvc = new AnomaWorkspaceService(_stateService);
                var anomaWs = anomaWsSvc.BuildWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed
                );

                // 5) 이번 실행 결과 폴더(로그/산출물)
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string runDir = IOPath.Combine(runRoot, $"run_{stamp}_all");
                string logsDir = IOPath.Combine(runDir, "logs");
                Directory.CreateDirectory(logsDir);

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");
                Directory.CreateDirectory(yoloOut);
                Directory.CreateDirectory(anomaOut);

                // 6) 파이썬 스크립트 실행(순차)
                string pythonExe = settings.PythonExe;
                string yoloScript = IOPath.Combine(projectRoot, settings.Scripts.YoloTrain);
                string anomaScript = IOPath.Combine(projectRoot, settings.Scripts.AnomaTrain);


                if (!File.Exists(yoloScript) || !File.Exists(anomaScript))
                {
                    MessageBox.Show("scripts/train_yolo.py 또는 scripts/train_anoma.py가 없습니다.");
                    return;
                }

                var runner = new PythonRunner();
                using var cts = new CancellationTokenSource(); // 추후 Cancel 메뉴 추가 가능

                // (1) YOLO
                int yoloCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: yoloScript,
                    args: $"--workspace \"{yoloWs.WorkspaceRoot}\" --out \"{yoloOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "yolo.log"),
                    ct: cts.Token
                );

                if (yoloCode != 0)
                {
                    MessageBox.Show($"YOLO 학습 실패 (ExitCode={yoloCode})\nlogs/yolo.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // (2) Anomalib
                int anomaCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: anomaScript,
                    args: $"--workspace \"{anomaWs.WorkspaceRoot}\" --out \"{anomaOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "anoma.log"),
                    ct: cts.Token
                );

                if (anomaCode != 0)
                {
                    MessageBox.Show($"Anomalib 학습 실패 (ExitCode={anomaCode})\nlogs/anoma.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // 7) inference package 생성
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");
                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                // ✅ 스크립트가 아래 파일명을 생성한다는 계약이 필요
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
                    System.Text.Json.JsonSerializer.Serialize(
                        pipelineObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                    ),
                    System.Text.Encoding.UTF8
                );



                // ✅ 패키지 검증 (없으면 성공 처리 금지)
                VerifyInferencePackageOrThrow(pkgDir);

                // ✅ manifest 기록 (재현성/디버깅용)
                WriteRunManifest(
                    runDir: runDir,
                    projectRoot: projectRoot,
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
                    normalImages: normalImages
                );



                MessageBox.Show($"Train All 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Train All 중 예외 발생:\n" + ex.Message);

                // runDir 변수가 스코프 밖이면, 최소 logs 위치라도 열게 구조를 잡으세요.
                // (가장 쉬운 방법: runDir을 함수 시작에서 string? runDir = null;로 선언하고 나중에 채우기)
            }

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

        // 0바이트 방지
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


        private void WriteRunManifest(
            string runDir,
            string projectRoot,
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
            int normalImages
        )
        {
            var manifest = new
            {
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProjectRoot = projectRoot,
                PythonExe = pythonExe,
                Scripts = new { Yolo = yoloScript, Anoma = anomaScript },
                Workspaces = new { Yolo = yoloWorkspaceRoot, Anoma = anomaWorkspaceRoot },
                Outputs = new { YoloOut = yoloOutDir, AnomaOut = anomaOutDir, InferencePackage = inferencePackageDir },
                Dataset = new { TotalImages = totalImages, NormalImages = normalImages },
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
                if (_images.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }

                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);

                string inputDir = IOPath.GetDirectoryName(_images[0].ProcessedPath)!;
                string runRoot = IOPath.Combine(inputDir, "_train_runs");
                if (!Directory.Exists(runRoot))
                {
                    MessageBox.Show("_train_runs 폴더가 없습니다.");
                    return;
                }

                // ✅ 가장 최근 run_..._all 찾기
                var latestRunDir = Directory.GetDirectories(runRoot, "run_*_all")
                                            .Select(d => new DirectoryInfo(d))
                                            .OrderByDescending(d => d.CreationTimeUtc)
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

                // ✅ 여기서부터는 학습 재실행 없음. 없으면 그냥 실패해야 정상.
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

                // 패키지 재생성
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");

                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                // pipeline.json은 기존이 있으면 그대로 두고, 없으면 최소 생성
                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");

                // ✅ appsettings 기반 pipeline 객체 생성
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

                // ✅ 항상 덮어쓰기 (조건문 제거)
                File.WriteAllText(
                    pipelinePath,
                    System.Text.Json.JsonSerializer.Serialize(pipelineObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                    System.Text.Encoding.UTF8
                );


                // ✅ 검증
                VerifyInferencePackageOrThrow(pkgDir);

                MessageBox.Show($"Package Only 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Build Package Only 실패:\n" + ex.Message);
            }
        }

        private void RefreshSummaryCounts()
        {
            int total = _images.Count;
            int visible = _imageCollectionView?.Cast<object>().OfType<ImageItem>().Count() ?? total;

            int defect = _images.Count(i => i.HasLabel || !i.IsNormal);
            int normal = total - defect;

            TotalCountText.Text = $"총 {total}개";
            VisibleCountText.Text = $"필터 후 {visible}개";
            NormalCountText.Text = $"정상 {normal}개";
            DefectCountText.Text = $"불량 {defect}개 (YOLO 또는 Anoma)";
        }

    }
}
