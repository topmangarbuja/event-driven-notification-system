using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.RabbitMq;

namespace tests;

public class RabbitMqFixture : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();

    public HttpClient HttpClient { get; private set; }

    public readonly RabbitMqContainer Container = new RabbitMqBuilder("rabbitmq:4.3.0-management-alpine")
        .WithUsername("admin")
        .WithPassword("admin")
        .Build();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        
        // Point the app under test at the Testcontainer
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", Container.Hostname);
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", Container.GetMappedPublicPort(5672).ToString());
        Environment.SetEnvironmentVariable("RABBITMQ_USER", "admin");
        Environment.SetEnvironmentVariable("RABBITMQ_PASS", "admin");
        
        HttpClient = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();
        await _factory.DisposeAsync();
        await Container.DisposeAsync();
    }
}