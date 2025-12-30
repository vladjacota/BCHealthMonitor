using BCHealthMonitor.Configuration;
using Microsoft.Extensions.Options;

namespace BCHealthMonitor.Services;

public class SchedulerBackgroundService : BackgroundService
{
    private readonly ILogger<SchedulerBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly HealthMonitorOptions _options;

    public SchedulerBackgroundService(
        ILogger<SchedulerBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptions<HealthMonitorOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.BCInstance.SchedulerControl.Enabled)
        {
            _logger.LogInformation("Scheduler control is disabled in configuration");
            return;
        }

        _logger.LogInformation("Scheduler background service started. Business hours: {Start} - {End} ({Days})",
            _options.BCInstance.SchedulerControl.BusinessHours.Start,
            _options.BCInstance.SchedulerControl.BusinessHours.End,
            string.Join(", ", _options.BCInstance.SchedulerControl.BusinessHours.Days));

        // Wait for startup delay
        await Task.Delay(TimeSpan.FromSeconds(_options.Server.StartupDelaySeconds + 5), stoppingToken);

        bool? lastBusinessHoursState = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerControlService>();

                var state = await schedulerService.GetStateAsync();
                var isBusinessHours = _options.BCInstance.SchedulerControl.BusinessHours.IsBusinessHours(DateTime.UtcNow);

                // Only act if there's no manual override
                if (!state.OverrideActive)
                {
                    // Check if we need to change state
                    if (lastBusinessHoursState != isBusinessHours)
                    {
                        if (isBusinessHours && state.Enabled)
                        {
                            // Entering business hours - disable scheduler
                            _logger.LogInformation("Entering business hours - disabling task scheduler");
                            await schedulerService.DisableAsync();
                        }
                        else if (!isBusinessHours && !state.Enabled)
                        {
                            // Leaving business hours - enable scheduler
                            _logger.LogInformation("Leaving business hours - enabling task scheduler");
                            await schedulerService.EnableAsync();
                        }

                        lastBusinessHoursState = isBusinessHours;
                    }
                }
                else
                {
                    _logger.LogDebug("Scheduler override active, skipping automatic control");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler background service");
            }

            // Check every minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
