namespace Overlock.Shared.Dtos;

/// <summary>
/// Complete document state for initial sync
/// </summary>
public record DocumentStateDto(
    string DocumentId,
    Dictionary<string, object?> Data,
    List<OperationDto> Operations
);
