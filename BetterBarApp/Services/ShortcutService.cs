using System.IO;

namespace BetterBarApp.Services;

/// <summary>
/// Creates launcher entries from dropped files. Shared by the start-menu icon list
/// and the Launcher item so both support the same "drop files to add" behaviour.
/// </summary>
public static class ShortcutService
{
    /// <summary>
    /// Adds each source path to <paramref name="targetDir"/> (copies .lnk files,
    /// otherwise creates a .lnk shortcut). Returns the file names that failed
    /// (e.g. folders, which aren't supported). Names are kept unique within the
    /// batch so two sources that normalize to the same name don't clobber.
    /// </summary>
    public static List<string> AddToDirectory(IEnumerable<string> sourcePaths, string targetDir)
    {
        var used     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var p in sourcePaths)
        {
            try { CreateShortcutOrCopy(p, targetDir, used); }
            catch { failures.Add(Path.GetFileName(p)); }
        }
        return failures;
    }

    // Naming rule:
    //   * .lnk source → copy it.
    //   * otherwise → filename with any trailing ".exe" stripped, then ".lnk".
    private static void CreateShortcutOrCopy(string sourcePath, string targetDir, HashSet<string> usedNames)
    {
        if (Directory.Exists(sourcePath))
            throw new InvalidOperationException("Folders are not supported");
        if (!File.Exists(sourcePath)) return;

        var fileName = Path.GetFileName(sourcePath);

        if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var dest = UniqueDestination(targetDir, Path.GetFileNameWithoutExtension(fileName), usedNames);
            if (!string.Equals(sourcePath, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, dest, overwrite: true);
            return;
        }

        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^4];

        var shortcutPath = UniqueDestination(targetDir, fileName, usedNames);

        // WScript.Shell.CreateShortcut — built-in COM, no extra references needed.
        var t = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell    = Activator.CreateInstance(t)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath       = sourcePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(sourcePath) ?? "";
        shortcut.Save();
    }

    // "<dir>\<baseName>.lnk", appending " (2)", " (3)", … if already claimed in this batch.
    private static string UniqueDestination(string targetDir, string baseName, HashSet<string> usedNames)
    {
        var candidate = Path.Combine(targetDir, baseName + ".lnk");
        for (int n = 2; !usedNames.Add(candidate); n++)
            candidate = Path.Combine(targetDir, $"{baseName} ({n}).lnk");
        return candidate;
    }
}
