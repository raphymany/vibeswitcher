using System.IO;

namespace VibeSwitcher.Helpers;

// Manages the shared "your uploads" libraries for custom icons and switch sounds.
// When a user browses for a file, the original is kept under its own name in the library folder
// so it can be re-picked later without browsing again. Files are stored inside IconsDir/SoundsDir,
// so the existing PathSafety guards already cover them.
public static class UploadLibrary
{
    // Copies sourcePath into libraryDir, preserving its original (sanitised) file name.
    // If a file of that name with the same byte length already exists it's treated as the same
    // upload and reused; otherwise a numeric suffix is added so distinct files don't clobber.
    // Returns the stored path, or null if the copy failed (callers treat the library as best-effort).
    public static string? Save(string sourcePath, string libraryDir)
    {
        try
        {
            if (!File.Exists(sourcePath)) return null;
            Directory.CreateDirectory(libraryDir);

            var name = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            var ext  = Path.GetExtension(sourcePath);
            var sourceLen = new FileInfo(sourcePath).Length;

            var candidate = Path.Combine(libraryDir, name + ext);
            int n = 2;
            while (File.Exists(candidate))
            {
                // Same name + same size → assume it's the same upload already in the library.
                if (new FileInfo(candidate).Length == sourceLen) return candidate;
                candidate = Path.Combine(libraryDir, $"{name} ({n++}){ext}");
            }

            File.Copy(sourcePath, candidate, overwrite: false);
            return candidate;
        }
        catch
        {
            return null; // non-fatal: the per-profile copy still happens; only the library entry is skipped
        }
    }

    // Returns the library's files matching searchPattern (e.g. "*.ico"), newest first. Never throws.
    public static IReadOnlyList<string> List(string libraryDir, string searchPattern)
    {
        try
        {
            if (!Directory.Exists(libraryDir)) return Array.Empty<string>();
            return Directory.GetFiles(libraryDir, searchPattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "upload" : name;
    }
}
