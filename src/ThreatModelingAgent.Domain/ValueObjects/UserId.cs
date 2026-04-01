namespace ThreatModelingAgent.Domain.ValueObjects;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public static UserId From(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(value));
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
