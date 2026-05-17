using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoilInspectionApp.Preprocess
{
    public sealed class MaskRuntimeInvocationResult : IDisposable
    {
        public string WorkingDirectory { get; private set; }
        public string MaskedImagePath { get; private set; }

        public MaskRuntimeInvocationResult(string workingDirectory, string maskedImagePath)
        {
            WorkingDirectory = workingDirectory;
            MaskedImagePath = maskedImagePath;
        }

        public void Dispose()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory))
                    Directory.Delete(WorkingDirectory, true);
            }
            catch
            {
                // 임시 폴더 정리 실패는 추론 성공보다 우선하지 않는다.
            }
        }
    }

    public sealed class MaskRuntimeRunner
    {
        private readonly string _pythonExe;
        private readonly string _runtimeRoot;
        private readonly string _scriptPath;
        private readonly string _modelPath;

        public MaskRuntimeRunner(string pythonExe, string runtimeRoot)
        {
            _pythonExe = pythonExe ?? throw new ArgumentNullException(nameof(pythonExe));
            _runtimeRoot = runtimeRoot ?? throw new ArgumentNullException(nameof(runtimeRoot));

            if (!Directory.Exists(_runtimeRoot))
                throw new DirectoryNotFoundException("mask-runtime 폴더가 없습니다: " + _runtimeRoot);

            _scriptPath = Path.Combine(_runtimeRoot, "src", "apply_ai_mask.py");
            _modelPath = Path.Combine(_runtimeRoot, "models", "coil_unetpp_effb4_scratch_v8_best.pt");

            if (!File.Exists(_scriptPath))
                throw new FileNotFoundException("apply_ai_mask.py를 찾을 수 없습니다.", _scriptPath);

            if (!File.Exists(_modelPath))
                throw new FileNotFoundException("mask model을 찾을 수 없습니다.", _modelPath);

            if (LooksLikeFilePath(_pythonExe) && !File.Exists(_pythonExe))
                throw new FileNotFoundException("Python 실행 파일을 찾을 수 없습니다.", _pythonExe);
        }

        public MaskRuntimeInvocationResult RunSingleImage(string rawImagePath)
        {
            if (string.IsNullOrWhiteSpace(rawImagePath) || !File.Exists(rawImagePath))
                throw new FileNotFoundException("전처리 입력 이미지를 찾을 수 없습니다.", rawImagePath);

            string workRoot = Path.Combine(
                Path.GetTempPath(),
                "coil_mask_runtime",
                Guid.NewGuid().ToString("N"));
            string inputDir = Path.Combine(workRoot, "input");
            string outputDir = Path.Combine(workRoot, "output");
            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(outputDir);

            string stagedInputPath = Path.Combine(inputDir, Path.GetFileName(rawImagePath));
            File.Copy(rawImagePath, stagedInputPath, true);

            string arguments = BuildArguments(inputDir, outputDir);
            string stdOut;
            string stdErr;
            int exitCode = ExecuteProcess(arguments, out stdOut, out stdErr);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    "mask runtime 실행 실패\n"
                    + "- exitCode: " + exitCode + "\n"
                    + "- stdout:\n" + stdOut + "\n"
                    + "- stderr:\n" + stdErr);
            }

            string maskedImagePath = Path.Combine(
                outputDir,
                Path.GetFileNameWithoutExtension(rawImagePath) + "_masked.bmp");

            if (!File.Exists(maskedImagePath))
            {
                throw new FileNotFoundException(
                    "mask runtime 결과 이미지를 찾을 수 없습니다.",
                    maskedImagePath);
            }

            return new MaskRuntimeInvocationResult(workRoot, maskedImagePath);
        }

        public IReadOnlyDictionary<string, string> RunBatch(
            IEnumerable<string> rawImagePaths,
            string outputDir,
            Action<string, string> onMaskedImageReady = null)
        {
            if (rawImagePaths == null)
                throw new ArgumentNullException(nameof(rawImagePaths));

            string[] paths = rawImagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("전처리 입력 이미지를 찾을 수 없습니다.", path);
            }

            Directory.CreateDirectory(outputDir);

            string workRoot = Path.Combine(
                Path.GetTempPath(),
                "coil_mask_runtime",
                Guid.NewGuid().ToString("N"));
            string inputDir = Path.Combine(workRoot, "input");
            Directory.CreateDirectory(inputDir);

            var stagedToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawPath in paths)
            {
                string stagedPath = BuildUniqueStagedPath(inputDir, rawPath);
                File.Copy(rawPath, stagedPath, true);
                stagedToSource[stagedPath] = rawPath;
            }

            try
            {
                string arguments = BuildArguments(inputDir, outputDir);
                var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Action pollReadyResults = () =>
                {
                    foreach (KeyValuePair<string, string> item in stagedToSource)
                    {
                        string maskedPath = Path.Combine(
                            outputDir,
                            Path.GetFileNameWithoutExtension(item.Key) + "_masked.bmp");

                        if (!results.ContainsKey(item.Value) && IsFileReady(maskedPath))
                        {
                            results[item.Value] = maskedPath;
                            onMaskedImageReady?.Invoke(item.Value, maskedPath);
                        }
                    }
                };

                string stdOut;
                string stdErr;
                int exitCode = ExecuteProcess(arguments, pollReadyResults, out stdOut, out stdErr);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        "mask runtime 실행 실패\n"
                        + "- exitCode: " + exitCode + "\n"
                        + "- stdout:\n" + stdOut + "\n"
                        + "- stderr:\n" + stdErr);
                }

                for (int i = 0; i < 10 && results.Count < stagedToSource.Count; i++)
                {
                    pollReadyResults();
                    if (results.Count < stagedToSource.Count)
                        Thread.Sleep(200);
                }

                return results;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workRoot))
                        Directory.Delete(workRoot, true);
                }
                catch
                {
                    // 임시 폴더 정리 실패는 전처리 결과보다 우선하지 않는다.
                }
            }
        }

        private string BuildArguments(string inputDir, string outputDir)
        {
            return string.Format(
                "\"{0}\" --input-dir \"{1}\" --output-dir \"{2}\" --model-path \"{3}\" --device auto --input-size 512 --mask-threshold 0.30 --min-component-area 64 --outer-recover-kernel 0 --overwrite",
                _scriptPath,
                inputDir,
                outputDir,
                _modelPath);
        }

        private int ExecuteProcess(string arguments, out string stdOut, out string stdErr)
        {
            return ExecuteProcess(arguments, null, out stdOut, out stdErr);
        }

        private int ExecuteProcess(string arguments, Action pollWhileRunning, out string stdOut, out string stdErr)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pythonExe,
                Arguments = arguments,
                WorkingDirectory = _runtimeRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var process = new Process())
            {
                process.StartInfo = psi;
                process.Start();
                Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

                while (!process.WaitForExit(250))
                    pollWhileRunning?.Invoke();

                pollWhileRunning?.Invoke();
                process.WaitForExit();
                stdOut = stdOutTask.Result;
                stdErr = stdErrTask.Result;
                return process.ExitCode;
            }
        }

        private static bool LooksLikeFilePath(string value)
        {
            return value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0 || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileReady(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUniqueStagedPath(string inputDir, string rawImagePath)
        {
            string extension = Path.GetExtension(rawImagePath);
            string stem = Path.GetFileNameWithoutExtension(rawImagePath);
            string candidate = Path.Combine(inputDir, stem + extension);
            int suffix = 1;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(inputDir, stem + "_" + suffix.ToString("000") + extension);
                suffix++;
            }

            return candidate;
        }
    }
}
