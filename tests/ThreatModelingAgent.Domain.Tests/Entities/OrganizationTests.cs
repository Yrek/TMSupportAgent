using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;

namespace ThreatModelingAgent.Domain.Tests.Entities;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_ValidInput_Succeeds()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");
        org.Name.Should().Be("Acme Corp");
        org.Slug.Should().Be("acme-corp");
        org.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyName_Throws(string name)
    {
        var act = () => Organization.Create(name, "valid-slug");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("UPPERCASE")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("has spaces")]
    [InlineData("has_underscores")]
    public void Create_InvalidSlug_Throws(string slug)
    {
        var act = () => Organization.Create("Valid Name", slug);
        act.Should().Throw<ArgumentException>().WithMessage("*Slug*");
    }

    [Fact]
    public void Create_SlugTooLong_Throws()
    {
        var longSlug = new string('a', 64);
        var act = () => Organization.Create("Name", longSlug);
        act.Should().Throw<ArgumentException>().WithMessage("*Slug exceeds*");
    }

    [Fact]
    public void SoftDelete_SetsDeletedAt()
    {
        var org = Organization.Create("Acme", "acme");
        org.SoftDelete();
        org.IsDeleted.Should().BeTrue();
        org.DeletedAt.Should().NotBeNull();
    }
}
