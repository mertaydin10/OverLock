using Microsoft.AspNetCore.SignalR;
using Overlock.Core.Document;
using Overlock.Core.Models;
using Overlock.Shared.Dtos;

namespace Overlock.Server.Hubs;

/// <summary>
/// SignalR hub for real-time collaboration
/// </summary>
public class CollaborationHub : Hub
{
    private readonly DocumentStore _documentStore;
    private readonly ILogger<CollaborationHub> _logger;

    public CollaborationHub(DocumentStore documentStore, ILogger<CollaborationHub> logger)
    {
        _documentStore = documentStore;
        _logger = logger;
    }

    /// <summary>
    /// Join a document for collaboration
    /// </summary>
    public async Task JoinDocument(string documentId, string clientId, PresenceDto? presence = null)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, documentId);
        
        var document = _documentStore.GetOrCreate(documentId, clientId);
        
        _logger.LogInformation("Client {ClientId} joined document {DocumentId}", clientId, documentId);

        // Send current document state to the joining client
        var state = new DocumentStateDto(
            documentId,
            document.GetAll().ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            document.GetOperations().Select(MapToDto).ToList()
        );
        
        await Clients.Caller.SendAsync("DocumentState", state);
        
        // Notify others
        await Clients.OthersInGroup(documentId).SendAsync("ClientJoined", clientId, presence);
    }

    /// <summary>
    /// Leave a document
    /// </summary>
    public async Task LeaveDocument(string documentId, string clientId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, documentId);
        await Clients.OthersInGroup(documentId).SendAsync("ClientLeft", clientId);
        
        _logger.LogInformation("Client {ClientId} left document {DocumentId}", clientId, documentId);
    }

    /// <summary>
    /// Broadcast operations to other clients
    /// </summary>
    public async Task BroadcastOperations(OperationBatchDto batch)
    {
        if (!_documentStore.TryGet(batch.DocumentId, out var document))
        {
            _logger.LogWarning("Document {DocumentId} not found", batch.DocumentId);
            return;
        }

        var operations = batch.Operations.Select(MapFromDto).ToList();
        document.ApplyOperations(operations);

        // Broadcast to all other clients in the document
        await Clients.OthersInGroup(batch.DocumentId).SendAsync("OperationsReceived", batch);
        
        _logger.LogDebug("Broadcasted {Count} operations for document {DocumentId}", 
            batch.Operations.Count, batch.DocumentId);
    }

    /// <summary>
    /// Update cursor position
    /// </summary>
    public async Task UpdateCursor(string documentId, CursorDto cursor)
    {
        await Clients.OthersInGroup(documentId).SendAsync("CursorUpdated", cursor);
    }

    /// <summary>
    /// Update presence information
    /// </summary>
    public async Task UpdatePresence(string documentId, PresenceDto presence)
    {
        await Clients.OthersInGroup(documentId).SendAsync("PresenceUpdated", presence);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static OperationDto MapToDto(Operation operation)
    {
        return operation switch
        {
            SetOperation setOp => new OperationDto("set", setOp.Timestamp.Counter, setOp.ClientId, setOp.Field, setOp.Value),
            DeleteOperation delOp => new OperationDto("delete", delOp.Timestamp.Counter, delOp.ClientId, delOp.Field, null),
            _ => throw new ArgumentException($"Unknown operation type: {operation.GetType()}")
        };
    }

    private static Operation MapFromDto(OperationDto dto)
    {
        var timestamp = new Timestamp(dto.Counter, dto.ClientId);
        
        return dto.Type.ToLower() switch
        {
            "set" => new SetOperation(timestamp, dto.ClientId, dto.Field ?? string.Empty, dto.Value),
            "delete" => new DeleteOperation(timestamp, dto.ClientId, dto.Field ?? string.Empty),
            _ => throw new ArgumentException($"Unknown operation type: {dto.Type}")
        };
    }
}
