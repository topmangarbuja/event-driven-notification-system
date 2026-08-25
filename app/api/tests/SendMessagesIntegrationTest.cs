using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using src;

namespace tests;

public class SendMessagesIntegrationTest(RabbitMqFixture fixture): IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task PostMessage_WhenValidRequest_StoresMessage()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Content = JsonContent.Create(new SendMessagesRequest(
            FullName: "Alex Joe",
            Message: "Hello, this is a test message.",
            Mobile: "0411222333",
            Email: "alexjoe@gmail.com"
        ));
        
        // Act
        var response = await fixture.HttpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var store = fixture.Factory.Services.GetRequiredService<MessageStore>();
        var message = store.GetMessages().FirstOrDefault(m => m.Mobile == "0411222333");
        message.Should().NotBeNull();
        Guid.TryParse(message.Id, out _).Should().BeTrue();
        message.FullName.Should().Be("Alex Joe");
        message.Body.Should().Be("Hello, this is a test message.");
        message.Mobile.Should().Be("0411222333");
        message.Email.Should().Be("alexjoe@gmail.com");
    }

    [Fact]
    public async Task PostMessage_WhenValidRequest_PublishesMessageToExchange()
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

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: Producer.ExchangeName, type: Producer.ExchangeType, durable: Producer.Durable, autoDelete: Producer.AutoDelete);

        var queueDeclare = await channel.QueueDeclareAsync();
        await channel.QueueBindAsync(queueDeclare.QueueName, Producer.ExchangeName, routingKey: string.Empty);

        // Act
        var response = await fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - poll until the published message lands in our probe queue
        var deadline = DateTime.UtcNow.AddSeconds(5);
        BasicGetResult? delivered = null;
        while (DateTime.UtcNow < deadline)
        {
            delivered = await channel.BasicGetAsync(queueDeclare.QueueName, autoAck: true);
            if (delivered is not null) break;
            await Task.Delay(300);
        }

        delivered.Should().NotBeNull("the API must publish the message to the exchange");
        delivered!.BasicProperties.Persistent.Should().BeTrue();

        var body = delivered.Body.ToArray();
        var message = JsonSerializer.Deserialize<Message>(body);
        message.Should().NotBeNull();
        Guid.TryParse(message.Id, out _).Should().BeTrue();
        message.FullName.Should().Be("John Doe");
        message.Body.Should().Be("Hello, this is a test message.");
        message.Mobile.Should().Be("0434567890");
        message.Email.Should().Be("example@gmail.com");
    }
}
