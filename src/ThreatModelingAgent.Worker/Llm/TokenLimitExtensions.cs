namespace ThreatModelingAgent.Worker.Llm;

internal static class TokenLimitExtensions
{
    /// <summary>
    /// Converts a config token ceiling to a nullable MaxTokens value for LlmRequest.
    /// 0 or any negative value means "no explicit limit" — passes null so the client
    /// omits the field and the model uses its own default ceiling.
    /// </summary>
    internal static int? ToMaxTokens(this int value) => value > 0 ? value : null;
}
