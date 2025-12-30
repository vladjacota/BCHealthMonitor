using System.Net;
using BCHealthMonitor.Configuration;
using BCHealthMonitor.Endpoints;
using BCHealthMonitor.Services;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting BC Health Monitor");

    var builder = WebApplication.CreateBuilder(args);

    // Configure as Windows Service
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "BCHealthMonitor";
    });

    // Configure Serilog from appsettings
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var loggingOptions = context.Configuration
            .GetSection("Logging")
            .Get<LoggingOptions>() ?? new LoggingOptions();

        // Read minimum level from appsettings.json Serilog section, default to Information
        var serilogSection = context.Configuration.GetSection("Serilog:MinimumLevel:Default");
        var minLevel = Enum.TryParse<LogEventLevel>(serilogSection.Value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;

        configuration
            .MinimumLevel.Is(minLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .WriteTo.Console();

        if (loggingOptions.EventLog)
        {
            try
            {
                configuration.WriteTo.EventLog(
                    "BCHealthMonitor",
                    manageEventSource: false, // Don't try to create source, must exist
                    restrictedToMinimumLevel: LogEventLevel.Information);
            }
            catch (Exception ex)
            {
                // EventLog requires admin to create source - skip if not available
                Console.WriteLine($"Warning: Could not configure EventLog sink: {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(loggingOptions.FilePath))
        {
            var logPath = Path.Combine(loggingOptions.FilePath, "bchealthmonitor-.log");
            configuration.WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);
        }
    });

    // Bind configuration
    builder.Services.Configure<HealthMonitorOptions>(builder.Configuration);

    // Get server port from configuration
    var serverOptions = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();

    // Configure Kestrel
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Any, serverOptions.Port);
    });

    // Add services
    builder.Services.AddMemoryCache();
    
    // HTTP client for BC API calls
    builder.Services.AddHttpClient("BCApi", (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<HealthMonitorOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseDefaultCredentials = true // Windows auth
    });

    // Register services
    builder.Services.AddSingleton<ICacheService, CacheService>();
    builder.Services.AddSingleton<SystemMetricsService>();
    builder.Services.AddSingleton<ISystemMetricsService>(sp => sp.GetRequiredService<SystemMetricsService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemMetricsService>());
    builder.Services.AddSingleton<ISchedulerControlService, SchedulerControlService>();
    builder.Services.AddSingleton<IBCAvailabilityService, BCAvailabilityService>();
    builder.Services.AddSingleton<IStartupStateService, StartupStateService>();
    builder.Services.AddScoped<ISessionDataService, SessionDataService>();
    builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();

    // Background service for automatic scheduler control
    builder.Services.AddHostedService<SchedulerBackgroundService>();

    // Build app
    var app = builder.Build();

    // Log startup info
    var options = app.Services.GetRequiredService<IOptions<HealthMonitorOptions>>().Value;
    Log.Information("BC Health Monitor starting on port {Port}", options.Server.Port);
    Log.Information("BC Instance: {Instance}", options.BCInstance.Name);
    Log.Information("Health Check Strategy: {Strategy}", options.BCInstance.Strategy);
    Log.Information("Startup delay: {Delay}s", options.Server.StartupDelaySeconds);
    Log.Information("Cache duration: {Cache}s", options.Server.CacheDurationSeconds);

    // Schedule startup completion after delay
    var startupState = app.Services.GetRequiredService<IStartupStateService>();
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(options.Server.StartupDelaySeconds));
        startupState.MarkStartupComplete();
        Log.Information("Startup delay complete, health checks now active");
    });

    // Map endpoints
    app.MapHealthEndpoints();
    app.MapSchedulerEndpoints();
    app.MapMetricsEndpoints();
    app.MapStatusEndpoints();

    // Root redirect to status page
    app.MapGet("/", () => Results.Redirect("/status"));

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
