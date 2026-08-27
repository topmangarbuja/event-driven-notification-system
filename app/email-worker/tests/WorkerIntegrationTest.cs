using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using src;
using Xunit.Abstractions;

namespace tests;

public class WorkerIntegrationTest(RabbitMqFixture fixture, ITestOutputHelper testOutputHelper) : IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task Given_ValidMessage_When_Consumed_Then_LogsAndPublishesProcessedEvent()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "example@gmail.com"
        };

        // Bind a probe queue to the processed exchange BEFORE publishing, so the worker's
        // processed event is captured rather than dropped by the broker
        await using var pubChannel = await connection.CreateChannelAsync();
        await pubChannel.ExchangeDeclareAsync(exchange: EmailConsumer.ProcessedExchangeName, type: EmailConsumer.ExchangeType, durable: EmailConsumer.Durable, autoDelete: EmailConsumer.AutoDelete);
        var probeQueue = await pubChannel.QueueDeclareAsync();
        await pubChannel.QueueBindAsync(probeQueue.QueueName, EmailConsumer.ProcessedExchangeName, routingKey: string.Empty);

        // Publish a message to the exchange the Worker is consuming from
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: body);

        // Poll for the log entry
        var logDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < logDeadline)
        {
            var found = fixture.FakeLogger.Collector.GetSnapshot()
                .Any(r => r.Message.Contains("example@gmail.com"));
            if (found) break;
            await Task.Delay(300);
        }

        // Assert the worker logged the email send
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.Message.Contains("Sending email to example@gmail.com: Dear John Doe, Hello, this is a test message."));

        // Assert a processed event was published to the processed exchange
        var processedDeadline = DateTime.UtcNow.AddSeconds(10);
        BasicGetResult? processed = null;
        while (DateTime.UtcNow < processedDeadline)
        {
            processed = await pubChannel.BasicGetAsync(probeQueue.QueueName, autoAck: true);
            if (processed is not null) break;
            await Task.Delay(300);
        }

        processed.Should().NotBeNull("the worker must publish a processed event after handling a message");
        var processedEvent = JsonSerializer.Deserialize<ProcessedMessage>(processed!.Body.ToArray());
        processedEvent.Should().NotBeNull();
        processedEvent!.Id.Should().Be(message.Id);
        processedEvent.Channel.Should().Be("email");
        processedEvent.FullName.Should().Be("John Doe");
        processedEvent.Body.Should().Be("Hello, this is a test message.");
    }

    [Fact]
    public async Task Given_PoisonMessage_When_DeserializationFails_Then_MessageGoesToDeadLetterQueue()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        // Publish an unprocessable payload that cannot be handled
        const string poisonBody = "this is not valid json";
        var poisonBytes = Encoding.UTF8.GetBytes(poisonBody);

        await using var pubChannel = await connection.CreateChannelAsync();
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: poisonBytes);

        // Wait for the worker to reject the message
        var logDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < logDeadline)
        {
            var rejected = fixture.FakeLogger.Collector.GetSnapshot()
                .Any(r => r.Level == LogLevel.Error && r.Message.Contains("Failed to process email message"));
            if (rejected) break;
            await Task.Delay(300);
        }

        var logs = fixture.FakeLogger.Collector.GetSnapshot();

        logs.Should().Contain(r =>
            r.Level == LogLevel.Error &&
            r.Message.Contains("Failed to process email message, sending to dead-letter queue"));

        // The message should end up in the dead-letter queue with its original body
        var dlDeadline = DateTime.UtcNow.AddSeconds(10);
        BasicGetResult? deadLettered = null;
        while (DateTime.UtcNow < dlDeadline)
        {
            deadLettered = await pubChannel.BasicGetAsync(EmailConsumer.DeadLetterQueueName, autoAck: true);
            if (deadLettered is not null) break;
            await Task.Delay(300);
        }

        deadLettered.Should().NotBeNull("the message must be dead-lettered when processing fails");
        Encoding.UTF8.GetString(deadLettered!.Body.ToArray()).Should().Be(poisonBody);
    }

    private async Task<IConnection> CreateConnectionAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.Container.Hostname,
            Port = fixture.Container.GetMappedPublicPort(5672),
            UserName = "admin",
            Password = "admin"
        };

        return await factory.CreateConnectionAsync();
    }

    /// <summary>
    /// Wait for the worker to declare the queue and start consuming
    /// </summary>
    private async Task WaitForWorkerReadyAsync(IConnection connection)
    {
        // Wait for the worker to declare the queue and start consuming
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var probeChannel = await connection.CreateChannelAsync();
                await probeChannel.QueueDeclarePassiveAsync(EmailConsumer.QueueName);
                testOutputHelper.WriteLine($"Queue '{EmailConsumer.QueueName}' exists. Worker is ready.");
                break;
            }
            catch (Exception ex)
            {
                // Queue doesn't exist yet — worker is still setting up
                testOutputHelper.WriteLine("Waiting for the worker to declare the queue...");

                testOutputHelper.WriteLine($"[ERROR] QueueDeclarePassiveAsync failed: {ex.GetType().Name} — {ex.Message}");
            }
            await Task.Delay(300);
        }
    }
}
