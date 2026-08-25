namespace src;

public class ProcessedMessageStore
{
    private readonly Dictionary<string, Message> _processedMessages = new();
    
    public IReadOnlyList<Message> GetMessages() => _processedMessages.Values.ToList();
    
    public void AddMessage(Message message)
    {
        _processedMessages.Add(message.Id, message);
    }
}

public class Message
{
    public string Id { get; init; }
    public string FullName { get; init; }
    public string Body { get; init; }
    public string Mobile { get; init; }
    public string Email { get; init; }
}