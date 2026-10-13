using System.Diagnostics;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Tests;

// The real process runner. The shell-based tests need /bin/sh, so they run on the Linux and macOS CI
// runners (and in WSL); on Windows they return early.
public class ProcessRunnerTests
{
    private static bool HasShell => !OperatingSystem.IsWindows() && File.Exists("/bin/sh");

    [Fact]
    public void MissingTool_IsToolNotFound()
    {
        var ex = Assert.Throws<ToolNotFoundException>(() =>
            ProcessRunner.Instance.Run("vs-definitely-not-a-real-tool-1f2e", [], TimeSpan.FromSeconds(5)));
        Assert.Equal("vs-definitely-not-a-real-tool-1f2e", ex.Tool);
        Assert.Throws<ToolNotFoundException>(() => ProcessRunner.Instance.Start("vs-definitely-not-a-real-tool-1f2e", []));
    }

    [Fact]
    public void Run_CapturesOutputAndExitCode_InTheCLocale()
    {
        if (!HasShell) return;
        var r = ProcessRunner.Instance.Run("/bin/sh", ["-c", "echo out; echo err >&2; echo \"$LC_ALL\"; exit 3"], TimeSpan.FromSeconds(10));
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("out\nC\n", r.StdOut);
        Assert.Equal("err\n", r.StdErr);
    }

    [Fact]
    public void Run_ArgumentsAreNeverShellParsed()
    {
        if (!HasShell) return;
        var r = ProcessRunner.Instance.Run("/bin/sh", ["-c", "printf '%s|' \"$@\"", "sh", "a b", "$(touch /tmp/x)", "-s"], TimeSpan.FromSeconds(10));
        Assert.Equal("a b|$(touch /tmp/x)|-s|", r.StdOut);
    }

    [Fact]
    public void Run_Timeout_KillsAndThrows()
    {
        if (!HasShell) return;
        var sw = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => ProcessRunner.Instance.Run("/bin/sh", ["-c", "sleep 30"], TimeSpan.FromMilliseconds(300)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Run_ChildHoldingThePipe_StillTimesOut()
    {
        if (!HasShell) return;
        // The shell exits at once, but a background child keeps stdout open (like an autospawned server).
        var sw = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() =>
            ProcessRunner.Instance.Run("/bin/sh", ["-c", "sleep 5 & echo started"], TimeSpan.FromMilliseconds(500)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Start_StreamsBothWays_AndKill()
    {
        if (!HasShell) return;
        using var p = ProcessRunner.Instance.Start("/bin/cat", []);
        p.StandardInput.Write("hello\n"u8);
        p.StandardInput.Flush();
        var buf = new byte[6];
        int read = 0;
        while (read < buf.Length) read += p.StandardOutput.Read(buf, read, buf.Length - read);
        Assert.Equal("hello\n"u8.ToArray(), buf);

        p.Kill();
        Assert.True(p.WaitForExit(TimeSpan.FromSeconds(5)));
    }
}
