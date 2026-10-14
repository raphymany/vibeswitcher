using System.Runtime.InteropServices;
using System.Text;

namespace VibeSwitcher.Platform.MacOS.Apps;

// The process list for app triggers, from libproc (what Activity Monitor uses):
//   ExePath  proc_pidpath — the program file, e.g. /Applications/Discord.app/Contents/MacOS/Discord
//   Names    that file's name and the process name
//   AppId    the app bundle it belongs to (the outermost "….app"), so a picked app matches its main
//            program and its helpers alike
public sealed class MacProcesses : IRunningProcessSource
{
    private readonly Func<int[]> _listPids;
    private readonly Func<int, string?> _path;
    private readonly Func<int, string?> _name;

    public MacProcesses() : this(ListPids, PidPath, PidName) { }

    internal MacProcesses(Func<int[]> listPids, Func<int, string?> path, Func<int, string?> name)
    {
        _listPids = listPids;
        _path = path;
        _name = name;
    }

    // (Locked: the native reads share one buffer.)
    public IReadOnlyList<RunningProcess>? Snapshot()
    {
        lock (Buffers)
        {
            int[] pids;
            try { pids = _listPids(); }
            catch (Exception) { return null; }
            if (pids.Length == 0) return null; // there's always at least launchd: the call failed
            var list = new List<RunningProcess>(pids.Length);
            foreach (var pid in pids)
            {
                if (pid <= 0) continue;
                string? exe, name;
                try { exe = _path(pid); name = _name(pid); }
                catch (Exception) { continue; }
                if (exe == null && string.IsNullOrEmpty(name)) continue; // gone, or not ours to see
                var names = new List<string>(2);
                if (exe != null) names.Add(Path.GetFileName(exe));
                if (!string.IsNullOrEmpty(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                list.Add(new RunningProcess(exe, names, BundleOf(exe)));
            }
            return list;
        }
    }

    private static readonly object Buffers = new();
    private static readonly byte[] PathBuffer = new byte[PathMax];
    private static readonly byte[] NameBuffer = new byte[NameMax];

    // "/Applications/Discord.app/Contents/Frameworks/Discord Helper.app/Contents/MacOS/Discord Helper"
    // → "/Applications/Discord.app". Null when the path isn't inside an app bundle.
    public static string? BundleOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/')) return null;
        int at = path.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? path : null;
        return path[..(at + 4)];
    }

    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const int PathMax = 4096; // PROC_PIDPATHINFO_MAXSIZE
    private const int NameMax = 2 * 16 + 1; // 2 * MAXCOMLEN + 1

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int proc_listallpids(int[]? buffer, int bufferSize);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int proc_name(int pid, byte[] buffer, uint bufferSize);

    private static int[] ListPids()
    {
        // The count can grow between the two calls: leave room.
        int count = proc_listallpids(null, 0);
        if (count <= 0) return [];
        var pids = new int[count + 64];
        int got = proc_listallpids(pids, pids.Length * sizeof(int));
        return got <= 0 ? [] : pids[..Math.Min(got, pids.Length)];
    }

    // Called under the Buffers lock.
    private static string? PidPath(int pid)
    {
        int n = proc_pidpath(pid, PathBuffer, (uint)PathBuffer.Length);
        return n > 0 ? Encoding.UTF8.GetString(PathBuffer, 0, n) : null;
    }

    private static string? PidName(int pid)
    {
        int n = proc_name(pid, NameBuffer, (uint)NameBuffer.Length);
        return n > 0 ? Encoding.UTF8.GetString(NameBuffer, 0, n) : null;
    }
}
