using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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
            CancellationToken ct,
            Action<string>? onOutputLine = null
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var logChannel = Channel.CreateUnbounded<(string Message, string OutputLine)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            async Task WriteLogAsync()
            {
                await using var stream = new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                await foreach (var entry in logChannel.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(entry.Message);
                    try
                    {
                        onOutputLine?.Invoke(entry.OutputLine);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"PythonRunner output callback failed: {ex}");
                    }
                }
            }

            Task logWriterTask = WriteLogAsync();

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

            void Enqueue(string line)
            {
                var msg = $"[{DateTime.Now:HH:mm:ss}] {line}";
                logChannel.Writer.TryWrite((msg, line));
            }

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Enqueue(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Enqueue("[ERR] " + e.Data); };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // 취소 처리
                using (ct.Register(() =>
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                }))
                {
                    try
                    {
                        await proc.WaitForExitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        try { if (!proc.HasExited) proc.Kill(true); } catch { }
                        try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
                        proc.WaitForExit();
                        throw;
                    }
                }

                // 비동기 stdout/stderr 이벤트가 모두 전달될 때까지 기다린다.
                proc.WaitForExit();
                return proc.ExitCode;
            }
            finally
            {
                logChannel.Writer.TryComplete();
                await logWriterTask;
            }
        }
    }
}
