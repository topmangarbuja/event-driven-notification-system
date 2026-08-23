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

        var connection = await factory.CreateConnectionAsync(stoppingToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        try
        {
            await DeclareDeadLetterTopologyAsync(channel, stoppingToken);
            await DeclareEmailTopologyAsync(channel, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                var body = eventArgs.Body.ToArray();

                try
                {
                    var message = JsonSerializer.Deserialize<SendMessagesRequest>(body);

                    logger.LogInformation("Sending email to {Email}: Dear {FullName}, {Message}", message!.Email, message.FullName, message.Message);

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
        finally
        {
            await channel.CloseAsync(stoppingToken);
            await connection.CloseAsync(stoppingToken);
            channel?.Dispose();
            connection?.Dispose();
        }
    }
    private static async Task DeclareEmailTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        await channel.ExchangeDeclareAsync(EmailConsumer.ExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(EmailConsumer.QueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = EmailConsumer.DlxExchangeName
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.QueueName, EmailConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }

    private static async Task DeclareDeadLetterTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        // Messages that are negatively acknowledged will be routed to the dead-letter exchange by the broker
        await channel.ExchangeDeclareAsync(EmailConsumer.DlxExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(EmailConsumer.DlqQueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(EmailConsumer.DlqQueueName, EmailConsumer.DlxExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }
}
public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);

public static class EmailConsumer
{
    public const string ExchangeName = "message.submitted";
    public const string QueueName = $"email-worker.{ExchangeName}";
    public const string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public const bool Durable = true;
    public const bool AutoDelete = false;
    public const string DlxExchangeName = $"{QueueName}.dlx";
    public const string DlqQueueName = $"{QueueName}.dlq";
}
