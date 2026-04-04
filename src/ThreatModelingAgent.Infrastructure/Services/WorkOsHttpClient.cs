using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Infrastructure.Services;

/// <summary>
/// HTTP implementation of <see cref="IWorkOsClient"/> backed by the WorkOS Management API.
///
/// SECURITY:
/// - API key is sourced from configuration, never from user input or hardcoded (CLAUDE.md §10.1).
/// - Outbound HTTP uses a managed HttpClient with explicit timeouts (CLAUDE.md §9.8).
/// - We never log the API key or any response tokens (CLAUDE.md §10.4).
/// - Invitation emails go from WorkOS directly to the user — we never hold them.
/// </summary>
internal sealed class WorkOsHttpClient : IWorkOsClient
{
    private const string BaseUrl = "https://api.workos.com";

    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public WorkOsHttpClient(IHttpClientFactory factory, IConfiguration configuration)
    {
        var apiKey = configuration["WorkOS:ApiKey"]
            ?? throw new InvalidOperationException("WorkOS:ApiKey is required.");

        _http = factory.CreateClient("WorkOS");
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> SendInvitationAsync(
        string email, string workOsOrgId, CancellationToken ct = default)
    {
        var body = new { email, organization_id = workOsOrgId };

        using var response = await _http.PostAsJsonAsync(
            "/user_management/invitations", body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            // Do not log detail — may contain email address (CLAUDE.md §10.4)
            throw new WorkOsException(
                $"WorkOS invitation failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new WorkOsException("WorkOS invitation response missing id.");
    }

    public async Task DeleteUserAsync(string workOsUserId, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(
            $"/user_management/users/{Uri.EscapeDataString(workOsUserId)}", ct);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new WorkOsException(
                $"WorkOS user deletion failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }
    }
}
