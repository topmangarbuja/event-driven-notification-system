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
    public async Task Given_ValidMessage_When_Consumed_Then_LogsAndAcknowledges()
    {
        await using var connection = await CreateConnectionAsync();
        
        await WaitForWorkerReadyAsync(connection);
        
        // Publish a message to the exchange the Worker is consuming from
        await using var pubChannel = await connection.CreateChannelAsync();
        var body = JsonSerializer.SerializeToUtf8Bytes(new SendMessagesRequest(
            FullName: "John Doe",
            Message: "Hello, this is a test message.",
            Mobile: "0434567890",
            Email: "example@gmail.com"
        ));

        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: body);
        
        // Poll for the log entry
        var logDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < logDeadline)
        {
            var found = fixture.FakeLogger.Collector.GetSnapshot()
                .Any(r => r.Message.Contains("0434567890"));
            if (found) break;
            await Task.Delay(300);
        }

        // Assert
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.Message.Contains("Sending SMS to 0434567890: Dear John Doe, Hello, this is a test message."));
                
    }

    [Fact]
    public async Task Given_PoisonMessage_When_DeserializationFails_Then_MessageGoesToDeadLetterQueue()
    {
        await using var connection = await CreateConnectionAsync();
        
        await WaitForWorkerReadyAsync(connection);
        
        // Publish a malformed message to the exchange the Worker is consuming from
        var poisonBody = "This will cause deserialization to fail";
        var poisonBytes = Encoding.UTF8.GetBytes(poisonBody);
        
        await using var pubChannel = await connection.CreateChannelAsync();
        await pubChannel.BasicPublishAsync(exchange: SmsConsumer.ExchangeName, routingKey: "", body: poisonBytes);
        
        // Poll for the log entry
        var logDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < logDeadline)
        {
            var found = fixture.FakeLogger.Collector.GetSnapshot()
                .Any(r => r.Message.Contains("Failed to process sms message"));
            if (found) break;
            await Task.Delay(300);
        }

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
}
