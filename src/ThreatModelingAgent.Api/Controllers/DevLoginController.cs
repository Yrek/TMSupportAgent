using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using DomainUser = ThreatModelingAgent.Domain.Entities.User;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Local development login endpoint. Only functional when DevAuth:Enabled=true.
/// Returns a locally-signed JWT carrying internal user and org GUIDs so the rest of
/// the auth pipeline (TenantContextMiddleware) works identically to the test path.
///
/// Security: this controller MUST NOT ship in a Production build with DevAuth active.
/// Program.cs enforces this at startup: DevAuth:Enabled=true + IsProduction() → hard fail.
/// </summary>
[ApiController]
[Route("v1/auth/dev-login")]
[EnableRateLimiting("strict")]
public sealed class DevLoginController(
    DevAuthSigningKeyHolder signingKey,
    IUserRepository users,
    IOrganizationRepository orgs,
    IMembershipRepository memberships,
    ILogger<DevLoginController> logger) : ControllerBase
{
    private const string DevOrgSlug = "dev-org";
    private const string DevOrgName = "Dev Org";
    // WorkOS user id field is reused with a prefix to avoid clashing with real WorkOS ids.
    private const string DevUserIdPrefix = "dev:";

    public sealed record DevLoginRequest(string Email);

    [HttpPost]
    public async Task<IActionResult> Login([FromBody] DevLoginRequest request, CancellationToken ct)
    {
        if (!signingKey.IsEnabled)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 255 ||
            !request.Email.Contains('@'))
        {
            return BadRequest(new { code = "INVALID_EMAIL", message = "A valid email address is required." });
        }

        // Normalise email for stable lookup
        var email = request.Email.Trim().ToLowerInvariant();

        // ── Find or create user ──────────────────────────────────────────────
        var devWorkOsId = DevUserIdPrefix + email;
        var user = await users.GetByWorkOsUserIdAsync(devWorkOsId, ct);
        if (user is null)
        {
            user = DomainUser.Create(devWorkOsId, email, email.Split('@')[0]);
            await users.AddAsync(user, ct);
            await users.SaveChangesAsync(ct);
            logger.LogInformation("DevAuth: created user {UserId} for {Email}", user.Id.Value, email);
        }

        // ── Find or create dev org ───────────────────────────────────────────
        var org = await orgs.GetBySlugAsync(DevOrgSlug, ct);
        if (org is null)
        {
            org = Organization.Create(DevOrgName, DevOrgSlug);
            await orgs.AddAsync(org, ct);
            await orgs.SaveChangesAsync(ct);
            logger.LogInformation("DevAuth: created dev org {OrgId}", org.Id.Value);
        }

        // ── Ensure membership ────────────────────────────────────────────────
        var membership = await memberships.GetAsync(org.Id, user.Id, ct);
        if (membership is null)
        {
            membership = OrgMembership.Create(org.Id, user.Id, OrgMemberRole.Owner);
            await memberships.AddAsync(membership, ct);
            await memberships.SaveChangesAsync(ct);
            logger.LogInformation("DevAuth: added user {UserId} as owner of org {OrgId}", user.Id.Value, org.Id.Value);
        }

        // ── Issue local JWT with internal GUIDs ─────────────────────────────
        // sub and org_id are internal GUIDs → TenantContextMiddleware uses the
        // existing test-path (Guid.TryParse) to resolve them without WorkOS.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new("org_id", org.Id.Value.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: DevAuthConstants.Issuer,
            audience: DevAuthConstants.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(DevAuthConstants.TokenLifetimeHours),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { accessToken = tokenString, userId = user.Id.Value, orgId = org.Id.Value });
    }
}
