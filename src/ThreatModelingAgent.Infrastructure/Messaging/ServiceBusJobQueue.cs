using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.Messaging;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Messaging;

/// <summary>
/// Sends analysis job messages to the Azure Service Bus queue.
/// Used by the API to trigger Phase 1 (after submit) and Phase 2 (after user confirms).
///
/// In production, uses managed identity via DefaultAzureCredential.
/// For local dev, uses connection string from configuration.
/// </summary>
internal sealed class ServiceBusJobQueue : IJobQueue, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusJobQueue> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceBusJobQueue(IConfiguration configuration, ILogger<ServiceBusJobQueue> logger)
    {
        _logger = logger;

        var queueName = configuration["AzureServiceBus:QueueName"]
            ?? throw new InvalidOperationException("AzureServiceBus:QueueName is required.");

        // Connection string for local dev; managed identity (FQDN) for production
        var connectionString = configuration["AzureServiceBus:ConnectionString"];
        var namespaceFqdn = configuration["AzureServiceBus:NamespaceFQDN"];

        if (!string.IsNullOrEmpty(connectionString))
        {
            _client = new ServiceBusClient(connectionString);
        }
        else if (!string.IsNullOrEmpty(namespaceFqdn))
        {
            // Production: managed identity via DefaultAzureCredential (CLAUDE.md §10.1)
            _client = new ServiceBusClient(namespaceFqdn, new Azure.Identity.DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException(
                "AzureServiceBus must be configured with either ConnectionString (local dev) " +
                "or NamespaceFQDN (production managed identity). Application cannot start. (CLAUDE.md §4.3)");
        }

        _sender = _client.CreateSender(queueName);
    }

    public Task EnqueueParsePhaseAsync(
        JobId jobId,
        OrgId orgId,
        string artifactBlobPath,
        string artifactType,
        CancellationToken ct = default)
        => SendAsync(new AnalysisJobMessage(jobId.Value, orgId.Value, artifactBlobPath, artifactType, PipelinePhase.Parse), ct);

    public Task EnqueueAnalyzePhaseAsync(
        JobId jobId,
        OrgId orgId,
        string artifactBlobPath,
        string artifactType,
        CancellationToken ct = default)
        => SendAsync(new AnalysisJobMessage(jobId.Value, orgId.Value, artifactBlobPath, artifactType, PipelinePhase.Analyze), ct);

    private async Task SendAsync(AnalysisJobMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var sbMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(json))
        {
            ContentType = "application/json",
            CorrelationId = message.JobId.ToString()
        };

        await _sender.SendMessageAsync(sbMessage, ct);

        _logger.LogInformation(
            "Job message enqueued. JobId={JobId} Phase={Phase}",
            message.JobId, message.Phase);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
