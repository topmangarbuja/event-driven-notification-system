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

const string exchange = "message.submitted";
// declare exchange
await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Fanout);

app.MapPost("/api/messages", async([FromBody] SendMessagesRequest request, ILogger<Program> logger) =>
{
    var body = JsonSerializer.SerializeToUtf8Bytes(request);
    await channel.BasicPublishAsync(exchange: exchange, 
        routingKey: string.Empty, 
        body: body);
    
    logger.LogInformation("Message sent to exchange {exchange}: {message}", exchange, request);
    
    return Results.Ok();
});

app.Run();

public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);