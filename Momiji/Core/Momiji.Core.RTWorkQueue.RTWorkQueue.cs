using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Momiji.Core.Threading;
using Momiji.Interop.RTWorkQ;
using RTWorkQ = Momiji.Interop.RTWorkQ.NativeMethods;

namespace Momiji.Core.RTWorkQueue;

[SupportedOSPlatform("windows")]
internal partial class RTWorkQueue : IRTWorkQueue
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueue> _logger;
    private bool _disposed;
    private bool _shutdown;
    internal ApartmentType CreatedApartmentType { get; init; }

    private readonly RTWorkQueueManager _parent;

    internal RTWorkQ.WorkQueueId WorkQueueId
    {
        get; init;
    }

    private readonly int _taskId;

    private RTWorkQ.DeadlineKey? _deadlineKey;

    private RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueue>();
        _parent = parent;
        CreatedApartmentType = ApartmentType.GetApartmentType();
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        int taskId
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(shared) {CreatedApartmentType}");

        if (usageClass != "")
        {
            var putTaskId = taskId;

            //Lock +1
            _logger.LogTrace($"RtwqLockSharedWorkQueue usageClass:{usageClass} basePriority:{basePriority} taskId:{putTaskId:X}");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqLockSharedWorkQueue(
                usageClass,
                (RTWorkQ.AVRT_PRIORITY)basePriority,
                ref putTaskId,
                out var workQueueId
            ));

            _taskId = putTaskId;
            WorkQueueId = workQueueId;
            _logger.LogDebug($"RTWorkQueue(shared) Class:{usageClass} Priority:{basePriority} TaskId:{_taskId:X} QueueId:{workQueueId.Id:X}");
        }
        else
        {
            //usageClass="" のときは、taskIdにnullを渡す必要アリ
            _logger.LogTrace("RtwqLockSharedWorkQueue usageClass:'' basePriority:0 taskId:null");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqLockSharedWorkQueue(
                usageClass,
                0,
                nint.Zero,
                out var workQueueId
            ));
            WorkQueueId = workQueueId;
            _logger.LogDebug($"RTWorkQueue(shared) Class:'' Priority:0 TaskId:null QueueId:{workQueueId.Id:X}");
        }
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        IRTWorkQueue.WorkQueueType type
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(private) {CreatedApartmentType}");

        //Lock +1
        _logger.LogTrace($"RtwqAllocateWorkQueue type:{type}");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqAllocateWorkQueue(
            (RTWorkQ.RTWQ_WORKQUEUE_TYPE)type,
            out var workQueueId
        ));
        WorkQueueId = workQueueId;
        _logger.LogDebug($"RTWorkQueue(private) type:{type} QueueId:{workQueueId.Id:X}");
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        RTWorkQueue workQueue
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(serial) {CreatedApartmentType}");

        //Lock +1
        _logger.LogTrace($"RtwqAllocateSerialWorkQueue parent.QueueId:{workQueue.WorkQueueId.Id:X}");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqAllocateSerialWorkQueue(
            workQueue.WorkQueueId,
            out var workQueueId
        ));
        WorkQueueId = workQueueId;
        _logger.LogDebug($"RTWorkQueue(serial) QueueId:{workQueueId.Id:X}");
    }

    ~RTWorkQueue()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _shutdown = true;

        if (_disposed)
        {
            _logger.LogDebug($"Disposed {CreatedApartmentType}");
            return;
        }

        _logger.LogTrace($"Dispose start {CreatedApartmentType}");

        if (disposing)
        {
        }

        //TODO STAから呼ばれたときはMTAに移動して解放しないとダメ？

        CancelDeadline();

        if (WorkQueueId.Id != default)
        {
            try
            {
                Marshal.ThrowExceptionForHR(WorkQueueId.RtwqUnlockWorkQueue());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "RtwqUnlockWorkQueue failed");
            }
        }

        _disposed = true;
        _logger.LogTrace($"Dispose end {CreatedApartmentType}");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    private class LockToken : IDisposable
    {
        private readonly ILogger<LockToken> _logger;
        private bool _disposed;

        private readonly RTWorkQueue _parent;

        public LockToken(
            RTWorkQueue parent,
            ILoggerFactory loggerFactory
        )
        {
            _parent = parent;
            _logger = loggerFactory.CreateLogger<LockToken>();
        }

        ~LockToken()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                _logger.LogDebug("Disposed");
                return;
            }

            _logger.LogTrace("Dispose start");

            if (disposing)
            {
            }

            try
            {
                _parent.Unlock();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unlock failed");
            }

            _disposed = true;
            _logger.LogTrace("Dispose end");
        }
    }

    public IDisposable Lock()
    {
        _logger.LogTrace("RtwqLockWorkQueue");
        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqLockWorkQueue());

        return new LockToken(this, _loggerFactory);
    }

    internal void Unlock()
    {
        _logger.LogTrace("RtwqUnlockWorkQueue");
        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqUnlockWorkQueue());
    }

    public SafeHandle Join(
        SafeHandle handle    
    )
    {
        _logger.LogTrace("RtwqJoinWorkQueue");
        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqJoinWorkQueue(handle, out var cookie));
        return cookie;
    }

    public void SetDeadline(
        long deadlineInHNS,
        long preDeadlineInHNS
    )
    {
        CancelDeadline();

        if (preDeadlineInHNS == 0)
        {
            _logger.LogTrace("RtwqSetDeadline");
            Marshal.ThrowExceptionForHR(WorkQueueId.RtwqSetDeadline(deadlineInHNS, out _deadlineKey));
        }
        else
        {
            _logger.LogTrace("RtwqSetDeadline2");
            Marshal.ThrowExceptionForHR(WorkQueueId.RtwqSetDeadline2(deadlineInHNS, preDeadlineInHNS, out _deadlineKey));
        }
    }

    public void CancelDeadline()
    {
        if (_deadlineKey != default)
        {
            _logger.LogTrace("RtwqCancelDeadline");
            _deadlineKey.Dispose();
            _deadlineKey = null;
        }
    }

    public int GetMMCSSTaskId()
    {
        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqGetWorkQueueMMCSSTaskId(out var taskId));
        return taskId;
    }
    public IRTWorkQueue.TaskPriority GetMMCSSPriority()
    {
        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqGetWorkQueueMMCSSPriority(out var priority));
        return (IRTWorkQueue.TaskPriority)priority;
    }

    public string GetMMCSSClass()
    {
        var length = 1;

        {
            Span<char> text = stackalloc char[length];

            var result = WorkQueueId.RtwqGetWorkQueueMMCSSClass(text, ref length);
            if (result != unchecked((int)0xC00D36B1)) //E_BUFFERTOOSMALL
            {
                var e = Marshal.GetExceptionForHR(result);
                if (e != null)
                {
                    throw e;
                }
                _logger.LogTrace("GetMMCSSClass ''");
                return "";
            }
        }

        if (length == 1)
        { //null terminated のみだった
            _logger.LogTrace("GetMMCSSClass ''");
            return "";
        }

        {
            Span<char> text = stackalloc char[length];

            var result = WorkQueueId.RtwqGetWorkQueueMMCSSClass(text, ref length);
            {
                var e = Marshal.GetExceptionForHR(result);
                if (e != null)
                {
                    throw e;
                }
                var text_ = new string(text.TrimEnd('\0'));
                _logger.LogTrace($"GetMMCSSClass {text_}");
                return text_;
            }
        }
    }

    [ClassInterface(ClassInterfaceType.None)]
    [GeneratedComClass]
    private partial class RegisterMMCSSAsyncCallback : RTWorkQ.IRtwqAsyncCallback
    {
        public int GetParameters(ref uint pdwFlags, ref RTWorkQ.WorkQueueId pdwQueue) {
            return unchecked((int)0x80004001); //E_NOTIMPL
        }

        public int Invoke(RTWorkQ.IRtwqAsyncResult pAsyncResult)
        {
            pAsyncResult.GetState(out var obj);
            if (obj is not Func<RTWorkQ.IRtwqAsyncResult, int> func)
            {
                return 0;
            }
            return func.Invoke(pAsyncResult);
        }
    }

    public Task RegisterMMCSSAsync(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        int taskId
    )
    {
        CheckShutdown();

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var callback = new RegisterMMCSSAsyncCallback();

        _logger.LogTrace($"RtwqBeginRegisterWorkQueueWithMMCSS usageClass:{usageClass} taskId:{taskId:X} basePriority:{basePriority}");

        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqBeginRegisterWorkQueueWithMMCSS(
            usageClass,
            taskId,
            (RTWorkQ.AVRT_PRIORITY)basePriority,
            callback,
            (RTWorkQ.IRtwqAsyncResult result) => {
                try
                {
                    _logger.LogTrace("RtwqEndRegisterWorkQueueWithMMCSS");
                    Marshal.ThrowExceptionForHR(result.RtwqEndRegisterWorkQueueWithMMCSS(out var taskId));
                    _logger.LogTrace($"RtwqEndRegisterWorkQueueWithMMCSS result taskId:{taskId:X}");
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error RtwqEndRegisterWorkQueueWithMMCSS");
                    tcs.SetException(e);
                }
                return 0;
            }
        ));

        return tcs.Task;
    }

    public Task UnregisterMMCSSAsync()
    {
        CheckShutdown();

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var callback = new RegisterMMCSSAsyncCallback();

        _logger.LogTrace("RtwqBeginUnregisterWorkQueueWithMMCSS");

        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqBeginUnregisterWorkQueueWithMMCSS(
            callback,
            (RTWorkQ.IRtwqAsyncResult result) => {
                try
                {
                    _logger.LogTrace("RtwqEndUnregisterWorkQueueWithMMCSS");
                    Marshal.ThrowExceptionForHR(result.RtwqEndUnregisterWorkQueueWithMMCSS());
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error RtwqEndUnregisterWorkQueueWithMMCSS");
                    tcs.SetException(e);
                }
                return 0;
            }
        ));

        return tcs.Task;
    }


    public void SetLongRunning(bool enable)
    {
        CheckShutdown();

        Marshal.ThrowExceptionForHR(WorkQueueId.RtwqSetLongRunning(enable));
    }

    public void PutWorkItem(
        IRTWorkQueue.TaskPriority priority,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    )
    {
        CheckShutdown();

        var asyncResult = _parent.GetAsyncResult(0, WorkQueueId, action, afterAction);
        PutWorkItemCore(priority, asyncResult, ct);
    }

    private void PutWorkItemCore(
        IRTWorkQueue.TaskPriority priority,
        RTWorkQueueAsyncResultPoolValue asyncResult,
        CancellationToken ct
    )
    {
        try
        {
            _logger.LogTrace($"PutWorkItem Id:{asyncResult.Id} {asyncResult.CreatedApartmentType}");
            asyncResult.WaitingToRun();
            asyncResult.BindCancellationToken(RTWorkQ.RtWorkItemKey.None, ct);

            Marshal.ThrowExceptionForHR(WorkQueueId.RtwqPutWorkItem(
                (RTWorkQ.AVRT_PRIORITY)priority,
                asyncResult.RtwqAsyncResult
            ));
        }
        catch
        {
            _parent.ReleaseAsyncResult(asyncResult);
            throw;
        }
    }

    public Task PutWorkItemAsync(
        IRTWorkQueue.TaskPriority priority,
        Action action,
        CancellationToken ct = default
    )
    {
        return _parent.ToAsync(
            afterAction_ => PutWorkItem(priority, action, afterAction_, ct)
        );
    }
}
