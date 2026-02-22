using AgentFlow.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentEventWorker>();

var host = builder.Build();
host.Run();
