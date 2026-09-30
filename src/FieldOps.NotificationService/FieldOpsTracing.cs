using System.Diagnostics;

namespace FieldOps.NotificationService;

// Day 85: this service's own copy of FieldOps.Api's FieldOpsTracing — same
// name, same reasoning (EventQueueNaming, Day 76): the two sides only need
// to agree on the STRING "FieldOps.Messaging", never share a type.
internal static class FieldOpsTracing
{
    public const string MessagingSourceName = "FieldOps.Messaging";

    public static readonly ActivitySource MessagingSource = new(MessagingSourceName);
}
