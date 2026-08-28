using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace src;

public class Worker(ProcessedMessageStore processedMessageStore, ILogger<Worker> logger) : BackgroundService
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
        await DeclareSmsTopologyAsync(channel, stoppingToken);
        
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var body = eventArgs.Body.ToArray();
                var message = JsonSerializer.Deserialize<Message>(body);

                if (processedMessageStore.IsMessageProcessed(message!.Id))
                {
                    logger.LogInformation("Message {Id} has already been processed, skipping", message.Id);
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                    return;
                }

                // simulate sending SMS
                await Task.Delay(100, stoppingToken);
                logger.LogInformation("Message {Id} - Sending SMS to {Mobile}: Dear {FullName}, {Body}",
                    message!.Id, message.Mobile, message.FullName, message.Body);
                
                // store the processed message
                processedMessageStore.AddMessage(message);

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
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
    
    private async Task DeclareSmsTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        await channel.ExchangeDeclareAsync(SmsConsumer.ExchangeName, type: SmsConsumer.ExchangeType, durable: SmsConsumer.Durable, autoDelete: SmsConsumer.AutoDelete, cancellationToken: stoppingToken);
        
        // Declare the queue with dead-letter exchange arguments so that
        // messages that are negatively acknowledged will be routed to the dead-letter exchange by the broker
        await channel.QueueDeclareAsync(SmsConsumer.QueueName, durable: SmsConsumer.Durable, exclusive: false, autoDelete: SmsConsumer.AutoDelete, 
            arguments: new Dictionary<string, object?>()
            {
                { "x-dead-letter-exchange", SmsConsumer.DeadLetterExchangeName },
            },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(SmsConsumer.QueueName, SmsConsumer.ExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
    }

    private async Task DeclareDeadLetterTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
        // Declare the dead-letter exchange and queue for the sms worker
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