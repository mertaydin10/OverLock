using Overlock.Core.Abstractions;
using Overlock.Core.Models;

namespace Overlock.Core.Crdts;

/// <summary>
/// Last-Write-Wins Register CRDT
/// Stores a single value with timestamp, last write wins on conflicts
/// </summary>
/// <typeparam name="T">Type of value stored</typeparam>
public class LwwRegister<T> : ICrdt
{
    private T? _value;
    private Timestamp _timestamp;

    public LwwRegister()
    {
        _timestamp = new Timestamp(0, string.Empty);
    }

    public T? Value => _value;
    public Timestamp Timestamp => _timestamp;

    public void Set(T? value, Timestamp timestamp)
    {
        if (timestamp > _timestamp)
        {
            _value = value;
            _timestamp = timestamp;
        }
    }

    public void Merge(ICrdt other)
    {
        if (other is not LwwRegister<T> otherRegister)
            throw new ArgumentException($"Cannot merge {other.GetType()} with LwwRegister<{typeof(T)}>");

        if (otherRegister._timestamp > _timestamp)
        {
            _value = otherRegister._value;
            _timestamp = otherRegister._timestamp;
        }
    }

    public void Apply(Operation operation)
    {
        if (operation is SetOperation setOp && setOp.Value is T typedValue)
        {
            Set(typedValue, operation.Timestamp);
        }
    }

    public IEnumerable<Operation> GetOperations()
    {
        if (_timestamp.Counter > 0)
        {
            yield return new SetOperation(_timestamp, _timestamp.ClientId, string.Empty, _value);
        }
    }
}
