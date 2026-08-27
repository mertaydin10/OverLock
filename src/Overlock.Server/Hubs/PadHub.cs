using Microsoft.AspNetCore.SignalR;
using Overlock.Server.Protocol;
using Overlock.Server.Rooms;

namespace Overlock.Server.Hubs;

public interface IPadClient
{
    Task Snapshot(SnapshotDto snapshot);
    Task Op(OpDto op, string fromReplica);
    Task PeerJoined(PeerDto peer);
    Task PeerLeft(string replicaId);
    Task CaretMoved(string replicaId, int index);
    Task Rejected(string reason);
}

public sealed class PadHub : Hub<IPadClient>
{
    private readonly RoomRegistry _rooms;
    private readonly ILogger<PadHub> _logger;

    public PadHub(RoomRegistry rooms, ILogger<PadHub> logger)
    {
        _rooms = rooms;
        _logger = logger;
    }

    public async Task Join(JoinRequest request)
    {
        PadRoom room;
        try
        {
            room = _rooms.GetOrCreate(request.RoomId);
        }
        catch (ArgumentException ex)
        {
            await Clients.Caller.Rejected(ex.Message);
            return;
        }

        var replicaId = Guid.NewGuid().ToString("N");
        Context.Items["room"] = room.Id;
        Context.Items["replica"] = replicaId;

        await Groups.AddToGroupAsync(Context.ConnectionId, room.Id);

        var (snapshot, peer) = await room.UseAsync(r =>
        {
            var joined = r.AddPeer(Context.ConnectionId, replicaId, request.Name);
            var state = new SnapshotDto(
                r.Document.Export().Atoms.Select(a => new AtomDto(
                    ItemIdDto.From(a.Id),
                    ItemIdDto.From(a.OriginLeft),
                    ItemIdDto.From(a.OriginRight),
                    a.Content,
                    a.IsDeleted)).ToList(),
                replicaId,
                r.Peers.Select(MapPeer).ToList());
            return (state, joined);
        });

        await Clients.Caller.Snapshot(snapshot);
        await Clients.OthersInGroup(room.Id).PeerJoined(MapPeer(peer));
        _logger.LogInformation("{Name} joined room {Room} as {Replica}", peer.Name, room.Id, replicaId);
    }

    public async Task Submit(OpDto dto)
    {
        if (!TryGetRoom(out var roomId) || !TryGetReplica(out var replicaId))
        {
            await Clients.Caller.Rejected("Join a room first.");
            return;
        }

        var room = _rooms.GetOrCreate(roomId);
        var ok = true;
        string? error = null;

        await room.UseAsync(r =>
        {
            try
            {
                r.Document.Apply(dto.ToModel());
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }
        });

        if (!ok)
        {
            await Clients.Caller.Rejected(error ?? "Invalid operation.");
            return;
        }

        await Clients.OthersInGroup(roomId).Op(dto, replicaId);
    }

    public async Task Caret(CaretUpdate update)
    {
        if (!TryGetRoom(out var roomId) || !TryGetReplica(out var replicaId))
        {
            return;
        }

        var room = _rooms.GetOrCreate(roomId);
        await room.UseAsync(r =>
        {
            var peer = r.FindByConnection(Context.ConnectionId);
            if (peer is not null)
            {
                peer.Caret = Math.Max(0, update.Index);
            }
        });

        await Clients.OthersInGroup(roomId).CaretMoved(replicaId, update.Index);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetRoom(out var roomId) && TryGetReplica(out var replicaId))
        {
            var room = _rooms.GetOrCreate(roomId);
            await room.UseAsync(r => r.RemovePeer(Context.ConnectionId));
            await Clients.OthersInGroup(roomId).PeerLeft(replicaId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetRoom(out string roomId)
    {
        roomId = Context.Items["room"] as string ?? "";
        return roomId.Length > 0;
    }

    private bool TryGetReplica(out string replicaId)
    {
        replicaId = Context.Items["replica"] as string ?? "";
        return replicaId.Length > 0;
    }

    private static PeerDto MapPeer(Peer peer) =>
        new(peer.ReplicaId, peer.Name, peer.Color, peer.Caret);
}
