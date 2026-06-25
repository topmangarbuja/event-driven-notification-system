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
    public async Task Worker_ConsumesAndAcknowledgesMessage()
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.Container.Hostname,
            Port = fixture.Container.GetMappedPublicPort(5672),
            UserName = "admin",
            Password = "admin"
        };

        await using var connection = await factory.CreateConnectionAsync();
        
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

        // Publish a message to the exchange the Worker is consuming from
        await using var pubChannel = await connection.CreateChannelAsync();
        var body = JsonSerializer.SerializeToUtf8Bytes(new SendMessagesRequest(
            FullName: "John Doe",
            Message: "Hello, this is a test message.",
            Mobile: "0434567890",
            Email: "example@gmail.com"
        ));
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

        // Assert
        fixture.FakeLogger.Collector.GetSnapshot()
            .Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.Message.Contains("Sending email to example@gmail.com: Dear John Doe, Hello, this is a test message."));
    }
}
