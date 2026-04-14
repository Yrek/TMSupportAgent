using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ThreatModelingAgent.Infrastructure.Persistence;

/// <summary>
/// Sets the PostgreSQL session variable 'app.current_org_id' before every command.
/// This variable is used by all RLS policies on tenant-scoped tables.
///
/// The org_id MUST be sourced from the validated JWT via ITenantContext — never
/// from request parameters, body, or any client-supplied value (CLAUDE.md §8.2).
///
/// IMPORTANT: We execute SET LOCAL as a SEPARATE preceding command rather than
/// prepending it to command.CommandText. Embedding SET LOCAL in CommandText creates
/// multi-statement commands; Npgsql then returns one extra CommandComplete per
/// statement. EF Core's NpgsqlModificationCommandBatch reads one CommandComplete per
/// entity — the SET LOCAL's CommandComplete (0 rows) is attributed to the first
/// entity, causing a spurious DbUpdateConcurrencyException.
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

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariableAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SetRlsVariable(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariableAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        SetRlsVariable(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariableAsync(command, cancellationToken);
        return result;
    }

    // ── Implementation ────────────────────────────────────────────────────────

    private void SetRlsVariable(DbCommand command)
    {
        var orgId = tenantContext.CurrentOrgId?.Value.ToString() ?? string.Empty;

        // Execute as a separate preceding command — do NOT embed in command.CommandText.
        // See class doc for why prepending creates batch result misalignment.
        using var setCmd = command.Connection!.CreateCommand();
        setCmd.Transaction = command.Transaction;
        setCmd.CommandText = "SELECT set_config('app.current_org_id', @v, true)";
        var p = setCmd.CreateParameter();
        p.ParameterName = "v";
        p.Value = orgId;
        setCmd.Parameters.Add(p);
        setCmd.ExecuteNonQuery();
    }

    private async ValueTask SetRlsVariableAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var orgId = tenantContext.CurrentOrgId?.Value.ToString() ?? string.Empty;

        await using var setCmd = command.Connection!.CreateCommand();
        setCmd.Transaction = command.Transaction;
        setCmd.CommandText = "SELECT set_config('app.current_org_id', @v, true)";
        var p = setCmd.CreateParameter();
        p.ParameterName = "v";
        p.Value = orgId;
        setCmd.Parameters.Add(p);
        await setCmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
