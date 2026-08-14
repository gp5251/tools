using System.ComponentModel;
using System.Diagnostics;

namespace Unlocker;

/// <summary>
/// Entry point and flow orchestration.
///
///   Unlocker.exe --install | --uninstall
///   Unlocker.exe [--yes] [--elevated] &lt;targetPath&gt;
///
/// UNELEVATED: Confirmation Gate → Normal Delete → relaunch elevated on failure.
/// ELEVATED:   admin retry → Handle Release → Reboot Delete (ADR 0002/0003).
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitCancelled = 1;
    private const int ExitFailed = 2;
    private const int ExitRelaunched = 3;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();

        if (args.Length == 0)
            return RunDoubleClick();

        var install = args.Contains("--install");
        var uninstall = args.Contains("--uninstall");
        var autoYes = args.Contains("--yes");
        var elevated = args.Contains("--elevated");
        var unlock = args.Contains("--unlock");
        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        try
        {
            if (install) return Install(autoYes);
            if (uninstall) return Uninstall(autoYes);
            if (path is null)
            {
                Dialogs.Error("参数错误", "缺少目标路径。");
                return ExitFailed;
            }
            if (unlock)
            {
                // Unlock always elevates: the confirmation dialog must show the
                // COMPLETE Locking Process list, which needs SeDebugPrivilege.
                return elevated ? RunUnlockElevated(path, autoYes)
                                : RelaunchElevated(path, autoYes, unlock: true);
            }
            // Ctrl+click on 强力删除 = Unattended Delete (ADR 0005). Checked once,
            // here: the key must still be held from the menu click, and the
            // relaunch below carries the decision forward as --yes (the user has
            // long released Ctrl by the time UAC clears).
            if (!autoYes && NativeMethods.IsControlHeld())
                autoYes = true;
            return elevated ? RunElevated(path, autoYes) : RunUnelevated(path, autoYes);
        }
        catch (Exception ex)
        {
            Dialogs.Error("Unlocker 出错", ex.Message);
            return ExitFailed;
        }
    }

    // ---- Unlock: Handle Release, then confirmed termination for Image Locks ----

    private static int RunUnlockElevated(string path, bool autoYes)
    {
        NativeMethods.EnablePrivilege("SeDebugPrivilege");

        var files = DeleteEngine.EnumerateTreeFiles(path);
        if (files.Count == 0)
        {
            if (!autoYes) Dialogs.Info("解除占用", "目标不存在。");
            return ExitOk;
        }

        // Stage 1: Handle Release (confirmed, aggregated locker list).
        int released = 0;
        using (var snapshot = HandleFinder.ScanFor(files))
        {
            var lockers = snapshot.DescribeLockers();
            if (lockers.Count > 0)
            {
                if (!autoYes && !Dialogs.ConfirmUnlock(files.Count, lockers))
                    return ExitCancelled;
                released = snapshot.ReleaseAll();
            }
        }

        // Stage 2: Image Locks — confirmed termination, the only way to free a
        // running exe or loaded DLL (ADR 0004).
        int killed = 0;
        var imageLockers = ImageLockFinder.Find(files);
        if (imageLockers.Count > 0)
        {
            if (!autoYes && !Dialogs.ConfirmUnlockKill(imageLockers))
            {
                if (!autoYes && released > 0)
                    Dialogs.Info("解除占用（部分完成）",
                        $"已释放 {released} 个句柄；{imageLockers.Count} 个运行中的进程未结束，相关项目仍被占用。");
                return ExitCancelled;
            }
            else
            {
                killed = ImageLockFinder.Kill(imageLockers).Count;
            }
        }

        // Final report — unlock's ONLY visible feedback (the file stays in place).
        if (!autoYes)
        {
            var remaining = killed > 0 ? ImageLockFinder.Find(files) : new List<ImageLockFinder.ImageLocker>();
            if (released == 0 && killed == 0 && imageLockers.Count == 0)
                Dialogs.Info("解除占用", "未发现占用：没有进程持有该目标的句柄。");
            else
            {
                var parts = new List<string>();
                if (released > 0) parts.Add($"已释放 {released} 个句柄");
                if (killed > 0) parts.Add($"已结束 {killed} 个进程");
                var text = string.Join("，", parts) + "。";
                if (remaining.Count > 0)
                    text += $"\n\n仍有 {remaining.Count} 个进程未能结束：\n" +
                            string.Join("\n", remaining.Take(5).Select(l => $"{l.Name} (PID {l.Pid})"));
                Dialogs.Info("解除占用完成", text);
            }
        }
        return ExitOk;
    }

    /// <summary>
    /// No arguments (double-click): act as a tiny installer. Not installed → offer
    /// to install; installed elsewhere → offer to relocate the verb here; installed
    /// here → offer to uninstall. Command line is never required.
    /// </summary>
    private static int RunDoubleClick()
    {
        var registered = Installer.GetRegisteredPath();
        var current = GetExePath();

        if (registered is null)
        {
            if (!Dialogs.ConfirmInstall())
                return ExitCancelled;
            return Install(quiet: false);
        }

        // Moved/renamed exe: the verb points at a stale path — offer to heal it.
        if (!string.Equals(registered, current, StringComparison.OrdinalIgnoreCase))
        {
            if (!Dialogs.ConfirmRelocate(registered, current))
                return ExitCancelled;
            return Install(quiet: false); // Install overwrites the stale registration
        }

        return Dialogs.InstalledActions() switch
        {
            InstalledAction.Uninstall => Uninstall(quiet: false),
            _ => ExitOk,
        };
    }

    private static string GetExePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new Win32Exception("无法确定程序路径。");

    // ---- Unelevated: gate + rung 1, then escalate ----

    private static int RunUnelevated(string path, bool autoYes)
    {
        if (!autoYes && !Dialogs.ConfirmGate(path))
            return ExitCancelled;

        var result = DeleteEngine.DeleteTree(path);
        if (result.Success)
            return ExitOk; // silent success — the vanished file IS the confirmation

        // Locked or denied — hand off to an elevated instance (ADR 0003).
        RelaunchElevated(path, autoYes, unlock: false);
        return ExitRelaunched;
    }

    // ---- Elevated: rungs 2-3 ----

    private static int RunElevated(string path, bool autoYes)
    {
        NativeMethods.EnablePrivilege("SeDebugPrivilege");

        // Admin retry first: an ACL denial needs no Handle Release at all.
        var result = DeleteEngine.DeleteTree(path);
        if (result.Success)
            return ExitOk; // silent success

        using var snapshot = HandleFinder.ScanFor(result.Failed);
        var lockers = snapshot.DescribeLockers();
        if (lockers.Count > 0)
        {
            if (!autoYes && !Dialogs.ConfirmRelease(result.Failed, lockers))
                return ExitCancelled;

            snapshot.ReleaseAll();
            result = DeleteEngine.DeleteTree(path);
            if (result.Success)
                return ExitOk; // silent success
        }

        // Rung 3: Image Locks (running exes, loaded DLLs) — confirmed termination.
        var imageLockers = ImageLockFinder.Find(result.Failed);
        if (imageLockers.Count > 0)
        {
            if (!autoYes && !Dialogs.ConfirmKill(result.Failed.Count, imageLockers))
                return ExitCancelled;

            ImageLockFinder.Kill(imageLockers);
            result = DeleteEngine.DeleteTree(path);
            if (result.Success)
                return ExitOk; // silent success
        }

        // Rung 4: user-confirmed Reboot Delete.
        if (!autoYes && !Dialogs.ConfirmReboot(result.Failed))
            return ExitCancelled;

        var scheduled = RebootScheduler.Schedule(result.Failed, out var scheduleFailed);
        if (scheduleFailed.Count == 0 && scheduled.Count > 0)
        {
            // Shown even under --yes: items staying on disk until reboot is an
            // outcome the user must see — a notice, not a confirmation.
            Dialogs.Info("已安排，下次重启时删除",
                $"共 {scheduled.Count} 个项目将在你下次开机时自动删除。\n不会立即重启。");
            return ExitOk;
        }

        // Shown even under --yes: silent failure would look like success.
        Dialogs.Error("无法删除", string.Join("\n", result.Errors.Take(10)));
        return ExitFailed;
    }

    // ---- install / uninstall ----

    private static int Install(bool quiet)
    {
        var exe = GetExePath();
        Installer.Install(exe);
        if (!quiet)
            Dialogs.Info("安装完成",
                "右键菜单已注册。\n\n在文件或文件夹上右键 → 显示更多选项 → 强力删除。\n" +
                "（Shift+右键可直接打开经典菜单）");
        return ExitOk;
    }

    private static int Uninstall(bool quiet)
    {
        Installer.Uninstall();
        if (!quiet) Dialogs.Info("已卸载", "右键菜单已移除。");
        return ExitOk;
    }

    private static int RelaunchElevated(string path, bool autoYes, bool unlock)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetExePath(),
            Arguments = $"--elevated{(unlock ? " --unlock" : "")}{(autoYes ? " --yes" : "")} \"{path}\"",
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            Process.Start(startInfo);
            return ExitRelaunched;
        }
        catch (Win32Exception)
        {
            Dialogs.Error("提权已取消", "需要管理员权限才能继续删除被占用的项目。");
            throw;
        }
    }
}
