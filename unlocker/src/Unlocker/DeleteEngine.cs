using System.IO.Enumeration;

namespace Unlocker;

/// <summary>
/// Rung 1 of the Deletion Ladder: Permanent Delete with per-entry failure
/// collection. Junctions and symlinks are deleted as links, never followed.
/// Read-only attributes are stripped before deletion — part of "force".
///
/// Speed: one enumeration pass carries each entry's attributes in the find
/// data (no per-entry GetAttributes round-trips), and deletion runs in
/// parallel — files and links first, then directories level by level,
/// deepest first, so a parent is always deleted after its children.
/// </summary>
internal static class DeleteEngine
{
    /// <summary>One tree entry with the attributes captured during enumeration (zero extra syscalls).</summary>
    private readonly record struct TreeEntry(string Path, FileAttributes Attributes, int Depth);

    private static readonly EnumerationOptions EnumOptions = new()
    {
        AttributesToSkip = 0,        // hidden/system entries must be deleted too
        IgnoreInaccessible = false,  // callers record (delete) or skip (unlock) failures themselves
        RecurseSubdirectories = false, // manual recursion: per-directory failure recording, never follow reparse points
    };

    private static IEnumerable<TreeEntry> EnumerateDirectory(string dir, int depth) =>
        new FileSystemEnumerable<TreeEntry>(
            dir,
            (ref FileSystemEntry e) => new TreeEntry(e.ToFullPath(), e.Attributes, depth),
            EnumOptions);

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

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(root);
        }
        catch
        {
            return files;
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            files.Add(root);
            return files;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            List<TreeEntry> entries;
            try
            {
                entries = EnumerateDirectory(dir, depth: 0).ToList();
            }
            catch
            {
                continue; // unreadable subtree — best effort
            }
            foreach (var entry in entries)
            {
                bool isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                bool isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                if (isDirectory && !isReparsePoint)
                    pending.Push(entry.Path);
                else if (!isDirectory)
                    files.Add(entry.Path);
            }
        }
        return files;
    }

    internal sealed class DeleteResult
    {
        /// <summary>
        /// Paths that could not be deleted: unreadable directories first (enumeration
        /// order), then files and directories roughly deepest-first — exact order is
        /// nondeterministic because deletion runs in parallel. Callers that care
        /// (RebootScheduler) re-sort themselves.
        /// </summary>
        public List<string> Failed { get; } = new();
        /// <summary>Human-readable reasons keyed by path fragment, for reporting.</summary>
        public List<string> Errors { get; } = new();
        public bool Success => Failed.Count == 0;
    }

    internal static DeleteResult DeleteTree(string root)
    {
        var result = new DeleteResult();
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        FileAttributes rootAttributes;
        try
        {
            // GetAttributes (not File.Exists) so dangling links are seen as
            // existing — they must be deletable too.
            rootAttributes = File.GetAttributes(root);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return result; // already gone — nothing to do
        }
        catch (Exception ex)
        {
            Record(result, root, ex);
            return result;
        }

        bool rootIsDirectory = (rootAttributes & FileAttributes.Directory) != 0;
        bool rootIsReparse = (rootAttributes & FileAttributes.ReparsePoint) != 0;

        // Single file, or a link (junction/symlink — removed as a link, target untouched).
        if (!rootIsDirectory || rootIsReparse)
        {
            DeleteOne(new TreeEntry(root, rootAttributes, Depth: 0), result);
            return result;
        }

        var files = new List<TreeEntry>();
        var dirs = new List<TreeEntry>();
        var unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(root, depth: 1, files, dirs, unreadable, result);

        // Deletion is syscall-bound, so it parallelizes well even on one disk.
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) };

        // Files and reparse-point directories (deleted as links) first.
        Parallel.ForEach(files, parallel, entry => DeleteOne(entry, result));

        // Real directories, one depth level at a time, deepest first: entries
        // within a level are never ancestor-related, so a level parallelizes
        // safely and the barrier between levels keeps children-before-parents.
        foreach (var level in dirs.GroupBy(e => e.Depth).OrderByDescending(g => g.Key))
            Parallel.ForEach(level, parallel, entry =>
            {
                // Enumeration failed inside Collect — already recorded; a delete
                // attempt here would just fail again. Escalation retries elevated.
                if (!unreadable.Contains(entry.Path))
                    DeleteOne(entry, result);
            });

        DeleteOne(new TreeEntry(root, rootAttributes, Depth: 0), result);
        return result;
    }

    /// <summary>Classifies every entry under <paramref name="dir"/>, recursing into real directories only.</summary>
    private static void Collect(string dir, int depth, List<TreeEntry> files, List<TreeEntry> dirs,
        HashSet<string> unreadable, DeleteResult result)
    {
        List<TreeEntry> entries;
        try
        {
            entries = EnumerateDirectory(dir, depth).ToList();
        }
        catch (Exception ex)
        {
            Record(result, dir, ex);
            unreadable.Add(dir);
            return;
        }

        foreach (var entry in entries)
        {
            bool isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
            bool isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            if (isDirectory && isReparsePoint)
                files.Add(entry); // junction/symlink: deleted as a link, never followed
            else if (isDirectory)
            {
                dirs.Add(entry);
                Collect(entry.Path, depth + 1, files, dirs, unreadable, result);
            }
            else
                files.Add(entry);
        }
    }

    /// <summary>
    /// Deletes one entry. Files and file symlinks go through <see cref="File.Delete"/>;
    /// directories AND directory links through <see cref="Directory.Delete(string, bool)"/>
    /// (RemoveDirectory removes a junction/symlink itself, never its target).
    /// </summary>
    private static void DeleteOne(TreeEntry entry, DeleteResult result)
    {
        TryStripReadOnly(entry);
        try
        {
            if ((entry.Attributes & FileAttributes.Directory) != 0)
                Directory.Delete(entry.Path, recursive: false);
            else
                File.Delete(entry.Path);
        }
        catch (Exception ex)
        {
            lock (result)
                Record(result, entry.Path, ex);
        }
    }

    private static void TryStripReadOnly(TreeEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReadOnly) == 0)
            return; // the common case costs no syscall at all
        try
        {
            // Re-read: the attributes captured during enumeration may be stale.
            var current = File.GetAttributes(entry.Path);
            File.SetAttributes(entry.Path, current & ~FileAttributes.ReadOnly);
        }
        catch { /* best effort — the delete attempt will report the real failure */ }
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
