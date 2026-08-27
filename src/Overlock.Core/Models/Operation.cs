namespace Overlock.Core.Models;

/// <summary>
/// Base operation type for CRDT operations
/// </summary>
public abstract record Operation(Timestamp Timestamp, string ClientId);

/// <summary>
/// Set operation for a field
/// </summary>
public record SetOperation(
    Timestamp Timestamp, 
    string ClientId, 
    string Field, 
    object? Value
) : Operation(Timestamp, ClientId);

/// <summary>
/// Delete operation for a field
/// </summary>
public record DeleteOperation(
    Timestamp Timestamp,
    string ClientId,
    string Field
) : Operation(Timestamp, ClientId);
