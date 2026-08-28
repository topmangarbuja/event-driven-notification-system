using System.Text.Json;

namespace src;

public class SmsMessageHandler(ProcessedMessageStore processedMessageStore, ILogger<SmsMessageHandler> logger)
{
    public async Task<MessageOutcome> HandleAsync(byte[] body, CancellationToken stoppingToken)
    {
        var message = JsonSerializer.Deserialize<Message>(body);
        if (message is null)
        {
            logger.LogWarning("Received null message, sending to dead-letter queue");
            return MessageOutcome.Nack;
        }

        if (HasMissingRequiredFields(message))
        {
            logger.LogWarning("Received message with missing required fields, sending to dead-letter queue");
            return MessageOutcome.Nack;
        }

        // Skip messages that have already been processed so a redelivered
        // message does not produce a duplicate SMS
        if (processedMessageStore.IsMessageProcessed(message.Id))
        {
            logger.LogInformation("Message {Id} has already been processed, skipping", message.Id);
            return MessageOutcome.Ack;
        }

        // simulate sending SMS
        await Task.Delay(100, stoppingToken);
        logger.LogInformation("Message {Id} - Sending SMS to {Mobile}: Dear {FullName}, {Body}",
            message.Id, message.Mobile, message.FullName, message.Body);

        // store the processed message
        processedMessageStore.AddMessage(message);

        return MessageOutcome.Ack;
    }

    private static bool HasMissingRequiredFields(Message message) =>
        string.IsNullOrWhiteSpace(message.Id) ||
        string.IsNullOrWhiteSpace(message.Mobile) ||
        string.IsNullOrWhiteSpace(message.FullName) ||
        string.IsNullOrWhiteSpace(message.Body);
}

public enum MessageOutcome
{
    Ack,
    Nack
}