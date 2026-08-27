using System.Collections.Concurrent;
using Overlock.Core.Crdts;
using Overlock.Core.Models;

namespace Overlock.Core.Document;

/// <summary>
/// Represents a collaborative document that can be edited by multiple clients
/// </summary>
public class CollaborativeDocument
{
    private readonly LwwMap _data = new();
    private readonly ConcurrentDictionary<string, long> _clientCounters = new();
    private readonly string _clientId;

    public string DocumentId { get; }
    public string ClientId => _clientId;

    public CollaborativeDocument(string documentId, string clientId)
    {
        DocumentId = documentId;
        _clientId = clientId;
    }

    /// <summary>
    /// Set a field value in the document
    /// </summary>
    public void Set(string field, object? value)
    {
        var timestamp = GetNextTimestamp();
        _data.Set(field, value, timestamp);
    }

    /// <summary>
    /// Delete a field from the document
    /// </summary>
    public void Delete(string field)
    {
        var timestamp = GetNextTimestamp();
        _data.Delete(field, timestamp);
    }

    /// <summary>
    /// Get a field value from the document
    /// </summary>
    public bool TryGetValue(string field, out object? value)
    {
        return _data.TryGetValue(field, out value);
    }

    /// <summary>
    /// Get all fields in the document
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        return _data.GetAll();
    }

    /// <summary>
    /// Apply operations from another peer
    /// </summary>
    public void ApplyOperations(IEnumerable<Operation> operations)
    {
        foreach (var operation in operations)
        {
            _data.Apply(operation);
            
            // Update our knowledge of other clients' counters
            _clientCounters.AddOrUpdate(
                operation.ClientId,
                _ => operation.Timestamp.Counter,
                (_, existing) => Math.Max(existing, operation.Timestamp.Counter)
            );
        }
    }

    /// <summary>
    /// Get all operations for synchronization
    /// </summary>
    public IEnumerable<Operation> GetOperations()
    {
        return _data.GetOperations();
    }

    /// <summary>
    /// Get operations that a specific client hasn't seen yet
    /// </summary>
    public IEnumerable<Operation> GetOperationsSince(string clientId, long counter)
    {
        return _data.GetOperations()
            .Where(op => op.ClientId != clientId || op.Timestamp.Counter > counter);
    }

    private Timestamp GetNextTimestamp()
    {
        var counter = _clientCounters.AddOrUpdate(
            _clientId,
            _ => 1,
            (_, existing) => existing + 1
        );
        return new Timestamp(counter, _clientId);
    }
}
