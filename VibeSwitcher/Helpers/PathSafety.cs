using System.IO;

namespace VibeSwitcher.Helpers;

public static class PathSafety
{
    // Resolves 'path' and confirms it lives inside 'directory' after canonicalization (so a crafted
    // "Icons\..\..\secret.ico" can't pass a raw prefix check and then be deleted/loaded outside the
    // folder). Returns true with the canonical full path on success; false on any malformed input or
    // a path outside the directory. The trailing-separator check stops a sibling like "Icons_x" from
    // matching the "Icons" prefix.
    public static bool TryResolveInside(string? path, string directory, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.GetFullPath(directory)
                             .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            canonical = full;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
