using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Timer;

[TestClass]
public class WaitableTimerTest : IDisposable
{
    private const int TIMES = 100;
    private const int INTERVAL = 5_000_0;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WaitableTimerTest()
    {
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

        _logger = _loggerFactory.CreateLogger<WaitableTimerTest>();
    }

    public void Dispose()
    {
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
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Test1Impl(
        bool manualReset,
        bool highResolution
    )
    {
        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();

        using var timer = new WaitableTimer(manualReset, highResolution);

        var dueTime = -INTERVAL;

        for (var i = 0; i < TIMES; i++)
        {
            timer.Set(dueTime);

            var start = counter.ElapsedTicks;

            timer.WaitOne();

            var end = counter.ElapsedTicks;

            list.Enqueue(($"LAP {(double)(end - start) / 10_000}", counter.ElapsedTicks));
        }
        PrintResult(list);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TestWait(bool highResolution)
    {
        var counter = new ElapsedTimeCounter();

        var sample = 1;
        var interval = (long)(10_000 * sample);

        var list = new List<(int, long, double, double, double)>();

        using var waiter = new Waiter(counter, interval, highResolution);
        counter.Reset();

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            for (var i = 0; i < 100; i++)
            {
                var leftTicks = waiter.Wait();

                var after = (double)counter.ElapsedTicks / 10_000;

                list.Add((i, waiter.ProgressedFrames, before, after, (double)leftTicks / 10_000));

                if (leftTicks < 0)
                {
                    i += (int)((leftTicks * -1) / interval);
                }

                before = after;

                //Thread.Sleep(1);
            }
        }

        foreach (var (i, laps, before, after, leftTicks) in list)
        {
            _logger.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}\t{leftTicks}");
        }
    }

    [TestMethod]
    public async Task TestPeriodicTimerAsync()
    {
        var counter = new ElapsedTimeCounter();

        var sample = 2;
        var interval = 10_000 * sample;

        var list = new List<(int, long, double, double)>();

        using var timer = new PeriodicTimer(TimeSpan.FromTicks(interval));
        counter.Reset();

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            for (var i = 0; i < 100; i++)
            {
                await timer.WaitForNextTickAsync();

                var after = (double)counter.ElapsedTicks / 10_000;

                list.Add((i, 0, before, after));
                before = after;
            }
        }

        foreach (var (i, laps, before, after) in list)
        {
            _logger.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}");
        }

    }

    [TestMethod]
    public async Task TestRegisterWaitForSingleObjectAsync()
    {
        var counter = new ElapsedTimeCounter();

        var sample = 2;
        var interval = 1 * sample;

        var list = new List<(int, long, double, double)>();

        using var w = new AutoResetEvent(false);

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            var i = 0;
            var r =
                ThreadPool.RegisterWaitForSingleObject(
                    w,
                    (object? _list, bool t) =>
                    {
                        var after = (double)counter.ElapsedTicks / 10_000;
                        list.Add((i++, 0, before, after));
                        before = after;
                    },
                    null,
                    1,
                    false
                );

            await Task.Delay(1000).ContinueWith((_) => {
                r.Unregister(w);
            });
        }

        foreach (var (i, laps, before, after) in list)
        {
            _logger.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}");
        }
    }
}
