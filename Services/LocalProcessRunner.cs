using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class LocalProcessRunner(IOptions<ProcessingOptions> options, ILogger<LocalProcessRunner> logger)
{
    public async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProcessTimeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new ProcessingException("executable_unavailable", "A required local OCR or PDF executable is unavailable.");
        }

        using var registration = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                logger.LogWarning("Unable to stop a local processing executable ({ErrorType}).", exception.GetType().Name);
            }
        });
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, 16 * 1024 * 1024, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 64 * 1024, timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            var output = await stdout;
            var error = await stderr;
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                logger.LogWarning("Local processing executable exited with code {ExitCode}; stderr length {Length}.",
                    process.ExitCode, error.Length);
                throw new ProcessingException("process_failed", "Local OCR or PDF rendering failed. Check the file and installed language packs.");
            }
            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessingException("process_timeout", "Local OCR or PDF rendering exceeded its time limit.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var remaining = limit - result.Length;
            exceeded |= count > remaining;
            if (remaining > 0)
                result.Append(buffer, 0, Math.Min(remaining, count));
        }
        if (exceeded)
            throw new ProcessingException("process_output_limit", "Local processing output exceeded the supported size limit.");
        return result.ToString();
    }
}
