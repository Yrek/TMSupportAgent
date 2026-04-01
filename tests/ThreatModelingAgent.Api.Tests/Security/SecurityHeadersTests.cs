using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ThreatModelingAgent.Api.Security;

namespace ThreatModelingAgent.Api.Tests.Security;

/// <summary>
/// Tests that SecurityHeadersMiddleware sets all required headers on every response
/// and removes identifying headers (CLAUDE.md §11).
/// </summary>
public sealed class SecurityHeadersTests
{
    private static async Task<HttpContext> RunMiddleware(Action<HttpContext>? configure = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        configure?.Invoke(context);

        var middleware = new SecurityHeadersMiddleware(async ctx =>
        {
            // Simulate a response being written so OnStarting callbacks fire
            await ctx.Response.StartAsync();
        });

        await middleware.InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()")]
    [InlineData("Cache-Control", "no-store")]
    public async Task RequiredHeader_IsPresent(string headerName, string expectedValue)
    {
        var context = await RunMiddleware();
        context.Response.Headers[headerName].ToString().Should().Be(expectedValue);
    }

    [Fact]
    public async Task CspHeader_IsPresent()
    {
        var context = await RunMiddleware();
        context.Response.Headers["Content-Security-Policy"].ToString()
            .Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task HstsHeader_IsPresent()
    {
        var context = await RunMiddleware();
        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("Server")]
    [InlineData("X-Powered-By")]
    [InlineData("X-AspNet-Version")]
    [InlineData("X-AspNetMvc-Version")]
    public async Task IdentifyingHeader_IsRemoved(string headerName)
    {
        var context = await RunMiddleware(ctx =>
            ctx.Response.Headers[headerName] = "something");

        context.Response.Headers.ContainsKey(headerName).Should().BeFalse();
    }
}
