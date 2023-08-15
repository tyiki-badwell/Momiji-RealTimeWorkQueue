using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.Kernel32;

internal static class Libraries
{
    public const string Kernel32 = "kernel32.dll";
}

internal static partial class NativeMethods
{
    [Flags]
    public enum BASE_SEARCH_PATH : uint
    {
        ENABLE_SAFE_SEARCHMODE = 0x00000001,
        DISABLE_SAFE_SEARCHMODE = 0x00010000,
        PERMANENT = 0x00008000
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetSearchPathMode(
        BASE_SEARCH_PATH Flags
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetDllDirectoryW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpPathName
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SYSTEM_INFO
    {
        public ushort wProcessorArchitecture;
        public ushort wReserved;
        public uint dwPageSize;
        public nint lpMinimumApplicationAddress;
        public nint lpMaximumApplicationAddress;
        public nint dwActiveProcessorMask;
        public uint dwNumberOfProcessors;
        public uint dwProcessorType;
        public uint dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void GetNativeSystemInfo(
        ref SYSTEM_INFO lpSystemInfo
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process2(
        nint hProcess,
        out ushort pProcessMachine,
        out ushort pNativeMachine
    );

    [Flags]
    public enum DEP_SYSTEM_POLICY_TYPE : uint
    {
        AlwaysOff,
        AlwaysOn,
        OptIn,
        Optout
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DEP_SYSTEM_POLICY_TYPE GetSystemDEPPolicy();

    [Flags]
    public enum PROCESS_DEP : uint
    {
        NONE = 0,
        ENABLE = 1,
        DISABLE_ATL_THUNK_EMULATION = 2
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessDEPPolicy(
        nint hProcess,
        out PROCESS_DEP lpFlags,
        [MarshalAs(UnmanagedType.Bool)] out bool lpPermanent
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDEPPolicy(
        PROCESS_DEP dwFlags
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetModuleHandleW(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName
    );

    internal static class WAITABLE_TIMER
    {
        [Flags]
        public enum FLAGS : uint
        {
            MANUAL_RESET = 0x00000001,
            HIGH_RESOLUTION = 0x00000002,
        }

        [Flags]
        public enum ACCESS_MASK : uint
        {
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            SYNCHRONIZE = 0x00100000,

            QUERY_STATE = 0x0001,
            MODIFY_STATE = 0x0002,

            ALL_ACCESS = 0x1F0003
        }
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeWaitHandle CreateWaitableTimerW(
        [Optional] nint lpTimerAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [Optional] nint lpTimerName
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeWaitHandle CreateWaitableTimerExW(
        [Optional] nint lpTimerAttributes,
        [Optional][MarshalAs(UnmanagedType.LPWStr)] string? lpTimerName,
        WAITABLE_TIMER.FLAGS dwFlags,
        WAITABLE_TIMER.ACCESS_MASK dwDesiredAccess
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimerEx(
        SafeWaitHandle hTimer,
        ref long lpDueTime,
        int lPeriod,
        [Optional] nint pfnCompletionRoutine,
        [Optional] nint lpArgToCompletionRoutine,
        [Optional] nint WakeContext,
        uint TolerableDelay
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static bool SetWaitableTimerEx(
        this SafeWaitHandle hTimer,
        ref long lpDueTime,
        int lPeriod,
        [Optional] nint pfnCompletionRoutine,
        [Optional] nint lpArgToCompletionRoutine,
        [Optional] nint WakeContext,
        uint TolerableDelay
    )
    {
        return NativeMethods.SetWaitableTimerEx(hTimer, ref lpDueTime, lPeriod, pfnCompletionRoutine, lpArgToCompletionRoutine, WakeContext, TolerableDelay);
    }
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int WaitForSingleObject(
        SafeWaitHandle hTimer,
        uint dwMilliseconds
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int WaitForSingleObject(
        this SafeWaitHandle hTimer,
        uint dwMilliseconds
    )
    {
        return NativeMethods.WaitForSingleObject(hTimer, dwMilliseconds);
    }
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(
        nint hObject
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        [Flags]
        public enum STARTF : uint
        {
            USESHOWWINDOW = 0x00000001,
            USESIZE = 0x00000002,
            USEPOSITION = 0x00000004,
            USECOUNTCHARS = 0x00000008,

            USEFILLATTRIBUTE = 0x00000010,
            RUNFULLSCREEN = 0x00000020,
            FORCEONFEEDBACK = 0x00000040,
            FORCEOFFFEEDBACK = 0x00000080,

            USESTDHANDLES = 0x00000100,
            USEHOTKEY = 0x00000200,
            TITLEISLINKNAME = 0x00000800,

            TITLEISAPPID = 0x00001000,
            PREVENTPINNING = 0x00002000,
            UNTRUSTEDSOURCE = 0x00008000,
        }

        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public STARTF dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void GetStartupInfoW(
        ref STARTUPINFOW lpStartupInfo
    );

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial uint FormatMessageW(
        uint dwFlags,
        nint lpSource,
        int dwMessageId,
        uint dwLanguageId,
        char* pszText,
        uint nSize,
        nint Arguments
    );

    public enum PROCESS_INFORMATION_CLASS : uint
    {
        ProcessMemoryPriority,
        ProcessMemoryExhaustionInfo,
        ProcessAppMemoryInfo,
        ProcessInPrivateInfo,
        ProcessPowerThrottling,
        ProcessReservedValue1,
        ProcessTelemetryCoverageInfo,
        ProcessProtectionLevelInfo,
        ProcessLeapSecondInfo,
        ProcessMachineTypeInfo,
        ProcessInformationClassMax
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class MEMORY_PRIORITY_INFORMATION
    {
        public enum MEMORY_PRIORITY : uint
        {
            UNKNOWN = 0,
            VERY_LOW,
            LOW,
            MEDIUM,
            BELOW_NORMAL,
            NORMAL
        };

        public MEMORY_PRIORITY MemoryPriority;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class PROCESS_POWER_THROTTLING_STATE
    {
        [Flags]
        public enum PROCESS_POWER_THROTTLING : uint
        {
            UNKNOWN = 0,
            EXECUTION_SPEED = 0x1,
            IGNORE_TIMER_RESOLUTION = 0x4
        };

        public uint Version;
        public PROCESS_POWER_THROTTLING ControlMask;
        public PROCESS_POWER_THROTTLING StateMask;
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessInformation(
        nint hProcess,
        PROCESS_INFORMATION_CLASS ProcessInformationClass,
        nint ProcessInformation,
        int ProcessInformationSize
    );

}

