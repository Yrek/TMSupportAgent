using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ThreatModelingAgent.Api.Security;

namespace ThreatModelingAgent.Api.Tests.Security;

/// <summary>
/// Tests for CorrelationIdMiddleware (CLAUDE.md §10.5).
/// Validates that client-supplied IDs are adopted only after validation,
/// and new IDs are generated when absent or invalid.
/// </summary>
public sealed class CorrelationIdTests
{
    [Fact]
    public async Task NoClientId_GeneratesNewCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var correlationId = context.Items["CorrelationId"];
        correlationId.Should().NotBeNull().And.BeOfType<Guid>();
        ((Guid)correlationId!).Should().NotBe(Guid.Empty);

        context.Response.Headers["X-Correlation-Id"].ToString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidClientId_IsAdopted()
    {
        var clientId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Correlation-Id"] = clientId.ToString();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        context.Items["CorrelationId"].Should().Be(clientId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidClientId_GeneratesNewId(string badId)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Correlation-Id"] = badId;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        // Should have generated a new valid Guid, not used the invalid client value
        var correlationId = (Guid)context.Items["CorrelationId"]!;
        correlationId.Should().NotBe(Guid.Empty);
        if (Guid.TryParse(badId, out var parsed))
            correlationId.Should().NotBe(parsed);
    }

    [Fact]
    public async Task OversizedClientId_IsRejected()
    {
        var oversized = new string('a', 65); // exceeds MaxLength of 64
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Correlation-Id"] = oversized;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        // A new ID should have been generated, not the oversized client value
        var correlationId = (Guid)context.Items["CorrelationId"]!;
        correlationId.Should().NotBe(Guid.Empty);
    }
}
