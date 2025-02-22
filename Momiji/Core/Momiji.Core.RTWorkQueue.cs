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

    void PutWaitingWorkItem(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task PutWaitingWorkItemAsync(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action action,
        CancellationToken ct = default
    );

    void ScheduleWorkItem(
        long timeout,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task ScheduleWorkItemAsync(
        long timeout,
        Action action,
        CancellationToken ct = default
    );

    IDisposable AddPeriodicCallback(
        Action action
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

    void PutWorkItem(
        TaskPriority priority,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    );

    Task PutWorkItemAsync(
        TaskPriority priority,
        Action action, 
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
