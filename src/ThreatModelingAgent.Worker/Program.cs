using Azure.Messaging.ServiceBus;
using Serilog;
using Serilog.Events;
using ThreatModelingAgent.Infrastructure;
using ThreatModelingAgent.Infrastructure.Persistence;
using ThreatModelingAgent.Worker;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline;
using ThreatModelingAgent.Worker.Pipeline.Stages;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((ctx, config) =>
        config.ReadFrom.Configuration(builder.Configuration)
              .Enrich.FromLogContext()
              .Enrich.WithProperty("Service", "worker")
              .WriteTo.Console());

    // ── Validate required config at startup (CLAUDE.md §4.3 Fail Secure) ────
    _ = builder.Configuration["AzureServiceBus:ConnectionString"]
        ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString is required.");
    _ = builder.Configuration["AzureServiceBus:QueueName"]
        ?? throw new InvalidOperationException("AzureServiceBus:QueueName is required.");

    // ── Infrastructure ───────────────────────────────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);

    // Worker has no HTTP requests so TenantContext is set from message metadata
    builder.Services.AddScoped<WorkerTenantContext>();
    builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());

    // ── HTTP clients with explicit timeouts (CLAUDE.md §9.8) ────────────────
    builder.Services.AddHttpClient("AzureOpenAI", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(120);
    });
    builder.Services.AddHttpClient("Anthropic", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(120);
        c.BaseAddress = new Uri("https://api.anthropic.com");
    });

    // ── LLM clients ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<AzureOpenAiClient>();
    builder.Services.AddScoped<AnthropicClient>();
    builder.Services.AddScoped<IEnumerable<ILlmClient>>(sp =>
    [
        sp.GetRequiredService<AzureOpenAiClient>(),
        sp.GetRequiredService<AnthropicClient>()
    ]);
    builder.Services.AddScoped<LlmClientFactory>();
    builder.Services.AddScoped<ILlmClientFactory>(sp => sp.GetRequiredService<LlmClientFactory>());

    // ── Pipeline stages ──────────────────────────────────────────────────────
    builder.Services.AddScoped<PipelineDbPersistence>();
    builder.Services.AddScoped<DetectStage>();
    builder.Services.AddScoped<ParseStage>();
    builder.Services.AddScoped<NormalizeStage>();
    builder.Services.AddScoped<ClassifyStage>();
    builder.Services.AddScoped<AnalyzeStage>();
    builder.Services.AddScoped<SynthesizeStage>();
    builder.Services.AddScoped<JobOrchestrator>();

    // ── Service Bus ──────────────────────────────────────────────────────────
    builder.Services.AddSingleton(sp =>
    {
        var connStr = builder.Configuration["AzureServiceBus:ConnectionString"]!;
        return new ServiceBusClient(connStr);
    });

    builder.Services.AddHostedService<ServiceBusWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Worker failed to start.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
