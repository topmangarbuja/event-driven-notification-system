using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using src;
using Testcontainers.RabbitMq;

namespace tests;

public class RabbitMqFixture : IAsyncLifetime
{
    private IHost _host = null!;

    public readonly RabbitMqContainer Container = new RabbitMqBuilder("rabbitmq:4.3.0-management-alpine")
        .WithUsername("admin")
        .WithPassword("admin")
        .Build();
    
    public FakeLogger<Worker> FakeLogger { get; } = new();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        
        // Point the app under test at the Testcontainer
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", Container.Hostname);
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", Container.GetMappedPublicPort(5672).ToString());
        Environment.SetEnvironmentVariable("RABBITMQ_USER", "admin");
        Environment.SetEnvironmentVariable("RABBITMQ_PASS", "admin");
        
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddHostedService<Worker>())
            .ConfigureServices(services => services.AddSingleton<ILogger<Worker>>(FakeLogger))
            .Build();

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Container.DisposeAsync();
    }
}