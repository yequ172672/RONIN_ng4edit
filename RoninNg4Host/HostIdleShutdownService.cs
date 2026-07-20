namespace RoninNg4Host;

public sealed class HostIdleShutdownService(HostState state, IHostApplicationLifetime lifetime) : BackgroundService
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (state.IsIdle(DefaultIdleTimeout))
            {
                lifetime.StopApplication();
                return;
            }
        }
    }
}
