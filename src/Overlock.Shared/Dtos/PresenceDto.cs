namespace Overlock.Shared.Dtos;

/// <summary>
/// Client presence information
/// </summary>
public record PresenceDto(
    string ClientId,
    string? UserName,
    Dictionary<string, object?>? Metadata
);

/// <summary>
/// Cursor position for collaborative editing
/// </summary>
public record CursorDto(
    string ClientId,
    string Field,
    int Position
);
