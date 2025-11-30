using System.Runtime.InteropServices;

namespace Momiji.Core.RTWorkQueue;

public interface IRTWorkQueuePlatformEventsHandler : IDisposable
{
}

public interface IRTWorkQueueTaskSchedulerManager : IDisposable, IAsyncDisposable
{
    TaskScheduler GetTaskScheduler(
        string usageClass = "",
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    );

    void ShutdownTaskScheduler(
        TaskScheduler taskScheduler
    );
}

public interface IRTWorkQueueManager : IDisposable, IAsyncDisposable
{
    IRTWorkQueue CreatePlatformWorkQueue(
        string usageClass = "",
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    );
    IRTWorkQueue CreatePrivateWorkQueue(
        IRTWorkQueue.WorkQueueType type
    );

    IRTWorkQueue CreateSerialWorkQueue(
        IRTWorkQueue workQueue
    );

    void RegisterMMCSS(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    );

    void UnregisterMMCSS();

    void PutWaitingWorkItem<TState>(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action<TState?> action,
        TState? state = default,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task PutWaitingWorkItemAsync<TState>(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action<TState?> action,
        TState? state = default,
        CancellationToken ct = default
    );

    void ScheduleWorkItem<TState>(
        long timeout,
        Action<TState?> action,
        TState? state = default,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task ScheduleWorkItemAsync<TState>(
        long timeout,
        Action<TState?> action,
        TState? state = default,
        CancellationToken ct = default
    );

    IDisposable AddPeriodicCallback<TState>(
        Action<TState?> action,
        TState? state = default
    );
}

public interface IRTWorkQueue : IDisposable
{
    enum TaskPriority : int
    {
        LOW = -1,
        NORMAL = 0,
        HIGH = 1,
        CRITICAL = 2
    }

    enum WorkQueueType : int
    {
        Standard = 0,
        Window = 1,
        MultiThreaded = 2
    }

    void PutWorkItem<TState>(
        TaskPriority priority,
        Action<TState?> action,
        TState? state = default,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task PutWorkItemAsync<TState>(
        TaskPriority priority,
        Action<TState?> action,
        TState? state = default,
        CancellationToken ct = default
    );

    IDisposable Lock();

    SafeHandle Join(
        SafeHandle handle
    );

    void SetDeadline(
        long deadlineInHNS,
        long preDeadlineInHNS = 0
    );

    int GetMMCSSTaskId();
    TaskPriority GetMMCSSPriority();
    string GetMMCSSClass();

    Task RegisterMMCSSAsync(
        string usageClass,
        TaskPriority basePriority,
        int taskId
    );

    Task UnregisterMMCSSAsync();

    void SetLongRunning(bool enable);
}
