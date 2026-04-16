namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Normalizes LLM-produced framework names to the canonical allowed values.
///
/// Used by both SynthesizeStage (framework mapping sub-step) and PipelineDbPersistence
/// (when persisting framework mappings to DB). Must not be duplicated — CLAUDE.md §14.
///
/// Allowed values: stride, vast, pasta, octave, trike, mitre_attack, owasp_cumulus,
/// owasp_cornucopia, owasp_top10, owasp_api_top10, asvs, cis_controls, ncsc, twelve_factor.
/// Returns null for unknown frameworks so the caller can skip them silently.
/// </summary>
internal static class FrameworkNormalizer
{
    public static string? Normalize(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework)) return null;
        return framework.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_") switch
        {
            "stride" => "stride",
            "vast" => "vast",
            "pasta" => "pasta",
            "octave" or "ocatve" => "octave",
            "trike" => "trike",
            "mitre_attack" or "mitre_att&ck" or "mitre_attck" or "mitre" => "mitre_attack",
            "owasp_cumulus" => "owasp_cumulus",
            "owasp_cornucopia" or "owasp_conicopia" => "owasp_cornucopia",
            "owasp" or "owasp_top10" or "owasp_top_10" => "owasp_top10",
            "owasp_api" or "owasp_api_top10" or "owasp_api_security" or "owasp_api_top_10" => "owasp_api_top10",
            "asvs" => "asvs",
            "cis" or "cis_controls" or "cis_benchmarks" => "cis_controls",
            "ncsc" => "ncsc",
            "twelve_factor" or "12_factor" or "12factor" => "twelve_factor",
            _ => null
        };
    }
}
