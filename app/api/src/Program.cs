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

app.MapPost("/api/send-messages", (SendMessagesRequest request) =>
{
    // Here you would typically handle the incoming message, e.g., save it to a database or send it to another service.
    // For this example, we'll just return a success response.
    return Results.Ok();
});

app.Run();

public record SendMessagesRequest(string FullName, string Message, string Mobile, string Email);