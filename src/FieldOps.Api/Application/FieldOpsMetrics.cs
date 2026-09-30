using System.Diagnostics.Metrics;

namespace FieldOps.Api.Application;

// Day 86: this process's own named Meter — the metrics counterpart to
// FieldOpsTracing's ActivitySource (Day 85). Same reasoning: an
// instrument nobody's "listening" to produces numbers that go nowhere,
// which is why Program.cs's .AddMeter(MeterName) call matters just as much
// as .AddSource(...) did for tracing.
public static class FieldOpsMetrics
{
    public const string MeterName = "FieldOps.Api";

    private static readonly Meter Meter = new(MeterName);

    // A Counter only ever goes up — exactly right for "how many work orders
    // have been completed, ever" (or per time window, once an aggregation
    // window is applied downstream). Never decremented, never reset by us.
    public static readonly Counter<long> WorkOrdersCompleted =
        Meter.CreateCounter<long>("workorders.completed", description: "Total number of work orders completed.");

    // A Histogram records the DISTRIBUTION of a value, not just a running
    // total — "how long did this take" needs to answer "how many were
    // fast, how many were slow," which a single running average would hide.
    // "outbox lag" (time between an outbox row being written and actually
    // being published) is a genuine, commonly-watched production metric —
    // a healthy system keeps this small; a rising lag is an early sign that
    // RabbitMQ (or whatever the outbox is publishing to) is falling behind.
    public static readonly Histogram<double> OutboxPublishLagSeconds =
        Meter.CreateHistogram<double>("outbox.publish.lag_seconds", unit: "s",
            description: "Seconds between an outbox message being created and successfully published.");
}
