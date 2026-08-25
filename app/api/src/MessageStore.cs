namespace src;

public class Message
{
    public string Id { get; init; }
    public string FullName { get; init; }
    public string Body { get; init; }
    public string Mobile { get; init; }
    public string Email { get; init; }
}

public class MessageStore
{
    private readonly List<Message> _messages = new();
    public IReadOnlyList<Message> GetMessages() => _messages;
    
    public Message AddMessage(SendMessagesRequest request)
    {
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            FullName = request.FullName,
            Body = request.Message,
            Mobile = request.Mobile,
            Email = request.Email
        };
        _messages.Add(message);
        return message;
    }
}