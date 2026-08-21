using System.Collections;
using System.Runtime.CompilerServices;

namespace Map;

internal struct StackFrame
{
    public NodeBase Node;
    public int DataIndex;
    public int NodeIndex;
}

[InlineArray(8)]
internal struct EnumeratorStack
{
    private StackFrame _element0;
}

public struct MapEnumerator<TK, TV> : IEnumerator<KeyValuePair<TK, TV>>
{
    private EnumeratorStack _stack;
    private int _depth;
    private DataSlot<TK, TV> _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MapEnumerator(NodeBase? root)
    {
        _current = default;
        if (root == null)
        {
            _depth = -1;
        }
        else
        {
            _depth = 0;
            _stack[0] = new StackFrame { Node = root, DataIndex = 0, NodeIndex = 0 };
        }
    }

    public readonly KeyValuePair<TK, TV> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_current.Key, _current.Value);
    }

    /// <summary>
    ///     The same entry as a <c>(key, value)</c> tuple.
    ///
    ///     The static interface hands entries out as tuples, so without this every call site
    ///     would build a <see cref="KeyValuePair{TK,TV}" /> only to take it apart again. Both are
    ///     reads of the two fields the enumerator already holds; this is the one that skips the
    ///     intermediate.
    /// </summary>
    public readonly (TK, TV) CurrentEntry
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_current.Key, _current.Value);
    }

    readonly object IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (_depth >= 0)
        {
            ref var frame = ref _stack[_depth];
            var flags = NodeOps.GetFlags(frame.Node.Meta);

            if (flags == NodeFlags.None)
            {
                var span = NodeOps.GetLeafDataSpan<TK, TV>(frame.Node);
                if (frame.DataIndex < span.Length)
                {
                    _current = span[frame.DataIndex++];
                    return true;
                }
            }
            else if (flags == NodeFlags.Internal)
            {
                var dataArray = NodeOps.GetDataArray<TK, TV>(frame.Node);

                if (dataArray != null && frame.DataIndex < dataArray.Length)
                {
                    _current = dataArray[frame.DataIndex++];
                    return true;
                }

                var childSpan = NodeOps.GetChildSpan<TK, TV>(frame.Node);
                if (frame.NodeIndex < childSpan.Length)
                {
                    var nextChild = childSpan[frame.NodeIndex++];
                    _depth++;
                    _stack[_depth] = new StackFrame { Node = nextChild, DataIndex = 0, NodeIndex = 0 };
                    continue;
                }
            }
            else // CollisionNode
            {
                var colNode = Unsafe.As<CollisionNode<TK, TV>>(frame.Node);
                if (frame.DataIndex < colNode.Slots.Length)
                {
                    _current = colNode.Slots[frame.DataIndex++];
                    return true;
                }
            }

            _depth--; // Pop the stack when the current node is exhausted
        }

        return false;
    }

    public readonly void Dispose()
    {
    }

    public void Reset()
    {
        throw new NotSupportedException();
    }
}