using FluentAssertions;
using ThreatModelingAgent.Api.Dtos;

namespace ThreatModelingAgent.Api.Tests.Validation;

/// <summary>
/// Validates input validation rules for org creation (CLAUDE.md §6.3 allowlist validation).
/// These tests confirm that malformed, missing, and oversized inputs are rejected at the boundary.
/// </summary>
public sealed class OrgValidatorTests
{
    private readonly CreateOrgRequestValidator _validator = new();

    [Fact]
    public async Task ValidRequest_PassesValidation()
    {
        var result = await _validator.ValidateAsync(new CreateOrgRequest("Acme Corp", "acme-corp"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "valid-slug")]
    [InlineData("  ", "valid-slug")]
    public async Task EmptyName_FailsValidation(string name, string slug)
    {
        var result = await _validator.ValidateAsync(new CreateOrgRequest(name, slug));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("Valid Name", "")]
    [InlineData("Valid Name", "UPPERCASE")]
    [InlineData("Valid Name", "-leading")]
    [InlineData("Valid Name", "trailing-")]
    [InlineData("Valid Name", "has spaces")]
    [InlineData("Valid Name", "has_underscores")]
    public async Task InvalidSlug_FailsValidation(string name, string slug)
    {
        var result = await _validator.ValidateAsync(new CreateOrgRequest(name, slug));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Slug");
    }

    [Fact]
    public async Task NameTooLong_FailsValidation()
    {
        var longName = new string('a', 256);
        var result = await _validator.ValidateAsync(new CreateOrgRequest(longName, "valid-slug"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task SlugTooLong_FailsValidation()
    {
        var longSlug = new string('a', 64); // max is 63
        var result = await _validator.ValidateAsync(new CreateOrgRequest("Valid Name", longSlug));
        result.IsValid.Should().BeFalse();
    }
}
