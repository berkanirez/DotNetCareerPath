using System.Diagnostics;

namespace FieldOps.Api.Application;

// Day 85: a single, named ActivitySource for this process's own manual
// spans (RabbitMQ publish/consume — nothing auto-instruments those, unlike
// the inbound HTTP requests ASP.NET Core's own instrumentation already
// covers). Deliberately a DUPLICATE of FieldOps.NotificationService's own
// copy of this class, not a shared reference — the same "no compile-time
// dependency between the two services" reasoning as EventQueueNaming
// (Day 76). Both sides only need to agree on the STRING name, exactly like
// EventQueueNaming's computed queue names.
public static class FieldOpsTracing
{
    public const string MessagingSourceName = "FieldOps.Messaging";

    public static readonly ActivitySource MessagingSource = new(MessagingSourceName);
}
