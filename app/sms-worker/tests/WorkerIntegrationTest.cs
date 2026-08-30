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
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: body);

        // Wait for the worker to log that it sent the SMS
        await WaitForLogAsync(r => r.Message.Contains("Sending SMS to 0434567890"));

        // Assert
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.Message.Contains("Sending SMS to 0434567890: Dear John Doe, Hello, this is a test message."));

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
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0400111222",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: body);

        // First delivery is processed
        await WaitForLogAsync(r => r.Message.Contains($"Sending SMS to {message.Mobile}"));

        // Deliver the same message again
        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: body);

        // Positive signal that the duplicate arrived and was deduplicated
        await WaitForLogAsync(r => r.Message.Contains("has already been processed, skipping"));

        // Nothing was sent a second time
        fixture.FakeLogger.Collector.GetSnapshot()
            .Count(r => r.Message.Contains($"Sending SMS to {message.Mobile}"))
            .Should().Be(1, "a previously processed message must not be sent again");

        var store = fixture.Host.Services.GetRequiredService<ProcessedMessageStore>();
        store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }
    
    [Fact]
    public async Task OnReceived_WhenConcurrentIdenticalValidMessage_OnlyProcessesOnce()
    {
        await using var connection = await CreateConnectionAsync();

        await WaitForWorkerReadyAsync(connection);

        // Publish a message to the exchange the Worker is consuming from
        await using var pubChannel = await connection.CreateChannelAsync();
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0400111222",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        
        // Deliver the same message concurrently multiple times to simulate a race condition
        var publishTasks = Enumerable.Range(0, 5)
            .Select(_ => pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: body))
            .ToArray();

        foreach (var publishTask in publishTasks)
        {
            await publishTask;
        }
        
        // Wait until all five messages have been processed (either sent or skipped)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var logCount = fixture.FakeLogger.Collector.GetSnapshot().Count(r=> r.Message.Contains(message.Id));
            if (logCount >= 5) break;
            await Task.Delay(300);
        }
        
        //Assert that only one SMS was sent and the rest were skipped
        fixture.FakeLogger.Collector.GetSnapshot()
            .Count(r => r.Message.Contains(message.Id) && r.Message.Contains($"Sending SMS to {message.Mobile}"))
            .Should().Be(1, "a previously processed message must not be sent again");
        fixture.FakeLogger.Collector.GetSnapshot()
            .Count(r => r.Message.Contains(message.Id) && r.Message.Contains("has already been processed, skipping"))
            .Should().Be(4, "four of the five concurrent deliveries must be deduplicated");
        
        var store = fixture.Host.Services.GetRequiredService<ProcessedMessageStore>();
        store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }

    [Fact]
    public async Task OnReceived_WhenPoisonMessage_MessageGoesToDeadLetterQueue()
    {
        await using var connection = await CreateConnectionAsync();
        
        await WaitForWorkerReadyAsync(connection);
        
        // Publish a malformed message to the exchange the Worker is consuming from
        var poisonBody = "This will cause deserialization to fail";
        var poisonBytes = Encoding.UTF8.GetBytes(poisonBody);
        
        await using var pubChannel = await connection.CreateChannelAsync();
        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: poisonBytes);
        
        // Wait for the worker to reject the message
        await WaitForLogAsync(r => r.Message.Contains("Failed to process sms message"));

        // Assert
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Error &&
                r.Message.Contains("Failed to process sms message, sending to dead-letter queue"));
        
        // The message should end up in the dead-letter queue with its original body
        var dlDeadline = DateTime.UtcNow.AddSeconds(10);
        BasicGetResult? deadLettered = null;
        while (DateTime.UtcNow < dlDeadline)
        {
            deadLettered = await pubChannel.BasicGetAsync(SmsConsumer.DeadLetterQueueName, autoAck: true);
            if (deadLettered is not null) break;
            await Task.Delay(300);
        }
        deadLettered.Should().NotBeNull("the message must be dead-lettered after processing failure");
        
        var dlqBody = Encoding.UTF8.GetString(deadLettered.Body.ToArray());
        dlqBody.Should().Be("This will cause deserialization to fail");
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
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var probeChannel = await connection.CreateChannelAsync();
                await probeChannel.QueueDeclarePassiveAsync(SmsConsumer.QueueName);
                testOutputHelper.WriteLine($"Queue '{SmsConsumer.QueueName}' exists. Worker is ready.");
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
