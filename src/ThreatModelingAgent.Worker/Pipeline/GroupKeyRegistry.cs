namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Single source of truth for all allowed candidate/threat group keys.
///
/// A group key encodes the fundamental attack vector of a threat candidate so that
/// synthesis can enforce non-merge constraints.  Adding a new attack-vector category
/// requires a single edit here; both the C# validation logic (AllowedKeys) and the
/// LLM prompt text (BuildPromptSection / BuildNoMergeSection) are derived from this registry.
///
/// NeverMergeWith: group keys that represent DISTINCT attack paths from this one even when
/// they share the same affected elements.  The synthesize stage uses these pairs to enforce
/// Rule 1 (explicit no-merge examples) and to exempt them from Rule 21 (3-key overlap merge).
/// </summary>
public sealed record GroupKeyDefinition(
    string Key,
    string Description,
    string[] NeverMergeWith);

public static class GroupKeyRegistry
{
    public static readonly GroupKeyDefinition[] All =
    [
        new("storage_shared_key",
            "Permanent account-level storage credential (no expiry, bypasses token controls)",
            ["sas_token_access"]),

        new("sas_token_access",
            "Delegated time-limited storage/resource token (SAS, presigned URL)",
            ["storage_shared_key"]),

        new("cicd_platform_permissions",
            "CI/CD identity holds broad cloud platform roles (Contributor, Owner) allowing infra/config modification",
            ["cicd_external_api_token", "supply_chain_ci_cd"]),

        new("cicd_external_api_token",
            "CI/CD secret token for an external service (Cloudflare, DNS, WAF, CDN routing) — distinct from cloud platform roles",
            ["cicd_platform_permissions", "supply_chain_ci_cd"]),

        new("bola_request_parameter",
            "BOLA/IDOR via attacker-controlled request parameter (customerId, tenantId) — application layer only",
            ["no_database_rls", "cross_tenant_isolation_flaw"]),

        new("no_database_rls",
            "Missing row-level security at database layer (application code as sole tenant guard, SQL queries lack tenant filter)",
            ["bola_request_parameter", "cross_tenant_isolation_flaw"]),

        new("break_glass_no_ca",
            "Emergency/break-glass account excluded from Conditional Access or MFA",
            ["standing_operational_access"]),

        new("standing_operational_access",
            "Operational roles (support/analyst/admin) without JIT/PIM — always-on privileged access",
            ["break_glass_no_ca"]),

        new("managed_identity_overpriv",
            "Workload identity with excessive cross-component permissions",
            []),

        new("api_bypass_edge",
            "Application tier (App Service, API, web backend) reachable without passing through edge security layer " +
            "(WAF, CDN, bot protection, rate limiting). Scope: the application server itself, NOT data services. " +
            "Use public_dataplane_endpoint for SQL/Storage/KeyVault/Secrets data services.",
            ["public_dataplane_endpoint"]),

        new("sensitive_data_in_logs",
            "Credentials, tokens, or SAS URLs written to log or telemetry storage",
            []),

        new("cross_tenant_isolation_flaw",
            "Application-code-only tenant isolation (no database-layer enforcement)",
            ["bola_request_parameter", "no_database_rls"]),

        new("supply_chain_ci_cd",
            "CI/CD pipeline compromise via dependency poisoning, artifact tampering, or build-step injection " +
            "(NOT for overprivileged CI/CD identity or stolen external API tokens — use cicd_platform_permissions or cicd_external_api_token for those)",
            ["cicd_platform_permissions", "cicd_external_api_token"]),

        new("storage_prefix_isolation",
            "Storage tenant isolation enforced by folder/prefix only within a shared container (no container or account per tenant)",
            []),

        new("no_bulk_export_approval",
            "Bulk data export or cross-customer data access without approval/four-eyes workflow",
            []),

        new("file_content_attack",
            "Malicious payload embedded in an uploaded file targeting the parser/processor (archive bomb, XXE, formula injection, polyglot)",
            ["ssrf_imds"]),

        new("ssrf_imds",
            "SSRF to cloud instance metadata endpoint (169.254.169.254) via a component with unrestricted outbound internet access",
            ["file_content_attack"]),

        new("xss_token_theft",
            "XSS via stored or reflected content stealing bearer tokens, SAS URLs, or session cookies from the browser",
            []),

        new("federated_claim_manipulation",
            "Malicious federated-tenant administrator issuing tokens with another tenant's claims, " +
            "exploiting platforms that trust without enrollment-record verification",
            []),

        new("data_retention_indefinite",
            "Customer or system data retained without an automated expiry policy, increasing breach impact and privacy/compliance risk over time",
            []),

        new("cdn_cache_leakage",
            "CDN/edge layer caches authenticated responses, generated download URLs, or dynamic content, leaking data across user sessions",
            ["api_bypass_edge"]),

        new("per_tenant_quota_exhaustion",
            "One tenant's unconstrained resource use (uploads, API calls, processing jobs, storage) exhausts shared capacity, degrading availability for all tenants",
            []),

        new("public_dataplane_endpoint",
            "Cloud data service (SQL DB, Key Vault, Blob Storage, or equivalent) reachable over public internet with no private endpoint or strict firewall, " +
            "enabling direct credential-based access that bypasses application-layer controls",
            ["api_bypass_edge"]),
    ];

    /// <summary>
    /// Set of all allowed group key strings, for O(1) validation in C# logic.
    /// </summary>
    public static readonly HashSet<string> AllowedKeys =
        new(All.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the ALLOWED GROUP KEY VALUES prompt section for the analyze stage.
    /// One line per key: "key   — description"
    /// </summary>
    public static string BuildPromptSection()
    {
        var maxLen = All.Max(g => g.Key.Length);
        return string.Join("\n", All.Select(g =>
            $"{g.Key.PadRight(maxLen)}  — {g.Description}"));
    }

    /// <summary>
    /// Builds the no-merge pair summary for Synthesis Rule 1 entries (e) onward.
    /// Only pairs where NeverMergeWith is non-empty AND the pair hasn't been emitted yet.
    /// Returns null if no additional pairs exist beyond the hardcoded (a)-(d) in the prompt.
    /// </summary>
    public static string? BuildNoMergeClusterSection()
    {
        // Deduplicate: emit each unordered pair only once
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();

        // The known never-merge clusters beyond the hardcoded (a)-(d) pairs.
        // (a)-(d) cover: storage_shared_key/sas_token_access, cicd_platform_permissions/cicd_external_api_token,
        // break_glass_no_ca/standing_operational_access — these are hardcoded in Rule 1 text.
        // The registry-derived section adds bola/no-rls/cross-tenant and api_bypass/public_dataplane.
        var registryClusters = new[]
        {
            new[] { "bola_request_parameter", "no_database_rls", "cross_tenant_isolation_flaw" },
            new[] { "api_bypass_edge", "public_dataplane_endpoint" },
            new[] { "cicd_platform_permissions", "cicd_external_api_token", "supply_chain_ci_cd" },
            new[] { "file_content_attack", "ssrf_imds" },
        };

        foreach (var cluster in registryClusters)
        {
            var key = string.Join("|", cluster.OrderBy(k => k));
            if (!emitted.Add(key)) continue;

            var keyList = string.Join(", ", cluster);
            var descs = cluster
                .Select(k => All.FirstOrDefault(d => d.Key == k))
                .Where(d => d is not null)
                .Select(d => $"  - {d!.Key}: {d.Description.Split('.')[0]}")
                .ToArray();

            lines.Add($"Group [{keyList}] — always distinct, NEVER merge across these keys:");
            lines.AddRange(descs);
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
