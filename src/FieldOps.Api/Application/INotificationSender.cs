namespace FieldOps.Api.Application;

// Day 51: a notification abstraction — deliberately channel-agnostic (no
// email address, phone number, or "To" concept at all). The real channel
// (email, SMS, push) is unknown today and out of scope; this interface only
// commits to "something happened, tell someone" so that business code
// (WorkOrdersController) never couples itself to a specific provider. The
// same shape Week 12's AI provider abstraction will repeat.
public interface INotificationSender
{
    Task NotifyAsync(string message, CancellationToken cancellationToken);
}
