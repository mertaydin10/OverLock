namespace Overlock.Core.Models;

/// <summary>
/// Lamport timestamp with client ID for causal ordering
/// </summary>
public readonly record struct Timestamp(long Counter, string ClientId) : IComparable<Timestamp>
{
    public int CompareTo(Timestamp other)
    {
        var counterComparison = Counter.CompareTo(other.Counter);
        if (counterComparison != 0)
            return counterComparison;

        // Break ties with client ID for deterministic ordering
        return string.Compare(ClientId, other.ClientId, StringComparison.Ordinal);
    }

    public static bool operator >(Timestamp left, Timestamp right) => left.CompareTo(right) > 0;
    public static bool operator <(Timestamp left, Timestamp right) => left.CompareTo(right) < 0;
    public static bool operator >=(Timestamp left, Timestamp right) => left.CompareTo(right) >= 0;
    public static bool operator <=(Timestamp left, Timestamp right) => left.CompareTo(right) <= 0;
}
