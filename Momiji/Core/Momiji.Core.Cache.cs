using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Cache;

public abstract class PoolValue<TParam> : IDisposable
{
    public enum PoolValueStatus : int
    {
        Free,
        Rent,
        WaitingToRun,
        Running,
        Canceling,
        RanToCompletion,
        Canceled,
        Faulted,
        Created
    }

    private PoolValueStatus _status = PoolValueStatus.Created;

    public PoolValueStatus Status
    {
        get => _status;
        private set => _status = value;
    }

    internal PoolValue()
    {
    }

    ~PoolValue()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);

    internal virtual void Free()
    {
        Status = PoolValueStatus.Free;
    }

    internal void Rent()
    {
        Status = PoolValueStatus.Rent;
    }

    internal void WaitingToRun()
    {
        Status = PoolValueStatus.WaitingToRun;
    }

    internal void RanToCompletion()
    {
        Status = PoolValueStatus.RanToCompletion;
    }

    internal void Canceled()
    {
        Status = PoolValueStatus.Canceled;
    }

    internal void Faulted()
    {
        Status = PoolValueStatus.Faulted;
    }

    public void Invoke(TParam param)
    {
        if (PoolValueStatus.WaitingToRun == Interlocked.CompareExchange(ref _status, PoolValueStatus.Running, PoolValueStatus.WaitingToRun))
        {
            InvokeCore(param, false);
        }
        else
        {
            InvokeCore(param, true);
        }
    }

    protected abstract void InvokeCore(TParam param, bool ignore);

    public void Cancel()
    {
        if (PoolValueStatus.WaitingToRun == Interlocked.CompareExchange(ref _status, PoolValueStatus.Canceling, PoolValueStatus.WaitingToRun))
        {
            CancelCore(false);
        }
        else
        {
            CancelCore(true);
        }
    }

    protected abstract void CancelCore(bool ignore);
}

public partial class Pool<TKey, TValue, TParam> : IDisposable, IAsyncDisposable
    where TKey : notnull
    where TValue : notnull, PoolValue<TParam>
{
    private readonly ILogger _logger;

    private bool _disposed;

    private readonly ConcurrentStack<(TKey, TValue)> _cache = new();
    private readonly ConcurrentStack<(TKey, TValue)> _avail = new();
    private readonly ConcurrentDictionary<TKey, TValue> _busy = new();

    private readonly Func<(TKey, TValue)> _allocator;

    public Pool(
        Func<(TKey, TValue)> allocator,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<Pool<TKey, TValue, TParam>>();
        _allocator = allocator;
    }

    public TValue Get()
    {
        if (!_avail.TryPop(out var item))
        {
            item = Add();
        }

        _logger.LogCacheKey(LogLevel.Trace, "busy", item.Item1);
        _busy.TryAdd(item.Item1, item.Item2);
        item.Item2.Rent();

        return item.Item2;
    }

    private (TKey, TValue) Add()
    {
        var (key, value) = _allocator();
        _logger.LogCacheKey(LogLevel.Trace, "create", key);
        _cache.Push((key, value));

        return (key, value);
    }

    public void Release(TKey key)
    {
        if (_busy.TryRemove(key, out var value))
        {
            _logger.LogCacheKey(LogLevel.Trace, "release", key);
            value.Free();
            _avail.Push((key, value));
        }
        else
        {
            _logger.LogCacheKey(LogLevel.Warning, "not busy", key);
        }
    }

    ~Pool()
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
            return;
        }

        if (disposing)
        {
            DisposeAsyncCore().AsTask().Wait();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start");

        _logger.LogWithLine(LogLevel.Debug, "busy items", _busy.Count);

        while (IsBusy())
        {
            foreach (var key in _busy.Keys)
            {
                _logger.LogCacheKey(LogLevel.Debug, "try cancel busy", key);
                if (_busy.TryGetValue(key, out var result))
                {
                    _logger.LogCacheKey(LogLevel.Debug, "try cancel", key, result.Status);
                    result.Cancel();
                }
            }

            _logger.LogWithLine(LogLevel.Debug, "wait ...");
            await Task.Delay(10).ConfigureAwait(false);
        }

        _logger.LogWithLine(LogLevel.Debug, "avail items", _avail.Count);
        _logger.LogWithLine(LogLevel.Debug, "cache items", _cache.Count);

        _avail.Clear();

        while (_cache.TryPop(out var result))
        {
            result.Item2.Dispose();
        }
        _cache.Clear();
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end");
    }

    public bool IsBusy()
    {
        return !_busy.IsEmpty;
    }
}
