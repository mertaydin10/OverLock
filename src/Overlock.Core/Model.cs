namespace Overlock.Core;

/// <summary>Unique id of one inserted atom: replica + per-replica sequence.</summary>
public readonly record struct ItemId(string Replica, long Seq)
{
    public override string ToString() => $"{Replica}:{Seq}";
}

/// <summary>One atom in document order. Deleted atoms stay as tombstones so later ops can still point at them.</summary>
public sealed class Atom
{
    public required ItemId Id { get; init; }
    public required ItemId? OriginLeft { get; init; }
    public required ItemId? OriginRight { get; init; }
    public required string Content { get; init; }
    public bool IsDeleted { get; set; }
}

public abstract record Op;

public sealed record InsertOp(
    ItemId Id,
    ItemId? OriginLeft,
    ItemId? OriginRight,
    string Content) : Op;

public sealed record DeleteOp(ItemId Target) : Op;

public sealed record ReviveOp(ItemId Target) : Op;

public sealed class DocumentSnapshot
{
    public required IReadOnlyList<AtomSnapshot> Atoms { get; init; }
}

public sealed class AtomSnapshot
{
    public required ItemId Id { get; init; }
    public required ItemId? OriginLeft { get; init; }
    public required ItemId? OriginRight { get; init; }
    public required string Content { get; init; }
    public required bool IsDeleted { get; init; }
}
