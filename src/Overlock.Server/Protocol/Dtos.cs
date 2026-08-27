using Overlock.Core;

namespace Overlock.Server.Protocol;

public sealed record ItemIdDto(string Replica, long Seq)
{
    public ItemId ToModel() => new(Replica, Seq);

    public static ItemIdDto From(ItemId id) => new(id.Replica, id.Seq);

    public static ItemIdDto? From(ItemId? id) => id is null ? null : From(id.Value);
}

public sealed record AtomDto(
    ItemIdDto Id,
    ItemIdDto? OriginLeft,
    ItemIdDto? OriginRight,
    string Content,
    bool IsDeleted);

public sealed record SnapshotDto(IReadOnlyList<AtomDto> Atoms, string ReplicaId, IReadOnlyList<PeerDto> Peers);

public sealed record OpDto(
    string Kind,
    ItemIdDto? Id,
    ItemIdDto? OriginLeft,
    ItemIdDto? OriginRight,
    string? Content,
    ItemIdDto? Target)
{
    public static OpDto From(Op op) => op switch
    {
        InsertOp insert => new OpDto("insert", ItemIdDto.From(insert.Id), ItemIdDto.From(insert.OriginLeft), ItemIdDto.From(insert.OriginRight), insert.Content, null),
        DeleteOp delete => new OpDto("delete", null, null, null, null, ItemIdDto.From(delete.Target)),
        ReviveOp revive => new OpDto("revive", null, null, null, null, ItemIdDto.From(revive.Target)),
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    public Op ToModel() => Kind switch
    {
        "insert" when Id is not null && Content is not null =>
            new InsertOp(Id.ToModel(), OriginLeft?.ToModel(), OriginRight?.ToModel(), Content),
        "delete" when Target is not null => new DeleteOp(Target.ToModel()),
        "revive" when Target is not null => new ReviveOp(Target.ToModel()),
        _ => throw new ArgumentException($"Unknown op '{Kind}'.")
    };
}

public sealed record PeerDto(string ReplicaId, string Name, string Color, int Caret);

public sealed record JoinRequest(string RoomId, string Name);

public sealed record CaretUpdate(int Index);
