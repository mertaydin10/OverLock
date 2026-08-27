# Overlock

Conflict-free live collaboration for .NET. Two people type in the same pad; concurrent inserts merge. Nobody loses.

The sequence engine is **YATA** (the algorithm behind Yjs). The relay is **ASP.NET Core SignalR**. Presence and carets are ephemeral; the document is a CRDT.

## Run

```bash
dotnet test
dotnet run --project src/Overlock.Server --urls http://localhost:5178
```

Open [http://localhost:5178](http://localhost:5178), copy the room link, paste it in another tab.

## Layout

- `src/Overlock.Core` — sequence CRDT, causal buffer, snapshots
- `src/Overlock.Server` — rooms, SignalR hub, pad UI
- `tests/Overlock.Core.Tests` — convergence, out-of-order delivery, fuzz

Clients join a room, receive a snapshot plus a replica id, then submit insert / delete / revive ops. The server applies the same algorithm and fans the op out. Late ops wait until their left/right origins exist.
