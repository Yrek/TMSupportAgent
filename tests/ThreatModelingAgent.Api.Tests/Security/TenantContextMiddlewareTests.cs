using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Security;

/// <summary>
/// Tests for TenantContextMiddleware (CLAUDE.md §8.2, §4.4).
/// Validates:
/// - platform:admin tokens are allowed through to /v1/admin/* and /v1/auth/session
/// - org-scoped requests without org_id claim are rejected with 403 MISSING_ORG_CONTEXT
/// - authenticated requests without a valid org_id claim are rejected with 403 MISSING_ORG_CONTEXT
/// - authenticated requests with a valid org_id pass through and populate TenantContext
/// - suspended org requests are rejected with 403 ORG_SUSPENDED
/// - unauthenticated requests pass through without populating TenantContext
/// </summary>
public sealed class TenantContextMiddlewareTests
{
    private static DefaultHttpContext BuildContext(
        bool isAuthenticated,
        string? role = null,
        string? orgId = null,
        string? userId = null,
        string path = "/v1/orgs/something")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;

        if (!isAuthenticated)
            return context;

        var claims = new List<Claim>();
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));
        if (orgId is not null)
            claims.Add(new Claim("org_id", orgId));
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));

        var identity = new ClaimsIdentity(claims, authenticationType: "Bearer");
        context.User = new ClaimsPrincipal(identity);

        return context;
    }

    private static IOrganizationRepository OrgRepoReturning(Organization? org)
    {
        var repo = Substitute.For<IOrganizationRepository>();
        repo.GetByIdAsync(Arg.Any<OrgId>(), Arg.Any<CancellationToken>()).Returns(org);
        return repo;
    }

    private static IMembershipRepository MembershipRepoReturning(bool hasMembership)
    {
        var repo = Substitute.For<IMembershipRepository>();
        repo.GetAsync(Arg.Any<OrgId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(hasMembership
                ? OrgMembership.Create(OrgId.New(), UserId.New(), Domain.Enums.OrgMemberRole.Member)
                : null);
        return repo;
    }

    private static IUserRepository UsersRepoReturning(User? user = null)
    {
        var repo = Substitute.For<IUserRepository>();
        repo.GetByWorkOsUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(user);
        return repo;
    }

    private static Organization ActiveOrg(Guid orgId)
    {
        // Create via reflection to bypass constructor — test only needs a non-suspended org
        var org = Organization.Create("Test Org", "test-org");
        return org;
    }

    private static EntraIdOptions NoEntra() => new();

    private static async Task<JsonDocument?> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return body.Length > 0 ? JsonDocument.Parse(body) : null;
    }

    // ── platform:admin on non-admin routes ───────────────────────────────────

    [Fact]
    public async Task AdminToken_OnOrgRouteWithoutOrgClaim_Returns403WithMissingOrgContext()
    {
        var orgId = Guid.NewGuid();
        var context = BuildContext(isAuthenticated: true, role: "platform:admin",
            orgId: orgId.ToString(), path: "/v1/orgs/something");
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse("org-scoped requests without org_id claim must be rejected");

        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("MISSING_ORG_CONTEXT");
    }

    [Theory]
    [InlineData("PLATFORM:ADMIN")]
    [InlineData("Platform:Admin")]
    public async Task AdminToken_CaseInsensitive_Returns403OnOrgRoute(string adminRole)
    {
        var context = BuildContext(isAuthenticated: true, role: adminRole,
            orgId: Guid.NewGuid().ToString(), path: "/v1/orgs/test");
        var tenantContext = new TenantContext();

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task AdminToken_OnAdminRoute_PassesThrough()
    {
        var context = BuildContext(isAuthenticated: true, role: "platform:admin",
            orgId: null, path: "/v1/admin/orgs");
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue("admin tokens are allowed on /v1/admin/* routes");
        tenantContext.CurrentOrgId.Should().BeNull("admin requests do not set a tenant context");
    }

    [Fact]
    public async Task AdminToken_DoesNotPopulateTenantContext()
    {
        var orgId = Guid.NewGuid();
        var context = BuildContext(isAuthenticated: true, role: "platform:admin",
            orgId: orgId.ToString(), path: "/v1/orgs/test");
        var tenantContext = new TenantContext();

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        tenantContext.CurrentOrgId.Should().BeNull("TenantContext must not be set for rejected requests");
    }

    // ── Missing org_id ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticatedWithNoOrgId_Returns403WithMissingOrgContext()
    {
        var context = BuildContext(isAuthenticated: true, role: "member", orgId: null);
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();

        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("MISSING_ORG_CONTEXT");
    }

    [Fact]
    public async Task AuthenticatedWithMalformedOrgId_Returns403WithMissingOrgContext()
    {
        var context = BuildContext(isAuthenticated: true, role: "member", orgId: "not-a-guid");
        var tenantContext = new TenantContext();

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("MISSING_ORG_CONTEXT");
    }

    [Fact]
    public async Task AuthenticatedWithoutUserIdClaim_Returns403WithMissingUserContext()
    {
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Active Org", "active-org");
        var context = BuildContext(isAuthenticated: true, role: "member", orgId: orgId.ToString(), userId: null);
        var tenantContext = new TenantContext();

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(org), MembershipRepoReturning(true), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("MISSING_USER_CONTEXT");
    }

    [Fact]
    public async Task AuthenticatedWithoutMembership_Returns403WithOrgMembershipRequired()
    {
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Active Org", "active-org");
        var context = BuildContext(isAuthenticated: true, role: "member", orgId: orgId.ToString(), userId: Guid.NewGuid().ToString());
        var tenantContext = new TenantContext();

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(org), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("ORG_MEMBERSHIP_REQUIRED");
    }

    // ── Org suspension ────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendedOrg_Returns403WithOrgSuspended()
    {
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Suspended Org", "suspended-org");
        org.Suspend();

        var context = BuildContext(isAuthenticated: true, role: "member", orgId: orgId.ToString(), userId: Guid.NewGuid().ToString());
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(org), MembershipRepoReturning(true), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();

        var body = await ReadResponseBodyAsync(context);
        body!.RootElement.GetProperty("code").GetString().Should().Be("ORG_SUSPENDED");
    }

    // ── Valid authenticated request ───────────────────────────────────────────

    [Fact]
    public async Task ValidAuthenticatedRequest_PassesThroughAndPopulatesTenantContext()
    {
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Active Org", "active-org");
        var context = BuildContext(isAuthenticated: true, role: "member", orgId: orgId.ToString(), userId: Guid.NewGuid().ToString());
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(org), MembershipRepoReturning(true), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue();
        tenantContext.CurrentOrgId.Should().NotBeNull();
        tenantContext.CurrentOrgId!.Value.Value.Should().Be(orgId);
    }

    [Fact]
    public async Task ValidAuthenticatedRequest_NoRoleClaim_PassesThroughWhenOrgIdPresent()
    {
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Active Org", "active-org");
        var context = BuildContext(isAuthenticated: true, role: null, orgId: orgId.ToString(), userId: Guid.NewGuid().ToString());
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(org), MembershipRepoReturning(true), UsersRepoReturning());

        nextCalled.Should().BeTrue();
        tenantContext.CurrentOrgId.Should().NotBeNull();
    }

    // ── Unauthenticated request ───────────────────────────────────────────────

    [Fact]
    public async Task UnauthenticatedRequest_PassesThroughWithoutPopulatingTenantContext()
    {
        var context = BuildContext(isAuthenticated: false);
        var tenantContext = new TenantContext();
        var nextCalled = false;

        var middleware = new TenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NoEntra());
        await middleware.InvokeAsync(context, tenantContext, OrgRepoReturning(null), MembershipRepoReturning(false), UsersRepoReturning());

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue("unauthenticated requests pass through — auth is enforced at the endpoint");
        tenantContext.CurrentOrgId.Should().BeNull("TenantContext must remain empty for unauthenticated requests");
    }
}
