using Microsoft.Win32;

namespace Unlocker;

/// <summary>
/// Registers/unregisters the classic shell verbs (ADR 0001) under HKCU —
/// the portable deployment needs no administrator rights. Two verbs, each
/// registered for files (<c>*</c>) and folders (<c>Directory</c>) alike;
/// single-selection only.
///
///   Unlocker         强力删除 — full Deletion Ladder; Ctrl+click = Unattended Delete (ADR 0005)
///   Unlocker.Unlock  解除占用 — Handle Release without deleting
/// </summary>
internal static class Installer
{
    private const string VerbName = "Unlocker";
    private const string UnlockVerbName = "Unlocker.Unlock";
    private const string LegacyForceVerbName = "Unlocker.Force"; // pre-ADR-0005-amendment 无需确认 verb
    private const string MenuText = "强力删除(&F)";
    private const string UnlockMenuText = "解除占用(&U)";

    private static readonly string[] ShellRoots =
    {
        @"Software\Classes\*\shell",
        @"Software\Classes\Directory\shell",
    };

    internal static void Install(string exePath)
    {
        foreach (var root in ShellRoots)
        {
            Register(root, VerbName, MenuText, exePath, $"\"{exePath}\" \"%1\"");
            Register(root, UnlockVerbName, UnlockMenuText, exePath, $"\"{exePath}\" --unlock \"%1\"");
        }
    }

    private static void Register(string shellRoot, string verbName, string menuText, string exePath, string command)
    {
        using var verb = Registry.CurrentUser.CreateSubKey($@"{shellRoot}\{verbName}", writable: true);
        verb.SetValue(null, menuText);
        verb.SetValue("Icon", $"\"{exePath}\"");
        using var commandKey = verb.CreateSubKey("command", writable: true);
        commandKey.SetValue(null, command);
    }

    internal static void Uninstall()
    {
        foreach (var root in ShellRoots)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{VerbName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{UnlockVerbName}", throwOnMissingSubKey: false);
            // Registered by the short-lived three-verb build; remove on upgrade too.
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{LegacyForceVerbName}", throwOnMissingSubKey: false);
        }
    }

    /// <summary>The exe path the registered verb points at, or null when not installed.</summary>
    internal static string? GetRegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ShellRoots[0]}\{VerbName}\command");
        var command = key?.GetValue(null) as string;
        if (command is null) return null;
        // Command form: "<exe>" "%1"
        if (!command.StartsWith('"')) return command;
        int end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : command;
    }

    internal static bool IsInstalled() => GetRegisteredPath() != null;
}
