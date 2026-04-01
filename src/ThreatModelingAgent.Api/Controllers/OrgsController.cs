using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Extensions;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

[ApiController]
[Authorize]
[Route("v1/orgs")]
[EnableRateLimiting("api")]
public sealed class OrgsController(
    IOrganizationRepository orgs,
    IMembershipRepository memberships,
    IAuditLogger audit,
    ILogger<OrgsController> logger) : ControllerBase
{
    // GET /v1/orgs — list orgs for the current user
    [HttpGet]
    public async Task<IActionResult> ListOrgs(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var userOrgs = await orgs.ListByUserAsync(userId, ct);

        // Load memberships to know role per org — one query per org (acceptable at this scale)
        var result = new List<OrgSummaryDto>();
        foreach (var org in userOrgs)
        {
            var membership = await memberships.GetAsync(org.Id, userId, ct);
            if (membership is null) continue;
            result.Add(OrgSummaryDto.From(org, membership.Role));
        }

        return Ok(new { data = result });
    }

    // POST /v1/orgs — create org; caller becomes owner
    [HttpPost]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> CreateOrg(
        [FromBody] CreateOrgRequest request,
        [FromServices] IValidator<CreateOrgRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToModelStateDictionary());

        if (await orgs.SlugExistsAsync(request.Slug, ct))
            return Conflict(new { code = "SLUG_TAKEN", message = "This slug is already in use." });

        var userId = User.GetUserId();
        var org = Organization.Create(request.Name, request.Slug);
        await orgs.AddAsync(org, ct);

        // Creator becomes owner
        var membership = OrgMembership.Create(org.Id, userId, OrgMemberRole.Owner);
        await memberships.AddAsync(membership, ct);
        await orgs.SaveChangesAsync(ct);

        await audit.LogAsync("org.created",
            orgId: org.Id,
            userId: userId,
            resourceType: "organization",
            resourceId: org.Id.Value,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Org created. OrgId={OrgId} UserId={UserId}", org.Id, userId);

        return CreatedAtAction(nameof(GetOrg), new { orgId = org.Id.Value },
            OrgDetailDto.From(org));
    }

    // GET /v1/orgs/{orgId}
    [HttpGet("{orgId:guid}")]
    public async Task<IActionResult> GetOrg(Guid orgId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var org = await orgs.GetByIdAsync(orgIdValue, ct);
        if (org is null) return NotFound();

        return Ok(OrgDetailDto.From(org));
    }

    // PATCH /v1/orgs/{orgId}
    [HttpPatch("{orgId:guid}")]
    public async Task<IActionResult> UpdateOrg(
        Guid orgId,
        [FromBody] UpdateOrgRequest request,
        [FromServices] IValidator<UpdateOrgRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToModelStateDictionary());

        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        // Owner only
        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        var org = await orgs.GetByIdAsync(orgIdValue, ct);
        if (org is null) return NotFound();

        org.UpdateName(request.Name);
        await orgs.SaveChangesAsync(ct);

        await audit.LogAsync("org.updated",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "organization",
            resourceId: orgId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        return Ok(OrgDetailDto.From(org));
    }

    // DELETE /v1/orgs/{orgId}
    [HttpDelete("{orgId:guid}")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> DeleteOrg(Guid orgId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        var org = await orgs.GetByIdAsync(orgIdValue, ct);
        if (org is null) return NotFound();

        org.SoftDelete();
        await orgs.SaveChangesAsync(ct);

        await audit.LogAsync("org.deleted",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "organization",
            resourceId: orgId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Org soft-deleted. OrgId={OrgId} UserId={UserId}", orgIdValue, userId);

        return NoContent();
    }
}
