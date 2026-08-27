using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using src;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSingleton<ProcessedMessageStore>();
builder.Services.AddHostedService<ProcessedEventConsumer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var factory = new ConnectionFactory()
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST")!,
    Port     = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT")!),
    UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER")!,
    Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS")!
};

var connection = await factory.CreateConnectionAsync();
var channel = await connection.CreateChannelAsync();

// declare exchange
await channel.ExchangeDeclareAsync(exchange: Producer.ExchangeName, type: Producer.ExchangeType, durable: Producer.Durable, autoDelete: Producer.AutoDelete);

app.MapPost("/api/messages", async([FromBody] SendMessagesRequest request, MessageStore messageStore, ILogger<Program> logger) =>
{
    var message = messageStore.AddMessage(request);
    
    var body = JsonSerializer.SerializeToUtf8Bytes(message);
    var basicProperties = new BasicProperties()
    {
        Persistent = true,
        ContentType = "application/json"
    };
    
    await channel.BasicPublishAsync(exchange: Producer.ExchangeName, 
        routingKey: string.Empty, 
        mandatory: false,
        basicProperties: basicProperties,
        body: body);
    
    logger.LogInformation("Message {Id} sent to exchange {Exchange} for {FullName} ({Email}, {Mobile})", message.Id, Producer.ExchangeName, message.FullName, message.Email, message.Mobile);
    
    return Results.Ok();
});

app.MapGet("/api/processed", (ProcessedMessageStore store) => Results.Ok(store.GetMessages()));
app.MapGet("/api/processed/{channel}", (string channel, ProcessedMessageStore store) => Results.Ok(store.GetMessages(channel)));

app.Run();

public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);

public static class Producer
{
    public static readonly string ExchangeName = "message.submitted";
    public static readonly string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public static readonly bool Durable = true;
    public static readonly bool AutoDelete = false;
    public const string ProcessedExchangeName = "message.processed";
}
