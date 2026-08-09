using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace tests;

public class SendMessagesIntegrationTest(RabbitMqFixture fixture): IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task SendMessages_WithValidRequests_ReturnsOK()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Content = JsonContent.Create(new SendMessagesRequest(
            FullName: "John Doe",
            Message: "Hello, this is a test message.",
            Mobile: "0434567890",
            Email: "example@gmail.com"
        ));
        
        // Act
        var response = await fixture.HttpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendMessages_WithValidRequests_PublishesToRabbitMQ()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Content = JsonContent.Create(new SendMessagesRequest(
            FullName: "John Doe",
            Message: "Hello, this is a test message.",
            Mobile: "0434567890",
            Email: "example@gmail.com"
        ));
        
        var factory = new ConnectionFactory()
        {
            HostName = fixture.Container.Hostname,
            Port     = fixture.Container.GetMappedPublicPort(5672),
            UserName = "admin",
            Password = "admin"
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        
        await channel.ExchangeDeclareAsync(exchange: Producer.ExchangeName, type: Producer.ExchangeType, durable: Producer.Durable, autoDelete: Producer.AutoDelete);
        
        var queueDeclare = await channel.QueueDeclareAsync();
        await channel.QueueBindAsync(queueDeclare.QueueName, Producer.ExchangeName, routingKey: string.Empty);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);

        var tcs = new TaskCompletionSource<BasicDeliverEventArgs>();
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, eventArgs) =>
        {
            tcs.TrySetResult(eventArgs);
            return Task.CompletedTask;
        };
        
        await channel.BasicConsumeAsync(queueDeclare.QueueName, autoAck: true, consumer: consumer);
        
        // Act
        var response = await fixture.HttpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var delivered = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        delivered.Should().NotBeNull();
        delivered.BasicProperties.Persistent.Should().BeTrue();
        
        var body = delivered.Body.ToArray();
        var message = JsonSerializer.Deserialize<SendMessagesRequest>(body);
        message.Should().NotBeNull();
        message.FullName.Should().Be("John Doe");
        message.Message.Should().Be("Hello, this is a test message.");
        message.Mobile.Should().Be("0434567890");
        message.Email.Should().Be("example@gmail.com");
    }
}
