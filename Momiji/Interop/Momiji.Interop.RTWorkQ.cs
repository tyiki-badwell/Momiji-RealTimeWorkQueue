using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.RTWorkQ;

internal static class Libraries
{
    public const string RTWorkQ = "RTWorkQ.dll";
}

internal static partial class NativeMethods
{
    //TODO LibraryImport
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqRegisterPlatformEvents(
        [In] IRtwqPlatformEvents platformEvents
    );

    //TODO LibraryImport
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnregisterPlatformEvents(
        [In] IRtwqPlatformEvents platformEvents
    );

    [ComImport]
    [Guid("63d9255a-7ff1-4b61-8faf-ed6460dacf2b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqPlatformEvents
    {
        [PreserveSig]
        int InitializationComplete();

        [PreserveSig]
        int ShutdownStart();

        [PreserveSig]
        int ShutdownComplete();
    }

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockPlatform();

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnlockPlatform();

    internal enum AVRT_PRIORITY
    {
        LOW = -1,
        NORMAL = 0,
        HIGH = 1,
        CRITICAL = 2
    }

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqRegisterPlatformWithMMCSS(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        ref int taskId,
        AVRT_PRIORITY lPriority
    );

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnregisterPlatformFromMMCSS();

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqStartup();

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqShutdown();

    //一旦、SafeHandleでは無くす
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct WorkQueueId
    {
        private readonly uint id;
        internal uint Id => id;

        internal static WorkQueueId None => default;
    }

    //Lockカウントが増える
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockSharedWorkQueue(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        AVRT_PRIORITY basePriority,
        ref int taskId,
        out WorkQueueId id
    );

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockSharedWorkQueue(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        AVRT_PRIORITY basePriority,
        nint taskId,
        out WorkQueueId id
    );

    //AddRef / Releaseの関係を表しているらしいので、WorkQueueに入れるときにLock / Responseが来たらUnlockが良さそう
    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqLockWorkQueue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockWorkQueue_(
        WorkQueueId workQueueId
    );

    internal static int RtwqLockWorkQueue(
        this WorkQueueId workQueueId
    )
    {
        return RtwqLockWorkQueue_(workQueueId);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqUnlockWorkQueue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqUnlockWorkQueue_(
        WorkQueueId workQueueId
    );

    internal static int RtwqUnlockWorkQueue(
        this WorkQueueId workQueueId
    )
    {
        return RtwqUnlockWorkQueue_(workQueueId);
    }


    internal sealed class JoinKey : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly WorkQueueId _workQueueId;
        public JoinKey(
            WorkQueueId workQueueId,
            nint handle_
        ) : base(true)
        {
            _workQueueId = workQueueId;
            SetHandle(handle_);
        }

        protected override bool ReleaseHandle()
        {
            var hResult = _workQueueId.RtwqUnjoinWorkQueue(handle);
            return (hResult == 0);
        }
    }

    //TODO Network Redirectorを作るためのもの？
    //https://learn.microsoft.com/en-us/windows/win32/fileio/network-redirectors
    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqJoinWorkQueue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqJoinWorkQueue_(
        WorkQueueId workQueueId,
        SafeHandle hFile,
        out nint out_
    );

    internal static int RtwqJoinWorkQueue(
        this WorkQueueId workQueueId,
        SafeHandle hFile,
        out JoinKey out_
    )
    {
        var result = RtwqJoinWorkQueue_(workQueueId, hFile, out var out__);
        out_ = new(workQueueId, out__);
        return result;
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqUnjoinWorkQueue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqUnjoinWorkQueue_(
        WorkQueueId workQueueId,
        nint hFile
    );

    internal static int RtwqUnjoinWorkQueue(
        this WorkQueueId workQueueId,
        nint hFile
    )
    {
        return RtwqUnjoinWorkQueue_(workQueueId, hFile);
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqCreateAsyncResult(
        [In][MarshalAs(UnmanagedType.IUnknown)] object? appObject,
        [In] IRtwqAsyncCallback callback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? appState,
        [Out] out IRtwqAsyncResult asyncResult
    );

    //RtwqPutWorkItemとの違いは？ platform queueに入る点？
    //stateの中にIRtwqAsyncResultを持ったものをPutして、IRtwqAsyncCallback.Invokeで取り出し、呼び出し元に完了通知をするときに使うもの
    //らしいが、これもWorkQueueに入ってから実行されるのを待つ仕組みになってる様子
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqInvokeCallback(
        [In] this IRtwqAsyncResult result
    );

    //IMFAsyncCallbackと同じGUID
    [ComImport]
    [Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqAsyncCallback
    {
        [PreserveSig]
        int GetParameters(
            [In][Out] ref uint pdwFlags,
            [In][Out] ref WorkQueueId pdwQueue
        );

        [PreserveSig]
        int Invoke(
            [In] IRtwqAsyncResult pAsyncResult
        );
    }

    //IMFAsyncResultと同じGUID
    [ComImport]
    [Guid("ac6b7889-0740-4d51-8619-905994a55cc6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqAsyncResult
    {
        [PreserveSig]
        int GetState(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? /*IUnknown*/ ppunkState
        );

        [PreserveSig]
        int GetStatus();

        [PreserveSig]
        int SetStatus(
            int hrStatus
        );

        [PreserveSig]
        int GetObject(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? /*IUnknown*/ ppObject
        );

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        object? /*IUnknown*/ GetStateNoAddRef();
    }

    /*
     * 使わない
     * IRtwqAsyncResultの実装を行いたいときに用いる
        typedef struct tagRTWQASYNCRESULT : public IRtwqAsyncResult
        {
            OVERLAPPED overlapped;
            IRtwqAsyncCallback * pCallback;
            HRESULT hrStatusResult;
            DWORD dwBytesTransferred;
            HANDLE hEvent;
        }   RTWQASYNCRESULT;
     */

    internal enum RTWQ_WORKQUEUE_TYPE
    {
        RTWQ_STANDARD_WORKQUEUE = 0,      // single threaded MTA
        RTWQ_WINDOW_WORKQUEUE = 1,        // Message loop that calls PeekMessage() / DispatchMessage()..
        RTWQ_MULTITHREADED_WORKQUEUE = 2, // multithreaded MTA
    }

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqAllocateWorkQueue(
        RTWQ_WORKQUEUE_TYPE WorkQueueType,
        out WorkQueueId workQueueId
    );

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqAllocateSerialWorkQueue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqAllocateSerialWorkQueue_(
        WorkQueueId workQueueIdIn,
        out WorkQueueId workQueueIdOut
    );

    internal static int RtwqAllocateSerialWorkQueue(
        this WorkQueueId workQueueIdIn,
        out WorkQueueId workQueueIdOut
    )
    {
        return RtwqAllocateSerialWorkQueue_(workQueueIdIn, out workQueueIdOut);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqSetLongRunning")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqSetLongRunning_(
        WorkQueueId dwQueue,
        [MarshalAs(UnmanagedType.Bool)] bool enable
    );

    internal static int RtwqSetLongRunning(
        this WorkQueueId dwQueue,
        bool enable
    )
    {
        return RtwqSetLongRunning_(dwQueue, enable);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqGetWorkQueueMMCSSClass", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqGetWorkQueueMMCSSClass_(
        WorkQueueId dwQueue,
        Span<char> usageClass,
        ref int usageClassLength
    );

    internal static int RtwqGetWorkQueueMMCSSClass(
        this WorkQueueId dwQueue,
        Span<char> usageClass,
        ref int usageClassLength
    )
    {
        return RtwqGetWorkQueueMMCSSClass_(dwQueue, usageClass, ref usageClassLength);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqGetWorkQueueMMCSSTaskId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqGetWorkQueueMMCSSTaskId_(
        WorkQueueId dwQueue,
        out int taskId
    );

    internal static int RtwqGetWorkQueueMMCSSTaskId(
        this WorkQueueId dwQueue,
        out int taskId
    )
    {
        return RtwqGetWorkQueueMMCSSTaskId_(dwQueue, out taskId);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqGetWorkQueueMMCSSPriority")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqGetWorkQueueMMCSSPriority_(
        WorkQueueId dwQueue,
        out AVRT_PRIORITY priority
    );

    internal static int RtwqGetWorkQueueMMCSSPriority(
        this WorkQueueId dwQueue,
        out AVRT_PRIORITY priority
    )
    {
        return RtwqGetWorkQueueMMCSSPriority_(dwQueue, out priority);
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqBeginRegisterWorkQueueWithMMCSS(
        [In] this WorkQueueId workQueueId,
        [In][MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        [In] int dwTaskId,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncCallback doneCallback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? doneState
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqEndRegisterWorkQueueWithMMCSS(
        [In] this IRtwqAsyncResult result,
        [Out] out nint taskId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqBeginUnregisterWorkQueueWithMMCSS(
        [In] this WorkQueueId workQueueId,
        [In] IRtwqAsyncCallback doneCallback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? doneState
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqEndUnregisterWorkQueueWithMMCSS(
        [In] this IRtwqAsyncResult result
    );

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RtWorkItemKey
    {
        private readonly ulong key;
        internal ulong Key => key;
        internal static RtWorkItemKey None => default;
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqPutWorkItem(
        [In] this WorkQueueId dwQueue,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncResult result
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqPutWaitingWorkItem(
        [In] SafeWaitHandle hEvent,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncResult result,
        [Out] out RtWorkItemKey Key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqScheduleWorkItem(
        [In] IRtwqAsyncResult result,
        [In] long Timeout,
        [Out] out RtWorkItemKey Key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqCancelWorkItem(
        [In] this RtWorkItemKey Key
    );

    internal sealed class DeadlineKey : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DeadlineKey() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            var hResult = RtwqCancelDeadline(handle);
            return (hResult == 0);
        }
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqSetDeadline")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqSetDeadline_(
        WorkQueueId workQueueId,
        long deadlineInHNS,
        out DeadlineKey pRequest
    );

    public static int RtwqSetDeadline(
        this WorkQueueId workQueueId,
        long deadlineInHNS,
        out DeadlineKey pRequest
    )
    {
        return RtwqSetDeadline_(workQueueId, deadlineInHNS, out pRequest);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqSetDeadline2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqSetDeadline2_(
        WorkQueueId workQueueId,
        long deadlineInHNS,
        long preDeadlineInHNS,
        out DeadlineKey pRequest
    );

    public static int RtwqSetDeadline2(
        this WorkQueueId workQueueId,
        long deadlineInHNS,
        long preDeadlineInHNS,
        out DeadlineKey pRequest
    )
    {
        return RtwqSetDeadline2_(workQueueId, deadlineInHNS, preDeadlineInHNS, out pRequest);
    }

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqCancelDeadline(
        nint pRequest
    );

    internal sealed class PeriodicCallbackKey : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PeriodicCallbackKey() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            var hResult = RtwqRemovePeriodicCallback(handle);
            return (hResult == 0);
        }
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqAddPeriodicCallback(
        [In] nint /*RTWQPERIODICCALLBACK*/ Callback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context,
        [Out] out PeriodicCallbackKey key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqRemovePeriodicCallback(
        [In] nint dwKey
    );

    internal delegate void RtwqPeriodicCallback(
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context
    );
}
