using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.RTWorkQ;

internal static class Libraries
{
    public const string RTWorkQ = "RTWorkQ.dll";
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqRegisterPlatformEvents(
        IRtwqPlatformEvents platformEvents
    );

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnregisterPlatformEvents(
        IRtwqPlatformEvents platformEvents
    );

    [GeneratedComInterface]
    [Guid("63d9255a-7ff1-4b61-8faf-ed6460dacf2b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IRtwqPlatformEvents
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

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqCreateAsyncResult(
        [MarshalAs(UnmanagedType.Interface)] object? appObject,
        IRtwqAsyncCallback callback,
        [MarshalAs(UnmanagedType.Interface)] object? appState,
        out IRtwqAsyncResult asyncResult
    );

    //RtwqPutWorkItemとの違いは？ platform queueに入る点？
    //stateの中にIRtwqAsyncResultを持ったものをPutして、IRtwqAsyncCallback.Invokeで取り出し、呼び出し元に完了通知をするときに使うもの
    //らしいが、これもWorkQueueに入ってから実行されるのを待つ仕組みになってる様子
    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqInvokeCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqInvokeCallback_(
        IRtwqAsyncResult result
    );

    internal static int RtwqInvokeCallback(
        this IRtwqAsyncResult result
    )
    {
        return RtwqInvokeCallback_(result);
    }

    //IMFAsyncCallbackと同じGUID
    [GeneratedComInterface]
    [Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IRtwqAsyncCallback
    {
        [PreserveSig]
        int GetParameters(
            ref uint pdwFlags,
            ref WorkQueueId pdwQueue
        );

        [PreserveSig]
        int Invoke(
            IRtwqAsyncResult pAsyncResult
        );
    }

    //TODO GeneratedComInterface
    //IMFAsyncResultと同じGUID
    [GeneratedComInterface]
    [Guid("ac6b7889-0740-4d51-8619-905994a55cc6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IRtwqAsyncResult
    {
        [PreserveSig]
        int GetState(
            [MarshalAs(UnmanagedType.Interface)] out object? /*IUnknown*/ ppunkState
        );

        [PreserveSig]
        int GetStatus();

        [PreserveSig]
        int SetStatus(
            int hrStatus
        );

        [PreserveSig]
        int GetObject(
            [MarshalAs(UnmanagedType.Interface)] out object? ppObject
        );

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Interface)] 
        object /*IUnknown*/ GetStateNoAddRef();
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

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqBeginRegisterWorkQueueWithMMCSS")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqBeginRegisterWorkQueueWithMMCSS_(
        WorkQueueId workQueueId,
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        int dwTaskId,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncCallback doneCallback,
        [MarshalAs(UnmanagedType.Interface)] object? doneState
    );

    internal static int RtwqBeginRegisterWorkQueueWithMMCSS(
        this WorkQueueId workQueueId,
        string usageClass,
        int dwTaskId,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncCallback doneCallback,
        object? doneState
    )
    {
        return RtwqBeginRegisterWorkQueueWithMMCSS_(workQueueId, usageClass, dwTaskId, lPriority, doneCallback, doneState);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqEndRegisterWorkQueueWithMMCSS")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqEndRegisterWorkQueueWithMMCSS_(
        IRtwqAsyncResult result,
        out nint taskId
    );

    internal static int RtwqEndRegisterWorkQueueWithMMCSS(
        this IRtwqAsyncResult result,
        out nint taskId
    )
    {
        return RtwqEndRegisterWorkQueueWithMMCSS_(result, out taskId);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqBeginUnregisterWorkQueueWithMMCSS")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqBeginUnregisterWorkQueueWithMMCSS_(
        WorkQueueId workQueueId,
        IRtwqAsyncCallback doneCallback,
        [MarshalAs(UnmanagedType.Interface)] object? doneState
    );

    internal static int RtwqBeginUnregisterWorkQueueWithMMCSS(
        this WorkQueueId workQueueId,
        IRtwqAsyncCallback doneCallback,
        object? doneState
    )
    {
        return RtwqBeginUnregisterWorkQueueWithMMCSS_(workQueueId, doneCallback, doneState);
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqEndUnregisterWorkQueueWithMMCSS")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqEndUnregisterWorkQueueWithMMCSS_(
        IRtwqAsyncResult result
    );

    internal static int RtwqEndUnregisterWorkQueueWithMMCSS(
        this IRtwqAsyncResult result
    )
    {
        return RtwqEndUnregisterWorkQueueWithMMCSS_(result);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RtWorkItemKey
    {
        private readonly ulong key;
        internal ulong Key => key;
        internal static RtWorkItemKey None => default;
    }

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqPutWorkItem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqPutWorkItem_(
        WorkQueueId dwQueue,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncResult result
    );

    internal static int RtwqPutWorkItem(
        this WorkQueueId dwQueue,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncResult result
    )
    {
        return RtwqPutWorkItem_(dwQueue, lPriority, result);
    }

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqPutWaitingWorkItem(
        SafeWaitHandle hEvent,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncResult result,
        out RtWorkItemKey Key
    );

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqScheduleWorkItem(
        IRtwqAsyncResult result,
        long Timeout,
        out RtWorkItemKey Key
    );

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqCancelWorkItem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqCancelWorkItem_(
        RtWorkItemKey Key
    );

    internal static int RtwqCancelWorkItem(
        this RtWorkItemKey Key
    )
    {
        return RtwqCancelWorkItem_(Key);
    }

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

    [LibraryImport(Libraries.RTWorkQ, EntryPoint = "RtwqCancelDeadline")]
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

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqAddPeriodicCallback(
        nint /*RTWQPERIODICCALLBACK*/ Callback,
        [MarshalAs(UnmanagedType.Interface)] object /*IUnknown* */ context,
        out PeriodicCallbackKey key
    );

    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqRemovePeriodicCallback(
        nint dwKey
    );

    internal delegate void RtwqPeriodicCallback(
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context
    );
}
