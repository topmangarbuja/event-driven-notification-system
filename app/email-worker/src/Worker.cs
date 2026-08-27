using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace src;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST")!,
            Port     = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT")!),
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER")!,
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS")!
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareDeadLetterTopologyAsync(channel, stoppingToken);
        await DeclareEmailTopologyAsync(channel, stoppingToken);
        await DeclareProcessedTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var body = eventArgs.Body.ToArray();

            try
            {
                var message = JsonSerializer.Deserialize<Message>(body);

                logger.LogInformation("Sending email to {Email}: Dear {FullName}, {Body}", message!.Email, message.FullName, message.Body);

                // Publish a processed event so the API/UI can show this message was handled
                var processed = new ProcessedMessage
                {
                    Id = message.Id,
                    Channel = "email",
                    ProcessedAt = DateTimeOffset.UtcNow,
                    FullName = message.FullName,
                    Body = message.Body,
                    Mobile = message.Mobile,
                    Email = message.Email
                };
                var processedBody = JsonSerializer.SerializeToUtf8Bytes(processed);
                await channel.BasicPublishAsync(
                    exchange: EmailConsumer.ProcessedExchangeName,
                    routingKey: string.Empty,
                    mandatory: false,
                    basicProperties: new BasicProperties { ContentType = "application/json" },
                    body: processedBody,
                    cancellationToken: stoppingToken);

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
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
    
    private static async Task DeclareEmailTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        await channel.ExchangeDeclareAsync(EmailConsumer.ExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        
        // Declare the queue with dead-letter exchange arguments so that
        // messages that are negatively acknowledged will be routed to the dead-letter exchange by the broker
        await channel.QueueDeclareAsync(EmailConsumer.QueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = EmailConsumer.DeadLetterExchangeName
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.QueueName, EmailConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }

    private static async Task DeclareDeadLetterTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        // Declare the dead-letter exchange and queue for the email worker
        await channel.ExchangeDeclareAsync(EmailConsumer.DeadLetterExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(EmailConsumer.DeadLetterQueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.DeadLetterQueueName, EmailConsumer.DeadLetterExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }

    private static async Task DeclareProcessedTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        // Declare the exchange that carries "processed" events back out to the API for display
        await channel.ExchangeDeclareAsync(EmailConsumer.ProcessedExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
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
    public const string ProcessedExchangeName = "message.processed";
}
