namespace FieldOps.NotificationService;

// Day 51 (originally in FieldOps.Api), moved here on Day 76 — today's only
// INotificationSender implementation; a real provider (email/SMS/push)
// would replace only this registration.
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
