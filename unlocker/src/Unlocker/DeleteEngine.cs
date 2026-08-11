namespace Unlocker;

/// <summary>
/// Rung 1 of the Deletion Ladder: Permanent Delete with per-entry failure
/// collection. Junctions and symlinks are deleted as links, never followed.
/// Read-only attributes are stripped before deletion — part of "force".
/// </summary>
internal static class DeleteEngine
{
    /// <summary>
    /// Every regular file under <paramref name="root"/> (or just the file itself).
    /// Used by Unlock, which must scan ALL files in the tree — not only the ones
    /// deletion failed on. Reparse-point directories are never entered; enumeration
    /// errors skip that subtree (best effort).
    /// </summary>
    internal static List<string> EnumerateTreeFiles(string root)
    {
        var files = new List<string>();
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.Exists(root))
        {
            files.Add(root);
            return files;
        }
        if (!Directory.Exists(root))
            return files;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch
            {
                continue; // unreadable subtree — best effort
            }
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    continue;
                }
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isDirectory && !isReparsePoint)
                    pending.Push(entry);
                else if (!isDirectory)
                    files.Add(entry);
            }
        }
        return files;
    }

    internal sealed class DeleteResult
    {
        /// <summary>Paths that could not be deleted, in depth-first post-order (children before parents).</summary>
        public List<string> Failed { get; } = new();
        /// <summary>Human-readable reasons keyed by path fragment, for reporting.</summary>
        public List<string> Errors { get; } = new();
        public bool Success => Failed.Count == 0;
    }

    internal static DeleteResult DeleteTree(string root)
    {
        var result = new DeleteResult();
        if (!File.Exists(root) && !Directory.Exists(root))
            return result; // already gone — nothing to do

        DeleteEntry(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), result);
        return result;
    }

    private static void DeleteEntry(string path, DeleteResult result)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex)
        {
            Record(result, path, ex);
            return;
        }

        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;

        // Junctions/symlinks are removed as links; their targets are never entered.
        if (isDirectory && !isReparsePoint)
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(path).ToList();
            }
            catch (Exception ex)
            {
                Record(result, path, ex);
                return;
            }

            foreach (var child in children)
                DeleteEntry(child, result);

            TryStripReadOnly(path);
            Try(result, path, () => Directory.Delete(path, recursive: false));
        }
        else
        {
            TryStripReadOnly(path);
            Try(result, path, () => File.Delete(path));
        }
    }

    private static void TryStripReadOnly(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        }
        catch { /* best effort — the delete attempt will report the real failure */ }
    }

    private static void Try(DeleteResult result, string path, Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex)
        {
            Record(result, path, ex);
        }
    }

    private static void Record(DeleteResult result, string path, Exception ex)
    {
        result.Failed.Add(path);
        result.Errors.Add($"{path} — {ex.Message}");
    }
}

/// <summary>
/// Rung 3 of the Deletion Ladder: schedule paths for deletion at next boot.
/// Files are scheduled first, then directories deepest-first, so Session Manager
/// empties a directory before trying to remove it.
/// </summary>
internal static class RebootScheduler
{
    /// <summary>Returns the paths successfully scheduled; failures come back in <paramref name="failed"/>.</summary>
    internal static IReadOnlyList<string> Schedule(IReadOnlyList<string> paths, out List<string> failed)
    {
        var scheduled = new List<string>();
        failed = new List<string>();

        var files = paths.Where(File.Exists).ToList();
        var dirs = paths.Where(Directory.Exists)
                        .OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar))
                        .ToList();

        foreach (var path in files.Concat(dirs))
        {
            if (NativeMethods.MoveFileEx(path, null, NativeMethods.MoveFileDelayUntilReboot))
                scheduled.Add(path);
            else
                failed.Add(path);
        }
        return scheduled;
    }
}
