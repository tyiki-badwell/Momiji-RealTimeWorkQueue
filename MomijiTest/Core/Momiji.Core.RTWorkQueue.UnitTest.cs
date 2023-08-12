using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Xunit;
using Xunit.Abstractions;

namespace Momiji.Core.RTWorkQueue;

public class RTWorkQueueTest : IDisposable
{
    private const int TIMES = 50;
    private const int WAIT = 10;

    private readonly ILoggerFactory _loggerFactory;
    private readonly RTWorkQueuePlatformEventsHandler _workQueuePlatformEventsHandler;
    private readonly RTWorkQueueManager _workQueueManager;

    private readonly ITestOutputHelper _output;
    
    public RTWorkQueueTest(
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
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _workQueuePlatformEventsHandler = new(_loggerFactory);
        _workQueueManager = new(configuration, _loggerFactory);
    }

    public void Dispose()
    {
        _workQueueManager.Dispose();
        _workQueuePlatformEventsHandler.Dispose();
        _loggerFactory.Dispose();
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

    private static T TestTask<T>(
        ElapsedTimeCounter counter,
        ConcurrentQueue<(string, long)> list,
        T id,
        CountdownEvent? cde = default
    )
    {
        list.Enqueue(($"action invoke {id}", counter.ElapsedTicks));
        Thread.CurrentThread.Join(WAIT);
        if (cde != null)
        {
            if (!cde.IsSet)
            {
                cde.Signal();
            }
        }
        list.Enqueue(($"action end {id}", counter.ElapsedTicks));
        return id;
    }

    private void PrintResult(
        ConcurrentQueue<(string, long)> list
    )
    {
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
    public void TestRtwqStartupTwice()
    {
        var configuration = CreateConfiguration();


        using var workQueueManager = new RTWorkQueueManager(configuration, _loggerFactory);
        using var workQueueManager2 = new RTWorkQueueManager(configuration, _loggerFactory);


    }

    [Theory]
    [InlineData("Pro Audio", IRTWorkQueue.TaskPriority.CRITICAL)]
    [InlineData("Pro Audio", IRTWorkQueue.TaskPriority.HIGH)]
    [InlineData("Pro Audio", IRTWorkQueue.TaskPriority.NORMAL)]
    [InlineData("Pro Audio", IRTWorkQueue.TaskPriority.LOW)]
    [InlineData("", IRTWorkQueue.TaskPriority.NORMAL, true)]
    public void TestRtwqCreateWorkQueue(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        bool comException = false
    )
    {
        var taskId = 0;

        using var workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass, basePriority);

        Assert.Equal(usageClass, workQueue.GetMMCSSClass());
        Assert.Equal(basePriority, workQueue.GetMMCSSPriority());

        try
        {
            Assert.NotEqual(taskId, workQueue.GetMMCSSTaskId());
        }
        catch (COMException)
        {
            if (comException)
            {
                //classが""の場合は、taskIdを取り出そうとするとE_FAILになる
                return;
            }
            throw;
        }
    }

    [Fact]
    public void TestRtwqLock()
    {
        using var workQueue = _workQueueManager.CreatePlatformWorkQueue();

        {
            using var key = workQueue.Lock();
            workQueue.PutWorkItemAsync(IRTWorkQueue.TaskPriority.NORMAL, () => { }).Wait();
        }

        {
            using var key = workQueue.Lock();
            workQueue.PutWorkItemAsync(IRTWorkQueue.TaskPriority.NORMAL, () => { }).Wait();
        }
    }

    [Fact]
    public async Task TestRtwqJoin()
    {
        using var workQueue = _workQueueManager.CreatePlatformWorkQueue();

        var ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345);

        var server = Task.Run(async () => {
            using var listener = 
                new Socket(
                    ipEndPoint.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );

            listener.Bind(ipEndPoint);
            listener.Listen(100);

            using var cookie = workQueue.Join(listener.SafeHandle);
            _output.WriteLine("server join");

            //TODO joinした後にasync版を呼び出すとエラーになる
            //var handler = await listener.AcceptAsync();
            var handler = listener.Accept();
            _output.WriteLine("accept");

            while(true)
            {
                var buffer = new byte[100];
                var length = handler.Receive(buffer);
                if (length == 0)
                {
                    break;
                }
                var s = System.Text.Encoding.ASCII.GetString(buffer);
                _output.WriteLine($"receive [{s}]");
            }

            await Task.Delay(5000);
        });

        var client = Task.Run(async () => {
            _output.WriteLine("run client");

            using var client = 
                new Socket(
                    ipEndPoint.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );
            await Task.Delay(100);

            using var cookie = workQueue.Join(client.SafeHandle);
            _output.WriteLine("client join");

            //TODO joinした後にasync版を呼び出すとエラーになる
            //await client.ConnectAsync(ipEndPoint);
            client.Connect(ipEndPoint);
            _output.WriteLine("connect");

            var options = new ParallelOptions
            {
                TaskScheduler = TaskScheduler.Default
            };

            Parallel.For(0, 100, options, (index) => {
                var text = "1234567890:" + index.ToString();
                var buffer = System.Text.Encoding.ASCII.GetBytes(text + "\n");
                client.Send(buffer);
                _output.WriteLine($"send [{text}]");
            });

            await Task.Delay(1000);
        });

        await server;
        await client;
    }

    [Fact]
    public async Task TestRtwqSetDeadline()
    {
        using var workQueue = _workQueueManager.CreatePlatformWorkQueue();
        workQueue.SetDeadline(1_000_0); // 1msec

        //Deadlineが短いQueueの方が優先され気味になる様子
        using var workQueue2 = _workQueueManager.CreatePlatformWorkQueue();
        workQueue2.SetDeadline(1_0); // 1usec

        var list = new ConcurrentQueue<(string, long)>();
        var taskMap = new ConcurrentDictionary<Task, int>();
        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var putTask = Task.Run(() => {
            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;
                list.Enqueue(($"action put {j}", counter.ElapsedTicks));
                taskMap.TryAdd(workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j);
                    }
                ), j);
            }
        });

        var putTask2 = Task.Run(() => {
            for (var i = 1; i <= TIMES; i++)
            {
                var j = i + TIMES;
                list.Enqueue(($"action put {j}", counter.ElapsedTicks));
                taskMap.TryAdd(workQueue2.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j);
                    }
                ), j);
            }
        });

        await putTask;
        await putTask2;

        //終了待ち
        while (taskMap.Count > 0)
        {
            var task = await Task.WhenAny(taskMap.Keys).ConfigureAwait(false);
            taskMap.Remove(task, out var id);

            _output.WriteLine(($"task {id} IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
        }

        PrintResult(list);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TestRtwqPutWorkItemAsync(
        bool cancel,
        bool error
    )
    {
        using var workQueue = _workQueueManager.CreatePlatformWorkQueue();

        using var cts = new CancellationTokenSource();
        var list = new ConcurrentQueue<(string, long)>();
        var taskMap = new Dictionary<Task, int>();
        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var putTask = Task.Run(() => {
            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;
                list.Enqueue(($"action put {j}", counter.ElapsedTicks));
                taskMap.Add(workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        if (error)
                        {
                            throw new Exception($"error {j}");
                        }
                        TestTask(counter, list, j);
                    },
                    cts.Token
                ), j);
            }
        });

        if (cancel)
        {
            cts.Cancel();
        }

        await putTask;

        //終了待ち
        while (taskMap.Count > 0)
        {
            var task = await Task.WhenAny(taskMap.Keys).ConfigureAwait(false);
            taskMap.Remove(task, out var id);

            _output.WriteLine(($"task {id} IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
        }

        PrintResult(list);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TestRtwqPutWorkItem(
        bool cancel,
        bool error
    )
    {
        using var workQueue = _workQueueManager.CreatePlatformWorkQueue();

        using var cde = new CountdownEvent(TIMES);
        using var cts = new CancellationTokenSource();
        var list = new ConcurrentQueue<(string, long)>();
        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var putTask = Task.Run(() => {
            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;
                list.Enqueue(($"action put {j}", counter.ElapsedTicks));
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        if (error)
                        {
                            throw new Exception($"error {j}");
                        }
                        TestTask(counter, list, j);
                        cde.Signal();
                    },
                    (e, ct) =>
                    {
                        list.Enqueue(($"after action invoke {j} e:{e} IsCancellationRequested:{ct.IsCancellationRequested}", counter.ElapsedTicks));
                        cde.Signal();
                    },
                    cts.Token
                );
            }
        });

        if (cancel)
        {
            cts.Cancel();
        }

        await putTask;

        //終了待ち
        cde.Wait();

        PrintResult(list);
    }

    [Theory]
    [InlineData("Audio")]
    [InlineData("Capture")]
    [InlineData("DisplayPostProcessing", null, false, true)]
    [InlineData("Distribution")]
    [InlineData("Games")]
    [InlineData("Playback")]
    [InlineData("Pro Audio")]
    [InlineData("Window Manager")]
    [InlineData("Audio", null, true)]
    [InlineData("Capture", null, true)]
    [InlineData("DisplayPostProcessing", null, true, true)]
    [InlineData("Distribution", null, true)]
    [InlineData("Games", null, true)]
    [InlineData("Playback", null, true)]
    [InlineData("Pro Audio", null, true)]
    [InlineData("Window Manager", null, true)]
    [InlineData("", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("", IRTWorkQueue.WorkQueueType.Window)]
    [InlineData("", IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [InlineData("", IRTWorkQueue.WorkQueueType.Standard, true)]
    [InlineData("", IRTWorkQueue.WorkQueueType.Window, true, true)] //windowのserialはNGらしい
    [InlineData("", IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
    public async Task TestRtwqAsync(
        string usageClass,
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        bool argumentException = false
    )
    {
        IRTWorkQueue workQueue;
        IRTWorkQueue? workQueueOrg = null;
        if (type != null)
        {
            workQueue = _workQueueManager.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)type);
        }
        else
        {
            try
            {
                workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass);
            }
            catch (ArgumentException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
        }

        try
        {
            if (serial)
            {
                workQueueOrg = workQueue;
                try
                {
                    workQueue = _workQueueManager.CreateSerialWorkQueue(workQueueOrg);
                }
                catch (ArgumentException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }
            }

            {
                //１回目の起動が遅いので、空打ちする
                _output.WriteLine("dummy action start.");
                var task = workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _output.WriteLine("dummy invoke.");
                    }
                );
                await task;

                _output.WriteLine($"dummy action end task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}");
            }

            using var cde = new CountdownEvent(TIMES);
            var list = new ConcurrentQueue<(string, long)>();
            var taskSet = new HashSet<Task>();
            var counter = new ElapsedTimeCounter();
            counter.Reset();

            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;
                list.Enqueue(($"action put {i}", counter.ElapsedTicks));
                taskSet.Add(workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j, cde);
                    }
                ));
            }

            cde.Wait();

            //終了待ち
            while (taskSet.Count > 0)
            {
                var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
                taskSet.Remove(task);

                _output.WriteLine(($"task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
            }

            PrintResult(list);
        }
        finally
        {
            workQueue.Dispose();

            if (serial)
            {
                workQueueOrg?.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(null, null, false, true)] //usage classにnullはNG
    [InlineData("")] //shared queue を""で作成するのはregular-priority queueを作る特殊な動作になっている
    [InlineData("Audio")]
    [InlineData("Capture")]
    [InlineData("DisplayPostProcessing", null, false, true)] //初めからDisplayPostProcessingは失敗する
    [InlineData("Distribution")]
    [InlineData("Games")]
    [InlineData("Playback")]
    [InlineData("Pro Audio")]
    [InlineData("Window Manager")]
    [InlineData("****", null, false, true)] //失敗する
    [InlineData("Audio", null, true)]
    [InlineData("Capture", null, true)]
    [InlineData("DisplayPostProcessing", null, true, true)] //失敗する
    [InlineData("Distribution", null, true)]
    [InlineData("Games", null, true)]
    [InlineData("Playback", null, true)]
    [InlineData("Pro Audio", null, true)]
    [InlineData("Window Manager", null, true)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("", IRTWorkQueue.WorkQueueType.Standard, false, true)] //private queueを""に登録すると失敗する
    [InlineData("Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Capture", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("DisplayPostProcessing", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Distribution", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Games", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Playback", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Pro Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData("Window Manager", IRTWorkQueue.WorkQueueType.Standard)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.Window)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.Standard, true)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.Window, true, true)]
    [InlineData(null, IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
    public async Task TestRtwq(
        string? usageClass = null,
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        bool argumentException = false
    )
    {
        IRTWorkQueue workQueue;
        IRTWorkQueue? workQueueOrg = null;
        if (type != null)
        {
            workQueue = _workQueueManager.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)type);
        }
        else
        {
            try
            {
                workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass!);
                if (argumentException)
                {
                    Assert.Fail("");
                }
            }
            catch (COMException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
            catch (NullReferenceException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
            catch (ArgumentException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
        }

        try
        {
            if (serial)
            {
                workQueueOrg = workQueue;
                try
                {
                    workQueue = _workQueueManager.CreateSerialWorkQueue(workQueueOrg);
                    if (argumentException)
                    {
                        Assert.Fail("");
                    }
                }
                catch (ArgumentException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }
            }

            if ((type != null) && (usageClass != null))
            {//private queueは、classの切り替えが出来る
                try
                {
                    await workQueue.RegisterMMCSSAsync(usageClass!, IRTWorkQueue.TaskPriority.NORMAL, 0);
                    if (argumentException)
                    {
                        Assert.Fail("");
                    }
                }
                catch (COMException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }

                var afterClass = workQueue.GetMMCSSClass();
                Assert.Equal(usageClass, afterClass);
            }

            {
                //１回目の起動が遅いので、空打ちする
                _output.WriteLine("dummy action start.");
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _output.WriteLine("dummy invoke.");
                    },
                    (error, ct) =>
                    {
                        _output.WriteLine($"dummy result error [{error}] ct [{ct.IsCancellationRequested}].");
                    }
                );
                _output.WriteLine("dummy action end.");
            }

            using var cde = new CountdownEvent(TIMES);
            var list = new ConcurrentQueue<(string, long)>();

            var counter = new ElapsedTimeCounter();
            counter.Reset();

            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;

                list.Enqueue(($"action put {i}", counter.ElapsedTicks));
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j, cde);
                    },
                    (error, ct) =>
                    {
                        list.Enqueue(($"result error [{error}] ct [{ct.IsCancellationRequested}].", counter.ElapsedTicks));
                    }
                );
            }

            cde.Wait();

            PrintResult(list);
        }
        finally
        {
            if (type != null)
            {
                if (usageClass != null && usageClass != "")
                {
                    await workQueue.UnregisterMMCSSAsync();
                }
            }

            workQueue.Dispose();

            if (serial)
            {
                workQueueOrg?.Dispose();
            }
        }
    }

    [Fact]
    public void TestWorkQueuePeriodicCallback()
    {
        _workQueueManager.RegisterMMCSS("Pro Audio");

        using var cde = new CountdownEvent(10);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();
        {
            var i = 1;

            //15msec で呼び出されている模様
            using var workQueuePeriodicCallback = _workQueueManager.AddPeriodicCallback(() => {
                list.Enqueue(($"periodic", counter.ElapsedTicks));
                TestTask(counter, list, i++, cde);
            });

            //終了待ち
            cde.Wait();
        }

        PrintResult(list);
    }

    [Theory]
    [InlineData("Pro Audio")]
    public async Task TestRtwqPutWaitingAsync_WaitableTimer(string usageClass)
    {
        _workQueueManager.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var timer = new WaitableTimer(false, true);

        for (var i = 1; i <= 10; i++)
        {
            using var cts = new CancellationTokenSource();

            var j = i;
            list.Enqueue(($"action put {j}", counter.ElapsedTicks));

            var task = _workQueueManager.PutWaitingWorkItemAsync(
                0, 
                timer, 
                () => {
                    TestTask(counter, list, j);
                }, 
                cts.Token
            );

            timer.Set(-10000);

            await task;
            list.Enqueue(($"result {j}", counter.ElapsedTicks));
        }

        PrintResult(list);
    }

    [Theory]
    [InlineData("Pro Audio")]
    public void TestRtwqPutWaiting_WaitableTimer(string usageClass)
    {
        _workQueueManager.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cde = new CountdownEvent(TIMES);

        using var timer = new WaitableTimer(false, true);

        using var cts = new CancellationTokenSource();
        using var wait = new ManualResetEventSlim();

        var msec = 5;
        var intervalTicks = (1_000 / 60) * TimeSpan.TicksPerMillisecond;
        var progressed = 0L;
        var progressedTicks = counter.ElapsedTicks;

        var i = 0;
        while (true)
        {
            var j = i++;

            list.Enqueue(($"action put {j} {progressed}", counter.ElapsedTicks));
            _workQueueManager.PutWaitingWorkItem(
                0,
                timer,
                () =>
                {
                    TestTask(counter, list, j, cde);
                },
                (error, ct) =>
                {
                    list.Enqueue(($"result error [{error}] ct [{ct.IsCancellationRequested}]", counter.ElapsedTicks));
                    wait.Set();
                },
                cts.Token
            );

            if (cde.IsSet)
            {
                break;
            }

            var nextTicks = intervalTicks * (progressed + 1);
            var leftTicks = nextTicks - counter.ElapsedTicks;

            if (leftTicks > 0)
            {
                list.Enqueue(($"timer set {j} left {leftTicks}", counter.ElapsedTicks));
                timer.Set(msec * -1_000_0);
            }
            else
            {
                list.Enqueue(($"skip left {leftTicks}", counter.ElapsedTicks));
                timer.Set(0);
            }

            wait.Wait();
            wait.Reset();

            progressedTicks = counter.ElapsedTicks;
            progressed = progressedTicks / intervalTicks;
        }

        cts.Cancel();

        PrintResult(list);
    }

    [Theory]
    [InlineData("Pro Audio", true, false, false, false)] //Fire
    [InlineData("Pro Audio", false, true, false, false)] //Cancel
    [InlineData("Pro Audio", true, true, false, false)] //Fire_Cancel
    [InlineData("Pro Audio", false, false, true, false)] //Already_Cancel
    [InlineData("Pro Audio", true, false, false, true)] //Fire_Error
    public void TestRtwqPutWaiting(
        string usageClass,
        bool fire,
        bool cancel,
        bool alreadyCancel,
        bool error
        )
    {
        _workQueueManager.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();
        using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

        if (alreadyCancel)
        {
            _output.WriteLine($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        var result = 0;
        Exception? exception = null;
        var ct = CancellationToken.None;

        using var wait = new ManualResetEventSlim();

        _workQueueManager.PutWaitingWorkItem(
            0,
            waitHandle,
            () => {
                _output.WriteLine($"invoke {counter.ElapsedTicks}");

                if (error)
                {
                    throw new Exception("error");
                }

                result = 1;
            },
            (exception_, ct_) =>
            {
                exception = exception_;
                ct = ct_;
                wait.Set();
            },
            cts.Token
        );

        if (cancel)
        {
            Task.Delay(100).Wait();
            _output.WriteLine($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            Task.Delay(100).Wait();
            _output.WriteLine($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        wait.Wait();

        if (ct.IsCancellationRequested)
        {
            _output.WriteLine($"cancel");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        else if (exception != null)
        {
            _output.WriteLine($"error {exception}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
        else
        {
            _output.WriteLine($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.Equal(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
    }

    [Theory]
    [InlineData("Pro Audio", true, false, false, false)] //Fire
    [InlineData("Pro Audio", false, true, false, false)] //Cancel
    [InlineData("Pro Audio", true, true, false, false)] //Fire_Cancel
    [InlineData("Pro Audio", false, false, true, false)] //Already_Cancel
    [InlineData("Pro Audio", true, false, false, true)] //Fire_Error
    public async Task TestRtwqPutWaitingAsync(
        string usageClass,
        bool fire,
        bool cancel,
        bool alreadyCancel,
        bool error
    )
    {
        _workQueueManager.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();
        using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

        if (alreadyCancel)
        {
            _output.WriteLine($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        var result = 0;
        var task = _workQueueManager.PutWaitingWorkItemAsync(
            0, 
            waitHandle, 
            () => {
                _output.WriteLine($"invoke {counter.ElapsedTicks}");

                if (error)
                {
                    throw new Exception("error");
                }

                result = 1;
            },
            cts.Token
        );

        if (cancel)
        {
            await Task.Delay(100);
            _output.WriteLine($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            await Task.Delay(100);
            _output.WriteLine($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        try
        {
            await task;

            _output.WriteLine($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.Equal(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
        catch (TaskCanceledException e)
        {
            _output.WriteLine($"error {e}");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        catch (Exception e)
        {
            _output.WriteLine($"error {e}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
    }

    [Theory]
    [InlineData("Pro Audio", 0, false, false, false)] //Fire
    [InlineData("Pro Audio", -1, false, false, false)] //Fire
    [InlineData("Pro Audio", -100, true, false, false)] //Cancel
    [InlineData("Pro Audio", -100, false, true, false)] //Already_Cancel
    [InlineData("Pro Audio", -1, false, false, true)] //Fire_Error
    public async Task TestRtwqScheduleAsync(
        string usageClass,
        long timeout,
        bool cancel,
        bool alreadyCancel,
        bool error
    )
    {
        _workQueueManager.RegisterMMCSS(usageClass);

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();

        if (alreadyCancel)
        {
            _output.WriteLine($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        //誤差 5msecくらい　20msec以下には出来ない模様

        var result = 0;

        _output.WriteLine($"put {counter.ElapsedTicks}");

        var task = _workQueueManager.ScheduleWorkItemAsync(
            timeout,
            () => {
                _output.WriteLine($"invoke {counter.ElapsedTicks}");

                if (error)
                {
                    throw new Exception("error");
                }

                result = 1;
            },
            cts.Token
        );

        if (cancel)
        {
            _output.WriteLine($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        try
        {
            await task;

            _output.WriteLine($"result {result} {counter.ElapsedTicks}");

            if (!alreadyCancel)
            {
                Assert.Equal(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
        catch (TaskCanceledException e)
        {
            _output.WriteLine($"error {e}");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        catch (Exception e)
        {
            _output.WriteLine($"error {e}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
    }

    [Fact]
    public void TestRegisterPlatformWithMMCSS()
    {
        var usageClass = "Audio";
        _workQueueManager.RegisterMMCSS(usageClass);

        _workQueueManager.UnregisterMMCSS();

        usageClass = "Pro Audio";
        _workQueueManager.RegisterMMCSS(usageClass, IRTWorkQueue.TaskPriority.HIGH, 1);

        usageClass = "Playback";
        _workQueueManager.RegisterMMCSS(usageClass, IRTWorkQueue.TaskPriority.LOW, 2);
    }

    [Fact]
    public async Task TestRegisterWorkQueueWithMMCSS()
    {
        var usageClass = "Audio";

        using var workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass);
        Assert.Equal(usageClass, workQueue.GetMMCSSClass());

        await workQueue.UnregisterMMCSSAsync();

        Assert.Equal("", workQueue.GetMMCSSClass());

        usageClass = "Pro Audio";
        await workQueue.RegisterMMCSSAsync(usageClass, IRTWorkQueue.TaskPriority.HIGH, 1);

        Assert.Equal(usageClass, workQueue.GetMMCSSClass());
        Assert.Equal(IRTWorkQueue.TaskPriority.HIGH, workQueue.GetMMCSSPriority());

    }

}
