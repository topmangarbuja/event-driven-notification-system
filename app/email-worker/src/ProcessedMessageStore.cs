namespace src;

public class ProcessedMessageStore
{
    private readonly List<string> _processedMessages = new();
    
    public IReadOnlyList<string> GetMessages() => _processedMessages.ToList();
    
    public void AddMessage(Message message)
    {
        _processedMessages.Add(message.Id);
    }

    public bool IsMessageProcessed(string id)
    {
        return _processedMessages.Contains(id);
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