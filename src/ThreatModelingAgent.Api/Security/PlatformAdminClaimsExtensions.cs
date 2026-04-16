using System.Security.Claims;

namespace ThreatModelingAgent.Api.Security;

public static class PlatformAdminClaimsExtensions
{
    public static bool IsPlatformAdmin(this ClaimsPrincipal user)
    {
        var roleValues = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(user.FindAll("role").Select(c => c.Value));

        if (roleValues.Any(v =>
            string.Equals(v, "platform:admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "platform_admin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var permissionValues = user.FindAll("permissions").Select(c => c.Value)
            .Concat(user.FindAll("permission").Select(c => c.Value))
            .Concat(user.FindAll("scp").Select(c => c.Value))
            .Concat(user.FindAll("scope").Select(c => c.Value));

        foreach (var raw in permissionValues)
        {
            var parts = raw.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(p => string.Equals(p, "platform.admin", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
