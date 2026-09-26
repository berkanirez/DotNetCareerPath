namespace FieldOps.Api.Application;

// Day 67: FieldOps's first domain event — a record of something that has
// genuinely already happened (a work order was completed), not a command
// or a request. Deliberately a plain, self-contained record: any future
// consumer (a notification service, a reporting service) needs only this
// data, never a callback into FieldOps.Api itself.
public record WorkOrderCompletedEvent(
    int WorkOrderId,
    int OrganizationId,
    int? CustomerId,
    string Title,
    DateTime CompletedAtUtc);
