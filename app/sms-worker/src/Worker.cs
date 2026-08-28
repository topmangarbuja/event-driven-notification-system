using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace src;

public class Worker(SmsMessageHandler messageHandler, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = await CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var outcome = await messageHandler.HandleAsync(eventArgs.Body.ToArray(), stoppingToken);
                if (outcome == MessageOutcome.Ack)
                {
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                }
                else
                {
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process sms message, sending to dead-letter queue");

                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(SmsConsumer.QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static async Task<IConnection> CreateConnectionAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST")!,
            Port     = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT")!),
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER")!,
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS")!
        };

        return await factory.CreateConnectionAsync(stoppingToken);
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        // Exchange that receives published messages
        await channel.ExchangeDeclareAsync(SmsConsumer.ExchangeName, type: SmsConsumer.ExchangeType, durable: SmsConsumer.Durable, autoDelete: SmsConsumer.AutoDelete, cancellationToken: stoppingToken);

        // Queue bound to the exchange, forwarding negative acknowledgements to the dead-letter exchange
        await channel.QueueDeclareAsync(SmsConsumer.QueueName, durable: SmsConsumer.Durable, exclusive: false, autoDelete: SmsConsumer.AutoDelete,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = SmsConsumer.DeadLetterExchangeName
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(SmsConsumer.QueueName, SmsConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

        // Dead-letter exchange and queue that hold failed messages
        await channel.ExchangeDeclareAsync(SmsConsumer.DeadLetterExchangeName, type: SmsConsumer.ExchangeType, durable: SmsConsumer.Durable, autoDelete: SmsConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(SmsConsumer.DeadLetterQueueName, durable: SmsConsumer.Durable, exclusive: false, autoDelete: SmsConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(SmsConsumer.DeadLetterQueueName, SmsConsumer.DeadLetterExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }
}

public static class SmsConsumer
{
    public const string ExchangeName = "message.submitted";
    public const string QueueName = $"sms-worker.{ExchangeName}";
    public const string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public const bool Durable = true;
    public const bool AutoDelete = false;
    public const string DeadLetterExchangeName = $"{QueueName}.dlx";
    public const string DeadLetterQueueName = $"{QueueName}.dlq";
}