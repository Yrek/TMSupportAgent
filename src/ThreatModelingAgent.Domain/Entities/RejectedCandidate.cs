using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// Records a threat candidate that was generated but rejected before final output.
/// Required by spec §20 to record rejections with reason.
/// Spec: data-model §4.13.
/// </summary>
public class RejectedCandidate
{
    public Guid Id { get; private set; }
    public JobId JobId { get; private set; }
    public OrgId OrgId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? MethodCategory { get; private set; }
    public string RejectionReason { get; private set; } = string.Empty;
    public string? RejectionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private static readonly HashSet<string> AllowedReasons =
        ["insufficient_evidence", "duplicate_root_cause", "out_of_scope", "mitigation_confirmed", "too_speculative"];

    private RejectedCandidate() { }

    public static RejectedCandidate Create(
        JobId jobId,
        OrgId orgId,
        string title,
        string? methodCategory,
        string rejectionReason,
        string? rejectionNote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!AllowedReasons.Contains(rejectionReason))
            throw new ArgumentException($"Unknown rejection reason: {rejectionReason}.", nameof(rejectionReason));

        return new RejectedCandidate
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            OrgId = orgId,
            Title = title,
            MethodCategory = methodCategory,
            RejectionReason = rejectionReason,
            RejectionNote = rejectionNote,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
