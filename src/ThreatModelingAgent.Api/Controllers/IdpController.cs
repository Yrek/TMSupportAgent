using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Org identity provider (SSO) configuration.
///
/// GET    /v1/orgs/{orgId}/idp — read current IDP config (owner only)
/// PUT    /v1/orgs/{orgId}/idp — create or replace IDP config (owner only)
/// DELETE /v1/orgs/{orgId}/idp — remove IDP config (owner only)
///
/// Security invariants:
/// - Owner-only for all operations.
/// - WorkOS connection ID is accepted as-is — WorkOS has already validated the OIDC/SAML
///   endpoint during connection setup in the WorkOS dashboard. We do not fetch arbitrary URLs
///   (CLAUDE.md §9.5 SSRF prevention).
/// - Domain hints are validated for basic format; not used for authentication decisions
///   (used only for login-hint routing).
/// </summary>
[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/idp")]
[EnableRateLimiting("api")]
public sealed class IdpController(
    IMembershipRepository memberships,
    IIdpConfigRepository idpConfigs,
    IAuditLogger audit,
    ILogger<IdpController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedProviderTypes =
        ["okta", "google_workspace", "entra_id", "oidc", "saml"];
    private static readonly System.Text.RegularExpressions.Regex DomainHintRegex =
        new(@"^(?=.{1,253}$)(?!-)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // GET /v1/orgs/{orgId}/idp
    [HttpGet]
    public async Task<IActionResult> GetIdpConfig(Guid orgId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        var config = await idpConfigs.GetByOrgAsync(orgIdValue, ct);
        if (config is null) return NotFound();

        return Ok(MapToDto(config));
    }

    // PUT /v1/orgs/{orgId}/idp — idempotent create-or-replace
    [HttpPut]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ConfigureIdp(
        Guid orgId,
        [FromBody] ConfigureIdpRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        // Input validation — allow-list (CLAUDE.md §6.3)
        if (string.IsNullOrWhiteSpace(request.WorkOsConnectionId) || request.WorkOsConnectionId.Length > 255)
            return BadRequest(new { code = "INVALID_CONNECTION_ID", message = "WorkOsConnectionId is required." });

        if (!AllowedProviderTypes.Contains(request.ProviderType))
            return BadRequest(new
            {
                code = "INVALID_PROVIDER_TYPE",
                message = $"ProviderType must be one of: {string.Join(", ", AllowedProviderTypes)}."
            });

        if (request.DomainHints is not { Length: > 0 })
            return BadRequest(new { code = "DOMAIN_HINTS_REQUIRED", message = "At least one domain hint is required." });

        var normalizedHints = request.DomainHints
            .Select(NormalizeDomainHint)
            .ToArray();

        if (normalizedHints.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { code = "INVALID_DOMAIN_HINT", message = "Domain hints must not be empty." });

        if (normalizedHints.Distinct(StringComparer.Ordinal).Count() != normalizedHints.Length)
            return BadRequest(new { code = "DUPLICATE_DOMAIN_HINT", message = "Domain hints must be unique." });

        foreach (var hint in normalizedHints)
        {
            if (!DomainHintRegex.IsMatch(hint))
                return BadRequest(new { code = "INVALID_DOMAIN_HINT", message = $"Invalid domain hint: '{hint}'." });

            if (await idpConfigs.DomainHintInUseByAnotherOrgAsync(orgIdValue, hint, ct))
                return Conflict(new
                {
                    code = "DOMAIN_HINT_ALREADY_CLAIMED",
                    message = $"Domain hint '{hint}' is already configured by another organization."
                });
        }

        // Replace existing config if present (idempotent PUT)
        var existing = await idpConfigs.GetByOrgAsync(orgIdValue, ct);
        if (existing is not null)
            idpConfigs.Remove(existing);

        var config = OrgIdpConfig.Create(
            orgIdValue,
            request.WorkOsConnectionId,
            request.ProviderType,
            normalizedHints);

        await idpConfigs.AddAsync(config, ct);
        await idpConfigs.SaveChangesAsync(ct);

        await audit.LogAsync("idp.configured",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "org_idp_config",
            resourceId: config.Id,
            details: new { providerType = request.ProviderType },
            ct: ct);

        logger.LogInformation("IDP configured. OrgId={OrgId} Provider={Provider}", orgIdValue, request.ProviderType);

        return Ok(MapToDto(config));
    }

    // DELETE /v1/orgs/{orgId}/idp
    [HttpDelete]
    public async Task<IActionResult> DeleteIdpConfig(Guid orgId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        var config = await idpConfigs.GetByOrgAsync(orgIdValue, ct);
        if (config is null) return NotFound();

        idpConfigs.Remove(config);
        await idpConfigs.SaveChangesAsync(ct);

        await audit.LogAsync("idp.removed",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "org_idp_config",
            resourceId: config.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        return NoContent();
    }

    private static object MapToDto(OrgIdpConfig c) => new
    {
        id = c.Id,
        providerType = c.ProviderType,
        domainHints = c.DomainHints,
        createdAt = c.CreatedAt,
        updatedAt = c.UpdatedAt
        // WorkOsConnectionId intentionally omitted from response — internal identifier
    };

    private static string NormalizeDomainHint(string hint)
        => hint.Trim().TrimEnd('.').ToLowerInvariant();
}
