using Overlock.Core.Models;

namespace Overlock.Core.Abstractions;

/// <summary>
/// Base interface for Conflict-free Replicated Data Types
/// </summary>
public interface ICrdt
{
    /// <summary>
    /// Merge another CRDT state into this one
    /// </summary>
    void Merge(ICrdt other);

    /// <summary>
    /// Apply an operation to this CRDT
    /// </summary>
    void Apply(Operation operation);

    /// <summary>
    /// Get all operations in this CRDT (for synchronization)
    /// </summary>
    IEnumerable<Operation> GetOperations();
}
