using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Momiji.Core.RTWorkQueue;

namespace Momiji.Sample.WASAPI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var host = Worker.CreateHost();
        _ = host.RunAsync();

        _window = host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private Window? _window;
}


public partial class Worker : BackgroundService
{
    public static IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder();

        builder.UseContentRoot(AppContext.BaseDirectory);

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<Worker>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<ViewModel>();
            services.AddSingleton<Model>();
            services.AddSingleton<WASAPIController>();
            services.AddSingleton<IRTWorkQueuePlatformEventsHandler, RTWorkQueuePlatformEventsHandler>();
            services.AddSingleton<IRTWorkQueueManager, RTWorkQueueManager>();
        });

        var host = builder.Build();

        return host;
    }

    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime hostApplicationLifetime,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;

        hostApplicationLifetime.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("ApplicationStopped");
        });
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExecuteAsync start");

        using var scope = _serviceScopeFactory.CreateScope();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("ExecuteAsync end");
    }

}
