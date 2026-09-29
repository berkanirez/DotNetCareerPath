namespace FieldOps.NotificationService;

// Day 51 (originally in FieldOps.Api), moved here on Day 76 — the actual
// notification-sending concern belongs entirely to THIS service now, per
// ADR 0005. FieldOps.Api no longer has any reason to know this interface
// exists at all.
public interface INotificationSender
{
    Task NotifyAsync(string message, CancellationToken cancellationToken);
}
