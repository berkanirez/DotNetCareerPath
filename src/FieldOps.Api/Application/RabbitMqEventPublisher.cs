using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace FieldOps.Api.Application;

// Day 67: today's only IEventPublisher implementation. A real production
// version would hold one long-lived connection/channel (Singleton,
// reused across every publish, the same shape as Day 48's
// IConnectionMultiplexer) — deliberately simplified here to open and
// close a fresh connection per publish instead. This is honestly worse
// for performance, but it avoids the async-singleton-initialization
// problem entirely (RabbitMQ.Client 7.x's connection APIs are async-only,
// with no synchronous equivalent to Day 48's ConnectionMultiplexer.Connect),
// which is the right trade to make for today's single, low-frequency event.
public class RabbitMqEventPublisher : IEventPublisher
{
    private readonly string _hostName;

    public RabbitMqEventPublisher(string hostName)
    {
        _hostName = hostName;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, string messageId, CancellationToken cancellationToken)
    {
        // Day 85: RabbitMQ.Client has no built-in OpenTelemetry
        // instrumentation — unlike the inbound HTTP request (auto-covered
        // by AddAspNetCoreInstrumentation), this span has to be started and
        // propagated BY HAND. ActivityKind.Producer marks this as the
        // sending half of a producer/consumer pair — the OpenTelemetry
        // convention for messaging systems.
        using var activity = FieldOpsTracing.MessagingSource.StartActivity(
            $"publish {typeof(TEvent).Name}", ActivityKind.Producer);

        var factory = new ConnectionFactory { HostName = _hostName };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Day 69: publish to a real, named fanout exchange instead of
        // Day 67/68's default exchange + single shared queue. A fanout
        // exchange delivers a copy of this message to EVERY queue bound to
        // it — this is what actually lets more than one independent
        // consumer (today: notifications AND audit) receive the same
        // event, which a single shared queue never could.
        var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
        await channel.ExchangeDeclareAsync(
            exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(domainEvent);
        var body = Encoding.UTF8.GetBytes(json);

        // Day 85: this is the actual "trace context propagation" — this
        // process's current trace/span id, written into the message's own
        // headers so that whichever process consumes it later (a genuinely
        // different process, possibly minutes from now) can pick up the
        // SAME trace instead of starting an unrelated one.
        var headers = new Dictionary<string, object?>();
        if (activity is not null)
        {
            DistributedContextPropagator.Current.Inject(
                activity,
                headers,
                static (carrier, key, value) => ((Dictionary<string, object?>)carrier!)[key] = value);
        }

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: string.Empty, // a fanout exchange ignores the routing key entirely
            mandatory: false,
            basicProperties: new BasicProperties { MessageId = messageId, Headers = headers },
            body: (ReadOnlyMemory<byte>)body,
            cancellationToken: cancellationToken);
    }
}
