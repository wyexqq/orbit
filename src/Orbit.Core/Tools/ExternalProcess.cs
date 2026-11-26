using System.Diagnostics;
using System.Text;

namespace Orbit.Core.Tools;

public sealed record ProcessOutcome(int ExitCode, string StandardOutput, string ErrorTail);

public static class ExternalProcess
{
    private const int ErrorTailLines = 12;

    public static async Task<ProcessOutcome> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellation)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var errorTail = new Queue<string>();
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null)
            {
                return;
            }
            if (onOutputLine is null)
            {
                output.AppendLine(line.Data);
            }
            else
            {
                onOutputLine(line.Data);
            }
        };
        process.ErrorDataReceived += (_, line) =>
        {
            if (string.IsNullOrWhiteSpace(line.Data))
            {
                return;
            }
            lock (errorTail)
            {
                errorTail.Enqueue(line.Data);
                if (errorTail.Count > ErrorTailLines)
                {
                    errorTail.Dequeue();
                }
            }
        };
        process.Start();
        LowerPriority(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            StopQuietly(process);
            throw;
        }
        process.WaitForExit();
        string tail;
        lock (errorTail)
        {
            tail = string.Join(Environment.NewLine, errorTail);
        }
        return new ProcessOutcome(process.ExitCode, output.ToString(), tail);
    }

    private static void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Info($"Не смог понизить приоритет {process.StartInfo.FileName}: {error.Message}");
        }
    }

    private static void StopQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Info($"Процесс уже завершился: {error.Message}");
        }
    }
}
