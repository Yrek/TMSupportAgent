namespace ThreatModelingAgent.Domain.Enums;

public enum JobStatus
{
    Pending,
    Parsing,
    Normalizing,
    AwaitingReview,
    Classifying,
    Analyzing,
    Synthesizing,
    Complete,
    Failed,
    Partial
}
