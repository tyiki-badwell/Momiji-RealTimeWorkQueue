using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Internal.Debug;
using Xunit;
using Xunit.Abstractions;

namespace Momiji.Core.RTWorkQueue.Tasks;

public class RTWorkQueueTasksTest : IDisposable
{
    private const int TIMES = 500;
    private const int WAIT = 10;

    private readonly ILoggerFactory _loggerFactory;
    private readonly RTWorkQueuePlatformEventsHandler _workQueuePlatformEventsHandler;
    private readonly RTWorkQueueTaskSchedulerManager _workQueueTaskSchedulerManager;

    private readonly ITestOutputHelper _output;

    public RTWorkQueueTasksTest(
        ITestOutputHelper output
    )
    {
        _output = output;

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

        _workQueuePlatformEventsHandler = new(_loggerFactory);
        //_workQueueManager = new(configuration, _loggerFactory);
        _workQueueTaskSchedulerManager = new(configuration, _loggerFactory);
    }

    public void Dispose()
    {
        _workQueueTaskSchedulerManager?.Dispose();
        _workQueuePlatformEventsHandler?.Dispose();
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
        Thread.CurrentThread.Join(WAIT);
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
            Assert.Equal(index+1, result[index]);
        }

        {
            var (tag, time) = list.ToList()[^1];
            _output.WriteLine($"LAST: {tag}\t{(double)time / 10000}");
        }

        foreach (var (tag, time) in list)
        {
            _output.WriteLine($"{tag}\t{(double)time / 10000}");
        }
    }

    [Fact]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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

    [Theory]
    [InlineData(false, ApartmentState.STA, ApartmentState.STA)]
    [InlineData(true, ApartmentState.STA, ApartmentState.STA)]
    [InlineData(false, ApartmentState.MTA, ApartmentState.STA)]
    [InlineData(true, ApartmentState.MTA, ApartmentState.STA)]
    [InlineData(false, ApartmentState.STA, ApartmentState.MTA)]
    [InlineData(true, ApartmentState.STA, ApartmentState.MTA)]
    [InlineData(false, ApartmentState.MTA, ApartmentState.MTA)]
    [InlineData(true, ApartmentState.MTA, ApartmentState.MTA)]
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
            _output.WriteLine($"thread 1 start {Environment.CurrentManagedThreadId:X}");
            ThreadDebug.PrintObjectContext(_loggerFactory!);

            var factory = new TaskFactory(scheduler);

            var task =
                factory.StartNew(() => {
                    ThreadDebug.PrintObjectContext(_loggerFactory!);
                    _output.WriteLine($"task 1 {Environment.CurrentManagedThreadId:X}");
                });
            await task.ContinueWith((task) => {
                _output.WriteLine($"task 1 continue {Environment.CurrentManagedThreadId:X}");
            });

            task.Wait();
            _output.WriteLine($"thread 1 end {Environment.CurrentManagedThreadId:X}");

            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

                var thread = new Thread(async () => {
                    _output.WriteLine($"thread 2 start {Environment.CurrentManagedThreadId:X}");
                    ThreadDebug.PrintObjectContext(_loggerFactory!);

                    var task =
                        factory.StartNew(() => {
                            ThreadDebug.PrintObjectContext(_loggerFactory!);
                            _output.WriteLine($"task 2 {Environment.CurrentManagedThreadId:X}");
                        });
                    await task.ContinueWith((task) => {
                        _output.WriteLine($"task 2 continue {Environment.CurrentManagedThreadId:X}");
                    });
                    task.Wait();
                    _output.WriteLine($"thread 2 end {Environment.CurrentManagedThreadId:X}");

                    tcs.SetResult();
                });
                thread.TrySetApartmentState(apartmentState2);
                thread.Start();
                await tcs.Task.ContinueWith((task) => {
                    _output.WriteLine($"thread 2 continue {Environment.CurrentManagedThreadId:X}");
                });
                _output.WriteLine($"thread 2 join {Environment.CurrentManagedThreadId:X}");
            }

            tcs.SetResult();
        });
        thread.TrySetApartmentState(apartmentState1);
        thread.Start();

        await tcs.Task.ContinueWith((task) => {
            _output.WriteLine($"thread 1 continue {Environment.CurrentManagedThreadId:X}");
        });

        _output.WriteLine($"thread 1 join {Environment.CurrentManagedThreadId:X}");
    }

    [Theory]
    [InlineData(false, TIMES)]
    [InlineData(true, TIMES)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void TestDataflow(bool rtwqTaskScheduler, int maxDegreeOfParallelism)
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var options = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            TaskScheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default
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

    [Fact]
    public void TestThreadPool_QueueUserWorkItem()
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        using var cde = new CountdownEvent(TIMES);

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var workItem = (int index) =>
        {
            TestTask(counter, list, result, index, index+1, cde);
        };

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

    [Theory]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false, true)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true, true)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, false)]
    [InlineData(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, true)]
    [InlineData(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [InlineData(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [InlineData(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, false)]
    [InlineData(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, true)]
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
        Assert.Equal(0, result[0]);
        Assert.Equal(0, result[1]);
        Assert.Equal(0, result[2]);

        Task? child = null;
        Task? cont = null;

        var parent = factory.StartNew(() => {

            child = factory.StartNew(() => {
                Task.Delay(100).Wait();
                _output.WriteLine($"2:{Environment.StackTrace}");

                if (childError)
                {
                    throw new Exception("child error");
                }

                result[1] = 2;
            }, taskCreationOptionsChild);

            cont = child.ContinueWith((task) => {
                Task.Delay(200).Wait();
                _output.WriteLine($"3:{Environment.StackTrace}");

                result[2] = 3;
            }, taskContinuationOptions);

            _output.WriteLine($"1:{Environment.StackTrace}");
            result[0] = 1;
        });

        await parent;
        _output.WriteLine($"Parent end {taskCreationOptionsParent} {taskCreationOptionsChild} {taskContinuationOptions}");

        Assert.Equal(1, result[0]);

        _output.WriteLine($"Parent !DenyChildAttach {((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)}");
        _output.WriteLine($"Child AttachedToParent {((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)
        )
        {
            Assert.Equal(2, result[1]);
        }
        else
        {
            try
            {
                await child!;
                Assert.Equal(2, result[1]);
            }
            catch(Exception e)
            {
                if (!childError)
                {
                    Assert.Fail(e.Message);
                }
            }
        }

        _output.WriteLine($"Continue AttachedToParent {((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)
        )
        {
            Assert.Equal(3, result[2]);
        }
        else
        {
            await cont!;
            Assert.Equal(3, result[2]);
        }
    }

    [Theory]
    [InlineData(false, TaskCreationOptions.None)]
    [InlineData(true, TaskCreationOptions.None)]
    [InlineData(false, TaskCreationOptions.AttachedToParent)]
    [InlineData(true, TaskCreationOptions.AttachedToParent)]
    public async Task TestTaskFactoryStartNewAction2(
        bool rtwqTaskScheduler,
        TaskCreationOptions taskCreationOptionsParent
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;
        var factory = new TaskFactory(CancellationToken.None, taskCreationOptionsParent, TaskContinuationOptions.None, scheduler);

        _output.WriteLine("START 1");
        await factory.StartNew(async () => {
            _output.WriteLine("START 2");
            await factory.StartNew(async () => {
                _output.WriteLine("START 3");
                await factory.StartNew(async () => {
                    await Task.Delay(1);
                });
                _output.WriteLine("END 3");
            });
            _output.WriteLine("END 2");
        });
        _output.WriteLine("END 1");
    }

    private async IAsyncEnumerable<int> GenerateAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var i = 0;
        while (!ct.IsCancellationRequested)
        {
            yield return i++;
            await Task.Delay(1, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestAsyncEnumerable(
        bool rtwqTaskScheduler
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskSchedulerManager!.GetTaskScheduler("Pro Audio") : TaskScheduler.Default;
        var factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.None, scheduler);

        var counter = new ElapsedTimeCounter();

        using var cts = new CancellationTokenSource();

        var _ = Task.Delay(500).ContinueWith((task) => {
            _output.WriteLine($"CANCEL {(double)counter.ElapsedTicks / 10_000}");
            cts.Cancel();
        });

        try
        {
            counter.Reset();
            _output.WriteLine($"START {(double)counter.ElapsedTicks / 10_000}");
            await factory.StartNew(async () => {
                _output.WriteLine($"TASK START {(double)counter.ElapsedTicks / 10_000}");
                await foreach (var a in GenerateAsync(cts.Token))
                {
                    _output.WriteLine($"GENERATE {(double)counter.ElapsedTicks / 10_000} {a}");
                }
                _output.WriteLine($"TASK END {(double)counter.ElapsedTicks / 10_000}");
            });
            _output.WriteLine($"END {(double)counter.ElapsedTicks / 10_000}");
        }
        catch (TaskCanceledException e)
        {
            _output.WriteLine($"cancel {e}");
        }

        await Task.Delay(5000);

        _output.WriteLine($"EXIT {(double)counter.ElapsedTicks / 10_000}");
    }

}
