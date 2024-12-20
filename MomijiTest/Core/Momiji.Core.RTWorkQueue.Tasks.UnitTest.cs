using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Timer;
using Momiji.Internal.Debug;

namespace Momiji.Core.RTWorkQueue.Tasks;

[TestClass]
public partial class RTWorkQueueTasksTest : IDisposable
{
    private const int TIMES = 10000;
    private const int SUB_TIMES = 100000;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly RTWorkQueueTaskSchedulerManager _workQueueTaskSchedulerManager;

    public RTWorkQueueTasksTest()
    {
        var configuration = CreateConfiguration(/*usageClass, 0, taskId*/);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Cache", LogLevel.Information);
            builder.AddFilter("Momiji.Core.RTWorkQueue", LogLevel.Information);
            builder.AddFilter("Momiji.Internal.Debug", LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<RTWorkQueueTasksTest>();

        _workQueueTaskSchedulerManager = new RTWorkQueueTaskSchedulerManager(configuration, _loggerFactory);
    }

    public void Dispose()
    {
        _workQueueTaskSchedulerManager?.Dispose();
        _loggerFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IConfiguration CreateConfiguration(string usageClass = "", int basePriority = 0, int taskId = 0)
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var section = configuration.GetSection("Momiji.Core.WorkQueue.WorkQueueManager");

        if (usageClass != "")
        {
            section["UsageClass"] = usageClass;
        }

        if (basePriority != 0)
        {
            section["BasePriority"] = basePriority.ToString();
        }

        if (taskId != 0)
        {
            section["TaskId"] = taskId.ToString();
        }

        return configuration;
    }

    private static int TestTask(
        ElapsedTimeCounter counter,
        ConcurrentQueue<(string, long)> list,
        int[] result,
        int index,
        int value,
        CountdownEvent? cde = default
    )
    {
        list.Enqueue(($"action invoke {index} {value}", counter.ElapsedTicks));
        for (var i = 0; i < SUB_TIMES; i++)
        {
            var a = 1;
            var b = 2;
            var _ = a * b * i;
        }
        result[index] = value;

        if (cde != null)
        {
            if (!cde.IsSet)
            {
                cde.Signal();
            }
        }
        list.Enqueue(($"action end {index}", counter.ElapsedTicks));
        return value;
    }

    private void PrintResult(
        ConcurrentQueue<(string, long)> list,
        int[] result
    )
    {
        for (var index = 0; index < result.Length; index++)
        {
            Assert.AreEqual(index+1, result[index]);
        }

        {
            var (tag, time) = list.ToList()[^1];
            _logger.LogInformation($"LAST: {tag}\t{(double)time / 10000}");
        }

        foreach (var (tag, time) in list)
        {
            _logger.LogInformation($"{tag}\t{(double)time / 10000}");
        }
    }

    [TestMethod]
    public void TestLoopNormal()
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        for (var index = 0; index < TIMES; index++)
        {
            list.Enqueue(($"action{index} put", counter.ElapsedTicks));
            TestTask(counter, list, result, index, index+1);
        }

        PrintResult(list, result);
    }

    [TestMethod]
    [Timeout(5000)]
    [DataRow(false)]
    [DataRow(true)]
    public void TestLoopParallel(bool rtwqTaskScheduler)
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var options = new ParallelOptions
        {
            TaskScheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default
        };

        Parallel.For(0, TIMES, options, (index) => {
            list.Enqueue(($"action put {index}", counter.ElapsedTicks));
            TestTask(counter, list, result, index, index + 1);
        });

        PrintResult(list, result);
    }

    [TestMethod]
    [Timeout(10000)]
    [DataRow(false, ApartmentState.STA, ApartmentState.STA)]
    [DataRow(true, ApartmentState.STA, ApartmentState.STA)]
    [DataRow(false, ApartmentState.MTA, ApartmentState.STA)]
    [DataRow(true, ApartmentState.MTA, ApartmentState.STA)]
    [DataRow(false, ApartmentState.STA, ApartmentState.MTA)]
    [DataRow(true, ApartmentState.STA, ApartmentState.MTA)]
    [DataRow(false, ApartmentState.MTA, ApartmentState.MTA)]
    [DataRow(true, ApartmentState.MTA, ApartmentState.MTA)]
    public async Task TestApartment(
        bool rtwqTaskScheduler, 
        ApartmentState apartmentState1, 
        ApartmentState apartmentState2
    )
    {
        var configuration = CreateConfiguration();
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

        var thread = new Thread(async () => {
            _logger.LogInformation($"thread 1 start {Environment.CurrentManagedThreadId:X}");
            ThreadDebug.PrintObjectContext(_loggerFactory!);

            var factory = new TaskFactory(scheduler);

            var task =
                factory.StartNew(() => {
                    ThreadDebug.PrintObjectContext(_loggerFactory!);
                    _logger.LogInformation($"task 1 {Environment.CurrentManagedThreadId:X}");
                });
            await task.ContinueWith((task) => {
                _logger.LogInformation($"task 1 continue {Environment.CurrentManagedThreadId:X}");
            });

            task.Wait();
            _logger.LogInformation($"thread 1 end {Environment.CurrentManagedThreadId:X}");

            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

                var thread = new Thread(async () => {
                    _logger.LogInformation($"thread 2 start {Environment.CurrentManagedThreadId:X}");
                    ThreadDebug.PrintObjectContext(_loggerFactory!);

                    var task =
                        factory.StartNew(() => {
                            ThreadDebug.PrintObjectContext(_loggerFactory!);
                            _logger.LogInformation($"task 2 {Environment.CurrentManagedThreadId:X}");
                        });
                    await task.ContinueWith((task) => {
                        _logger.LogInformation($"task 2 continue {Environment.CurrentManagedThreadId:X}");
                    });
                    task.Wait();
                    _logger.LogInformation($"thread 2 end {Environment.CurrentManagedThreadId:X}");

                    tcs.SetResult();
                });
                thread.TrySetApartmentState(apartmentState2);
                thread.Start();
                await tcs.Task.ContinueWith((task) => {
                    _logger.LogInformation($"thread 2 continue {Environment.CurrentManagedThreadId:X}");
                });
                _logger.LogInformation($"thread 2 join {Environment.CurrentManagedThreadId:X}");
            }

            tcs.SetResult();
        });
        thread.TrySetApartmentState(apartmentState1);
        thread.Start();

        await tcs.Task.ContinueWith((task) => {
            _logger.LogInformation($"thread 1 continue {Environment.CurrentManagedThreadId:X}");
        });

        _logger.LogInformation($"thread 1 join {Environment.CurrentManagedThreadId:X}");
    }

    [TestMethod]
    [Timeout(1000000)]
    [DataRow(false, TIMES)]
    [DataRow(true, TIMES, "Pro Audio")]
    [DataRow(true, TIMES, "Pro Audio", IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [DataRow(true, TIMES, "Pro Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow(true, TIMES, "Pro Audio", IRTWorkQueue.WorkQueueType.Window)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    public void TestDataflow(
        bool rtwqTaskScheduler, 
        int maxDegreeOfParallelism,
        string usageClass = "",
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    )
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var options = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            TaskScheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler(usageClass, type, serial, basePriority, taskId) : TaskScheduler.Default
        };

        var block = new TransformBlock<int, int>(index => {
            TestTask(counter, list, result, index, index + 1);
            return index;
        }, options);

        var next = new TransformBlock<int, int>(index => {
            TestTask(counter, list, result, index, index + 1);
            return index;
        }, options);

        var last = new ActionBlock<int>(index => {
            TestTask(counter, list, result, index, index + 1);
        }, options);

        block.LinkTo(next);
        next.LinkTo(last);

        for (var index = 0; index < TIMES; index++)
        {
            list.Enqueue(($"action put {index}", counter.ElapsedTicks));
            block.Post(index);
        }
        block.Complete();

        //終了待ち
        block.Completion.Wait();

        PrintResult(list, result);
    }

    [TestMethod]
    public void TestThreadPool_QueueUserWorkItem()
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        using var cde = new CountdownEvent(TIMES);

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        void workItem(int index)
        {
            TestTask(counter, list, result, index, index + 1, cde);
        }

        for (var i = 0; i < TIMES; i++)
        {
            var j = i;
            list.Enqueue(($"action put {j}", counter.ElapsedTicks));
            ThreadPool.QueueUserWorkItem(workItem, j, false);
        }

        //終了待ち
        cde.Wait();

        PrintResult(list, result);
    }

    [TestMethod]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, true)]
    [DataRow(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [DataRow(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [DataRow(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, false)]
    [DataRow(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, true)]
    public async Task TestTaskFactoryStartNewAction(
        TaskCreationOptions taskCreationOptionsParent,
        TaskCreationOptions taskCreationOptionsChild,
        TaskContinuationOptions taskContinuationOptions,
        bool rtwqTaskScheduler,
        bool childError = false
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;
        var factory =
            new TaskFactory(
                CancellationToken.None,
                taskCreationOptionsParent,
                taskContinuationOptions,
                scheduler
            );

        var result = new int[3];
        Assert.AreEqual(0, result[0]);
        Assert.AreEqual(0, result[1]);
        Assert.AreEqual(0, result[2]);

        Task? child = null;
        Task? cont = null;

        var parent = factory.StartNew(() => {

            child = factory.StartNew(() => {
                Task.Delay(100).Wait();
                _logger.LogInformation($"2:{Environment.StackTrace}");

                if (childError)
                {
                    throw new Exception("child error");
                }

                result[1] = 2;
            }, taskCreationOptionsChild);

            cont = child.ContinueWith((task) => {
                Task.Delay(200).Wait();
                _logger.LogInformation($"3:{Environment.StackTrace}");

                result[2] = 3;
            }, taskContinuationOptions);

            _logger.LogInformation($"1:{Environment.StackTrace}");
            result[0] = 1;
        });

        await parent;
        _logger.LogInformation($"Parent end {taskCreationOptionsParent} {taskCreationOptionsChild} {taskContinuationOptions}");

        Assert.AreEqual(1, result[0]);

        _logger.LogInformation($"Parent !DenyChildAttach {((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)}");
        _logger.LogInformation($"Child AttachedToParent {((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)
        )
        {
            Assert.AreEqual(2, result[1]);
        }
        else
        {
            try
            {
                await child!;
                Assert.AreEqual(2, result[1]);
            }
            catch(Exception e)
            {
                if (!childError)
                {
                    Assert.Fail(e.Message);
                }
            }
        }

        _logger.LogInformation($"Continue AttachedToParent {((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)
        )
        {
            Assert.AreEqual(3, result[2]);
        }
        else
        {
            await cont!;
            Assert.AreEqual(3, result[2]);
        }
    }

    [TestMethod]
    [DataRow(false, TaskCreationOptions.DenyChildAttach)]
    [DataRow(true, TaskCreationOptions.DenyChildAttach)]
    [DataRow(false, TaskCreationOptions.AttachedToParent)]
    [DataRow(true, TaskCreationOptions.AttachedToParent)]
    public async Task TestTaskFactoryStartNewAction2(
        bool rtwqTaskScheduler,
        TaskCreationOptions taskCreationOptionsParent
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;
        var factory = new TaskFactory(CancellationToken.None, taskCreationOptionsParent, TaskContinuationOptions.None, scheduler);

        _logger.LogInformation("START 1");
        var result = await await factory.StartNew(async () => {
            _logger.LogInformation("START 2");
            var result = await await factory.StartNew(async () => {
                _logger.LogInformation("START 3");
                var result = await await factory.StartNew(async () => {
                    _logger.LogInformation("START 4");
                    await Task.Delay(1);
                    _logger.LogInformation("END 4");
                    return 1;
                });
                _logger.LogInformation($"END 3 {result}");
                return result + 1;
            });
            _logger.LogInformation($"END 2 {result}");
            return result + 1;
        });
        _logger.LogInformation($"END 1 {result}");
    }

    private async IAsyncEnumerable<int> GenerateAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var i = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(0, ct);
            yield return i++;
        }
    }

    [TestMethod]
    [Timeout(10000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestAsyncEnumerable(
        bool rtwqTaskScheduler
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;
        var factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.None, scheduler);

        var counter = new ElapsedTimeCounter();

        using var cts = new CancellationTokenSource();

        var _ = Task.Delay(50000).ContinueWith((task) => {
            _logger.LogInformation($"CANCEL {(double)counter.ElapsedTicks / 10_000}");
            cts.Cancel();
        });

        try
        {
            counter.Reset();
            _logger.LogInformation($"START {(double)counter.ElapsedTicks / 10_000}");
            await await factory.StartNew(async () => {
                _logger.LogInformation($"TASK START {(double)counter.ElapsedTicks / 10_000}");
                await foreach (var a in GenerateAsync(cts.Token))
                {
                    _logger.LogInformation($"GENERATE {(double)counter.ElapsedTicks / 10_000} {a}");
                    if (a >= TIMES)
                    {
                        break;
                    }
                }
                _logger.LogInformation($"TASK END {(double)counter.ElapsedTicks / 10_000}");
            });
            _logger.LogInformation($"END {(double)counter.ElapsedTicks / 10_000}");
        }
        catch (TaskCanceledException e)
        {
            _logger.LogInformation($"cancel {e}");
        }

        //await Task.Delay(5000);

        _logger.LogInformation($"EXIT {(double)counter.ElapsedTicks / 10_000}");
    }

}
