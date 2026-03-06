using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoilTrainingUI.Services
{
    public class PythonRunner
    {
        public async Task<int> RunAsync(
            string pythonExe,
            string scriptPath,
            string args,
            string workingDir,
            string logPath,
            CancellationToken ct
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" {args}",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var sb = new StringBuilder();
            void Append(string line)
            {
                var msg = $"[{DateTime.Now:HH:mm:ss}] {line}";
                sb.AppendLine(msg);
                File.AppendAllText(logPath, msg + Environment.NewLine, Encoding.UTF8);
            }

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Append(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Append("[ERR] " + e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 취소 처리
            using (ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
            }))
            {
                await proc.WaitForExitAsync(ct);
            }

            return proc.ExitCode;
        }
    }
}
