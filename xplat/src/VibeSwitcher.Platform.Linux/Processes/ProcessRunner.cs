using System.ComponentModel;
using System.Diagnostics;

namespace VibeSwitcher.Platform.Linux.Processes;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

// The tool isn't installed (or isn't on PATH).
public sealed class ToolNotFoundException(string tool, Exception inner)
    : Exception($"'{tool}' is not installed or not on PATH.", inner)
{
    public string Tool { get; } = tool;
}

// A started process with its standard streams. Dispose kills it if it's still running.
public interface IRunningProcess : IDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    // Everything the process wrote to stderr so far (collected in the background).
    string StandardError { get; }
    bool WaitForExit(TimeSpan timeout);
    int ExitCode { get; }
    void Kill();
}

// Every external tool goes through here, so tests can substitute canned output.
public interface IProcessRunner
{
    // Runs to completion. Throws ToolNotFoundException, or TimeoutException (after killing it).
    ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout);

    // Starts a long-running process with redirected stdin/stdout/stderr.
    IRunningProcess Start(string file, IReadOnlyList<string> args);
}

public sealed class ProcessRunner : IProcessRunner
{
    public static readonly ProcessRunner Instance = new();

    public ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        using var p = Launch(file, args, redirectStdin: false);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            TryKill(p);
            throw TimedOut(file, args, timeout);
        }
        // The output pipes close when every process holding them exits — a child it spawned (e.g. an
        // autospawned sound server) can keep them open, so the reads get the remaining time too.
        // (No time limit — a password prompt — still bounds this part.)
        var remaining = timeout == Timeout.InfiniteTimeSpan ? TimeSpan.FromSeconds(30) : timeout - started.Elapsed;
        if (!Task.WaitAll([stdout, stderr], remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
            throw TimedOut(file, args, timeout);
        return new ProcessResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    private static TimeoutException TimedOut(string file, IReadOnlyList<string> args, TimeSpan timeout) =>
        new($"'{file} {string.Join(' ', args)}' did not finish within {timeout.TotalSeconds:0.#} s.");

    public IRunningProcess Start(string file, IReadOnlyList<string> args) =>
        new Running(Launch(file, args, redirectStdin: true));

    private static Process Launch(string file, IReadOnlyList<string> args, bool redirectStdin)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Tool output is parsed, so it must not be translated: force the C locale.
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        psi.Environment.Remove("LANGUAGE");
        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException($"Could not start '{file}'.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2) // ENOENT / ERROR_FILE_NOT_FOUND
        {
            throw new ToolNotFoundException(file, ex);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start '{file}': {ex.Message}", ex);
        }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (Exception) { /* already gone */ }
    }

    private sealed class Running : IRunningProcess
    {
        private readonly Process _p;
        private readonly System.Text.StringBuilder _err = new();

        public Running(Process p)
        {
            _p = p;
            _p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_err) _err.AppendLine(e.Data); };
            _p.BeginErrorReadLine();
        }

        public Stream StandardInput => _p.StandardInput.BaseStream;
        public Stream StandardOutput => _p.StandardOutput.BaseStream;
        public string StandardError { get { lock (_err) return _err.ToString(); } }
        public int ExitCode => _p.ExitCode;

        public bool WaitForExit(TimeSpan timeout)
        {
            if (!_p.WaitForExit(timeout)) return false;
            _p.WaitForExit(TimeSpan.FromSeconds(2)); // let the stderr reader drain (bounded: a child may hold the pipe)
            return true;
        }

        public void Kill() => TryKill(_p);

        public void Dispose()
        {
            TryKill(_p);
            _p.Dispose();
        }
    }
}
