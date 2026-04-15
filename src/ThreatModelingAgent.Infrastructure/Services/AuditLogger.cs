using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Infrastructure.Persistence;

namespace ThreatModelingAgent.Infrastructure.Services;

/// <summary>
/// Writes to the append-only audit_log table.
/// Serializes only non-PII identifiers in the details payload (CLAUDE.md §10.4).
/// </summary>
internal sealed class AuditLogger(AppDbContext db, IHttpContextAccessor? httpContextAccessor = null) : IAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task LogAsync(
        string eventType,
        OrgId? orgId = null,
        UserId? userId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        object? details = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        // Correlation ID should come from request middleware when available.
        // Worker/background flows have no HttpContext, so we safely fall back.
        var correlationId = httpContextAccessor?.HttpContext?.Items["CorrelationId"] is Guid g
            ? g
            : Guid.NewGuid();

        var detailsJson = details is null
            ? "{}"
            : JsonSerializer.Serialize(details, SerializerOptions);

        var entry = AuditLog.Create(
            correlationId: correlationId,
            eventType: eventType,
            orgId: orgId,
            userId: userId,
            resourceType: resourceType,
            resourceId: resourceId,
            details: detailsJson,
            ipAddress: ipAddress);

        await db.AuditLogs.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }
}
