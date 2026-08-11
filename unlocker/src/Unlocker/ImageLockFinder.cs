using System.Diagnostics;

namespace Unlocker;

/// <summary>
/// Detects Image Locks: running executables or loaded DLLs pinning target paths
/// via kernel image sections. These never appear in the handle table, so this is a
/// separate scan over processes' loaded modules. Used by rung 3 (Process
/// Termination) and by Unlock's honest reporting.
/// </summary>
internal static class ImageLockFinder
{
    internal sealed record ImageLocker(int Pid, string Name, string? ImagePath, string MatchedModule);

    // Processes whose termination would crash the OS itself — never offered.
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "smss", "csrss", "wininit", "winlogon", "lsass", "lsm", "services", "svchost",
    };

    /// <summary>Distinct processes with at least one loaded module under any of <paramref name="paths"/>.</summary>
    internal static List<ImageLocker> Find(IReadOnlyList<string> paths)
    {
        var targets = new HashSet<string>(
            paths.Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);

        // A folder in the failure list means its children may hold image locks too —
        // match modules by path prefix as well.
        var prefixes = paths.Where(Directory.Exists)
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar)
            .ToList();

        var found = new Dictionary<int, ImageLocker>();
        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                if (proc.Id == Environment.ProcessId || proc.Id <= 4)
                    continue;
                string name;
                try
                {
                    name = proc.ProcessName;
                }
                catch
                {
                    continue;
                }
                if (CriticalProcesses.Contains(name))
                    continue;

                string? matched = null;
                string? imagePath = null;
                try
                {
                    imagePath = proc.MainModule?.FileName;
                    foreach (ProcessModule module in proc.Modules)
                    {
                        var file = module.FileName;
                        if (file is null) continue;
                        if (targets.Contains(file) || prefixes.Any(pre => file.StartsWith(pre, StringComparison.OrdinalIgnoreCase)))
                        {
                            matched = file;
                            break;
                        }
                    }
                }
                catch
                {
                    continue; // protected / exited / bitness boundary — cannot inspect
                }

                if (matched != null && !found.ContainsKey(proc.Id))
                    found[proc.Id] = new ImageLocker(proc.Id, name, imagePath, matched);
            }
        }
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Terminates the given processes. Returns the Pids successfully killed.</summary>
    internal static List<int> Kill(IReadOnlyList<ImageLocker> lockers)
    {
        var killed = new List<int>();
        foreach (var locker in lockers)
        {
            if (CriticalProcesses.Contains(locker.Name))
                continue; // belt and braces — confirmation must never reach these
            try
            {
                using var proc = Process.GetProcessById(locker.Pid);
                proc.Kill();
                proc.WaitForExit(5000);
                killed.Add(locker.Pid);
            }
            catch { /* already exited or protected — the delete retry will say */ }
        }
        return killed;
    }
}
