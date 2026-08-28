using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace src;

public class Worker(EmailMessageHandler messageHandler, ILogger<Worker> logger) : BackgroundService
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
                logger.LogError(ex, "Failed to process email message, sending to dead-letter queue");

                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(EmailConsumer.QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

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
        await channel.ExchangeDeclareAsync(EmailConsumer.ExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);

        // Queue bound to the exchange, forwarding negative acknowledgements to the dead-letter exchange
        await channel.QueueDeclareAsync(EmailConsumer.QueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = EmailConsumer.DeadLetterExchangeName
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.QueueName, EmailConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

        // Dead-letter exchange and queue that hold failed messages
        await channel.ExchangeDeclareAsync(EmailConsumer.DeadLetterExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(EmailConsumer.DeadLetterQueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.DeadLetterQueueName, EmailConsumer.DeadLetterExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }
}

public static class EmailConsumer
{
    public const string ExchangeName = "message.submitted";
    public const string QueueName = $"email-worker.{ExchangeName}";
    public const string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public const bool Durable = true;
    public const bool AutoDelete = false;
    public const string DeadLetterExchangeName = $"{QueueName}.dlx";
    public const string DeadLetterQueueName = $"{QueueName}.dlq";
}