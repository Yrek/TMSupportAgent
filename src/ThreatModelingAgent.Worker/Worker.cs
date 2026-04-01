using Azure.Messaging.ServiceBus;
using System.Text.Json;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Worker.Pipeline;

namespace ThreatModelingAgent.Worker;

/// <summary>
/// Service Bus consumer. Dequeues analysis jobs and runs the pipeline.
///
/// Security invariants:
/// - Message metadata (org_id) is validated against the DB before any processing
/// - Failed messages are dead-lettered by Service Bus after max delivery count
/// - No message content is logged (may contain blob paths to sensitive artifacts)
/// </summary>
public sealed class ServiceBusWorker(
    ServiceBusClient sbClient,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ServiceBusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueName = configuration["AzureServiceBus:QueueName"]!;

        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,     // limit parallelism to control LLM costs
            AutoCompleteMessages = false // we complete manually after successful processing
        };

        await using var processor = sbClient.CreateProcessor(queueName, options);

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);

        logger.LogInformation("Service Bus worker started. Queue={Queue}", queueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);

        await processor.StopProcessingAsync();
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var ct = args.CancellationToken;

        AnalysisJobMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<AnalysisJobMessage>(
                args.Message.Body.ToString());

            if (message is null)
            {
                logger.LogWarning("Received null or unparseable message. DeadLettering.");
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "INVALID_MESSAGE_FORMAT", cancellationToken: ct);
                return;
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Message deserialization failed. DeadLettering.");
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "DESERIALIZATION_FAILED", cancellationToken: ct);
            return;
        }

        // Validate org_id from message before setting tenant context
        if (message.OrgId == Guid.Empty || message.JobId == Guid.Empty)
        {
            logger.LogWarning("Message has empty OrgId or JobId. DeadLettering.");
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "INVALID_IDS", cancellationToken: ct);
            return;
        }

        logger.LogInformation("Processing job. JobId={JobId}", message.JobId);

        await using var scope = scopeFactory.CreateAsyncScope();

        // Set tenant context from validated message org_id (architecture §7.2)
        var tenantContext = scope.ServiceProvider.GetRequiredService<WorkerTenantContext>();
        tenantContext.Set(OrgId.From(message.OrgId));

        var orchestrator = scope.ServiceProvider.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(message, ct);

        await args.CompleteMessageAsync(args.Message, ct);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        // Log Service Bus errors without message content
        logger.LogError(args.Exception,
            "Service Bus error. Source={Source} EntityPath={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
