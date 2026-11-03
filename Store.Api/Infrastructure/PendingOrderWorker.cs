using Store.Api.Application;

namespace Store.Api.Infrastructure;

public sealed class PendingOrderWorker(IServiceScopeFactory scopeFactory, ILogger<PendingOrderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderService>();
                var count = await service.CancelExpiredAsync(DateTime.UtcNow.AddMinutes(-30), stoppingToken);
                if (count > 0) logger.LogInformation("Automatically cancelled {Count} expired pending orders", count);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pending-order cleanup failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
