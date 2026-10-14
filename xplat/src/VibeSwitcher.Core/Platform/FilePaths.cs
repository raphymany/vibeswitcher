using System.IO;

namespace VibeSwitcher.Platform;

public static class FilePaths
{
    // realpath(3): every symlink along an absolute path resolved, each relative target against the
    // real directory that contains the link — what the system reports for a running program
    // (/proc/<pid>/exe on Linux; /var → /private/var on macOS). Null on a loop, a missing component
    // or a folder it isn't allowed to look into; never throws.
    public static string? RealPath(string path)
    {
        try { return Resolve(path); }
        catch (Exception) { return null; }
    }

    private static string? Resolve(string path)
    {
        var pending = new Stack<string>(Split(path).Reverse());
        var resolved = new List<string>();
        int hops = 0;
        while (pending.Count > 0)
        {
            var part = pending.Pop();
            if (part == ".") continue;
            if (part == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); continue; }

            var candidate = Join(resolved, part);
            var target = new FileInfo(candidate).LinkTarget;
            if (target == null)
            {
                if (!Path.Exists(candidate)) return null;
                resolved.Add(part);
                continue;
            }
            if (++hops > 40) return null;
            if (Path.IsPathRooted(target)) resolved.Clear();
            foreach (var p in Split(target).Reverse()) pending.Push(p);
        }
        return Join(resolved, null);
    }

    private static IEnumerable<string> Split(string p) =>
        p.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static string Join(List<string> parts, string? last)
    {
        var all = last == null ? parts : [.. parts, last];
        // Keep the original root form: "/" on Unix, "C:\" on Windows (tests).
        if (OperatingSystem.IsWindows() && all.Count > 0 && all[0].EndsWith(':'))
            return Path.Combine([all[0] + Path.DirectorySeparatorChar, .. all.Skip(1)]);
        return "/" + string.Join('/', all);
    }
}
