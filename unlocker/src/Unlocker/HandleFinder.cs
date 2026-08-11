using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Unlocker;

/// <summary>
/// Locates the processes holding open handles on target paths, and closes those
/// handles (Handle Release, rung 2 of the Deletion Ladder).
/// Handles are matched by FILE IDENTITY (volume serial + 128-bit file ID) — one
/// metadata query per handle, no namespace walk. This is both fast and more
/// correct than path comparison: a hardlink handle is a lock on the same file.
/// </summary>
internal static class HandleFinder
{
    internal sealed record LockingProcess(int Pid, string Name, string? ImagePath);

    internal readonly record struct FileIdentity(ulong VolumeSerial, ulong FileIdLow, ulong FileIdHigh);

    /// <summary>
    /// One scan serves the whole rung: the same snapshot identifies the Locking
    /// Processes for the confirmation dialog and releases their handles.
    /// </summary>
    internal static HandleSnapshot ScanFor(IReadOnlyList<string> paths) =>
        HandleSnapshot.Scan(ResolveTargets(paths));

    /// <summary>
    /// Opens each target with zero requested access (bypasses share modes) and reads
    /// its file identity. Targets that no longer exist resolve to nothing.
    /// </summary>
    private static HashSet<FileIdentity> ResolveTargets(IReadOnlyList<string> paths)
    {
        var set = new HashSet<FileIdentity>();
        foreach (var p in paths)
        {
            var handle = NativeMethods.CreateFile(
                p, 0, NativeMethods.ShareReadWriteDelete, IntPtr.Zero,
                NativeMethods.OpenExisting, NativeMethods.FileFlagBackupSemantics, IntPtr.Zero);
            if (handle == NativeMethods.InvalidHandleValue)
                continue;
            try
            {
                if (TryGetIdentity(handle, out var id))
                    set.Add(id);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
        return set;
    }

    private static bool TryGetIdentity(IntPtr handle, out FileIdentity identity)
    {
        identity = default;
        if (!NativeMethods.GetFileInformationByHandleEx(
                handle, NativeMethods.FileIdInfo, out var info,
                (uint)Marshal.SizeOf<NativeMethods.FileIdInfoStruct>()))
            return false;
        identity = new FileIdentity(info.VolumeSerialNumber, info.FileIdLow, info.FileIdHigh);
        return true;
    }

    internal static LockingProcess DescribeProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string? image = null;
            try { image = proc.MainModule?.FileName; } catch { /* protected process */ }
            return new LockingProcess(pid, proc.ProcessName, image);
        }
        catch
        {
            return new LockingProcess(pid, $"PID {pid}", null);
        }
    }

    /// <summary>
    /// A scanned set of matching handles. Owns the duplicated-source process handles
    /// the hits borrow; dispose before the owning processes are expected to change.
    ///
    /// Identity queries against foreign handles can stall indefinitely (offline
    /// cloud placeholders, dead network shares, misbehaving filter drivers), so the
    /// queries run on supervised workers. Any item stuck past <see cref="ItemTimeout"/>
    /// is abandoned (skipped) and its worker replaced; leaked threads die with the
    /// process. A hung handle can never hang the tool.
    /// </summary>
    internal sealed class HandleSnapshot : IDisposable
    {
        internal readonly record struct HandleHit(int Pid, UIntPtr HandleValue, IntPtr ProcessHandle);

        private static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(60);

        internal List<HandleHit> Hits { get; } = new();
        /// <summary>Disk handles abandoned after stalling (never identity-checked). Diagnostics only.</summary>
        internal int SkippedCount;

        private readonly Dictionary<uint, IntPtr> _processHandles = new();

        /// <summary>Distinct Locking Processes behind this snapshot's hits, for the confirmation dialog.</summary>
        internal List<LockingProcess> DescribeLockers() =>
            Hits.Select(h => h.Pid)
                .Distinct()
                .Select(HandleFinder.DescribeProcess)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Closes every hit handle in its owning process. Returns the count closed.</summary>
        internal int ReleaseAll()
        {
            int closed = 0;
            foreach (var hit in Hits)
            {
                // DUPLICATE_CLOSE_SOURCE closes the handle in the owning process while
                // handing us a copy; closing the copy completes the release.
                if (NativeMethods.DuplicateHandle(
                        hit.ProcessHandle, hit.HandleValue,
                        NativeMethods.GetCurrentProcess(), out var copy,
                        0, false, NativeMethods.DuplicateCloseSource))
                {
                    NativeMethods.CloseHandle(copy);
                    closed++;
                }
            }
            return closed;
        }

        // Worker slot: the watchdog marks a stalled slot Dead and spawns a replacement.
        private sealed class WorkerSlot
        {
            public int Item = -1;          // candidate index being processed, -1 idle, -2 dead
            public DateTime StartUtc;      // when the current item was picked up
        }

        internal static HandleSnapshot Scan(HashSet<FileIdentity> targets)
        {
            var snapshot = new HandleSnapshot();
            if (targets.Count == 0)
                return snapshot;

            var entries = SnapshotHandles();
            Trace.Log($"handle table: {entries.Count} handles");

            // Phase 1 (this thread): duplicate every disk-file handle. DuplicateHandle
            // and GetFileType are instant and non-blocking; only identity queries stall.
            var candidates = new List<(int Pid, UIntPtr HandleValue, IntPtr ProcessHandle, IntPtr Copy)>();
            int selfPid = Environment.ProcessId;
            foreach (var entry in entries)
            {
                uint pid = (uint)entry.UniqueProcessId;
                if ((int)pid == selfPid || pid == 0)
                    continue;

                if (!snapshot._processHandles.TryGetValue(pid, out var hProc))
                {
                    hProc = NativeMethods.OpenProcess(NativeMethods.ProcessDupHandle, false, pid);
                    snapshot._processHandles[pid] = hProc;
                }
                if (hProc == IntPtr.Zero)
                    continue; // protected process (System, AV, other users) — cannot inspect

                if (!NativeMethods.DuplicateHandle(
                        hProc, entry.HandleValue,
                        NativeMethods.GetCurrentProcess(), out var copy,
                        0, false, NativeMethods.DuplicateSameAccess))
                    continue;

                if (NativeMethods.GetFileType(copy) != NativeMethods.FileTypeDisk)
                {
                    NativeMethods.CloseHandle(copy); // pipes, sockets, devices
                    continue;
                }
                candidates.Add(((int)pid, entry.HandleValue, hProc, copy));
            }
            Trace.Log($"disk-file candidates: {candidates.Count}");

            if (candidates.Count == 0)
                return snapshot;

            // Phase 2: supervised identity queries.
            var results = new FileIdentity?[candidates.Count];
            var gate = new object();
            var completed = new ManualResetEventSlim(false);
            var slots = new List<WorkerSlot>();
            int cursor = -1;
            int finishedCount = 0;

            void Worker(WorkerSlot slot)
            {
                while (true)
                {
                    int index;
                    lock (gate)
                    {
                        if (slot.Item == -2) return; // declared dead by the watchdog
                        if (cursor + 1 >= candidates.Count) { slot.Item = -1; return; }
                        index = ++cursor;
                        slot.Item = index;
                        slot.StartUtc = DateTime.UtcNow;
                    }

                    var (_, _, _, copy) = candidates[index];
                    try
                    {
                        if (TryGetIdentity(copy, out var id))
                            results[index] = id;
                    }
                    catch { /* foreign handle raced away */ }
                    finally
                    {
                        // Abandoned handles leak rather than close — the stuck call may
                        // still hold the value; closing could alias a live handle.
                        bool abandoned;
                        lock (gate)
                        {
                            abandoned = slot.Item == -2;
                            if (!abandoned)
                            {
                                NativeMethods.CloseHandle(copy);
                                slot.Item = -1;
                                finishedCount++;
                                if (finishedCount == candidates.Count) completed.Set();
                            }
                        }
                    }
                }
            }

            void SpawnWorker()
            {
                var slot = new WorkerSlot();
                slots.Add(slot);
                new Thread(() => Worker(slot)) { IsBackground = true }.Start();
            }

            int initialWorkers = Math.Min(4, candidates.Count);
            for (int i = 0; i < initialWorkers; i++)
                SpawnWorker();

            var overallDeadline = DateTime.UtcNow + OverallTimeout;
            while (!completed.Wait(500))
            {
                lock (gate)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var slot = slots[i];
                        if (slot.Item >= 0 && DateTime.UtcNow - slot.StartUtc > ItemTimeout)
                        {
                            var abandoned = candidates[slot.Item];
                            Trace.Log($"abandoned stalled handle (pid {abandoned.Pid}, handle 0x{abandoned.HandleValue:X})");
                            slot.Item = -2; // dead; leaked thread exits on next pickup check
                            finishedCount++;
                            snapshot.SkippedCount++;
                            SpawnWorker();
                        }
                    }
                    if (finishedCount >= candidates.Count) break;
                    if (DateTime.UtcNow > overallDeadline)
                    {
                        snapshot.SkippedCount += candidates.Count - finishedCount;
                        Trace.Log($"overall timeout: abandoning {candidates.Count - finishedCount} items");
                        break;
                    }
                }
            }

            for (int i = 0; i < candidates.Count; i++)
                if (results[i] is { } id && targets.Contains(id))
                    snapshot.Hits.Add(new HandleHit(candidates[i].Pid, candidates[i].HandleValue, candidates[i].ProcessHandle));

            Trace.Log($"scan done: {snapshot.Hits.Count} hits, {snapshot.SkippedCount} skipped");
            return snapshot;
        }

        public void Dispose()
        {
            foreach (var hProc in _processHandles.Values)
                if (hProc != IntPtr.Zero)
                    NativeMethods.CloseHandle(hProc);
            _processHandles.Clear();
        }

        private static unsafe List<NativeMethods.SystemHandleTableEntryInfoEx> SnapshotHandles()
        {
            int bufferSize = 1 << 20; // 1 MiB initial; grows until the table fits
            IntPtr buffer = IntPtr.Zero;
            try
            {
                uint status;
                do
                {
                    if (buffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = NativeMethods.NtQuerySystemInformation(
                        NativeMethods.SystemExtendedHandleInformation,
                        buffer, bufferSize, out _);
                    bufferSize *= 2;
                }
                while (status == NativeMethods.StatusInfoLengthMismatch && bufferSize <= (1 << 28));

                if (status != 0)
                    throw new Win32Exception($"NtQuerySystemInformation failed: 0x{status:X8}");

                ulong count = ((ulong*)buffer)[0];
                var entries = new List<NativeMethods.SystemHandleTableEntryInfoEx>((int)Math.Min(count, int.MaxValue));
                var first = (byte*)buffer + sizeof(ulong) * 2; // NumberOfHandles + Reserved
                int entrySize = sizeof(NativeMethods.SystemHandleTableEntryInfoEx);
                for (ulong i = 0; i < count; i++)
                    entries.Add(*(NativeMethods.SystemHandleTableEntryInfoEx*)(first + (nuint)i * (nuint)entrySize));
                return entries;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }
    }
}

/// <summary>
/// Optional diagnostics: when FORCEDELETE_DEBUG names a file, scan milestones are
/// appended there. Off by default; costs nothing in normal runs.
/// </summary>
internal static class Trace
{
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("FORCEDELETE_DEBUG");

    internal static void Log(string message)
    {
        if (LogPath is null) return;
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* diagnostics never break the tool */ }
    }
}
