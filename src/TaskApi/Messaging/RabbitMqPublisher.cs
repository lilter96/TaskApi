using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskApi.Messaging;

internal sealed partial class RabbitMqPublisher : IAsyncDisposable
{
    private const string ExchangeName = "task.events";

    private static readonly CreateChannelOptions ChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        _connectionFactory = new ConnectionFactory
        {
            Uri = options.Value.Uri,
            ClientProvidedName = "task-api",
            AutomaticRecoveryEnabled = false,
        };
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds()),
        };

        await channel.BasicPublishAsync(
            ExchangeName,
            message.RoutingKey,
            mandatory: false,
            properties,
            Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_channel is { IsOpen: true })
            {
                await _channel.CloseAsync(cancellationToken);
            }

            if (_connection is { IsOpen: true })
            {
                await _connection.CloseAsync(cancellationToken);
                LogConnectionClosed();
            }
        }
        catch (Exception ex)
        {
            LogCloseFailed(ex);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private async ValueTask<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await DisposeAsync();

        _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        LogConnected(_connection.Endpoint.ToString());

        _channel = await _connection.CreateChannelAsync(ChannelOptions, cancellationToken);
        await _channel.ExchangeDeclareAsync(
            ExchangeName,
            ExchangeType.Topic,
            durable: true,
            cancellationToken: cancellationToken);

        return _channel;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Endpoint}")]
    private partial void LogConnected(string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection closed")]
    private partial void LogConnectionClosed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to close RabbitMQ connection gracefully")]
    private partial void LogCloseFailed(Exception exception);
}
