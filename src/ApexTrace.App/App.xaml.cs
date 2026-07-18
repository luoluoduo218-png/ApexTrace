using System.IO;
using System.Windows;
using ApexTrace.Analysis;
using ApexTrace.Lmu;
using ApexTrace.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ApexTrace.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexTrace");
        Directory.CreateDirectory(appRoot);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(appRoot, "Logs", "apextrace-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<LmuInstallationProbe>();
                services.AddSingleton<LmuDuckDbImporter>();
                services.AddSingleton<ApexTracePackageService>();
                services.AddSingleton<DrivingEventDetector>();
                services.AddSingleton<EvidenceRecommendationEngine>();
                services.AddSingleton(sp => new LocalSessionRepository(Path.Combine(appRoot, "Sessions")));
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
