using System.Collections;
using System.Runtime.CompilerServices;

namespace Map;

public struct MapKeyEnumerator<TK, TV> : IEnumerator<TK>
{
    private MapEnumerator<TK, TV> _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MapKeyEnumerator(NodeBase? root) => _inner = new MapEnumerator<TK, TV>(root);

    public readonly TK Current => _inner.Current.Key;

    readonly object? IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() => _inner.MoveNext();

    public void Reset() => _inner.Reset();
    
    public readonly void Dispose() {}
}

public struct MapValueEnumerator<TK, TV> : IEnumerator<TV>
{
    private MapEnumerator<TK, TV> _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MapValueEnumerator(NodeBase? root) => _inner = new MapEnumerator<TK, TV>(root);

    public readonly TV Current => _inner.Current.Value;

    readonly object? IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() => _inner.MoveNext();

    public void Reset() => _inner.Reset();

    public readonly void Dispose() {}
}

public readonly struct MapKeyCollection<TK, TV> : IReadOnlyCollection<TK>
{
    private readonly NodeBase? _root;
    private readonly int _count;

    internal MapKeyCollection(NodeBase? root, int count)
    {
        _root = root;
        _count = count;
    }

    public int Count => _count;

    // Returnerar den allokeringsfria structen för foreach
    public MapKeyEnumerator<TK, TV> GetEnumerator() => new(_root);

    IEnumerator<TK> IEnumerable<TK>.GetEnumerator() => new MapKeyEnumerator<TK, TV>(_root);
    IEnumerator IEnumerable.GetEnumerator() => new MapKeyEnumerator<TK, TV>(_root);
}

public readonly struct MapValueCollection<TK, TV> : IReadOnlyCollection<TV>
{
    private readonly NodeBase? _root;
    private readonly int _count;

    internal MapValueCollection(NodeBase? root, int count)
    {
        _root = root;
        _count = count;
    }

    public int Count => _count;

    // Returnerar den allokeringsfria structen för foreach
    public MapValueEnumerator<TK, TV> GetEnumerator() => new(_root);

    IEnumerator<TV> IEnumerable<TV>.GetEnumerator() => new MapValueEnumerator<TK, TV>(_root);
    IEnumerator IEnumerable.GetEnumerator() => new MapValueEnumerator<TK, TV>(_root);
}