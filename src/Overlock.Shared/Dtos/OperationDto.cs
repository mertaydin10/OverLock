namespace Overlock.Shared.Dtos;

/// <summary>
/// Data transfer object for CRDT operations
/// </summary>
public record OperationDto(
    string Type,
    long Counter,
    string ClientId,
    string? Field,
    object? Value
);

/// <summary>
/// Batch of operations for sync
/// </summary>
public record OperationBatchDto(
    string DocumentId,
    List<OperationDto> Operations
);
