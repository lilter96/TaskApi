using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TaskApi.IntegrationTests;

public sealed record ReceivedMessage(
    string Exchange,
    string RoutingKey,
    string? MessageId,
    string? ContentType,
    JsonElement Body);

public sealed class RabbitMqTestConsumer : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly Channel<ReceivedMessage> _received = Channel.CreateUnbounded<ReceivedMessage>();

    private RabbitMqTestConsumer(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqTestConsumer> StartAsync(
        string connectionString,
        string exchange,
        string routingKey,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue.QueueName, exchange, routingKey, cancellationToken: cancellationToken);

        var testConsumer = new RabbitMqTestConsumer(connection, channel);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += testConsumer.OnReceivedAsync;
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, cancellationToken);

        return testConsumer;
    }

    public async Task<ReceivedMessage> WaitForAsync(
        Func<ReceivedMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        await foreach (var message in _received.Reader.ReadAllAsync(cts.Token))
        {
            if (predicate(message))
            {
                return message;
            }
        }

        throw new InvalidOperationException("Consumer was stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        _received.Writer.TryComplete();
        await _channel.CloseAsync();
        await _connection.CloseAsync();
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        using var body = JsonDocument.Parse(args.Body);
        _received.Writer.TryWrite(new ReceivedMessage(
            args.Exchange,
            args.RoutingKey,
            args.BasicProperties.MessageId,
            args.BasicProperties.ContentType,
            body.RootElement.Clone()));

        return Task.CompletedTask;
    }
}
