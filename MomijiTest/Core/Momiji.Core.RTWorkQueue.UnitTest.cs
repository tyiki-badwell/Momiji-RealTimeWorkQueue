using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Timer;

namespace Momiji.Core.RTWorkQueue;

[TestClass]
public class RTWorkQueueTest : IDisposable
{
    private const int TIMES = 100;
    private const int WAIT = 10;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly RTWorkQueuePlatformEventsHandler _workQueuePlatformEventsHandler;
    private readonly RTWorkQueueManager _workQueueManager;

    public RTWorkQueueTest()
    {
        var configuration = CreateConfiguration(/*usageClass, 0, taskId*/);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Cache", LogLevel.Information);
            builder.AddFilter("Momiji.Core.RTWorkQueue", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<RTWorkQueueTest>();

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
            _logger.LogInformation($"LAST: {tag}\t{(double)time / 10000}");
        }

        foreach (var (tag, time) in list)
        {
            _logger.LogInformation($"{tag}\t{(double)time / 10000}");
        }
    }

    [TestMethod]
    public void TestRtwqStartupTwice()
    {
        var configuration = CreateConfiguration();


        using var workQueueManager = new RTWorkQueueManager(configuration, _loggerFactory);
        using var workQueueManager2 = new RTWorkQueueManager(configuration, _loggerFactory);


    }

    [TestMethod]
    [DataRow("Pro Audio", IRTWorkQueue.TaskPriority.CRITICAL)]
    [DataRow("Pro Audio", IRTWorkQueue.TaskPriority.HIGH)]
    [DataRow("Pro Audio", IRTWorkQueue.TaskPriority.NORMAL)]
    [DataRow("Pro Audio", IRTWorkQueue.TaskPriority.LOW)]
    [DataRow("", IRTWorkQueue.TaskPriority.NORMAL, true)]
    public void TestRtwqCreateWorkQueue(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        bool comException = false
    )
    {
        var taskId = 0;

        using var workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass, basePriority);

        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());
        Assert.AreEqual(basePriority, workQueue.GetMMCSSPriority());

        try
        {
            Assert.AreNotEqual(taskId, workQueue.GetMMCSSTaskId());
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

    [TestMethod]
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

    [TestMethod]
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
            _logger.LogInformation("server join");

            //TODO joinした後にasync版を呼び出すとエラーになる
            //var handler = await listener.AcceptAsync();
            var handler = listener.Accept();
            _logger.LogInformation("accept");

            while(true)
            {
                var buffer = new byte[100];
                var length = handler.Receive(buffer);
                if (length == 0)
                {
                    break;
                }
                var s = System.Text.Encoding.ASCII.GetString(buffer);
                _logger.LogInformation($"receive [{s}]");
            }

            await Task.Delay(5000);
        });

        var client = Task.Run(async () => {
            _logger.LogInformation("run client");

            using var client = 
                new Socket(
                    ipEndPoint.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );
            await Task.Delay(100);

            using var cookie = workQueue.Join(client.SafeHandle);
            _logger.LogInformation("client join");

            //TODO joinした後にasync版を呼び出すとエラーになる
            //await client.ConnectAsync(ipEndPoint);
            client.Connect(ipEndPoint);
            _logger.LogInformation("connect");

            var options = new ParallelOptions
            {
                TaskScheduler = TaskScheduler.Default
            };

            Parallel.For(0, 100, options, (index) => {
                var text = "1234567890:" + index.ToString();
                var buffer = System.Text.Encoding.ASCII.GetBytes(text + "\n");
                client.Send(buffer);
                _logger.LogInformation($"send [{text}]");
            });

            await Task.Delay(1000);
        });

        await server;
        await client;
    }

    [TestMethod]
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

            _logger.LogInformation(($"task {id} IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
        }

        PrintResult(list);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
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

            _logger.LogInformation(($"task {id} IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
        }

        PrintResult(list);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
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

    [TestMethod]
    [Timeout(10000)]
    [DataRow("Audio")]
    [DataRow("Audio", null, true)]
    [DataRow("Capture")]
    [DataRow("Capture", null, true)]
    [DataRow("DisplayPostProcessing", null, false, true)]
    [DataRow("DisplayPostProcessing", null, true, true)]
    [DataRow("Distribution")]
    [DataRow("Distribution", null, true)]
    [DataRow("Games")]
    [DataRow("Games", null, true)]
    [DataRow("Playback")]
    [DataRow("Playback", null, true)]
    [DataRow("Pro Audio")]
    [DataRow("Pro Audio", null, true)]
    [DataRow("Window Manager")]
    [DataRow("Window Manager", null, true)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard, true)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Window)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Window, true, true)] //windowのserialはNGらしい
    [DataRow("", IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [DataRow("", IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
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
                _logger.LogInformation("dummy action start.");
                var task = workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _logger.LogInformation("dummy invoke.");
                    }
                );
                await task.ConfigureAwait(false);

                _logger.LogInformation($"dummy action end task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}");
            }

            {
                using var cde = new CountdownEvent(TIMES);
                var list = new ConcurrentQueue<(string, long)>();
                var taskSet = new HashSet<Task>();
                var counter = new ElapsedTimeCounter();
                counter.Reset();

                for (var i = 1; i <= TIMES; i++)
                {
                    var j = i;
                    _logger.LogInformation($"action put {j}");
                    list.Enqueue(($"action put {j}", counter.ElapsedTicks));
                    taskSet.Add(workQueue.PutWorkItemAsync(
                        IRTWorkQueue.TaskPriority.NORMAL,
                        () =>
                        {
                            _logger.LogInformation($"invoke {j}");
                            TestTask(counter, list, j, cde);
                        }
                    ));
                }
                _logger.LogInformation("cde wait ...");

                //serial queueなどでは、putしたスレッドでwaitしてブロックすると、invokeされなくなってしまう
                await Task.Run(() => { cde.Wait(); }).ConfigureAwait(false);

                //終了待ち
                while (taskSet.Count > 0)
                {
                    _logger.LogInformation("task wait ...");
                    var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
                    taskSet.Remove(task);

                    _logger.LogInformation(($"task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
                }

                PrintResult(list);
            }
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

    [TestMethod]
    [Timeout(5000)]
    [DataRow(null, null, false, true)] //usage classにnullはNG
    [DataRow("")] //shared queue を""で作成するのはregular-priority queueを作る特殊な動作になっている
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard, false, true)] //private queueを""に登録すると失敗する
    [DataRow("****", null, false, true)] //失敗する
    [DataRow("Audio")]
    [DataRow("Audio", null, true)]
    [DataRow("Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Capture")]
    [DataRow("Capture", null, true)]
    [DataRow("Capture", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("DisplayPostProcessing", null, false, true)] //初めからDisplayPostProcessingは失敗する
    [DataRow("DisplayPostProcessing", null, true, true)] //失敗する
    [DataRow("DisplayPostProcessing", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Distribution")]
    [DataRow("Distribution", null, true)]
    [DataRow("Distribution", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Games")]
    [DataRow("Games", null, true)]
    [DataRow("Games", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Playback")]
    [DataRow("Playback", null, true)]
    [DataRow("Playback", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Pro Audio")]
    [DataRow("Pro Audio", null, true)]
    [DataRow("Pro Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Window Manager")]
    [DataRow("Window Manager", null, true)]
    [DataRow("Window Manager", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Standard, true)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Window)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Window, true, true)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
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
                Assert.AreEqual(usageClass, afterClass);
            }

            {
                using var wait = new ManualResetEventSlim(false);
                //１回目の起動が遅いので、空打ちする
                _logger.LogInformation("dummy action start.");
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _logger.LogInformation("dummy invoke.");
                    },
                    (error, ct) =>
                    {
                        _logger.LogInformation($"dummy result error [{error}] ct [{ct.IsCancellationRequested}].");
                        wait.Set();
                    }
                );

                await Task.Run(()=>{ wait.Wait(); }).ConfigureAwait(false);
                
                _logger.LogInformation("dummy action end.");
            }

            {
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

                await Task.Run(() => { cde.Wait(); }).ConfigureAwait(false);

                PrintResult(list);
            }
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

    [TestMethod]
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

    [TestMethod]
    [DataRow("Pro Audio")]
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

    [TestMethod]
    [DataRow("Pro Audio")]
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

    [TestMethod]
    [DataRow("Pro Audio", true, false, false, false)] //Fire
    [DataRow("Pro Audio", false, true, false, false)] //Cancel
    [DataRow("Pro Audio", true, true, false, false)] //Fire_Cancel
    [DataRow("Pro Audio", false, false, true, false)] //Already_Cancel
    [DataRow("Pro Audio", true, false, false, true)] //Fire_Error
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
            _logger.LogInformation($"already cancel {counter.ElapsedTicks}");
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
                _logger.LogInformation($"invoke {counter.ElapsedTicks}");

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
            _logger.LogInformation($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            Task.Delay(100).Wait();
            _logger.LogInformation($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        wait.Wait();

        if (ct.IsCancellationRequested)
        {
            _logger.LogInformation($"cancel");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        else if (exception != null)
        {
            _logger.LogInformation($"error {exception}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
        else
        {
            _logger.LogInformation($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
    }

    [TestMethod]
    [DataRow("Pro Audio", true, false, false, false)] //Fire
    [DataRow("Pro Audio", false, true, false, false)] //Cancel
    [DataRow("Pro Audio", true, true, false, false)] //Fire_Cancel
    [DataRow("Pro Audio", false, false, true, false)] //Already_Cancel
    [DataRow("Pro Audio", true, false, false, true)] //Fire_Error
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
            _logger.LogInformation($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        var result = 0;
        var task = _workQueueManager.PutWaitingWorkItemAsync(
            0, 
            waitHandle, 
            () => {
                _logger.LogInformation($"invoke {counter.ElapsedTicks}");

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
            _logger.LogInformation($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            await Task.Delay(100);
            _logger.LogInformation($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        try
        {
            await task;

            _logger.LogInformation($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
        catch (TaskCanceledException e)
        {
            _logger.LogInformation($"error {e}");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        catch (Exception e)
        {
            _logger.LogInformation($"error {e}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
    }

    [TestMethod]
    [DataRow("Pro Audio", 0, false, false, false)] //Fire
    [DataRow("Pro Audio", -1, false, false, false)] //Fire
    [DataRow("Pro Audio", -100, true, false, false)] //Cancel
    [DataRow("Pro Audio", -100, false, true, false)] //Already_Cancel
    [DataRow("Pro Audio", -1, false, false, true)] //Fire_Error
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
            _logger.LogInformation($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        //誤差 5msecくらい　20msec以下には出来ない模様

        var result = 0;

        _logger.LogInformation($"put {counter.ElapsedTicks}");

        var task = _workQueueManager.ScheduleWorkItemAsync(
            timeout,
            () => {
                _logger.LogInformation($"invoke {counter.ElapsedTicks}");

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
            _logger.LogInformation($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        try
        {
            await task;

            _logger.LogInformation($"result {result} {counter.ElapsedTicks}");

            if (!alreadyCancel)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.Fail("");
            }
        }
        catch (TaskCanceledException e)
        {
            _logger.LogInformation($"error {e}");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail("");
            }
        }
        catch (Exception e)
        {
            _logger.LogInformation($"error {e}");

            if (!error)
            {
                Assert.Fail("");
            }
        }
    }

    [TestMethod]
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

    [TestMethod]
    public async Task TestRegisterWorkQueueWithMMCSS()
    {
        var usageClass = "Audio";

        using var workQueue = _workQueueManager.CreatePlatformWorkQueue(usageClass);
        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());

        await workQueue.UnregisterMMCSSAsync();

        Assert.AreEqual("", workQueue.GetMMCSSClass());

        usageClass = "Pro Audio";
        await workQueue.RegisterMMCSSAsync(usageClass, IRTWorkQueue.TaskPriority.HIGH, 1);

        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());
        Assert.AreEqual(IRTWorkQueue.TaskPriority.HIGH, workQueue.GetMMCSSPriority());

    }

}
