using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ThreatModelingAgent.Infrastructure.Persistence;

/// <summary>
/// Sets the PostgreSQL session variable 'app.current_org_id' before every command.
/// This variable is used by all RLS policies on tenant-scoped tables.
///
/// The org_id MUST be sourced from the validated JWT via ITenantContext — never
/// from request parameters, body, or any client-supplied value (CLAUDE.md §8.2).
/// </summary>
public sealed class RlsSessionInterceptor(ITenantContext tenantContext) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        SetRlsVariable(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        SetRlsVariable(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SetRlsVariable(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetRlsVariable(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        SetRlsVariable(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        SetRlsVariable(command);
        return ValueTask.FromResult(result);
    }

    private void SetRlsVariable(DbCommand command)
    {
        // If no org context (e.g. platform-level queries), set to empty string.
        // RLS will deny access to any tenant-scoped row — fail-secure behavior (CLAUDE.md §4.3).
        var orgId = tenantContext.CurrentOrgId?.Value.ToString() ?? string.Empty;

        // Prepend SET LOCAL so it applies only to the current transaction,
        // preventing cross-request leakage in connection pooling scenarios.
        command.CommandText = $"SET LOCAL \"app.current_org_id\" = '{orgId}';\n" + command.CommandText;
    }
}
