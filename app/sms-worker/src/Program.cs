using src;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<ProcessedMessageStore>();

var host = builder.Build();
host.Run();
