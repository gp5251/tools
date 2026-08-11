using Microsoft.Win32;

namespace Unlocker;

/// <summary>
/// Registers/unregisters the classic shell verbs (ADR 0001) under HKCU —
/// the portable deployment needs no administrator rights. One verb for files
/// (<c>*</c>) and one for folders (<c>Directory</c>); single-selection only.
/// </summary>
internal static class Installer
{
    private const string VerbName = "Unlocker";
    private const string UnlockVerbName = "Unlocker.Unlock";
    private const string MenuText = "强力删除(&F)";
    private const string UnlockMenuText = "解除占用(&U)";
    private static readonly string[] Roots =
    {
        $@"Software\Classes\*\shell\{VerbName}",
        $@"Software\Classes\Directory\shell\{VerbName}",
    };

    internal static void Install(string exePath)
    {
        foreach (var root in Roots)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(root, writable: true);
            verb.SetValue(null, MenuText);
            verb.SetValue("Icon", $"\"{exePath}\"");
            using var command = verb.CreateSubKey("command", writable: true);
            command.SetValue(null, $"\"{exePath}\" \"%1\"");

            // Sibling Unlock verb on the same targets (files + folders).
            using var unlockVerb = Registry.CurrentUser.CreateSubKey(
                root.Replace(VerbName, UnlockVerbName), writable: true);
            unlockVerb.SetValue(null, UnlockMenuText);
            unlockVerb.SetValue("Icon", $"\"{exePath}\"");
            using var unlockCommand = unlockVerb.CreateSubKey("command", writable: true);
            unlockCommand.SetValue(null, $"\"{exePath}\" --unlock \"%1\"");
        }
    }

    internal static void Uninstall()
    {
        foreach (var root in Roots)
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                root.Replace(VerbName, UnlockVerbName), throwOnMissingSubKey: false);
        }
    }

    /// <summary>The exe path the registered verb points at, or null when not installed.</summary>
    internal static string? GetRegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Roots[0]}\command");
        var command = key?.GetValue(null) as string;
        if (command is null) return null;
        // Command form: "<exe>" "%1"
        if (!command.StartsWith('"')) return command;
        int end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : command;
    }

    internal static bool IsInstalled() => GetRegisteredPath() != null;
}
