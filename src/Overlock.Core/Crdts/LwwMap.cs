using System.Collections.Concurrent;
using Overlock.Core.Abstractions;
using Overlock.Core.Models;

namespace Overlock.Core.Crdts;

/// <summary>
/// Last-Write-Wins Map CRDT
/// A map where each key has an independent LWW register
/// </summary>
public class LwwMap : ICrdt
{
    private readonly ConcurrentDictionary<string, (object? Value, Timestamp Timestamp)> _map = new();

    public void Set(string key, object? value, Timestamp timestamp)
    {
        _map.AddOrUpdate(
            key,
            _ => (value, timestamp),
            (_, existing) => timestamp > existing.Timestamp ? (value, timestamp) : existing
        );
    }

    public void Delete(string key, Timestamp timestamp)
    {
        _map.AddOrUpdate(
            key,
            _ => (null, timestamp),
            (_, existing) => timestamp > existing.Timestamp ? (null, timestamp) : existing
        );
    }

    public bool TryGetValue(string key, out object? value)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    public IReadOnlyDictionary<string, object?> GetAll()
    {
        return _map
            .Where(kvp => kvp.Value.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
    }

    public void Merge(ICrdt other)
    {
        if (other is not LwwMap otherMap)
            throw new ArgumentException("Cannot merge different CRDT types");

        foreach (var (key, (value, timestamp)) in otherMap._map)
        {
            Set(key, value, timestamp);
        }
    }

    public void Apply(Operation operation)
    {
        switch (operation)
        {
            case SetOperation setOp:
                Set(setOp.Field, setOp.Value, setOp.Timestamp);
                break;
            case DeleteOperation delOp:
                Delete(delOp.Field, delOp.Timestamp);
                break;
        }
    }

    public IEnumerable<Operation> GetOperations()
    {
        foreach (var (key, (value, timestamp)) in _map)
        {
            if (value != null)
            {
                yield return new SetOperation(timestamp, timestamp.ClientId, key, value);
            }
            else
            {
                yield return new DeleteOperation(timestamp, timestamp.ClientId, key);
            }
        }
    }
}
