/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Linus Björnstam
 *
 */

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Map;

// Splits a trie into about k tries that together hold each entry once, for work on the
// parts at the same time. The parts share the trie's nodes; only the nodes on the way down
// to the cut are new. Nothing is counted: a node holds no size, and counting the trie costs
// about as much as a walk of it, so the parts are balanced by the trie's shape.
//
// - A unit is a data entry or a child of some node, with the bits of the path to that node.
//   The units of the root, in iteration order, cover the trie.
// - With spread hashes, the children of one level hold about the same number of entries:
//   (count - data entries - collision entries) / children. While that is more than
//   count / k, every child that is a leaf or an internal node is replaced by its own units.
//   A collision node stays one unit, of a known size.
// - The parts are runs of units, cut where the running weight passes i * count / k. Hashes
//   that are not spread make parts of different sizes, not wrong ones.
// - A part's root is built from its run: the units with the same path below a level become
//   one new node at that level. A new node that holds one entry and no children is put in
//   its parent's data, as removal would put it, so no part has a one-entry leaf below the
//   root.
//
// The split runs once for each call of in-parallel, so its code would stay unoptimised under
// tiered compilation: AggressiveOptimization compiles it optimised at the first call.
[NonUserCode]
internal static class TrieSplit<TK, TV>
{
    // 24 bytes, so that the list of a large trie's units stays out of the large object heap.
    private struct Unit
    {
        public ulong Path;         // the bits of the nodes above, 5 for each level, the root's lowest
        public NodeBase Node;      // a child, or the node that holds a data entry
        public byte Depth;         // the level of the node the unit is in; 0 is the root
        public byte Bit;           // the unit's bit in its node
        public byte Index;         // a data entry's index in its node's data
        public bool IsData;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static NodeBase?[] Split(NodeBase? root, int count, int k)
    {
        if (root == null || k <= 1 || count <= 1) return [root];

        var units = new List<Unit>();
        AddUnitsOf(root, 0, 0, units);
        var target = (double)count / k;
        double childWeight;
        while (true)
        {
            childWeight = ChildWeight(units, count, out var expandable);
            if (childWeight <= target || expandable == 0) break;
            var size = 0;
            foreach (var u in units)
                size += Expands(u) ? BitOperations.PopCount(u.Node.Map) : 1;
            var expanded = new List<Unit>(size);
            foreach (var u in units)
                if (Expands(u))
                    AddUnitsOf(u.Node, u.Depth + 1, u.Path | ((ulong)u.Bit << (5 * u.Depth)), expanded);
                else
                    expanded.Add(u);
            units = expanded;
        }

        var weights = new double[units.Count];
        double total = 0;
        for (var j = 0; j < units.Count; j++)
        {
            var u = units[j];
            weights[j] = u.IsData ? 1
                : NodeOps.GetFlags(u.Node.Meta) == NodeFlags.Collision ? CollisionSize(u.Node)
                : childWeight;
            total += weights[j];
        }

        var parts = Math.Min(k, units.Count);
        var scratch = new Scratch?[12];
        var result = new List<NodeBase?>(parts);
        var start = 0;
        double done = 0;
        for (var j = 0; j < units.Count; j++)
        {
            done += weights[j];
            var last = j == units.Count - 1;
            if (!last && (result.Count >= parts - 1 || done < total * (result.Count + 1) / parts)) continue;
            result.Add(Build(units, start, j + 1, 0, scratch).Node);
            start = j + 1;
        }

        return result.ToArray();
    }

    // The entries a child that is not a collision node is taken to hold, and how many such
    // children there are.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double ChildWeight(List<Unit> units, int count, out int children)
    {
        children = 0;
        long rest = count;
        foreach (var u in units)
            if (u.IsData) rest--;
            else if (NodeOps.GetFlags(u.Node.Meta) == NodeFlags.Collision) rest -= CollisionSize(u.Node);
            else children++;
        return children == 0 ? 0 : Math.Max(1.0, (double)rest / children);
    }

    private static int CollisionSize(NodeBase node) => Unsafe.As<CollisionNode<TK, TV>>(node).Slots.Length;

    private static bool Expands(Unit u) => !u.IsData && NodeOps.GetFlags(u.Node.Meta) != NodeFlags.Collision;

    private static DataSlot<TK, TV> SlotOf(Unit u) =>
        NodeOps.GetFlags(u.Node.Meta) == NodeFlags.None
            ? NodeOps.GetLeafDataSpan<TK, TV>(u.Node)[u.Index]
            : NodeOps.GetDataArray<TK, TV>(u.Node)![u.Index];

    // The units of one node, in iteration order: its data, then its children.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void AddUnitsOf(NodeBase node, int depth, ulong path, List<Unit> units)
    {
        var flags = NodeOps.GetFlags(node.Meta);
        var i = 0;
        for (var m = (uint)node.Map; m != 0; m &= m - 1)
            units.Add(new Unit
            {
                Depth = (byte)depth, Path = path, Bit = (byte)BitOperations.TrailingZeroCount(m), Node = node,
                Index = (byte)i++, IsData = true
            });

        if (flags != NodeFlags.Internal) return;
        var children = NodeOps.GetChildSpan<TK, TV>(node);
        i = 0;
        for (var m = (uint)(node.Map >> 32); m != 0; m &= m - 1)
            units.Add(new Unit
                { Depth = (byte)depth, Path = path, Bit = (byte)BitOperations.TrailingZeroCount(m), Node = children[i++] });
    }

    // The entries and children of the node being built at one level, by bit. One for each
    // level, as the build of a node is done before the next one at its level starts.
    [NonUserCode]
    private sealed class Scratch
    {
        public readonly DataSlot<TK, TV>[] Data = new DataSlot<TK, TV>[32];
        public readonly NodeBase[] Children = new NodeBase[32];
    }

    // A new node at `depth` for units[from..to), which all lie at or below it on one path.
    // When it would hold one entry and no children, the entry, for the parent's data.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static (NodeBase? Node, DataSlot<TK, TV> Slot) Build(List<Unit> units, int from, int to, int depth,
        Scratch?[] scratch)
    {
        var sc = scratch[depth] ??= new Scratch();
        uint dataMap = 0, nodeMap = 0;
        var i = from;
        while (i < to)
        {
            var u = units[i];
            if (u.Depth == depth)
            {
                if (u.IsData)
                {
                    sc.Data[u.Bit] = SlotOf(u);
                    dataMap |= 1u << u.Bit;
                }
                else
                {
                    sc.Children[u.Bit] = u.Node;
                    nodeMap |= 1u << u.Bit;
                }

                i++;
                continue;
            }

            var bit = (int)((u.Path >> (5 * depth)) & 0x1F);
            var j = i + 1;
            while (j < to && units[j].Depth > depth && (int)((units[j].Path >> (5 * depth)) & 0x1F) == bit) j++;
            var piece = Build(units, i, j, depth + 1, scratch);
            if (piece.Node == null)
            {
                sc.Data[bit] = piece.Slot;
                dataMap |= 1u << bit;
            }
            else
            {
                sc.Children[bit] = piece.Node;
                nodeMap |= 1u << bit;
            }

            i = j;
        }

        var dataCount = BitOperations.PopCount(dataMap);
        if (nodeMap == 0 && dataCount == 1 && depth > 0)
            return (null, sc.Data[BitOperations.TrailingZeroCount(dataMap)]);

        if (nodeMap == 0)
        {
            var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, OwnerId.None, dataMap);
            var span = NodeOps.GetLeafDataSpan<TK, TV>(leaf);
            var d = 0;
            for (var m = dataMap; m != 0; m &= m - 1) span[d++] = sc.Data[BitOperations.TrailingZeroCount(m)];
            return (leaf, default);
        }

        var slots = dataCount == 0 ? Array.Empty<DataSlot<TK, TV>>() : new DataSlot<TK, TV>[dataCount];
        var s = 0;
        for (var m = dataMap; m != 0; m &= m - 1) slots[s++] = sc.Data[BitOperations.TrailingZeroCount(m)];
        var node = NodeOps.AllocateInternal<TK, TV>((byte)BitOperations.PopCount(nodeMap), NodeFlags.Internal,
            OwnerId.None, dataMap | ((ulong)nodeMap << 32));
        Unsafe.As<InternalNode1<TK, TV>>(node).Data = slots;
        var children = NodeOps.GetChildSpan<TK, TV>(node);
        var c = 0;
        for (var m = nodeMap; m != 0; m &= m - 1) children[c++] = sc.Children[BitOperations.TrailingZeroCount(m)];
        return (node, default);
    }

    // The entries under a node. A walk of every node: the count of a part made by Split.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int CountTree(NodeBase node)
    {
        var flags = NodeOps.GetFlags(node.Meta);
        if (flags == NodeFlags.Collision) return CollisionSize(node);
        var n = BitOperations.PopCount((uint)node.Map);
        if (flags == NodeFlags.Internal)
            foreach (var child in NodeOps.GetChildSpan<TK, TV>(node))
                n += CountTree(child);
        return n;
    }
}
