/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */


using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Map;

internal static partial class TrieOps
{
    // Look, I know this method is a bit of a monster. I apologize for the sheer amount of code here,
    // but the following handles inserting a key-value pair into the trie like this: we navigate down 
    // the tree using chunks of the hash, and depending on whether we hit an empty spot, a leaf, or an 
    // internal node, we either slot it right in or do some painful allocations to expand the node. 
    // It's not pretty, but it avoids even worse performance.
    public static NodeBase Insert<TK, TV>(NodeBase? node, TK key, TV value, int hash, int shift,
        IEqualityComparer<TK> comparer, out bool added)
    {
        // The following handles the base case of an empty tree like this: we just allocate a single 
        // leaf node, stuff our data in there, and call it a day.
        if (node == null)
        {
            var bitpos0 = 1u << ((hash >> shift) & 0x1F);
            var newNode = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitpos0);
            var mySpan = NodeOps.GetLeafDataSpan<TK, TV>(newNode);
            mySpan[0] = new DataSlot<TK, TV> { Key = key, Value = value };
            // We added a value.
            added = true;
            return newNode;
        }

        var flags = NodeOps.GetFlags(node.Meta);

        // The following handles the dreaded hash collision like this: we iterate through an array of 
        // existing slots because their hashes are identical. If we find the key, we update it; otherwise, 
        // we just tack it onto the end.  This is rare enough to not actually be slow.
        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var oldSlots = colNode.Slots;
            var length = oldSlots.Length;

            for (var i = 0; i < length; i++)
                if (comparer.Equals(oldSlots[i].Key, key))
                {
                    // Is this optimization worth it? Probably not, but I left it in anyway.
                    if (ReferenceEquals(oldSlots[i].Value, value))
                    {
                        added = false;
                        return node;
                    }

                    var updatedSlots = new DataSlot<TK, TV>[length];
                    oldSlots.AsSpan().CopyTo(updatedSlots);
                    // Array.Copy(oldSlots, updatedSlots, length);
                    updatedSlots[i] = DataSlot<TK, TV>.Data(key, value);
                    added = false;
                    return new CollisionNode<TK, TV>(updatedSlots);
                }

            var appendedSlots = new DataSlot<TK, TV>[length + 1];
            //Array.Copy(oldSlots, appendedSlots, length);
            oldSlots.AsSpan(0, length).CopyTo(appendedSlots);
            appendedSlots[length] = DataSlot<TK, TV>.Data(key, value);
            added = true;
            return new CollisionNode<TK, TV>(appendedSlots);
        }

        var bit = (hash >> shift) & 0x1F;
        var bitpos = 1u << bit;
        var dataMap = (uint)node.Map;
        var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

        // The Interal node is actually my least favourite node. Her we 
        // check the bitmaps to see if the hash fragment points to local data or a child node.
        if (flags == NodeFlags.Internal)
        {
            var dataArray = NodeOps.GetDataArray<TK, TV>(node);

            var nodeMap = (uint)(node.Map >> 32);

            if ((dataMap & bitpos) != 0)
            {
                ref readonly var existingSlot =
                    ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!), dataIdx);

                if (comparer.Equals(existingSlot.Key, key))
                {
                    added = false;
                    if (EqualityComparer<TV>.Default.Equals(existingSlot.Value, value)) return node;

                    var newData0 = new DataSlot<TK, TV>[dataArray!.Length];
                    dataArray.AsSpan().CopyTo(newData0);
                    newData0[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };


                    var childCap = NodeOps.GetCapacity(node.Meta);
                    var newNode = NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, 0, node.Map);
                    Unsafe.As<InternalNode1<TK, TV>>(newNode).Data = newData0;


                    NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(newNode));
                    return newNode;
                }

                // Data collision -> Promote data slot to sub-node
                var subNode = MergeDataSlots(existingSlot, key, value, hash, shift + 5, comparer);
                var newMap = ((ulong)(nodeMap | bitpos) << 32) | (dataMap & ~bitpos);

                var newData = new DataSlot<TK, TV>[dataArray!.Length - 1];
                dataArray.AsSpan(0, dataIdx).CopyTo(newData);
                dataArray.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

                var childCapCurrent = NodeOps.GetCapacity(node.Meta);
                var newNodeObj =
                    NodeOps.AllocateInternal<TK, TV>((byte)(childCapCurrent + 1), NodeFlags.Internal, 0,
                        newMap);
                Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;

                var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));
                var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                childSpan[..nodeIdx].CopyTo(newChildSpan);
                newChildSpan[nodeIdx] = subNode;
                childSpan[nodeIdx..].CopyTo(newChildSpan[(nodeIdx + 1)..]);

                added = true;
                return newNodeObj;
            }

            if ((nodeMap & bitpos) != 0)
            {
                var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

                ref var firstChild =
                    ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(node).Children);
                var childNode = Unsafe.Add(ref firstChild, nodeIdx);

                var newChildNode = Insert(childNode, key, value, hash, shift + 5, comparer, out added);

                if (ReferenceEquals(childNode, newChildNode)) return node;

                var childCap = NodeOps.GetCapacity(node.Meta);
                var newNodeObj = NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, 0, node.Map);
                Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = dataArray;

                var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                childSpan.CopyTo(newChildSpan);
                newChildSpan[nodeIdx] = newChildNode;

                return newNodeObj;
            }

            // Empty slot
            added = true;
            var appendedData = new DataSlot<TK, TV>[dataArray!.Length + 1];
            dataArray.AsSpan(0, dataIdx).CopyTo(appendedData);
            appendedData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
            dataArray.AsSpan(dataIdx).CopyTo(appendedData.AsSpan(dataIdx + 1));

            var childCapEmpty = NodeOps.GetCapacity(node.Meta);
            var appendedNodeObj =
                NodeOps.AllocateInternal<TK, TV>(childCapEmpty, NodeFlags.Internal, 0, node.Map | bitpos);
            Unsafe.As<InternalNode1<TK, TV>>(appendedNodeObj).Data = appendedData;

            NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(appendedNodeObj));

            return appendedNodeObj;
        }

        // The following handles leaf nodes like this: it's basically a cut-down version of the internal node 
        // logic, since leaves don't have sub-nodes. Sorry for the code duplication.

        if ((dataMap & bitpos) != 0)
        {
            ref var firstSlot =
                ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(node).Data);
            ref readonly var existingSlot = ref Unsafe.Add(ref firstSlot, dataIdx);

            if (comparer.Equals(existingSlot.Key, key))
            {
                added = false;
                if (EqualityComparer<TV>.Default.Equals(existingSlot.Value, value)) return node;

                var leafCap = NodeOps.GetCapacity(node.Meta);
                var updatedLeaf = NodeOps.AllocateLeaf<TK, TV>(leafCap, NodeFlags.None, 0, node.Map);

                var oldLeafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
                var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
                oldLeafSpan.CopyTo(newSpan);
                newSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
                return updatedLeaf;
            }

            // Leaf collision -> Must upgrade to InternalNode
            added = true;
            var subNode = MergeDataSlots(existingSlot, key, value, hash, shift + 5, comparer);
            var newMap = ((ulong)bitpos << 32) | (dataMap & ~bitpos);

            var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
            var newData = new DataSlot<TK, TV>[leafSpan.Length - 1];
            leafSpan[..dataIdx].CopyTo(newData);
            leafSpan[(dataIdx + 1)..].CopyTo(newData.AsSpan(dataIdx));

            var newNodeObj = NodeOps.AllocateInternal<TK, TV>(1, NodeFlags.Internal, 0, newMap);
            Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;
            NodeOps.GetChildSpan<TK, TV>(newNodeObj)[0] = subNode;

            return newNodeObj;
        }

        // Empty slot in LeafNode -> Append inline
        added = true;
        var currentLeafCap = NodeOps.GetCapacity(node.Meta);
        var expandedLeaf =
            NodeOps.AllocateLeaf<TK, TV>((byte)(currentLeafCap + 1), NodeFlags.None, 0, node.Map | bitpos);

        var leafSpanOld = NodeOps.GetLeafDataSpan<TK, TV>(node);
        var expandedSpan = NodeOps.GetLeafDataSpan<TK, TV>(expandedLeaf);

        leafSpanOld[..dataIdx].CopyTo(expandedSpan);
        expandedSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
        leafSpanOld[dataIdx..].CopyTo(expandedSpan[(dataIdx + 1)..]);

        return expandedLeaf;
    }

    // The following handles merging two values that hash to the same spot. 
    // it figures out how deep we have to go before their hashes finally differ, and builds 
    // up the necessary nodes along the way.
    private static NodeBase MergeDataSlots<TK, TV>(DataSlot<TK, TV> existingSlot, TK newKey, TV newValue, int newHash,
        int shift, IEqualityComparer<TK> comparer, ulong ownerId = 0)
    {
        var existingHash = comparer.GetHashCode(existingSlot.Key!);

        // 1. Full 32-bit hash collision
        if (existingHash == newHash)
            return new CollisionNode<TK, TV>([
                existingSlot,
                new DataSlot<TK, TV> { Key = newKey, Value = newValue }
            ], ownerId);

        var existingBit = (existingHash >> shift) & 0x1F;
        var newBit = (newHash >> shift) & 0x1F;

        // 2. Hashes diverge at this specific bit depth
        if (existingBit != newBit)
        {
            var dataMap = (1u << existingBit) | (1u << newBit);
            var leafNode = NodeOps.AllocateLeaf<TK, TV>(2, NodeFlags.None, ownerId, dataMap);
            var span = NodeOps.GetLeafDataSpan<TK, TV>(leafNode);

            if (existingBit < newBit)
            {
                span[0] = existingSlot;
                span[1] = new DataSlot<TK, TV> { Key = newKey, Value = newValue };
            }
            else
            {
                span[0] = new DataSlot<TK, TV> { Key = newKey, Value = newValue };
                span[1] = existingSlot;
            }

            return leafNode;
        }

        // 3. Bits are identical at this depth, recurse deeper
        var nodeMap = 1u << existingBit;
        var subNode = MergeDataSlots(existingSlot, newKey, newValue, newHash, shift + 5, comparer, ownerId);

        var newNodeObj =
            NodeOps.AllocateInternal<TK, TV>(1, NodeFlags.Internal, ownerId, (ulong)nodeMap << 32);
        Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = Array.Empty<DataSlot<TK, TV>>();
        NodeOps.GetChildSpan<TK, TV>(newNodeObj)[0] = subNode;

        return newNodeObj;
    }

    // Key lookup! it zips down the tree, decoding the bitmap
    // at each level to figure out exactly which array index holds our data or next node.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValue<TK, TV>(NodeBase? node, TK key, int hash, IEqualityComparer<TK> comparer,
        out TV value)
    {
        var shift = 0;
        var current = node;

        while (current != null)
        {
            var meta = current.Meta;
            var flags = (byte)((meta >> 8) & 0xFF);

            if (flags == (byte)NodeFlags.Collision)
            {
                var slots = Unsafe.As<CollisionNode<TK, TV>>(current).Slots;
                for (var i = 0; i < slots.Length; i++)
                    if (comparer.Equals(slots[i].Key, key))
                    {
                        value = slots[i].Value;
                        return true;
                    }

                break;
            }

            var bit = (hash >> shift) & 0x1F;
            var bitpos = 1u << bit;
            var map = current.Map;
            var dataMap = (uint)map;

            if (flags == (byte)NodeFlags.Internal)
            {
                if ((dataMap & bitpos) != 0)
                {
                    var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
                    var dataArray = NodeOps.GetDataArray<TK, TV>(current);

                    ref readonly var slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!),
                        dataIdx);

                    if (comparer.Equals(slot.Key, key))
                    {
                        value = slot.Value;
                        return true;
                    }

                    break;
                }

                var nodeMap = (uint)(map >> 32);
                if ((nodeMap & bitpos) != 0)
                {
                    var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

                    ref var firstChild =
                        ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(current).Children);
                    current = Unsafe.Add(ref firstChild, nodeIdx);

                    shift += 5;
                    continue;
                }

                break;
            }

            // Leaf Node Phase
            if ((dataMap & bitpos) != 0)
            {
                var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

                ref var firstSlot =
                    ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(current).Data);
                ref readonly var slot = ref Unsafe.Add(ref firstSlot, dataIdx);

                if (comparer.Equals(slot.Key, key))
                {
                    value = slot.Value;
                    return true;
                }
            }

            break;
        }

        value = default!;
        return false;
    }

    // Yes, I am the code-duplication-batman!
    // See if you can see the difference to the above function
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetKey<TK, TV>(NodeBase? node, TK key, int hash, IEqualityComparer<TK> comparer,
        out TK value)
    {
        var shift = 0;
        var current = node;

        while (current != null)
        {
            var meta = current.Meta;
            var flags = (byte)((meta >> 8) & 0xFF);

            if (flags == (byte)NodeFlags.Collision)
            {
                var slots = Unsafe.As<CollisionNode<TK, TV>>(current).Slots;
                for (var i = 0; i < slots.Length; i++)
                    if (comparer.Equals(slots[i].Key, key))
                    {
                        value = slots[i].Key;
                        return true;
                    }

                break;
            }

            var bit = (hash >> shift) & 0x1F;
            var bitpos = 1u << bit;
            var map = current.Map;
            var dataMap = (uint)map;

            if (flags == (byte)NodeFlags.Internal)
            {
                if ((dataMap & bitpos) != 0)
                {
                    var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
                    var dataArray = NodeOps.GetDataArray<TK, TV>(current);

                    ref readonly var slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!),
                        dataIdx);

                    if (comparer.Equals(slot.Key, key))
                    {
                        value = slot.Key;
                        return true;
                    }

                    break;
                }

                var nodeMap = (uint)(map >> 32);
                if ((nodeMap & bitpos) != 0)
                {
                    var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

                    ref var firstChild =
                        ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(current).Children);
                    current = Unsafe.Add(ref firstChild, nodeIdx);

                    shift += 5;
                    continue;
                }

                break;
            }

            // Leaf Node Phase
            if ((dataMap & bitpos) != 0)
            {
                var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

                ref var firstSlot =
                    ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(current).Data);
                ref readonly var slot = ref Unsafe.Add(ref firstSlot, dataIdx);

                if (comparer.Equals(slot.Key, key))
                {
                    value = slot.Key;
                    return true;
                }
            }

            break;
        }

        value = default!;
        return false;
    }

// The following handles deleting a key from the trie. it hunts down the node, strips out
// the data slot, and then painstakingly re-compacts the tree if a node becomes too sparse. 
// It's a complete nightmare to read, and for that, I am not that sorry.
    public static NodeBase? Remove<TK, TV>(NodeBase? node, TK key, int hash, int shift, IEqualityComparer<TK> comparer,
        out bool removed)
    {
        if (node == null)
        {
            removed = false;
            return null;
        }

        var flags = NodeOps.GetFlags(node.Meta);

        // Phase 1: Collision Node
        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var slots = colNode.Slots;

            for (var i = 0; i < slots.Length; i++)
                if (comparer.Equals(slots[i].Key, key))
                {
                    removed = true;
                    if (slots.Length == 1) return null; // Should be rare, collisions usually start at 2

                    var updatedSlots = new DataSlot<TK, TV>[slots.Length - 1];
                    //Array.Copy(slots, 0, updatedSlots, 0, i);
                    slots.AsSpan(0, i).CopyTo(updatedSlots);
                    slots.AsSpan(i + 1).CopyTo(updatedSlots.AsSpan(i));
                    return new CollisionNode<TK, TV>(updatedSlots);
                }

            removed = false;
            return node;
        }

        var bit = (hash >> shift) & 0x1F;
        var bitpos = 1u << bit;
        var dataMap = (uint)node.Map;

        // Phase 2: Internal Node
        if (flags == NodeFlags.Internal)
        {
            var dataArray = NodeOps.GetDataArray<TK, TV>(node);
            var nodeMap = (uint)(node.Map >> 32);

            // Case 2a
            if ((dataMap & bitpos) != 0)
            {
                var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

                ref readonly var slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!), dataIdx);

                if (comparer.Equals(slot.Key, key))
                {
                    removed = true;
                    var newMap = node.Map & ~(ulong)bitpos;
                    var childCap = NodeOps.GetCapacity(node.Meta);

                    if (dataArray!.Length == 1 && childCap == 0)
                        return null;

                    var newData = new DataSlot<TK, TV>[dataArray.Length - 1];
                    dataArray.AsSpan(0, dataIdx).CopyTo(newData);
                    dataArray.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

                    if (childCap == 0)
                    {
                        var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, 0,
                            newMap);
                        newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    var newNodeObj =
                        NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, 0, newMap);
                    Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;

                    // Sorry about all the unsafe stuff, folks, but that means I can move the getspan all the way down here
                    // where we really need it. It gives me about 5-20% speed increase.
                    NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(newNodeObj));
                    return newNodeObj;
                }
            }

            // Case 2b
            if ((nodeMap & bitpos) != 0)
            {
                var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

                // I would love to be able to use the getxyzspan from nodeops
                // but this we don't always need the spans, and doing it like this means quite a substantial speedup.
                ref var firstChild =
                    ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(node).Children);
                var childNode = Unsafe.Add(ref firstChild, nodeIdx);

                var newChild = Remove<TK, TV>(childNode, key, hash, shift + 5, comparer, out removed);

                if (!removed) return node;

                var childCap = NodeOps.GetCapacity(node.Meta);

                if (newChild == null)
                {
                    var newMap = node.Map & ~((ulong)bitpos << 32);

                    if (childCap == 1)
                    {
                        if (dataArray!.Length == 0) return null;

                        var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)dataArray.Length, NodeFlags.None, 0, newMap);
                        dataArray.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    var newNodeObj =
                        NodeOps.AllocateInternal<TK, TV>((byte)(childCap - 1), NodeFlags.Internal, 0, newMap);
                    Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = dataArray;

                    // Here we do a copy, and thus need spans. 
                    var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                    var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                    childSpan[..nodeIdx].CopyTo(newChildSpan);
                    childSpan[(nodeIdx + 1)..].CopyTo(newChildSpan[nodeIdx..]);

                    return newNodeObj;
                }

                // CHAMP Compaction
                if (NodeOps.GetFlags(newChild.Meta) == NodeFlags.None)
                {
                    var nextChildCap = NodeOps.GetCapacity(newChild.Meta);
                    if (nextChildCap == 1)
                    {
                        var singleData = NodeOps.GetLeafDataSpan<TK, TV>(newChild)[0];

                        var newMap = (node.Map & ~((ulong)bitpos << 32)) | bitpos;
                        var newDataIdx = BitOperations.PopCount((uint)newMap & (bitpos - 1));

                        var newData = new DataSlot<TK, TV>[dataArray!.Length + 1];
                        dataArray.AsSpan(0, newDataIdx).CopyTo(newData);
                        newData[newDataIdx] = singleData;
                        dataArray.AsSpan(newDataIdx).CopyTo(newData.AsSpan(newDataIdx + 1));

                        if (childCap == 1)
                        {
                            var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, 0, newMap);
                            newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                            return leaf;
                        }

                        var newNodeObj =
                            NodeOps.AllocateInternal<TK, TV>((byte)(childCap - 1), NodeFlags.Internal, 0, newMap);
                        Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;

                        var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                        var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                        childSpan[..nodeIdx].CopyTo(newChildSpan);
                        childSpan[(nodeIdx + 1)..].CopyTo(newChildSpan[nodeIdx..]);

                        return newNodeObj;
                    }
                }

                // Normal sub-node replacement
                var updatedNodeObj = NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, 0, node.Map);
                Unsafe.As<InternalNode1<TK, TV>>(updatedNodeObj).Data = dataArray;

                var childSpanOld = NodeOps.GetChildSpan<TK, TV>(node);
                var updatedChildSpan = NodeOps.GetChildSpan<TK, TV>(updatedNodeObj);
                childSpanOld.CopyTo(updatedChildSpan);
                updatedChildSpan[nodeIdx] = newChild;

                return updatedNodeObj;
            }

            removed = false;
            return node;
        }

        // Phase 3: Leaf Node
        if ((dataMap & bitpos) != 0)
        {
            var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
            var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);

            if (comparer.Equals(leafSpan[dataIdx].Key, key))
            {
                removed = true;
                if (leafSpan.Length == 1) return null;

                var newMap = node.Map & ~(ulong)bitpos;
                var updatedLeaf =
                    NodeOps.AllocateLeaf<TK, TV>((byte)(leafSpan.Length - 1), NodeFlags.None, 0, newMap);
                var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);

                leafSpan[..dataIdx].CopyTo(newSpan);
                leafSpan[(dataIdx + 1)..].CopyTo(newSpan[dataIdx..]);

                return updatedLeaf;
            }
        }

        removed = false;
        return node;
    }

    /// <summary>
    ///     Pure structural merge of two CHAMP nodes
    /// </summary>
    public static NodeBase? Merge<TK, TV>(
        NodeBase? node1,
        NodeBase? node2,
        int shift,
        IEqualityComparer<TK> comparer,
        Func<TK, TV, TV, TV>? conflictResolver)
    {
        if (node1 == null) return node2;
        if (node2 == null) return node1;

        var flags1 = NodeOps.GetFlags(node1.Meta);
        var flags2 = NodeOps.GetFlags(node2.Meta);

        // Fallback for hash collision nodes
        if (flags1 == NodeFlags.Collision)
        {
            var col1 = Unsafe.As<CollisionNode<TK, TV>>(node1);
            var current = node2;
            foreach (var slot in col1.Slots)
            {
                var h = comparer.GetHashCode(slot.Key!);
                if (TryGetValue(node2, slot.Key, h, comparer, out TV existingVal2))
                {
                    var resolvedVal = conflictResolver != null
                        ? conflictResolver(slot.Key, slot.Value, existingVal2)
                        : existingVal2;
                    current = Insert(current, slot.Key, resolvedVal, h, shift, comparer, out _);
                }
                else
                {
                    current = Insert(current, slot.Key, slot.Value, h, shift, comparer, out _);
                }
            }

            return current;
        }

        if (flags2 == NodeFlags.Collision)
        {
            var col2 = Unsafe.As<CollisionNode<TK, TV>>(node2);
            var current = node1;
            foreach (var slot in col2.Slots)
            {
                var h = comparer.GetHashCode(slot.Key!);
                if (TryGetValue(node1, slot.Key, h, comparer, out TV existingVal1))
                {
                    var resolvedVal = conflictResolver != null
                        ? conflictResolver(slot.Key, existingVal1, slot.Value)
                        : slot.Value;
                    current = Insert(current, slot.Key, resolvedVal, h, shift, comparer, out _);
                }
                else
                {
                    current = Insert(current, slot.Key, slot.Value, h, shift, comparer, out _);
                }
            }

            return current;
        }

        // Fast path: Both are leaves with no overlapping bits
        if (flags1 == NodeFlags.None && flags2 == NodeFlags.None)
        {
            var map1 = (uint)node1.Map;
            var map2 = (uint)node2.Map;
            if ((map1 & map2) == 0)
            {
                var combinedMap = map1 | map2;
                var totalCount = BitOperations.PopCount(combinedMap);
                var newLeaf = NodeOps.AllocateLeaf<TK, TV>((byte)totalCount, NodeFlags.None, 0, combinedMap);
                var destSpan = NodeOps.GetLeafDataSpan<TK, TV>(newLeaf);
                var span = NodeOps.GetLeafDataSpan<TK, TV>(node1);
                var span0 = NodeOps.GetLeafDataSpan<TK, TV>(node2);

                int idx1 = 0, idx2 = 0, destIdx = 0;
                var tempMap = combinedMap;
                while (tempMap != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(tempMap);
                    var bitpos = 1u << bit;
                    if ((map1 & bitpos) != 0)
                        destSpan[destIdx++] = span[idx1++];
                    else
                        destSpan[destIdx++] = span0[idx2++];
                    tempMap &= ~bitpos;
                }

                return newLeaf;
            }
        }

        var dataMap1 = (uint)node1.Map;
        var nodeMap1 = flags1 == NodeFlags.Internal ? (uint)(node1.Map >> 32) : 0;
        var span1 = flags1 == NodeFlags.Internal
            ? NodeOps.GetDataArray<TK, TV>(node1).AsSpan()
            : NodeOps.GetLeafDataSpan<TK, TV>(node1);
        var nodes1 = flags1 == NodeFlags.Internal
            ? NodeOps.GetChildSpan<TK, TV>(node1)
            : Span<NodeBase>.Empty;

        var dataMap2 = (uint)node2.Map;
        var nodeMap2 = flags2 == NodeFlags.Internal ? (uint)(node2.Map >> 32) : 0;
        var span2 = flags2 == NodeFlags.Internal
            ? NodeOps.GetDataArray<TK, TV>(node2).AsSpan()
            : NodeOps.GetLeafDataSpan<TK, TV>(node2);
        var nodes2 = flags2 == NodeFlags.Internal
            ? NodeOps.GetChildSpan<TK, TV>(node2)
            : Span<NodeBase>.Empty;
        var allBits = dataMap1 | nodeMap1 | dataMap2 | nodeMap2;

        var pooledData = ArrayPool<DataSlot<TK, TV>>.Shared.Rent(32);
        var pooledNodes = ArrayPool<NodeBase>.Shared.Rent(32);

        var dataCount = 0;
        var nodeCount = 0;
        ulong finalDataMap = 0;
        ulong finalNodeMap = 0;

        var tempBits = allBits;
        while (tempBits != 0)
        {
            var bit = BitOperations.TrailingZeroCount(tempBits);
            var bitpos = 1u << bit;
            tempBits &= ~bitpos;

            var hasData1 = (dataMap1 & bitpos) != 0;
            var hasNode1 = (nodeMap1 & bitpos) != 0;
            var hasData2 = (dataMap2 & bitpos) != 0;
            var hasNode2 = (nodeMap2 & bitpos) != 0;

            DataSlot<TK, TV> d1 = default;
            NodeBase? n1 = null;
            if (hasData1) d1 = span1[BitOperations.PopCount(dataMap1 & (bitpos - 1))];
            if (hasNode1) n1 = nodes1[BitOperations.PopCount(nodeMap1 & (bitpos - 1))];

            DataSlot<TK, TV> d2 = default;
            NodeBase? n2 = null;
            if (hasData2) d2 = span2[BitOperations.PopCount(dataMap2 & (bitpos - 1))];
            if (hasNode2) n2 = nodes2[BitOperations.PopCount(nodeMap2 & (bitpos - 1))];

            // Case 1: Populated exclusively in tree 1
            if ((hasData1 || hasNode1) && !(hasData2 || hasNode2))
            {
                if (hasData1)
                {
                    pooledData[dataCount++] = d1;
                    finalDataMap |= bitpos;
                }
                else
                {
                    pooledNodes[nodeCount++] = n1!;
                    finalNodeMap |= bitpos;
                }
            }
            // Case 2: Populated exclusively in tree 2
            else if (!(hasData1 || hasNode1) && (hasData2 || hasNode2))
            {
                if (hasData2)
                {
                    pooledData[dataCount++] = d2;
                    finalDataMap |= bitpos;
                }
                else
                {
                    pooledNodes[nodeCount++] = n2!;
                    finalNodeMap |= bitpos;
                }
            }
            // Case 3: Both contain inline data slots
            else if (hasData1 && hasData2)
            {
                if (comparer.Equals(d1.Key, d2.Key))
                {
                    var resolvedVal = conflictResolver != null
                        ? conflictResolver(d1.Key!, d1.Value!, d2.Value!)
                        : d2.Value;
                    pooledData[dataCount++] = DataSlot<TK, TV>.Data(d1.Key!, resolvedVal!);
                    finalDataMap |= bitpos;
                }
                else
                {
                    var h2 = comparer.GetHashCode(d2.Key!);
                    var subNode = MergeDataSlots(d1!, d2.Key, d2.Value, h2, shift + 5, comparer!);
                    pooledNodes[nodeCount++] = subNode;
                    finalNodeMap |= bitpos;
                }
            }
            // Case 4: Both elements contain internal sub-nodes
            else if (hasNode1 && hasNode2)
            {
                var subNode = Merge(n1, n2, shift + 5, comparer, conflictResolver);
                if (subNode != null)
                {
                    pooledNodes[nodeCount++] = subNode;
                    finalNodeMap |= bitpos;
                }
            }
            // Case 5: Layer mismatch. Wrap slot into a micro-leaf and run pure Merge.
            else if (hasData1 && hasNode2)
            {
                var h1 = comparer.GetHashCode(d1.Key!);
                var bitposNext = 1u << ((h1 >> (shift + 5)) & 0x1F);
                var microLeaf = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitposNext);
                NodeOps.GetLeafDataSpan<TK, TV>(microLeaf)[0] = d1;

                var mergedSubNode = Merge(microLeaf, n2, shift + 5, comparer, conflictResolver);
                if (mergedSubNode != null)
                {
                    pooledNodes[nodeCount++] = mergedSubNode;
                    finalNodeMap |= bitpos;
                }
            }
            else if (hasNode1 && hasData2)
            {
                var h2 = comparer.GetHashCode(d2.Key!);
                var bitposNext = 1u << ((h2 >> (shift + 5)) & 0x1F);
                var microLeaf = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitposNext);
                NodeOps.GetLeafDataSpan<TK, TV>(microLeaf)[0] = d2;

                var mergedSubNode = Merge(n1, microLeaf, shift + 5, comparer, conflictResolver);
                if (mergedSubNode != null)
                {
                    pooledNodes[nodeCount++] = mergedSubNode;
                    finalNodeMap |= bitpos;
                }
            }
        }

        NodeBase resultNode;
        if (nodeCount == 0 && dataCount > 0)
        {
            resultNode = NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, 0, finalDataMap);
            pooledData.AsSpan(0, dataCount).CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(resultNode));
        }
        else
        {
            var finalData = new DataSlot<TK, TV>[dataCount];
            pooledData.AsSpan(0, dataCount).CopyTo(finalData);

            resultNode = NodeOps.AllocateInternal<TK, TV>((byte)nodeCount, NodeFlags.Internal, 0,
                finalDataMap | (finalNodeMap << 32));
            Unsafe.As<InternalNode1<TK, TV>>(resultNode).Data = finalData;

            var resultNodesSpan = NodeOps.GetChildSpan<TK, TV>(resultNode);
            pooledNodes.AsSpan(0, nodeCount).CopyTo(resultNodesSpan);
        }

        ArrayPool<DataSlot<TK, TV>>.Shared.Return(pooledData);
        ArrayPool<NodeBase>.Shared.Return(pooledNodes);

        return resultNode;
    }

    public static bool Iter<TK, TV>(NodeBase? node, Func<TK, TV, bool> action)
    {
        if (node == null) return true;

        var flags = NodeOps.GetFlags(node.Meta);

        if (flags == NodeFlags.None)
        {
            var span = NodeOps.GetLeafDataSpan<TK, TV>(node);
            for (var i = 0; i < span.Length; i++)
                if (!action(span[i].Key, span[i].Value))
                    return false;
            return true;
        }

        if (flags == NodeFlags.Internal)
        {
            var dataArray = NodeOps.GetDataArray<TK, TV>(node);
            if (dataArray != null)
                for (var i = 0; i < dataArray.Length; i++)
                    if (dataArray[i].Key != null && !action(dataArray[i].Key, dataArray[i].Value))
                        return false;

            var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
            for (var i = 0; i < childSpan.Length; i++)
                if (!Iter(childSpan[i], action))
                    return false;

            return true;
        }

        // CollisionNode
        var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
        var slots = colNode.Slots;

        for (var i = 0; i < slots.Length; i++)
            if (!action(slots[i].Key, slots[i].Value))
                return false;


        return true;
    }
}