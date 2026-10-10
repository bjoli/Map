/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Linus Björnstam
 *
 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Map;

/// <summary>
///     A position in a walk of a <see cref="Map{TK, TV}" />.
///
///     A struct, and <see cref="Next" /> answers the advanced cursor, so a walk allocates
///     nothing. That matters for the short walks a loop starts many times.
///
///     A node keeps its entries in one run: a leaf in the node itself, an internal node and a
///     collision node in an array. The cursor holds the object that holds the run, and the byte
///     positions of the current entry and of the end of the run in that object. A step in a run
///     is an add and a compare, and the read of an entry does not ask which kind of node holds
///     it.
///
///     At the end of a run, <see cref="Advance" /> finds the next node with entries, out of line.
///     The trie has no parent links, so the cursor keeps the path to its node: the child index
///     at each level, packed in a <c>ulong</c>. <see cref="Advance" /> follows the path from the
///     root again. The trie is a few levels deep, and a node holds several entries, so this costs
///     little for each entry.
///
///     The entries come in the order <see cref="MapEnumerator{TK, TV}" /> gives: the entries of
///     a node, then its children in order.
/// </summary>
public readonly struct MapCursor<TK, TV>
{
    // The depth of the node is in the low bits of the path, and the child index at each
    // level above it in 5 bits per level.
    private const int DepthBits = 4;
    private const ulong DepthMask = (1UL << DepthBits) - 1;
    private const int IndexBits = 5;
    private const int MaxDepth = 11;

    // The path of a run with no run after it. Not a path: its depth is more than MaxDepth.
    private const ulong LastRun = ulong.MaxValue;

    private readonly object? _holder;
    private readonly nint _pos;
    private readonly nint _end;
    private readonly NodeBase? _root;
    private readonly ulong _path;

    private MapCursor(object holder, nint pos, nint end, NodeBase root, ulong path)
    {
        _holder = holder;
        _pos = pos;
        _end = end;
        _root = root;
        _path = path;
    }

    /// <summary>
    ///     The cursor on the first entry. Inlined where a walk starts, so the walk of a map
    ///     whose root is a leaf makes no call. Most small maps are one leaf.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static MapCursor<TK, TV> Start(NodeBase? root)
    {
        if (root == null) return default;
        if (NodeOps.GetFlags(root.Meta) == NodeFlags.None)
        {
            var pos = LeafRunOffset(root);
            return new MapCursor<TK, TV>(root, pos, pos + NodeOps.GetCapacity(root.Meta) * SlotSize, root, LastRun);
        }

        return StartAtInternal(root);
    }

    public bool Done
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pos >= _end;
    }

    /// <summary>The entry the cursor is on. Only when not <see cref="Done" />.</summary>
    public (TK, TV) Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref var slot = ref Unsafe.As<byte, DataSlot<TK, TV>>(
                ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(_holder!).Data, _pos));
            return (slot.Key, slot.Value);
        }
    }

    // The slow path takes and answers values. A method called on the cursor itself would
    // take its address, and the JIT then keeps the whole cursor in memory rather than in
    // registers.
    /// <summary>The cursor on the next entry. Only when not <see cref="Done" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MapCursor<TK, TV> Next()
    {
        var pos = _pos + SlotSize;
        if (pos < _end || _path == LastRun) return new MapCursor<TK, TV>(_holder!, pos, _end, _root!, _path);
        return Advance(_root!, _path);
    }

    private static nint SlotSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.SizeOf<DataSlot<TK, TV>>();
    }

    // The byte position of the first entry of a leaf, from the first field of the node. The
    // same for all leaf sizes, as all keep their entries in the field `Data`.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint LeafRunOffset(NodeBase leaf) =>
        Unsafe.ByteOffset(
            ref Unsafe.As<RawData>(leaf).Data,
            ref Unsafe.As<LeafSlot1<TK, TV>, byte>(ref Unsafe.As<Node1<TK, TV>>(leaf).Data));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint ArrayRunOffset(DataSlot<TK, TV>[] array) =>
        Unsafe.ByteOffset(
            ref Unsafe.As<RawData>(array).Data,
            ref Unsafe.As<DataSlot<TK, TV>, byte>(ref MemoryMarshal.GetArrayDataReference(array)));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MapCursor<TK, TV> StartAtInternal(NodeBase root) =>
        TryEnter(root, root, 0, out var cursor) ? cursor : Advance(root, 0);

    // The cursor on the first entry of `node`, if it has entries.
    private static bool TryEnter(NodeBase node, NodeBase root, ulong path, out MapCursor<TK, TV> cursor)
    {
        object holder;
        nint pos;
        int count;
        var flags = NodeOps.GetFlags(node.Meta);
        if (flags == NodeFlags.None)
        {
            holder = node;
            pos = LeafRunOffset(node);
            count = NodeOps.GetCapacity(node.Meta);
        }
        else
        {
            var array = flags == NodeFlags.Internal
                ? NodeOps.GetDataArray<TK, TV>(node)
                : Unsafe.As<CollisionNode<TK, TV>>(node).Slots;
            if (array == null || array.Length == 0)
            {
                cursor = default;
                return false;
            }

            holder = array;
            pos = ArrayRunOffset(array);
            count = array.Length;
        }

        cursor = new MapCursor<TK, TV>(holder, pos, pos + count * SlotSize, root, path);
        return true;
    }

    private static int ChildCount(NodeBase node) =>
        NodeOps.GetFlags(node.Meta) == NodeFlags.Internal ? NodeOps.GetCapacity(node.Meta) : 0;

    private static NodeBase Child(NodeBase node, int index) => NodeOps.GetChildSpan<TK, TV>(node)[index];

    private static int IndexAt(ulong path, int level) =>
        (int)(path >> (DepthBits + IndexBits * level)) & ((1 << IndexBits) - 1);

    private static ulong WithIndex(ulong path, int level, int index)
    {
        var shift = DepthBits + IndexBits * level;
        return (path & ~(((1UL << IndexBits) - 1) << shift)) | ((ulong)index << shift);
    }

    /// <summary>
    ///     The cursor on the first entry after the run of the node at <paramref name="path" />,
    ///     or a done cursor. The nodes come in the order of the enumerator: first child, else
    ///     the next sibling of the node or of the nearest ancestor that has one.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MapCursor<TK, TV> Advance(NodeBase root, ulong path)
    {
        var nodes = new PathNodes();
        var depth = (int)(path & DepthMask);
        nodes[0] = root;
        for (var level = 0; level < depth; level++)
            nodes[level + 1] = Child(nodes[level], IndexAt(path, level));

        var node = nodes[depth];
        while (true)
        {
            if (ChildCount(node) > 0)
            {
                if (depth == MaxDepth) throw new InvalidOperationException("The map is deeper than a cursor can walk.");
                path = WithIndex(path, depth, 0);
                depth++;
                node = nodes[depth] = Child(node, 0);
            }
            else
            {
                while (true)
                {
                    if (depth == 0) return default;
                    depth--;
                    var next = IndexAt(path, depth) + 1;
                    if (next < ChildCount(nodes[depth]))
                    {
                        path = WithIndex(path, depth, next);
                        node = Child(nodes[depth], next);
                        depth++;
                        nodes[depth] = node;
                        break;
                    }
                }
            }

            if (TryEnter(node, root, (path & ~DepthMask) | (ulong)depth, out var cursor)) return cursor;
        }
    }

    [InlineArray(MaxDepth + 1)]
    private struct PathNodes
    {
        private NodeBase _element0;
    }
}

/// <summary>
///     A view of any object as its first byte after the object header. A position in the
///     object is then a byte offset from <see cref="Data" />, the same for a node and for an
///     array.
/// </summary>
internal sealed class RawData
{
    public byte Data;
}
