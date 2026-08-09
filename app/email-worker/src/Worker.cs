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
            await channel.ExchangeDeclareAsync(EmailConsumer.ExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);

            await channel.QueueDeclareAsync(EmailConsumer.QueueName, durable: EmailConsumer.Durable, exclusive: false, autoDelete: EmailConsumer.AutoDelete, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(EmailConsumer.QueueName, EmailConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                //TODO: Implement retry logic and dead-lettering for failed messages.
                var body = eventArgs.Body.ToArray();
                var message = JsonSerializer.Deserialize<SendMessagesRequest>(body);

                logger.LogInformation("Sending email to {Email}: Dear {FullName}, {Message}", message.Email, message.FullName, message.Message);

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
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
}

public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);

public static class EmailConsumer
{
    public static readonly string ExchangeName = "message.submitted";
    public static string QueueName = $"email-worker.{ExchangeName}";
    public static readonly string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public static readonly bool Durable = true;
    public static readonly bool AutoDelete = false;
}

