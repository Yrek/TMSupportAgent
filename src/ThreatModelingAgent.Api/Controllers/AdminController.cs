using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Platform admin API — /v1/admin/*
///
/// Only accessible with a platform:admin JWT (WorkOS role claim = "platform:admin").
/// TenantContextMiddleware ensures admin tokens never reach org-scoped routes, and
/// regular user tokens never reach these routes (enforced by the "PlatformAdmin" policy).
///
/// No org_id context is set for these requests — all queries are cross-tenant.
/// </summary>
[ApiController]
[Authorize(Policy = "PlatformAdmin")]
[Route("v1/admin")]
[EnableRateLimiting("api")]
public sealed class AdminController(
    IAdminRepository admin,
    IAuditLogger audit,
    ILogger<AdminController> logger) : ControllerBase
{
    // GET /v1/admin/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await admin.GetSystemStatsAsync(ct);
        return Ok(AdminSystemStatsDto.From(stats));
    }

    // GET /v1/admin/orgs?search=&page=1&pageSize=20
    [HttpGet("orgs")]
    public async Task<IActionResult> ListOrgs(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await admin.ListOrgsAsync(search, page, pageSize, ct);

        return Ok(new
        {
            data = items.Select(AdminOrgDto.From),
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            }
        });
    }

    // GET /v1/admin/orgs/{orgId}
    [HttpGet("orgs/{orgId:guid}")]
    public async Task<IActionResult> GetOrg(Guid orgId, CancellationToken ct)
    {
        var summary = await admin.GetOrgSummaryAsync(OrgId.From(orgId), ct);
        if (summary is null) return NotFound();
        return Ok(AdminOrgDto.From(summary));
    }

    // POST /v1/admin/orgs/{orgId}/suspend
    [HttpPost("orgs/{orgId:guid}/suspend")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> SuspendOrg(Guid orgId, CancellationToken ct)
    {
        var org = await admin.GetOrgAsync(OrgId.From(orgId), ct);
        if (org is null) return NotFound();
        if (org.IsSuspended) return Ok(new { message = "Organization is already suspended." });

        org.Suspend();
        await admin.SaveChangesAsync(ct);

        await audit.LogAsync("admin.org.suspended",
            orgId: org.Id,
            userId: User.GetUserId(),
            resourceType: "organization",
            resourceId: orgId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Org suspended by admin. OrgId={OrgId}", orgId);

        var summary = await admin.GetOrgSummaryAsync(org.Id, ct);
        return Ok(AdminOrgDto.From(summary!));
    }

    // POST /v1/admin/orgs/{orgId}/unsuspend
    [HttpPost("orgs/{orgId:guid}/unsuspend")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> UnsuspendOrg(Guid orgId, CancellationToken ct)
    {
        var org = await admin.GetOrgAsync(OrgId.From(orgId), ct);
        if (org is null) return NotFound();
        if (!org.IsSuspended) return Ok(new { message = "Organization is not suspended." });

        org.Unsuspend();
        await admin.SaveChangesAsync(ct);

        await audit.LogAsync("admin.org.unsuspended",
            orgId: org.Id,
            userId: User.GetUserId(),
            resourceType: "organization",
            resourceId: orgId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Org unsuspended by admin. OrgId={OrgId}", orgId);

        var summary = await admin.GetOrgSummaryAsync(org.Id, ct);
        return Ok(AdminOrgDto.From(summary!));
    }

    // DELETE /v1/admin/orgs/{orgId} — hard/soft delete by platform admin
    [HttpDelete("orgs/{orgId:guid}")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> DeleteOrg(Guid orgId, CancellationToken ct)
    {
        var org = await admin.GetOrgAsync(OrgId.From(orgId), ct);
        if (org is null) return NotFound();

        org.SoftDelete();
        await admin.SaveChangesAsync(ct);

        await audit.LogAsync("admin.org.deleted",
            orgId: org.Id,
            userId: User.GetUserId(),
            resourceType: "organization",
            resourceId: orgId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Org deleted by admin. OrgId={OrgId}", orgId);

        return NoContent();
    }
}
