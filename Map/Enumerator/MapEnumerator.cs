/*
 * This was written by AI using an enumerator from another of my projects
 * that was written by almost verbatim copying a tutorial.
 * I don't think I can make any kind of copyright claims at all.
 */

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

public struct MapEnumerator<TK, TV>
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (_depth >= 0)
        {
            ref var frame = ref _stack[_depth];
            var flags = NodeOps.GetFlags(frame.Node.Meta);

            if (flags == NodeFlags.None)
            {
                int capacity = NodeOps.GetCapacity(frame.Node.Meta);
                if (frame.DataIndex < capacity)
                {
                    // Bypass GetLeafDataSpan completely using raw ref arithmetic
                    ref var firstSlot = ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(frame.Node).Data);
                    _current = Unsafe.Add(ref firstSlot, frame.DataIndex++);
                    return true;
                }
            }
            else if (flags == NodeFlags.Internal)
            {
                var dataArray = NodeOps.GetDataArray<TK, TV>(frame.Node);

                if (frame.DataIndex < dataArray.Length)
                {
                    _current = dataArray[frame.DataIndex++];
                    return true;
                }

                // Read the child capacity directly from Meta instead of checking childSpan.Length
                int childCapacity = NodeOps.GetCapacity(frame.Node.Meta);

                if (frame.NodeIndex < childCapacity)
                {
                    // Bypass GetChildSpan completely using raw pointer/ref arithmetic
                    ref var firstChild = ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(frame.Node).Children);
                    var nextChild = Unsafe.Add(ref firstChild, frame.NodeIndex++);

                    _stack[_depth + 1] = new StackFrame
                        { Node = nextChild, DataIndex = 0, NodeIndex = 0 };
                    _depth++;
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
    }