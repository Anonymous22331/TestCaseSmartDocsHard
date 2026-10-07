using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;

namespace TestCaseSmartDocsHard.Tests;

public sealed class ProcessTests
{
    [Fact]
    public async Task MissingExecutableReportsActionableError()
    {
        var runner = Runner();
        var exception = await Assert.ThrowsAsync<ProcessingException>(() => runner.RunAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), [], CancellationToken.None));
        Assert.Equal("executable_unavailable", exception.Code);
    }

    [Fact]
    public async Task TimeoutStopsLocalProcess()
    {
        var (executable, arguments) = SleepCommand();
        var exception = await Assert.ThrowsAsync<ProcessingException>(() => Runner(1).RunAsync(executable, arguments, CancellationToken.None));
        Assert.Equal("process_timeout", exception.Code);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        var (executable, arguments) = SleepCommand();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner().RunAsync(executable, arguments, cancellation.Token));
    }

    [Fact]
    public async Task NonZeroExitDoesNotExposeStderr()
    {
        var executable = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        string[] arguments = OperatingSystem.IsWindows()
            ? ["-NoProfile", "-Command", "[Console]::Error.WriteLine('private text'); exit 7"]
            : ["-c", "printf 'private text' >&2; exit 7"];
        var exception = await Assert.ThrowsAsync<ProcessingException>(() => Runner().RunAsync(executable, arguments, CancellationToken.None));
        Assert.Equal("process_failed", exception.Code);
        Assert.DoesNotContain("private text", exception.Message);
    }

    private static LocalProcessRunner Runner(int timeout = 10) => new(
        Options.Create(new ProcessingOptions { ProcessTimeoutSeconds = timeout }), NullLogger<LocalProcessRunner>.Instance);

    private static (string, string[]) SleepCommand() => OperatingSystem.IsWindows()
        ? ("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"])
        : ("/bin/sleep", ["30"]);
}
