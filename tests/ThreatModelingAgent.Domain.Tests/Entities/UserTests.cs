using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;

namespace ThreatModelingAgent.Domain.Tests.Entities;

/// <summary>
/// Tests for GDPR erasure behavior — validates PII is nulled and IDs are retained
/// for audit log integrity (data-model spec §8, security-spec §6.2).
/// </summary>
public sealed class UserTests
{
    [Fact]
    public void Create_ValidInput_StoresEmail()
    {
        var user = User.Create("wos_123", "test@example.com", "Test User");
        user.Email.Should().Be("test@example.com");
        user.DisplayName.Should().Be("Test User");
        user.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Erase_NullsPii_RetainsIds()
    {
        var user = User.Create("wos_123", "test@example.com", "Test User");
        var workOsId = user.WorkOsUserId;
        var userId = user.Id;

        user.Erase();

        // PII must be nulled
        user.Email.Should().BeNull();
        user.DisplayName.Should().BeNull();

        // Identifiers retained for audit log FK integrity (GDPR spec §6.2)
        user.WorkOsUserId.Should().Be(workOsId);
        user.Id.Should().Be(userId);
        user.IsDeleted.Should().BeTrue();
        user.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Create_EmailTooLong_Throws()
    {
        var longEmail = new string('a', 250) + "@b.com";
        var act = () => User.Create("wos_123", longEmail);
        act.Should().Throw<ArgumentException>().WithMessage("*Email exceeds*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyWorkOsId_Throws(string workOsId)
    {
        var act = () => User.Create(workOsId, "test@example.com");
        act.Should().Throw<ArgumentException>();
    }
}
