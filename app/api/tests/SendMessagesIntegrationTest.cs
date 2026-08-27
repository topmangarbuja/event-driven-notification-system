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

    [Fact]
    public async Task ProcessedEvent_WhenPublished_IsStoredAndReturnedByChannel()
    {
        // Arrange - publish a processed event straight into the processed exchange
        var factory = new ConnectionFactory()
        {
            HostName = fixture.Container.Hostname,
            Port     = fixture.Container.GetMappedPublicPort(5672),
            UserName = "admin",
            Password = "admin"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: Producer.ProcessedExchangeName, type: Producer.ExchangeType, durable: Producer.Durable, autoDelete: Producer.AutoDelete);

        // Wait for the API's processed consumer to declare and bind its queue so the published event isn't lost
        var readyDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < readyDeadline)
        {
            try
            {
                await channel.QueueDeclarePassiveAsync(ProcessedStore.QueueName);
                break;
            }
            catch
            {
                await Task.Delay(300);
            }
        }

        var processed = new ProcessedMessage
        {
            Id = Guid.NewGuid().ToString(),
            Channel = "email",
            ProcessedAt = DateTimeOffset.UtcNow,
            FullName = "Talia Lindgren",
            Body = "Hi, your order is confirmed.",
            Mobile = "0422999666",
            Email = "talia@example.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(processed);
        await channel.BasicPublishAsync(
            exchange: Producer.ProcessedExchangeName,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json" },
            body: body);

        // Act - poll the endpoint until the consumer stores it
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var store = fixture.Factory.Services.GetRequiredService<ProcessedMessageStore>();
        while (DateTime.UtcNow < deadline && !store.GetMessages("email").Any(m => m.Id == processed.Id))
        {
            await Task.Delay(300);
        }

        // Assert
        var response = await fixture.HttpClient.GetAsync($"/api/processed/{processed.Channel}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await response.Content.ReadFromJsonAsync<List<ProcessedMessage>>();
        stored.Should().ContainSingle(m => m.Id == processed.Id);
        var storedMessage = stored!.Single(m => m.Id == processed.Id);
        storedMessage.FullName.Should().Be("Talia Lindgren");
        storedMessage.Email.Should().Be("talia@example.com");
    }
}
