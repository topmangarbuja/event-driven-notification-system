using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

app.MapPost("/api/messages", async([FromBody] SendMessagesRequest request, ILogger<Program> logger) =>
{
    var body = JsonSerializer.SerializeToUtf8Bytes(request);
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
    
    logger.LogInformation("Message sent to exchange {exchange}: {message}", Producer.ExchangeName, request);
    
    return Results.Ok();
});

app.Run();

public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);

public static class Producer
{
    public static readonly string ExchangeName = "message.submitted";
    public static readonly string ExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
    public static readonly bool Durable = true;
    public static readonly bool AutoDelete = false;
}
