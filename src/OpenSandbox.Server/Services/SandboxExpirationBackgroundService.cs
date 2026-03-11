using Microsoft.Extensions.Options;
using OpenSandbox.Server.Options;

namespace OpenSandbox.Server.Services;

public sealed class SandboxExpirationBackgroundService : BackgroundService
{
    private readonly OpenSandboxService _sandboxService;
    private readonly TimeSpan _interval;
    private readonly ILogger<SandboxExpirationBackgroundService> _logger;

    public SandboxExpirationBackgroundService(
        OpenSandboxService sandboxService,
        IOptions<OpenSandboxServerOptions> options,
        ILogger<SandboxExpirationBackgroundService> logger)
    {
        _sandboxService = sandboxService;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.CleanupIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _sandboxService.DeleteExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup expired sandboxes.");
            }
        }
    }
}
