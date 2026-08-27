using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace src;

public class ProcessedEventConsumer(ProcessedMessageStore store, ILogger<ProcessedEventConsumer> logger) : BackgroundService
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

        // Declare the exchange that carries processed events, plus our own queue bound to it
        await channel.ExchangeDeclareAsync(Producer.ProcessedExchangeName, type: Producer.ExchangeType, durable: Producer.Durable, autoDelete: Producer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(ProcessedStore.QueueName, durable: Producer.Durable, exclusive: false, autoDelete: Producer.AutoDelete, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(ProcessedStore.QueueName, Producer.ProcessedExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var processed = JsonSerializer.Deserialize<ProcessedMessage>(eventArgs.Body.ToArray());
                store.Add(processed!);
                logger.LogInformation("Stored processed {Channel} message {Id} for {FullName}", processed!.Channel, processed.Id, processed.FullName);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to store processed message");

                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(ProcessedStore.QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}

public static class ProcessedStore
{
    public const string QueueName = "api.message.processed";
}
