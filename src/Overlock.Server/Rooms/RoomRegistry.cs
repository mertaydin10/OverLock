using Overlock.Core;

namespace Overlock.Server.Rooms;

public sealed class Peer
{
    public required string ConnectionId { get; init; }
    public required string ReplicaId { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public int Caret { get; set; }
}

public sealed class PadRoom
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Peer> _peers = new(StringComparer.Ordinal);

    public PadRoom(string id)
    {
        Id = id;
        Document = new SequenceDocument("server");
    }

    public string Id { get; }

    public SequenceDocument Document { get; }

    public async Task<T> UseAsync<T>(Func<PadRoom, T> action, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return action(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UseAsync(Action<PadRoom> action, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            action(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Peer AddPeer(string connectionId, string replicaId, string name)
    {
        var peer = new Peer
        {
            ConnectionId = connectionId,
            ReplicaId = replicaId,
            Name = string.IsNullOrWhiteSpace(name) ? "Guest" : name.Trim(),
            Color = ColorFor(replicaId),
            Caret = 0
        };
        _peers[connectionId] = peer;
        return peer;
    }

    public Peer? RemovePeer(string connectionId)
    {
        _peers.TryGetValue(connectionId, out var peer);
        _peers.Remove(connectionId);
        return peer;
    }

    public Peer? FindByConnection(string connectionId) =>
        _peers.TryGetValue(connectionId, out var peer) ? peer : null;

    public IReadOnlyList<Peer> Peers => _peers.Values.ToList();

    private static string ColorFor(string replicaId)
    {
        ReadOnlySpan<string> palette =
        [
            "#e8b86d",
            "#7eb8a8",
            "#d47b7b",
            "#8aa7d4",
            "#c4a0d8",
            "#d4c56b"
        ];
        var hash = 0;
        foreach (var ch in replicaId)
        {
            hash = (hash * 31) + ch;
        }

        return palette[Math.Abs(hash) % palette.Length];
    }
}

public sealed class RoomRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PadRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public PadRoom GetOrCreate(string roomId)
    {
        var id = Normalize(roomId);
        return _rooms.GetOrAdd(id, static key => new PadRoom(key));
    }

    public static string Normalize(string roomId)
    {
        var trimmed = roomId.Trim();
        if (trimmed.Length is < 2 or > 32)
        {
            throw new ArgumentException("Room id must be 2–32 characters.");
        }

        foreach (var ch in trimmed)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                throw new ArgumentException("Room id may contain letters, digits, '-' and '_'.");
            }
        }

        return trimmed.ToLowerInvariant();
    }
}
