using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoilTrainingUI.Services
{
    public class TrainingOrchestratorService
    {
        private readonly PythonRunner _runner = new();

        public async Task<TrainAllResult> TrainAllAsync(
            string pythonExe,
            string projectRoot,
            string yoloScriptPath,
            string anomaScriptPath,
            string yoloWorkspaceRoot,
            string anomaWorkspaceRoot,
            string outRunRoot,
            CancellationToken ct
        )
        {
            // run 폴더
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string runDir = Path.Combine(outRunRoot, $"run_{stamp}");
            Directory.CreateDirectory(runDir);

            string logsDir = Path.Combine(runDir, "logs");
            Directory.CreateDirectory(logsDir);

            // 1) YOLO 학습 + ONNX export
            // 스크립트 계약(권장):
            // python train_yolo.py --workspace "<...>" --out "<runDir>/yolo_out"
            string yoloOut = Path.Combine(runDir, "yolo_out");
            Directory.CreateDirectory(yoloOut);

            int yoloCode = await _runner.RunAsync(
                pythonExe: pythonExe,
                scriptPath: yoloScriptPath,
                args: $"--workspace \"{yoloWorkspaceRoot}\" --out \"{yoloOut}\"",
                workingDir: projectRoot,
                logPath: Path.Combine(logsDir, "yolo.log"),
                ct: ct
            );
            if (yoloCode != 0)
                throw new InvalidOperationException($"YOLO training failed. ExitCode={yoloCode}. See logs/yolo.log");

            // 2) Anomalib 학습 + ONNX export
            string anomaOut = Path.Combine(runDir, "anoma_out");
            Directory.CreateDirectory(anomaOut);

            int anomaCode = await _runner.RunAsync(
                pythonExe: pythonExe,
                scriptPath: anomaScriptPath,
                args: $"--workspace \"{anomaWorkspaceRoot}\" --out \"{anomaOut}\"",
                workingDir: projectRoot,
                logPath: Path.Combine(logsDir, "anoma.log"),
                ct: ct
            );
            if (anomaCode != 0)
                throw new InvalidOperationException($"Anomalib training failed. ExitCode={anomaCode}. See logs/anoma.log");

            // 3) inference package 생성
            // 여기서는 “스크립트가 out 폴더에 onnx를 만든다”는 계약으로 단순 복사
            // yolo.onnx, anoma.onnx 파일명을 강제하면 제일 관리가 쉬움
            string pkgDir = Path.Combine(runDir, "inference_package");
            Directory.CreateDirectory(pkgDir);

            string modelsDir = Path.Combine(pkgDir, "models");
            Directory.CreateDirectory(modelsDir);

            string yoloOnnx = Path.Combine(yoloOut, "yolo.onnx");
            string anomaOnnx = Path.Combine(anomaOut, "anoma.onnx");

            if (!File.Exists(yoloOnnx))
                throw new FileNotFoundException("Missing yolo.onnx in yolo_out. Script must export it.", yoloOnnx);

            if (!File.Exists(anomaOnnx))
                throw new FileNotFoundException("Missing anoma.onnx in anoma_out. Script must export it.", anomaOnnx);

            File.Copy(yoloOnnx, Path.Combine(modelsDir, "yolo.onnx"), overwrite: true);
            File.Copy(anomaOnnx, Path.Combine(modelsDir, "anoma.onnx"), overwrite: true);

            // config 생성(최소)
            string cfgDir = Path.Combine(pkgDir, "config");
            Directory.CreateDirectory(cfgDir);

            File.WriteAllText(Path.Combine(cfgDir, "pipeline.json"),
@"{
  ""preprocess"": { ""use_roi_processed"": true },
  ""yolo"": { ""classes"": { ""dent"": 0, ""loose"": 1 } },
  ""fusion"": { ""rule"": ""AND"", ""yolo_threshold"": 0.25, ""anoma_threshold"": 0.5 }
}", System.Text.Encoding.UTF8);

            // runner(추론 실행) 파일은 나중에 붙이되, 지금은 자리만 만들어 둠
            Directory.CreateDirectory(Path.Combine(pkgDir, "run"));

            return new TrainAllResult
            {
                RunDir = runDir,
                InferencePackageDir = pkgDir
            };
        }
    }

    public class TrainAllResult
    {
        public string RunDir { get; set; } = "";
        public string InferencePackageDir { get; set; } = "";
    }
}
