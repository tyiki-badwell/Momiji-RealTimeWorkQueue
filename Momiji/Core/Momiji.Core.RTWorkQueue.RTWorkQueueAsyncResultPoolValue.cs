using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Extensions.Logging;
using Momiji.Core.Cache;
using Momiji.Core.Threading;
using Momiji.Internal.Log;
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
                _parent._logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Error, e, "Invoke failed", _parent.Id, _parent.CreatedApartmentType);
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
        _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "create", Id, CreatedApartmentType);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Warning, "already Disposed", Id, CreatedApartmentType);
            return;
        }

        _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "Dispose start", Id, CreatedApartmentType);
        if (disposing)
        {
        }

        if (_rtwqAsyncResult != null)
        {
            //TODO GeneratedComInterfaceでimportしたものをreleaseする方法？ FinalReleaseComObjectだとエラーになる

            _rtwqAsyncResult = null;
        }

        _disposed = true;
        _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "Dispose end", Id, CreatedApartmentType);
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

        _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqAsyncCallback.Invoke", Id, CreatedApartmentType, Status, apartmentType);

        if (ignore && _completeOnCancel)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqAsyncCallback.Invoke skip", Id, CreatedApartmentType);
            return;
        }

        Exception? error = null;
        var afterAction = _afterAction;

        try
        {
            if (!ignore)
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "_func.invoke", Id, CreatedApartmentType);
                //TODO ここで実行コンテキスト切り替え？
                _action?.Invoke();
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "_func.invoke ok", Id, CreatedApartmentType);
                RanToCompletion();
                RtwqAsyncResult.SetStatus(0);
            }
            else
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "canceled", Id, CreatedApartmentType);

                Canceled();

                RtwqAsyncResult.SetStatus(
                    unchecked((int)0x80004004) // E_ABORT
                );
            }
        }
        catch (Exception e)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Error, e, "_func.failed", Id, CreatedApartmentType);
            error = e;
            Faulted();

            RtwqAsyncResult.SetStatus(
                unchecked((int)0x8000FFFF) // E_UNEXPECTED
            );
        }

        try
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "afterAction.Invoke", Id, CreatedApartmentType);
            //TODO ここで実行コンテキスト切り替え？
            //TODO continuetionの仕掛け方は再考する
            afterAction?.Invoke(error, ignore ? _ct : CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Error, e, "afterAction.Invoke failed", Id, CreatedApartmentType, _key.Key);
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

        _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqAsyncCallback.Cancel", Id, CreatedApartmentType, Status, apartmentType);

        if (ignore)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqAsyncCallback.Cancel skip", Id, CreatedApartmentType);
            return;
        }

        var afterAction = _afterAction;

        try
        {
            if (_key.Key != RTWorkQ.RtWorkItemKey.None.Key)
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqCancelWorkItem", Id, CreatedApartmentType, _key.Key);
                //TODO RtwqCancelWorkItemするとInvokeに移るので、そちらでReleaseした方がよいかも？
                Marshal.ThrowExceptionForHR(_key.RtwqCancelWorkItem());
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "RtwqCancelWorkItem ok", Id, CreatedApartmentType, _key.Key);
            }

            if (_completeOnCancel)
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "canceled", Id, CreatedApartmentType);

                Canceled();

                RtwqAsyncResult.SetStatus(
                    unchecked((int)0x80004004) // E_ABORT
                );
            }
        }
        catch (COMException e) when (e.HResult == unchecked((int)0xC00D36D5)) //E_NOT_FOUND
        {
            //先に完了している場合は何もしない
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Debug, "already invoked", Id, CreatedApartmentType, _key.Key, Status);
        }
        catch (Exception e)
        {
            _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Error, e, "failed", Id, CreatedApartmentType, _key.Key);

            RtwqAsyncResult.SetStatus(
                unchecked((int)0x8000FFFF) // E_UNEXPECTED
            );
        }

        if (_completeOnCancel)
        {
            try
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Trace, "afterAction.Invoke completeOnCancel", Id, CreatedApartmentType);
                //TODO ここで実行コンテキスト切り替え？
                afterAction?.Invoke(null, _ct);
            }
            catch (Exception e)
            {
                _logger.LogRTWorkQueueAsyncResultPoolValue(LogLevel.Error, e, "afterAction.Invoke failed", Id, CreatedApartmentType, _key.Key);
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
