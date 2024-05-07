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
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnregisterPlatformEvents(
        IRtwqPlatformEvents platformEvents
    );
}
internal static partial class NativeMethods
{
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
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockPlatform();
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnlockPlatform();
}
internal static partial class NativeMethods
{
    internal enum AVRT_PRIORITY
    {
        LOW = -1,
        NORMAL = 0,
        HIGH = 1,
        CRITICAL = 2
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqRegisterPlatformWithMMCSS(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        ref int taskId,
        AVRT_PRIORITY lPriority
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnregisterPlatformFromMMCSS();
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqStartup();
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqShutdown();
}
internal static partial class NativeMethods
{
    //一旦、SafeHandleでは無くす
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct WorkQueueId
    {
        private readonly uint id;
        internal uint Id => id;

        internal static WorkQueueId None => default;
    }
}
internal static partial class NativeMethods
{
    //Lockカウントが増える
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockSharedWorkQueue(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        AVRT_PRIORITY basePriority,
        ref int taskId,
        out WorkQueueId id
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockSharedWorkQueue(
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        AVRT_PRIORITY basePriority,
        nint taskId,
        out WorkQueueId id
    );
}
internal static partial class NativeMethods
{
    //AddRef / Releaseの関係を表しているらしいので、WorkQueueに入れるときにLock / Responseが来たらUnlockが良さそう
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqLockWorkQueue(
        WorkQueueId workQueueId
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqLockWorkQueue(
        this NativeMethods.WorkQueueId workQueueId
    )
    {
        return NativeMethods.RtwqLockWorkQueue(workQueueId);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnlockWorkQueue(
        WorkQueueId workQueueId
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqUnlockWorkQueue(
        this NativeMethods.WorkQueueId workQueueId
    )
    {
        return NativeMethods.RtwqUnlockWorkQueue(workQueueId);
    }
}
internal static partial class NativeMethods
{
    internal sealed partial class JoinKey : SafeHandleZeroOrMinusOneIsInvalid
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
}
internal static partial class NativeMethods
{
    //TODO Network Redirectorを作るためのもの？
    //https://learn.microsoft.com/en-us/windows/win32/fileio/network-redirectors
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqJoinWorkQueue(
        WorkQueueId workQueueId,
        SafeHandle hFile,
        out nint out_
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqJoinWorkQueue(
        this NativeMethods.WorkQueueId workQueueId,
        SafeHandle hFile,
        out NativeMethods.JoinKey out_
    )
    {
        var result = NativeMethods.RtwqJoinWorkQueue(workQueueId, hFile, out var out__);
        out_ = new(workQueueId, out__);
        return result;
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqUnjoinWorkQueue(
        WorkQueueId workQueueId,
        nint hFile
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqUnjoinWorkQueue(
        this NativeMethods.WorkQueueId workQueueId,
        nint hFile
    )
    {
        return NativeMethods.RtwqUnjoinWorkQueue(workQueueId, hFile);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqCreateAsyncResult(
        [MarshalAs(UnmanagedType.Interface)] object? appObject,
        IRtwqAsyncCallback callback,
        [MarshalAs(UnmanagedType.Interface)] object? appState,
        out IRtwqAsyncResult asyncResult
    );
}
internal static partial class NativeMethods
{
    //RtwqPutWorkItemとの違いは？ platform queueに入る点？
    //stateの中にIRtwqAsyncResultを持ったものをPutして、IRtwqAsyncCallback.Invokeで取り出し、呼び出し元に完了通知をするときに使うもの
    //らしいが、これもWorkQueueに入ってから実行されるのを待つ仕組みになってる様子
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqInvokeCallback(
        IRtwqAsyncResult result
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqInvokeCallback(
        this NativeMethods.IRtwqAsyncResult result
    )
    {
        return NativeMethods.RtwqInvokeCallback(result);
    }
}
internal static partial class NativeMethods
{
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
}
internal static partial class NativeMethods
{
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
}
internal static partial class NativeMethods
{
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
}
internal static partial class NativeMethods
{
    internal enum RTWQ_WORKQUEUE_TYPE
    {
        RTWQ_STANDARD_WORKQUEUE = 0,      // single threaded MTA
        RTWQ_WINDOW_WORKQUEUE = 1,        // Message loop that calls PeekMessage() / DispatchMessage()..
        RTWQ_MULTITHREADED_WORKQUEUE = 2, // multithreaded MTA
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqAllocateWorkQueue(
        RTWQ_WORKQUEUE_TYPE WorkQueueType,
        out WorkQueueId workQueueId
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqAllocateSerialWorkQueue(
        WorkQueueId workQueueIdIn,
        out WorkQueueId workQueueIdOut
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqAllocateSerialWorkQueue(
        this NativeMethods.WorkQueueId workQueueIdIn,
        out NativeMethods.WorkQueueId workQueueIdOut
    )
    {
        return NativeMethods.RtwqAllocateSerialWorkQueue(workQueueIdIn, out workQueueIdOut);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqSetLongRunning(
        WorkQueueId dwQueue,
        [MarshalAs(UnmanagedType.Bool)] bool enable
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqSetLongRunning(
        this NativeMethods.WorkQueueId dwQueue,
        bool enable
    )
    {
        return NativeMethods.RtwqSetLongRunning(dwQueue, enable);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqGetWorkQueueMMCSSClass(
        WorkQueueId dwQueue,
        Span<char> usageClass,
        ref int usageClassLength
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqGetWorkQueueMMCSSClass(
        this NativeMethods.WorkQueueId dwQueue,
        Span<char> usageClass,
        ref int usageClassLength
    )
    {
        return NativeMethods.RtwqGetWorkQueueMMCSSClass(dwQueue, usageClass, ref usageClassLength);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqGetWorkQueueMMCSSTaskId(
        WorkQueueId dwQueue,
        out int taskId
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqGetWorkQueueMMCSSTaskId(
        this NativeMethods.WorkQueueId dwQueue,
        out int taskId
    )
    {
        return NativeMethods.RtwqGetWorkQueueMMCSSTaskId(dwQueue, out taskId);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqGetWorkQueueMMCSSPriority(
        WorkQueueId dwQueue,
        out AVRT_PRIORITY priority
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqGetWorkQueueMMCSSPriority(
        this NativeMethods.WorkQueueId dwQueue,
        out NativeMethods.AVRT_PRIORITY priority
    )
    {
        return NativeMethods.RtwqGetWorkQueueMMCSSPriority(dwQueue, out priority);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqBeginRegisterWorkQueueWithMMCSS(
        WorkQueueId workQueueId,
        [MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        int dwTaskId,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncCallback doneCallback,
        [MarshalAs(UnmanagedType.Interface)] object? doneState
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqBeginRegisterWorkQueueWithMMCSS(
        this NativeMethods.WorkQueueId workQueueId,
        string usageClass,
        int dwTaskId,
        NativeMethods.AVRT_PRIORITY lPriority,
        NativeMethods.IRtwqAsyncCallback doneCallback,
        object? doneState
    )
    {
        return NativeMethods.RtwqBeginRegisterWorkQueueWithMMCSS(workQueueId, usageClass, dwTaskId, lPriority, doneCallback, doneState);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqEndRegisterWorkQueueWithMMCSS(
        IRtwqAsyncResult result,
        out nint taskId
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqEndRegisterWorkQueueWithMMCSS(
        this NativeMethods.IRtwqAsyncResult result,
        out nint taskId
    )
    {
        return NativeMethods.RtwqEndRegisterWorkQueueWithMMCSS(result, out taskId);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqBeginUnregisterWorkQueueWithMMCSS(
        WorkQueueId workQueueId,
        IRtwqAsyncCallback doneCallback,
        [MarshalAs(UnmanagedType.Interface)] object? doneState
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqBeginUnregisterWorkQueueWithMMCSS(
        this NativeMethods.WorkQueueId workQueueId,
        NativeMethods.IRtwqAsyncCallback doneCallback,
        object? doneState
    )
    {
        return NativeMethods.RtwqBeginUnregisterWorkQueueWithMMCSS(workQueueId, doneCallback, doneState);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqEndUnregisterWorkQueueWithMMCSS(
        IRtwqAsyncResult result
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqEndUnregisterWorkQueueWithMMCSS(
        this NativeMethods.IRtwqAsyncResult result
    )
    {
        return NativeMethods.RtwqEndUnregisterWorkQueueWithMMCSS(result);
    }
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RtWorkItemKey
    {
        private readonly ulong key;
        internal ulong Key => key;
        internal static RtWorkItemKey None => default;
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqPutWorkItem(
        WorkQueueId dwQueue,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncResult result
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqPutWorkItem(
        this NativeMethods.WorkQueueId dwQueue,
        NativeMethods.AVRT_PRIORITY lPriority,
        NativeMethods.IRtwqAsyncResult result
    )
    {
        return NativeMethods.RtwqPutWorkItem(dwQueue, lPriority, result);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqPutWaitingWorkItem(
        SafeWaitHandle hEvent,
        AVRT_PRIORITY lPriority,
        IRtwqAsyncResult result,
        out RtWorkItemKey Key
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqScheduleWorkItem(
        IRtwqAsyncResult result,
        long Timeout,
        out RtWorkItemKey Key
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqCancelWorkItem(
        RtWorkItemKey Key
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqCancelWorkItem(
        this NativeMethods.RtWorkItemKey Key
    )
    {
        return NativeMethods.RtwqCancelWorkItem(Key);
    }
}
internal static partial class NativeMethods
{
    internal sealed partial class DeadlineKey : SafeHandleZeroOrMinusOneIsInvalid
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
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqSetDeadline(
        WorkQueueId workQueueId,
        long deadlineInHNS,
        out DeadlineKey pRequest
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqSetDeadline(
        this NativeMethods.WorkQueueId workQueueId,
        long deadlineInHNS,
        out NativeMethods.DeadlineKey pRequest
    )
    {
        return NativeMethods.RtwqSetDeadline(workQueueId, deadlineInHNS, out pRequest);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqSetDeadline2(
        WorkQueueId workQueueId,
        long deadlineInHNS,
        long preDeadlineInHNS,
        out DeadlineKey pRequest
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static int RtwqSetDeadline2(
        this NativeMethods.WorkQueueId workQueueId,
        long deadlineInHNS,
        long preDeadlineInHNS,
        out NativeMethods.DeadlineKey pRequest
    )
    {
        return NativeMethods.RtwqSetDeadline2(workQueueId, deadlineInHNS, preDeadlineInHNS, out pRequest);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RtwqCancelDeadline(
        nint pRequest
    );
}
internal static partial class NativeMethods
{
    internal sealed partial class PeriodicCallbackKey : SafeHandleZeroOrMinusOneIsInvalid
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
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqAddPeriodicCallback(
        nint /*RTWQPERIODICCALLBACK*/ Callback,
        [MarshalAs(UnmanagedType.Interface)] object /*IUnknown* */ context,
        out PeriodicCallbackKey key
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.RTWorkQ)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RtwqRemovePeriodicCallback(
        nint dwKey
    );
}
internal static partial class NativeMethods
{
    internal delegate void RtwqPeriodicCallback(
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context
    );
}
