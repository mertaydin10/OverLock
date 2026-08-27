namespace Overlock.Core;

/// <summary>
/// YATA sequence CRDT (the algorithm behind Yjs). Concurrent inserts at the same
/// index converge to the same document regardless of delivery order.
/// </summary>
public sealed class SequenceDocument
{
    private readonly List<Atom> _content = [];
    private readonly Dictionary<string, long> _version = new(StringComparer.Ordinal);
    private readonly HashSet<ItemId> _known = [];
    private readonly List<Op> _pending = [];
    private readonly List<Op> _log = [];

    public SequenceDocument(string replicaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);
        ReplicaId = replicaId;
    }

    public string ReplicaId { get; }

    public int Length { get; private set; }

    public string Text
    {
        get
        {
            var chars = new System.Text.StringBuilder(Length);
            foreach (var atom in _content)
            {
                if (!atom.IsDeleted)
                {
                    chars.Append(atom.Content);
                }
            }

            return chars.ToString();
        }
    }

    public IReadOnlyList<Op> Log => _log;

    public int PendingCount => _pending.Count;

    public static SequenceDocument FromSnapshot(string replicaId, DocumentSnapshot snapshot)
    {
        var doc = new SequenceDocument(replicaId);
        foreach (var atom in snapshot.Atoms)
        {
            var loaded = new Atom
            {
                Id = atom.Id,
                OriginLeft = atom.OriginLeft,
                OriginRight = atom.OriginRight,
                Content = atom.Content,
                IsDeleted = atom.IsDeleted
            };
            doc._content.Add(loaded);
            doc._known.Add(loaded.Id);
            doc.NoteSeq(loaded.Id);
            if (!loaded.IsDeleted)
            {
                doc.Length++;
            }
        }

        return doc;
    }

    public DocumentSnapshot Export()
    {
        var atoms = _content.Select(a => new AtomSnapshot
        {
            Id = a.Id,
            OriginLeft = a.OriginLeft,
            OriginRight = a.OriginRight,
            Content = a.Content,
            IsDeleted = a.IsDeleted
        }).ToList();

        return new DocumentSnapshot { Atoms = atoms };
    }

    public IReadOnlyList<Op> Insert(int index, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Length);

        var ops = new List<Op>();
        var cursor = index;
        foreach (var grapheme in TextAtoms.Split(text))
        {
            ops.Add(InsertOne(cursor, grapheme));
            cursor++;
        }

        return ops;
    }

    public IReadOnlyList<Op> Delete(int index, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, Length);

        var ops = new List<Op>(count);
        for (var n = 0; n < count; n++)
        {
            var i = FindItemAtPos(index);
            if (i >= _content.Count)
            {
                throw new InvalidOperationException("Delete past end of document.");
            }

            var op = new DeleteOp(_content[i].Id);
            Apply(op);
            ops.Add(op);
        }

        return ops;
    }

    public void Apply(Op op)
    {
        if (!TryApply(op, bufferIfStuck: true))
        {
            return;
        }

        DrainPending();
    }

    public void MergeFrom(SequenceDocument other)
    {
        foreach (var op in other._log)
        {
            Apply(op);
        }
    }

    private InsertOp InsertOne(int index, string grapheme)
    {
        var i = FindItemAtPos(index);
        var last = _version.GetValueOrDefault(ReplicaId, -1);
        var op = new InsertOp(
            new ItemId(ReplicaId, last + 1),
            i > 0 ? _content[i - 1].Id : null,
            i < _content.Count ? _content[i].Id : null,
            grapheme);

        Apply(op);
        return op;
    }

    private bool TryApply(Op op, bool bufferIfStuck)
    {
        switch (op)
        {
            case InsertOp insert:
                if (_known.Contains(insert.Id))
                {
                    return true;
                }

                if (!CanInsert(insert))
                {
                    if (bufferIfStuck)
                    {
                        _pending.Add(insert);
                    }

                    return false;
                }

                IntegrateYata(insert);
                Remember(insert);
                return true;

            case DeleteOp delete:
                if (!TryFind(delete.Target, out var deleteAt))
                {
                    if (bufferIfStuck)
                    {
                        _pending.Add(delete);
                    }

                    return false;
                }

                if (!_content[deleteAt].IsDeleted)
                {
                    _content[deleteAt].IsDeleted = true;
                    Length--;
                    Remember(delete);
                }

                return true;

            case ReviveOp revive:
                if (!TryFind(revive.Target, out var reviveAt))
                {
                    if (bufferIfStuck)
                    {
                        _pending.Add(revive);
                    }

                    return false;
                }

                if (_content[reviveAt].IsDeleted)
                {
                    _content[reviveAt].IsDeleted = false;
                    Length++;
                    Remember(revive);
                }

                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }
    }

    private void DrainPending()
    {
        bool progress;
        do
        {
            progress = false;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (TryApply(_pending[i], bufferIfStuck: false))
                {
                    _pending.RemoveAt(i);
                    progress = true;
                }
            }
        } while (progress);
    }

    private bool CanInsert(InsertOp op)
    {
        var last = _version.GetValueOrDefault(op.Id.Replica, -1);
        if (op.Id.Seq != last + 1)
        {
            return false;
        }

        if (op.OriginLeft is { } left && !_known.Contains(left))
        {
            return false;
        }

        if (op.OriginRight is { } right && !_known.Contains(right))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Yjs/YATA integrate. See josephg/reference-crdts integrateYjs.
    /// originLeft / originRight record the neighbors at insert time; concurrent
    /// inserts in that span are ordered by replica id.
    /// </summary>
    private void IntegrateYata(InsertOp op)
    {
        var left = op.OriginLeft is null ? -1 : IndexOf(op.OriginLeft.Value);
        var destIdx = left + 1;
        var right = op.OriginRight is null ? _content.Count : IndexOf(op.OriginRight.Value);
        var scanning = false;

        for (var i = destIdx; ; i++)
        {
            if (!scanning)
            {
                destIdx = i;
            }

            if (i == _content.Count || i == right)
            {
                break;
            }

            var other = _content[i];
            var oleft = other.OriginLeft is null ? -1 : IndexOf(other.OriginLeft.Value);
            var oright = other.OriginRight is null ? _content.Count : IndexOf(other.OriginRight.Value);

            if (oleft < left)
            {
                break;
            }

            if (oleft == left)
            {
                var replicaCmp = string.CompareOrdinal(op.Id.Replica, other.Id.Replica);
                if (replicaCmp > 0)
                {
                    scanning = false;
                    continue;
                }

                if (oright == right)
                {
                    break;
                }

                scanning = true;
                continue;
            }
        }

        var atom = new Atom
        {
            Id = op.Id,
            OriginLeft = op.OriginLeft,
            OriginRight = op.OriginRight,
            Content = op.Content,
            IsDeleted = false
        };

        _content.Insert(destIdx, atom);
        _known.Add(atom.Id);
        NoteSeq(atom.Id);
        Length++;
    }

    private void Remember(Op op)
    {
        _log.Add(op);
    }

    private void NoteSeq(ItemId id)
    {
        if (!_version.TryGetValue(id.Replica, out var seen) || id.Seq > seen)
        {
            _version[id.Replica] = id.Seq;
        }
    }

    private int FindItemAtPos(int pos)
    {
        var i = 0;
        for (; i < _content.Count; i++)
        {
            if (_content[i].IsDeleted)
            {
                continue;
            }

            if (pos == 0)
            {
                return i;
            }

            pos--;
        }

        if (pos == 0)
        {
            return i;
        }

        throw new ArgumentOutOfRangeException(nameof(pos));
    }

    private int IndexOf(ItemId id)
    {
        if (!TryFind(id, out var index))
        {
            throw new InvalidOperationException($"Atom {id} is not in the document.");
        }

        return index;
    }

    private bool TryFind(ItemId id, out int index)
    {
        for (var i = 0; i < _content.Count; i++)
        {
            if (_content[i].Id == id)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}
