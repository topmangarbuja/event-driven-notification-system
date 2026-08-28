using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using src;

namespace tests;

public class EmailMessageHandlerTest
{
    private readonly ProcessedMessageStore _store = new();
    private readonly EmailMessageHandler _handler;

    public EmailMessageHandlerTest()
    {
        _handler = new EmailMessageHandler(_store, NullLogger<EmailMessageHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_WhenBodyIsNull_ReturnsNack()
    {
        var outcome = await _handler.HandleAsync("null"u8.ToArray(), CancellationToken.None);

        outcome.Should().Be(MessageOutcome.Nack);
        _store.GetMessages().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenRequiredFieldsMissing_ReturnsNack()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { Id = "1", FullName = "John Doe" });

        var outcome = await _handler.HandleAsync(body, CancellationToken.None);

        outcome.Should().Be(MessageOutcome.Nack);
        _store.GetMessages().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenValidMessage_ReturnsAckAndStoresMessage()
    {
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var outcome = await _handler.HandleAsync(body, CancellationToken.None);

        outcome.Should().Be(MessageOutcome.Ack);
        _store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenMessageAlreadyProcessed_ReturnsAckAndDoesNotStoreTwice()
    {
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "John Doe",
            Body = "Hello, this is a test message.",
            Mobile = "0434567890",
            Email = "example@gmail.com"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var first = await _handler.HandleAsync(body, CancellationToken.None);
        var second = await _handler.HandleAsync(body, CancellationToken.None);

        first.Should().Be(MessageOutcome.Ack);
        second.Should().Be(MessageOutcome.Ack);
        _store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }
}