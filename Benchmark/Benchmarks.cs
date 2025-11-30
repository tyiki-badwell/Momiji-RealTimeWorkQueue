using System.Threading;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VSDiagnostics;
using Momiji.Core.RTWorkQueue;

namespace Benchmark;
// For more information on the VS BenchmarkDotNet Diagnosers see https://learn.microsoft.com/visualstudio/profiling/profiling-with-benchmark-dotnet
[CPUUsageDiagnoser]
[MemoryDiagnoser]
//[ShortRunJob]
public class Benchmarks
{
    private ILoggerFactory _loggerFactory;
    private ILogger _logger;

    private IRTWorkQueueManager _manager;
    private IRTWorkQueue _queue;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = CreateConfiguration(/*usageClass, 0, taskId*/);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Cache", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.RTWorkQueue", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<Benchmarks>();

        _manager = new RTWorkQueueManager(configuration, _loggerFactory);
        _queue = _manager.CreatePrivateWorkQueue(IRTWorkQueue.WorkQueueType.MultiThreaded);
        _queue.RegisterMMCSSAsync("Pro Audio", IRTWorkQueue.TaskPriority.HIGH, 0).Wait();
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

    // Use a static delegate to avoid per-call delegate allocations
    private static readonly System.Action<CountdownEvent> s_task = (cde) => {
        cde.Signal();
    };

    private const int TIMES = 1000;
    private const int TIMEOUT = 10000;

    [Benchmark]
    public void PutWorkItem()
    {
        using var cde = new CountdownEvent(TIMES);

        for (var i = 0; i < 1000; i++)
        {
            _queue.PutWorkItem(
                IRTWorkQueue.TaskPriority.NORMAL,
                s_task,
                cde
            );
        }

        //終了待ち
        cde.Wait(TIMEOUT);
    }

    [Benchmark]
    public void QueueUserWorkItem()
    {
        using var cde = new CountdownEvent(TIMES);

        for (var i = 0; i < 1000; i++)
        {
            ThreadPool.QueueUserWorkItem(
                s_task,
                cde,
                false
            );
        }

        //終了待ち
        cde.Wait(TIMEOUT);
    }

}
