using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Extensions.Logging;
using Momiji.Core.Cache;
using Momiji.Core.Threading;
using Momiji.Interop.RTWorkQ;
using RTWorkQ = Momiji.Interop.RTWorkQ.NativeMethods;

namespace Momiji.Core.RTWorkQueue;

internal partial class RTWorkQueueAsyncResultPoolValue : PoolValue<RTWorkQ.IRtwqAsyncResult>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueAsyncResultPoolValue> _logger;
    private bool _disposed;
    private RTWorkQ.IRtwqAsyncResult? _rtwqAsyncResult;
    private readonly RTWorkQ.IRtwqAsyncCallback _rtwqAsyncCallback;

    internal ApartmentType CreatedApartmentType { get; init; }
    internal RTWorkQ.IRtwqAsyncResult RtwqAsyncResult => _rtwqAsyncResult!;
    internal uint Id { get; init; }

    private readonly RTWorkQueueManager _parent;

    //GetParameters
    private uint _flags;
    private RTWorkQ.WorkQueueId _workQueueId;

    private Action? _action;
    
    private RTWorkQ.RtWorkItemKey _key;
    private CancellationToken _ct;

    private Action<Exception?, CancellationToken>? _afterAction;

    private bool _completeOnCancel = false;

    [ClassInterface(ClassInterfaceType.None)]
    [GeneratedComClass]
    private partial class RtwqAsyncCallbackImpl(
        RTWorkQueueAsyncResultPoolValue parent
    ) : RTWorkQ.IRtwqAsyncCallback
    {
        private readonly RTWorkQueueAsyncResultPoolValue _parent = parent;

        public int GetParameters(
            ref uint pdwFlags,
            ref RTWorkQ.WorkQueueId pdwQueue
        )
        {
            pdwFlags = _parent._flags;
            pdwQueue = _parent._workQueueId;
            return 0;
        }

        public int Invoke(
            RTWorkQ.IRtwqAsyncResult pAsyncResult
        )
        {
            try
            {
                _parent.Invoke(pAsyncResult);
            }
            catch (Exception e)
            {
                _parent._logger.LogError(e, $"Invoke failed Id:{_parent.Id} {_parent.CreatedApartmentType}");
            }
            return 0;
        }
    }

    internal RTWorkQueueAsyncResultPoolValue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent
    ): base()
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueAsyncResultPoolValue>();
        _parent = parent;
        CreatedApartmentType = ApartmentType.GetApartmentType();

        _rtwqAsyncCallback = new RtwqAsyncCallbackImpl(this);

        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqCreateAsyncResult(
            null, //使わない
            _rtwqAsyncCallback,
            null, //使わない
            out _rtwqAsyncResult
        ));

        Id = _parent.GenerateIdAsyncResult();
        _logger.LogTrace($"create Id:{Id} {CreatedApartmentType}");
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            _logger.LogWarning($"already Disposed Id:{Id} {CreatedApartmentType}");
            return;
        }

        _logger.LogTrace($"Dispose start Id:{Id} {CreatedApartmentType}");
        if (disposing)
        {
        }

        if (_rtwqAsyncResult != null)
        {
            //TODO GeneratedComInterfaceでimportしたものをreleaseする方法？ FinalReleaseComObjectだとエラーになる

            _rtwqAsyncResult = null;
        }

        _disposed = true;
        _logger.LogTrace($"Dispose end Id:{Id} {CreatedApartmentType}");
    }

    internal void Initialize(
        uint flags,
        RTWorkQ.WorkQueueId queue,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        bool completeOnCancel = false
    )
    {
        _key = RTWorkQ.RtWorkItemKey.None;
        _ct = CancellationToken.None;

        RtwqAsyncResult.SetStatus(0);

        _flags = flags;
        _workQueueId = queue;
        _action = action;
        _afterAction = afterAction;
        _completeOnCancel = completeOnCancel;
    }

    internal override void Free()
    {
        base.Free();

        _key = RTWorkQ.RtWorkItemKey.None;
        _ct = CancellationToken.None;

        RtwqAsyncResult.SetStatus(0);

        _flags = 0;
        _workQueueId = RTWorkQ.WorkQueueId.None;
        _action = null;
        _afterAction = null;
        _completeOnCancel = false;
    }

    protected override void InvokeCore(RTWorkQ.IRtwqAsyncResult asyncResult, bool ignore)
    {
        var apartmentType = ApartmentType.GetApartmentType();

        _logger.LogTrace($"RtwqAsyncCallback.Invoke Id:[{Id}] {CreatedApartmentType} {Status} / {apartmentType}");

        if (ignore && _completeOnCancel)
        {
            _logger.LogTrace($"RtwqAsyncCallback.Invoke skip Id:[{Id}] {CreatedApartmentType}");
            return;
        }

        Exception? error = null;
        var afterAction = _afterAction;

        try
        {
            if (!ignore)
            {
                _logger.LogTrace($"_func.invoke Id:[{Id}] {CreatedApartmentType}");
                _action?.Invoke();
                _logger.LogTrace($"_func.invoke Id:[{Id}] {CreatedApartmentType} ok.");
                RanToCompletion();
                RtwqAsyncResult.SetStatus(0);
            }
            else
            {
                _logger.LogTrace($"canceled Id:[{Id}] {CreatedApartmentType}");

                Canceled();

                RtwqAsyncResult.SetStatus(
                    unchecked((int)0x80004004) // E_ABORT
                );
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"_func.failed Id:[{Id}] {CreatedApartmentType}");
            error = e;
            Faulted();

            RtwqAsyncResult.SetStatus(
                unchecked((int)0x8000FFFF) // E_UNEXPECTED
            );
        }

        try
        {
            _logger.LogTrace($"afterAction.Invoke Id:[{Id}]");
            afterAction?.Invoke(error, ignore ? _ct : CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"afterAction.Invoke failed Id:[{Id}] {CreatedApartmentType} {_key.Key}.");
        }
        finally
        {
            _parent.ReleaseAsyncResult(this);
        }
    }

    protected override void CancelCore(bool ignore)
    {
        //TODO InvokeCoreとCancelCoreが同時に動いても問題ないようにする必要アリ？
        var apartmentType = ApartmentType.GetApartmentType();

        _logger.LogTrace($"RtwqAsyncCallback.Cancel Id:[{Id}] {CreatedApartmentType} {Status} / {apartmentType}");

        if (ignore)
        {
            _logger.LogTrace($"RtwqAsyncCallback.Cancel skip Id:[{Id}] {CreatedApartmentType}");
            return;
        }

        var afterAction = _afterAction;

        try
        {
            if (_key.Key != RTWorkQ.RtWorkItemKey.None.Key)
            {
                _logger.LogTrace($"RtwqCancelWorkItem Id:[{Id}] {CreatedApartmentType} {_key.Key}.");
                //TODO RtwqCancelWorkItemするとInvokeに移るので、そちらでReleaseした方がよいかも？
                Marshal.ThrowExceptionForHR(_key.RtwqCancelWorkItem());
                _logger.LogTrace($"RtwqCancelWorkItem Id:[{Id}] {CreatedApartmentType} {_key.Key} ok.");
            }

            if (_completeOnCancel)
            {
                _logger.LogTrace($"canceled Id:[{Id}] {CreatedApartmentType}");

                Canceled();

                RtwqAsyncResult.SetStatus(
                    unchecked((int)0x80004004) // E_ABORT
                );
            }
        }
        catch (COMException e) when (e.HResult == unchecked((int)0xC00D36D5)) //E_NOT_FOUND
        {
            //先に完了している場合は何もしない
            _logger.LogDebug($"already invoked Id:[{Id}] {CreatedApartmentType} {_key.Key} {Status}.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"failed Id:[{Id}] {CreatedApartmentType} {_key.Key}.");

            RtwqAsyncResult.SetStatus(
                unchecked((int)0x8000FFFF) // E_UNEXPECTED
            );
        }

        if (_completeOnCancel)
        {
            try
            {
                _logger.LogTrace($"afterAction.Invoke completeOnCancel Id:[{Id}]");
                afterAction?.Invoke(null, _ct);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"afterAction.Invoke failed Id:[{Id}] {CreatedApartmentType} {_key.Key}.");
            }
            finally
            {
                _parent.ReleaseAsyncResult(this);
            }
        }
    }

    internal void BindCancellationToken(
        RTWorkQ.RtWorkItemKey key,
        CancellationToken ct
    )
    {
        _key = key;
        _ct = ct;

        _ct.Register(Cancel);
    }
}
