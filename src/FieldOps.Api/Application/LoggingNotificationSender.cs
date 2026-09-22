namespace FieldOps.Api.Application;

// Day 51: today's only implementation of INotificationSender — a real
// provider (email/SMS/push) would genuinely need to be async (a network
// call), so the interface is async-shaped even though this demo
// implementation does no real I/O, mirroring StockPilot Day 16's
// InMemoryProductStore (Task.FromResult/Task.CompletedTask, no real
// awaiting, purely to satisfy the interface).
public class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _logger;

    public LoggingNotificationSender(ILogger<LoggingNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Notification: {Message}", message);
        return Task.CompletedTask;
    }
}
