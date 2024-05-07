using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Momiji.Core.RTWorkQueue.Tasks;
public class RTWorkQueueTaskSchedulerManagerException : Exception
{
    public RTWorkQueueTaskSchedulerManagerException(string message) : base(message)
    {
    }

    public RTWorkQueueTaskSchedulerManagerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public partial class RTWorkQueueTaskSchedulerManager : IRTWorkQueueTaskSchedulerManager
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueTaskSchedulerManager> _logger;

    private readonly ConcurrentDictionary<RTWorkQueueTaskScheduler.Key, RTWorkQueueTaskScheduler> _taskSchedulerMap = new();

    private bool _disposed;
    private bool _shutdown;

    public RTWorkQueueTaskSchedulerManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTaskSchedulerManager>();
    }

    ~RTWorkQueueTaskSchedulerManager()
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
            _logger.LogDebug("Disposed");
            return;
        }

        _logger.LogTrace("Dispose start");

        if (disposing)
        {
            DisposeAsyncCore().AsTask().Wait();
        }

        _disposed = true;
        _logger.LogTrace("Dispose end");
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown = true;

        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogTrace("DisposeAsync start");
        foreach (var (_, taskScheduler) in _taskSchedulerMap)
        {
            await taskScheduler.DisposeAsync().ConfigureAwait(false);
        }
        _taskSchedulerMap.Clear();
        _logger.LogTrace("DisposeAsync end");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    public TaskScheduler GetTaskScheduler(
        string usageClass = "",
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    )
    {
        CheckShutdown();

        var key =
            new RTWorkQueueTaskScheduler.Key(
                usageClass,
                type,
                serial,
                basePriority,
                taskId
        );

        //valueFactory は多重に実行される可能性があるが、RTWorkQueueTaskSchedulerを作っただけではリソース確保はしないので、良しとする
        return _taskSchedulerMap.GetOrAdd(key, (key) => {
            return 
                new RTWorkQueueTaskScheduler(
                    _configuration,
                    _loggerFactory,
                    key
                );
        });
    }

    public void ShutdownTaskScheduler(
        TaskScheduler taskScheduler
    )
    {
        if (taskScheduler is not RTWorkQueueTaskScheduler target)
        {
            throw new RTWorkQueueTaskSchedulerManagerException("invalid taskScheduler.");
        }

        if (_taskSchedulerMap.TryRemove(target.SelfKey, out var _))
        {
            target.Dispose();
        }
        else
        {
            throw new RTWorkQueueTaskSchedulerManagerException("not found taskScheduler.");
        }
    }
}

internal partial class RTWorkQueueTaskScheduler : TaskScheduler, IDisposable, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueTaskScheduler> _logger;

    private readonly WorkQueues _workQueues = new();

    internal record class Key(
        string UsageClass,
        IRTWorkQueue.WorkQueueType? Type,
        bool Serial,
        IRTWorkQueue.TaskPriority BasePriority,
        int TaskId
    );

    private readonly Key _key;
    internal Key SelfKey => _key;

    private class WorkQueues
    {
        public IRTWorkQueueManager? workQueueManager;
        public IRTWorkQueue? workQueue;
        public IRTWorkQueue? workQueueForLongRunning;

        public WorkQueues Clone()
        {
            return (WorkQueues)MemberwiseClone();
        }
    }

    private bool _disposed;
    private bool _shutdown;

    public RTWorkQueueTaskScheduler(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        Key key
    )
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTaskScheduler>();
        _key = key;
    }

    ~RTWorkQueueTaskScheduler()
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
            _logger.LogDebug($"Disposed {_key}");
            return;
        }

        _logger.LogTrace($"Dispose start {_key}");

        if (disposing)
        {
            DisposeAsyncCore().AsTask().Wait();
        }

        _disposed = true;
        _logger.LogTrace($"Dispose end {_key}");
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown = true;

        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogTrace("DisposeAsync start");
        WorkQueues temp;

        lock (_workQueues)
        {
            temp = _workQueues.Clone();

            _workQueues.workQueueForLongRunning = null;
            _workQueues.workQueue = null;
            _workQueues.workQueueManager = null;
        }

        temp.workQueueForLongRunning?.Dispose();
        temp.workQueue?.Dispose();

        if (temp.workQueueManager != null)
        {
            await temp.workQueueManager.DisposeAsync();
        }
        _logger.LogTrace("DisposeAsync end");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    protected override IEnumerable<Task>? GetScheduledTasks() => throw new NotImplementedException();

    private IRTWorkQueue GetWorkQueue(bool longRunning)
    {
        var workQueues = _workQueues;
        var workQueueManager = workQueues.workQueueManager;
        if (workQueueManager == null)
        {
            lock (workQueues)
            {
                if (workQueueManager == null)
                {
                    workQueueManager = new RTWorkQueueManager(_configuration, _loggerFactory);

                    workQueues.workQueueManager = workQueueManager;
                }
            }
        }

        IRTWorkQueue? workQueue;
        if (longRunning)
        {
            workQueue = workQueues.workQueueForLongRunning;
        }
        else
        {
            workQueue = workQueues.workQueue;
        }

        if (workQueue == null)
        {
            lock (workQueues)
            {
                if (longRunning)
                {
                    workQueue = workQueues.workQueueForLongRunning;
                }
                else
                {
                    workQueue = workQueues.workQueue;
                }

                if (workQueue == null)
                {
                    if (_key.Type != null)
                    {
                        workQueue = workQueueManager.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)_key.Type);
                    }
                    else
                    {
                        workQueue = workQueueManager.CreatePlatformWorkQueue(_key.UsageClass, _key.BasePriority, _key.TaskId);
                    }

                    //TODO serial

                    if (longRunning)
                    {
                        workQueue.SetLongRunning(true);
                        workQueues.workQueueForLongRunning = workQueue;
                    }
                    else
                    {
                        workQueues.workQueue = workQueue;
                    }
                }
            }
        }
        return workQueue;
    }

    protected override void QueueTask(Task task)
    {
        _logger.LogTrace($"QueueTask {task.Id} {task.CreationOptions}");

        CheckShutdown();

        var workQueue = GetWorkQueue(((task.CreationOptions & TaskCreationOptions.LongRunning) != 0));

        workQueue.PutWorkItem(
            IRTWorkQueue.TaskPriority.NORMAL,
            () => {
                TryExecuteTask(task);
            }
        );
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
        _logger.LogTrace($"TryExecuteTaskInline {task.Id} {taskWasPreviouslyQueued}");

        if (taskWasPreviouslyQueued && !TryDequeue(task))
        {
            return false;
        }

        return TryExecuteTask(task);
    }

    protected override bool TryDequeue(Task task)
    {
        _logger.LogTrace($"TryDequeue {task.Id}");
        //キャンセルは出来ない
        return false;
    }
}
