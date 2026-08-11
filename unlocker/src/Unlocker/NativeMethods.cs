using System.Runtime.InteropServices;

namespace Unlocker;

/// <summary>Win32/NT P/Invoke declarations and constants.</summary>
internal static partial class NativeMethods
{
    // ---- Handle enumeration (SystemExtendedHandleInformation) ----

    internal const int SystemExtendedHandleInformation = 64;
    internal const uint StatusInfoLengthMismatch = 0xC0000004;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemHandleTableEntryInfoEx
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public uint CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public ushort HandleAttributes;
        public uint Reserved;
    }

    [DllImport("ntdll.dll")]
    internal static extern uint NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    // ---- Process / handle manipulation ----

    internal const uint ProcessDupHandle = 0x0040;

    internal const uint DuplicateSameAccess = 0x00000002;
    internal const uint DuplicateCloseSource = 0x00000001;

    internal const uint FileTypeDisk = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        UIntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    // ---- File identity (volume serial + 128-bit file ID; ReFS-safe) ----

    internal const uint ShareReadWriteDelete = 0x00000007;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const int FileIdInfo = 18;
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIdInfoStruct
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        IntPtr file,
        int infoClass,
        out FileIdInfoStruct fileInfo,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    internal static extern uint GetFileType(IntPtr file);

    // ---- Privileges ----

    internal const uint TokenAdjustPrivileges = 0x0020;
    internal const uint TokenQuery = 0x0008;
    internal const uint SePrivilegeEnabled = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    // ---- Reboot Delete ----

    internal const uint MoveFileDelayUntilReboot = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);

    internal static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
                return;
            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled },
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
