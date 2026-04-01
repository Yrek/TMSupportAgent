namespace ThreatModelingAgent.Domain.ValueObjects;

public readonly record struct OrgId(Guid Value)
{
    public static OrgId New() => new(Guid.NewGuid());

    public static OrgId From(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("OrgId cannot be empty.", nameof(value));
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
