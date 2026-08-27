namespace src;

public class Message
{
    public string Id { get; init; }
    public string FullName { get; init; }
    public string Body { get; init; }
    public string Mobile { get; init; }
    public string Email { get; init; }
}

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
