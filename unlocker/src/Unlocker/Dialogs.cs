namespace Unlocker;

internal enum InstalledAction { Close, Uninstall }

/// <summary>
/// All user-facing dialogs. TaskDialog with destructive action NEVER as default —
/// the safe choice is always pre-selected.
/// </summary>
internal static class Dialogs
{
    private const string Caption = "Unlocker";

    /// <summary>The Confirmation Gate: first dialog of every Force Delete.</summary>
    internal static bool ConfirmGate(string targetPath)
    {
        var delete = new TaskDialogButton("永久删除(&D)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "永久删除此项目？",
            Text = $"{targetPath}\n\n此操作不进回收站，删除后不可恢复。",
            Icon = TaskDialogIcon.Warning,
            Buttons = { delete, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == delete;
    }

    /// <summary>Rung 2 confirmation: aggregated list of every Locking Process.</summary>
    internal static bool ConfirmRelease(IReadOnlyList<string> failedPaths, IReadOnlyList<HandleFinder.LockingProcess> lockers)
    {
        var release = new TaskDialogButton("关闭句柄并删除(&R)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = $"{failedPaths.Count} 个项目正被占用",
            Text = $"以下 {lockers.Count} 个进程持有这些项目的句柄：",
            Icon = TaskDialogIcon.Warning,
            Buttons = { release, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            Footnote = new TaskDialogFootnote { Text = "强行关闭句柄可能导致这些程序未保存的数据丢失。" },
            Expander = new TaskDialogExpander
            {
                Text = string.Join("\n", lockers.Select(Describe)),
                Position = TaskDialogExpanderPosition.AfterFootnote,
                CollapsedButtonText = "详细信息",
                ExpandedButtonText = "收起",
            },
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == release;
    }

    /// <summary>Rung 3 confirmation: schedule Reboot Delete.</summary>
    internal static bool ConfirmReboot(IReadOnlyList<string> failedPaths)
    {
        var schedule = new TaskDialogButton("下次重启时删除(&B)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "仍无法删除",
            Text = $"剩余 {failedPaths.Count} 个项目无法立即删除。\n是否在下次系统重启时自动删除？\n\n不会立即重启，文件在你下次开机时被删除。",
            Icon = TaskDialogIcon.Warning,
            Buttons = { schedule, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == schedule;
    }

    internal static void Info(string heading, string? details = null)
    {
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = heading,
            Text = details,
            Icon = TaskDialogIcon.Information,
            Buttons = { TaskDialogButton.OK },
            SizeToContent = true,
        };
        TaskDialog.ShowDialog(page);
    }

    internal static void Error(string heading, string? details = null)
    {
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = heading,
            Text = details,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
            SizeToContent = true,
        };
        TaskDialog.ShowDialog(page);
    }

    private static string Describe(HandleFinder.LockingProcess p) =>
        p.ImagePath is null ? $"{p.Name} (PID {p.Pid})" : $"{p.Name} (PID {p.Pid}) — {p.ImagePath}";

    /// <summary>Rung 3 confirmation: terminate processes holding Image Locks.</summary>
    internal static bool ConfirmKill(int failedCount, IReadOnlyList<ImageLockFinder.ImageLocker> lockers)
    {
        var kill = new TaskDialogButton("结束进程并删除(&K)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = $"{lockers.Count} 个进程正在运行这些文件",
            Text = $"剩余 {failedCount} 个项目被运行中的程序锁定（映像锁定，无法通过关闭句柄释放）。\n将结束以下进程：",
            Icon = TaskDialogIcon.Warning,
            Buttons = { kill, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            Footnote = new TaskDialogFootnote { Text = "结束进程将强制其退出，未保存的数据会丢失。" },
            Expander = new TaskDialogExpander
            {
                Text = string.Join("\n", lockers.Select(l => $"{l.Name} (PID {l.Pid}) — {l.MatchedModule}")),
                Position = TaskDialogExpanderPosition.AfterFootnote,
                CollapsedButtonText = "详细信息",
                ExpandedButtonText = "收起",
            },
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == kill;
    }

    /// <summary>Unlock confirmation: aggregated list of every Locking Process.</summary>
    internal static bool ConfirmUnlock(int fileCount, IReadOnlyList<HandleFinder.LockingProcess> lockers)
    {
        var unlock = new TaskDialogButton("解除占用(&U)");
        var page = new TaskDialogPage
        {
            Caption = "解除占用",
            Heading = $"{lockers.Count} 个进程正在占用",
            Text = $"{fileCount} 个项目被以下进程持有句柄：",
            Icon = TaskDialogIcon.Warning,
            Buttons = { unlock, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            Footnote = new TaskDialogFootnote { Text = "强行关闭句柄可能导致这些程序未保存的数据丢失。" },
            Expander = new TaskDialogExpander
            {
                Text = string.Join("\n", lockers.Select(Describe)),
                Position = TaskDialogExpanderPosition.AfterFootnote,
                CollapsedButtonText = "详细信息",
                ExpandedButtonText = "收起",
            },
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == unlock;
    }

    /// <summary>Unlock, rung 2: terminate processes holding Image Locks to free the file.</summary>
    internal static bool ConfirmUnlockKill(IReadOnlyList<ImageLockFinder.ImageLocker> lockers)
    {
        var kill = new TaskDialogButton("结束进程以解除(&K)");
        var page = new TaskDialogPage
        {
            Caption = "解除占用",
            Heading = $"{lockers.Count} 个进程正在运行这些文件",
            Text = "这些项目作为程序运行（映像锁定），关闭句柄无法解除。\n只有结束以下进程才能释放它们：",
            Icon = TaskDialogIcon.Warning,
            Buttons = { kill, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
            Footnote = new TaskDialogFootnote { Text = "结束进程将强制其退出，未保存的数据会丢失。" },
            Expander = new TaskDialogExpander
            {
                Text = string.Join("\n", lockers.Select(l => $"{l.Name} (PID {l.Pid}) — {l.MatchedModule}")),
                Position = TaskDialogExpanderPosition.AfterFootnote,
                CollapsedButtonText = "详细信息",
                ExpandedButtonText = "收起",
            },
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == kill;
    }

    /// <summary>Double-click, not yet installed: offer installation.</summary>
    internal static bool ConfirmInstall()
    {
        var install = new TaskDialogButton("安装(&I)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "安装 Unlocker",
            Text = "把「强力删除」加入文件和文件夹的右键菜单？\n\n仅写入当前用户的注册表，不需要管理员权限，可随时卸载。",
            Icon = TaskDialogIcon.Information,
            Buttons = { install, TaskDialogButton.Cancel },
            DefaultButton = install,
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == install;
    }

    /// <summary>Double-click, verb points at a stale exe path: offer to relocate.</summary>
    internal static bool ConfirmRelocate(string oldPath, string newPath)
    {
        var relocate = new TaskDialogButton("更新到当前位置(&R)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "右键菜单指向旧位置",
            Text = $"注册的路径：\n{oldPath}\n\n当前程序位置：\n{newPath}\n\n是否把右键菜单更新到当前位置？",
            Icon = TaskDialogIcon.Warning,
            Buttons = { relocate, TaskDialogButton.Cancel },
            DefaultButton = relocate,
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == relocate;
    }

    /// <summary>Double-click, already installed: status + uninstall option.</summary>
    internal static InstalledAction InstalledActions()
    {
        var uninstall = new TaskDialogButton("卸载右键菜单(&U)");
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "Unlocker 已安装",
            Text = "在文件或文件夹上右键 → 显示更多选项 → 强力删除。\n（Shift+右键可直接打开经典菜单）",
            Icon = TaskDialogIcon.Information,
            Buttons = { uninstall, TaskDialogButton.OK },
            DefaultButton = TaskDialogButton.OK,
            SizeToContent = true,
        };
        return TaskDialog.ShowDialog(page) == uninstall ? InstalledAction.Uninstall : InstalledAction.Close;
    }
}
