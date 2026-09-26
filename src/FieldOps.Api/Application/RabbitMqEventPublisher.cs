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

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { HostName = _hostName };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var queueName = QueueNameFor<TEvent>();
        await channel.QueueDeclareAsync(
            queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(domainEvent);
        var body = Encoding.UTF8.GetBytes(json);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: (ReadOnlyMemory<byte>)body,
            cancellationToken: cancellationToken);
    }

    // Day 68+ (a real exchange/routing-key design) will replace this
    // one-queue-per-event-type convention with something that lets more
    // than one independent consumer receive the same event.
    private static string QueueNameFor<TEvent>() => $"fieldops.{typeof(TEvent).Name}";
}
