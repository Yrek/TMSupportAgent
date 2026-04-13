using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

public sealed class FrameworkMappingTests
{
    private static readonly OrgId SomeOrg = OrgId.New();

    [Theory]
    [InlineData("owasp_top10")]
    [InlineData("owasp_api_top10")]
    [InlineData("asvs")]
    [InlineData("cis_controls")]
    [InlineData("ncsc")]
    [InlineData("twelve_factor")]
    public void Create_AllowedFrameworks_Succeed(string framework)
    {
        var act = () => FrameworkMapping.Create(Guid.NewGuid(), SomeOrg, framework, "A01", "direct");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("OWASP")]
    [InlineData("nist")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("owasp")]  // not the canonical normalized value
    public void Create_UnknownFramework_Throws(string framework)
    {
        var act = () => FrameworkMapping.Create(Guid.NewGuid(), SomeOrg, framework, "A01", "direct");
        act.Should().Throw<ArgumentException>().WithMessage("*framework*");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("approximate")]
    public void Create_AllowedMappingTypes_Succeed(string mappingType)
    {
        var act = () => FrameworkMapping.Create(Guid.NewGuid(), SomeOrg, "asvs", "V2.1.1", mappingType);
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_UnknownMappingType_Throws()
    {
        var act = () => FrameworkMapping.Create(Guid.NewGuid(), SomeOrg, "asvs", "V2.1.1", "partial");
        act.Should().Throw<ArgumentException>().WithMessage("*MappingType*");
    }
}
