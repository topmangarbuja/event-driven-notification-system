using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RabbitMQ.Client;
using src;
using Xunit.Abstractions;

namespace tests;

public class WorkerIntegrationTest(RabbitMqFixture fixture, ITestOutputHelper testOutputHelper) : IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task OnReceived_WhenValidMessage_LogsAndStoresIt()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        // Publish a message to the exchange the Worker is consuming from
        await using var pubChannel = await connection.CreateChannelAsync();
        var message = new Message()
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: body);

        // Wait for the worker to log that it sent the email
        await WaitForLogAsync(r => r.Message.Contains("Sending email to example@gmail.com"));

        // Assert
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.Message.Contains("Sending email to example@gmail.com: Dear John Doe, Hello, this is a test message."));
        
        var store = fixture.Host.Services.GetRequiredService<ProcessedMessageStore>();
        store.GetMessages().Should().Contain(m => m == message.Id);
    }
    
    [Fact]
    public async Task OnReceived_WhenPreviousValidMessage_DoesNotLogIt()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        // Publish a message to the exchange the Worker is consuming from
        await using var pubChannel = await connection.CreateChannelAsync();
        var message = new Message()
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "idempotent_example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: body);

        // First delivery is processed
        await WaitForLogAsync(r => r.Message.Contains($"Sending email to {message.Email}"));

        // Deliver the same message again
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: body);

        // Positive signal that the duplicate arrived and was deduplicated
        await WaitForLogAsync(r => r.Message.Contains("has already been processed, skipping"));

        // Nothing was sent a second time
        fixture.FakeLogger.Collector.GetSnapshot()
            .Count(r => r.Message.Contains($"Sending email to {message.Email}"))
            .Should().Be(1, "a previously processed message must not be sent again");

        var store = fixture.Host.Services.GetRequiredService<ProcessedMessageStore>();
        store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }

    [Fact]
    public async Task OnReceived_WhenPoisonMessage_MessageGoesToDeadLetterQueue()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        // Publish an unprocessable payload that cannot be handled
        const string poisonBody = "this is not valid json";
        var poisonBytes = Encoding.UTF8.GetBytes(poisonBody);

        await using var pubChannel = await connection.CreateChannelAsync();
        await pubChannel.BasicPublishAsync(exchange: EmailConsumer.ExchangeName, routingKey: "", body: poisonBytes);

        // Wait for the worker to reject the message
        await WaitForLogAsync(r => r.Level == LogLevel.Error && r.Message.Contains("Failed to process email message"));

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

    private async Task WaitForLogAsync(Func<FakeLogRecord, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (fixture.FakeLogger.Collector.GetSnapshot().Any(predicate)) return;
            await Task.Delay(300);
        }
    }
}
