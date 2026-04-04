using FluentValidation;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;

namespace ThreatModelingAgent.Api.Dtos;

// ── Response DTOs — purpose-specific; no domain model exposed (CLAUDE.md §6.6) ──

public record OrgSummaryDto(Guid Id, string Name, string Slug, string Role)
{
    public static OrgSummaryDto From(Organization org, OrgMemberRole role)
        => new(org.Id.Value, org.Name, org.Slug, role.ToString().ToLower());
}

public record OrgDetailDto(Guid Id, string Name, string Slug, bool HasCustomIdp, DateTimeOffset CreatedAt)
{
    public static OrgDetailDto From(Organization org)
        => new(org.Id.Value, org.Name, org.Slug, false, org.CreatedAt);
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CreateOrgRequest(string Name, string Slug);

public sealed class CreateOrgRequestValidator : AbstractValidator<CreateOrgRequest>
{
    public CreateOrgRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Slug)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(63)
            .Matches(@"^[a-z0-9][a-z0-9\-]*[a-z0-9]$")
            .WithMessage("Slug must be lowercase alphanumeric with hyphens, and must start and end with alphanumeric.");
    }
}

public record UpdateOrgRequest(string Name);

public sealed class UpdateOrgRequestValidator : AbstractValidator<UpdateOrgRequest>
{
    public UpdateOrgRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }
}

public class InviteMemberRequest
{
    public string Email { get; set; } = string.Empty;
}

public class UpdateMemberRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

public class ConfigureIdpRequest
{
    public string WorkOsConnectionId { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string[] DomainHints { get; set; } = [];
}
