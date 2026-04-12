namespace Agent.SDK.Console;

/// <summary>
/// Fixed-capacity circular buffer. Drops oldest entries when full.
/// Single-threaded consumer only — no locking.
/// </summary>
public sealed class RingBuffer<T>(int capacity)
{
    private readonly T[] _items = new T[capacity];
    private int _head;
    private int _count;

    public int Count => _count;
    public int Capacity => capacity;

    public void Add(T item)
    {
        _items[_head] = item;
        _head = (_head + 1) % capacity;

        if (_count < capacity)
        {
            _count++;
        }
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        Array.Clear(_items);
    }

    /// <summary>Returns items in insertion order (oldest first).</summary>
    public IReadOnlyList<T> ToList()
    {
        var result = new T[_count];
        var start = (_head - _count + capacity) % capacity;

        for (var i = 0; i < _count; i++)
        {
            result[i] = _items[(start + i) % capacity];
        }

        return result;
    }

    /// <summary>Returns the most recently added item, or default if empty.</summary>
    public T? Newest => _count > 0 ? _items[(_head - 1 + capacity) % capacity] : default;
}
