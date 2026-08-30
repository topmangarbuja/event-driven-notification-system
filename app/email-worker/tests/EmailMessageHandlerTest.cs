using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Testing;
using src;

namespace tests;

public class EmailMessageHandlerTest
{
    private readonly ProcessedMessageStore _store = new();
    private readonly FakeLogger<EmailMessageHandler> _logger = new(new FakeLogCollector());
    private readonly EmailMessageHandler _handler;

    public EmailMessageHandlerTest()
    {
        _handler = new EmailMessageHandler(_store, _logger);
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

    [Fact]
    public async Task HandleAsync_WhenSimultaneousMessagesWithSameId_ReturnsAckAndStoresOnce()
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

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _handler.HandleAsync(body, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        tasks.All(t => t.Result == MessageOutcome.Ack).Should().BeTrue();
        _store.GetMessages().Should().ContainSingle(m => m == message.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenSimultaneousMessagesWithSameId_LogsOnceAndSkipsDuplicates()
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

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _handler.HandleAsync(body, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        var logSnapshot = _logger.Collector.GetSnapshot();
        logSnapshot.Count(r => r.Message.Contains($"Message {message.Id} has already been processed, skipping"))
            .Should().Be(9, "only the first message should be processed, the rest should be skipped");
        logSnapshot.Count(r => r.Message.Contains($"Sending email to {message.Email}"))
            .Should().Be(1, "only the first message should be processed and logged, the rest should be skipped");
    }
}