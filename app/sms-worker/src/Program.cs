using src;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<ProcessedMessageStore>();
builder.Services.AddSingleton<SmsMessageHandler>();

var host = builder.Build();
host.Run();
