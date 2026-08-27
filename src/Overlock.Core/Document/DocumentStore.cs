using System.Collections.Concurrent;

namespace Overlock.Core.Document;

/// <summary>
/// In-memory store for collaborative documents
/// </summary>
public class DocumentStore
{
    private readonly ConcurrentDictionary<string, CollaborativeDocument> _documents = new();

    /// <summary>
    /// Get or create a document
    /// </summary>
    public CollaborativeDocument GetOrCreate(string documentId, string clientId)
    {
        return _documents.GetOrAdd(documentId, _ => new CollaborativeDocument(documentId, clientId));
    }

    /// <summary>
    /// Try to get an existing document
    /// </summary>
    public bool TryGet(string documentId, out CollaborativeDocument? document)
    {
        return _documents.TryGetValue(documentId, out document);
    }

    /// <summary>
    /// Remove a document from the store
    /// </summary>
    public bool Remove(string documentId)
    {
        return _documents.TryRemove(documentId, out _);
    }

    /// <summary>
    /// Get all document IDs
    /// </summary>
    public IEnumerable<string> GetAllDocumentIds()
    {
        return _documents.Keys;
    }
}
