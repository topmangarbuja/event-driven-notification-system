namespace src;

public class ProcessedMessage
{
    public string Id { get; init; }
    public string Channel { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
    public string FullName { get; init; }
    public string Body { get; init; }
    public string Mobile { get; init; }
    public string Email { get; init; }
}

public class ProcessedMessageStore
{
    private readonly List<ProcessedMessage> _processed = new();

    public IReadOnlyList<ProcessedMessage> GetMessages() => _processed;

    public IReadOnlyList<ProcessedMessage> GetMessages(string channel) =>
        _processed.Where(m => string.Equals(m.Channel, channel, StringComparison.OrdinalIgnoreCase)).ToList();

    public void Add(ProcessedMessage message) => _processed.Add(message);
}
