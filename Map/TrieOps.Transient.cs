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
using System.Runtime.InteropServices;

namespace Map;

internal static partial class TrieOps
{
    // Well, if you thought the immutable Insert, this is actually the same, but it checks if the instance of TransientMap that 
    // had Insert called on it owns the node.
    public static NodeBase InsertTransient<TK, TV>(NodeBase? node, TK key, TV value, int hash, int shift,
        IEqualityComparer<TK> comparer, ulong ownerId, out bool added)
    {
        if (node == null)
        {
            var bitpos0 = 1u << ((hash >> shift) & 0x1F);
            var newNode = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, ownerId, bitpos0);
            NodeOps.GetLeafDataSpan<TK, TV>(newNode)[0] = new DataSlot<TK, TV> { Key = key, Value = value };
            added = true;
            return newNode;
        }

        var flags = NodeOps.GetFlags(node.Meta);
        var isMutable = NodeOps.GetOwnerId(node.Meta) == ownerId;

        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var slots = colNode.Slots;

            for (var i = 0; i < slots.Length; i++)
                if (comparer.Equals(slots[i].Key, key))
                {
                    added = false;
                    if (isMutable)
                    {
                        slots[i].Value = value;
                        return node;
                    }

                    var updatedSlots = new DataSlot<TK, TV>[slots.Length];
                    slots.AsSpan().CopyTo(updatedSlots);
                    updatedSlots[i] = new DataSlot<TK, TV> { Key = key, Value = value };
                    return new CollisionNode<TK, TV>(updatedSlots, ownerId);
                }

            added = true;
            var appendedSlots = new DataSlot<TK, TV>[slots.Length + 1];
            slots.AsSpan().CopyTo(appendedSlots);
            appendedSlots[slots.Length] = new DataSlot<TK, TV> { Key = key, Value = value };

            if (isMutable)
            {
                colNode.Slots = appendedSlots;
                return node;
            }

            return new CollisionNode<TK, TV>(appendedSlots, ownerId);
        }

        var bit = (hash >> shift) & 0x1F;
        var bitpos = 1u << bit;
        var dataMap = (uint)node.Map;
        var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

        if (flags == NodeFlags.Internal)
        {
            var nodeMap = (uint)(node.Map >> 32);

            if ((dataMap & bitpos) != 0)
            {
                var dataArray = NodeOps.GetDataArray<TK, TV>(node);
                ref var slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!), dataIdx);

                if (comparer.Equals(slot.Key, key))
                {
                    added = false;
                    if (isMutable)
                    {
                        slot.Value = value;
                        return node;
                    }

                    var newData = new DataSlot<TK, TV>[dataArray!.Length];
                    dataArray.AsSpan().CopyTo(newData);
                    newData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };

                    var childCap = NodeOps.GetCapacity(node.Meta);
                    var newNodeObj0 = NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, ownerId, node.Map);
                    Unsafe.As<InternalNode1<TK, TV>>(newNodeObj0).Data = newData;
                    NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(newNodeObj0));
                    return newNodeObj0;
                }

                var subNode = MergeDataSlots(slot, key, value, hash, shift + 5, comparer, ownerId);
                var newMap = ((ulong)(nodeMap | bitpos) << 32) | (dataMap & ~bitpos);

                var shrunkData = new DataSlot<TK, TV>[dataArray!.Length - 1];
                dataArray.AsSpan(0, dataIdx).CopyTo(shrunkData);
                dataArray.AsSpan(dataIdx + 1).CopyTo(shrunkData.AsSpan(dataIdx));

                var childCapCurrent = NodeOps.GetCapacity(node.Meta);
                var newNodeObj =
                    NodeOps.AllocateInternal<TK, TV>((byte)(childCapCurrent + 1), NodeFlags.Internal, ownerId, newMap);
                Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = shrunkData;

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

                var newChildNode =
                    InsertTransient(childNode, key, value, hash, shift + 5, comparer, ownerId, out added);

                if (isMutable)
                {
                    Unsafe.Add(ref firstChild, nodeIdx) = newChildNode;
                    return node;
                }

                if (ReferenceEquals(childNode, newChildNode)) return node;


                var dataArray = NodeOps.GetDataArray<TK, TV>(node);
                var childCap = NodeOps.GetCapacity(node.Meta);
                var newNodeObj = NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, ownerId, node.Map);
                Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = dataArray;

                var childSpan0 = NodeOps.GetChildSpan<TK, TV>(node);
                var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                childSpan0.CopyTo(newChildSpan);
                newChildSpan[nodeIdx] = newChildNode;
                return newNodeObj;
            }

            // Empty slot
            added = true;
            var currentData = NodeOps.GetDataArray<TK, TV>(node);
            var appendedData = new DataSlot<TK, TV>[currentData!.Length + 1];
            currentData.AsSpan(0, dataIdx).CopyTo(appendedData);
            appendedData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
            currentData.AsSpan(dataIdx).CopyTo(appendedData.AsSpan(dataIdx + 1));

            var insertMap = node.Map | bitpos;

            if (isMutable)
            {
                Unsafe.As<InternalNode1<TK, TV>>(node).Data = appendedData;
                node.Map = insertMap;
                return node;
            }

            var childCapEmpty = NodeOps.GetCapacity(node.Meta);
            var appendedNodeObj =
                NodeOps.AllocateInternal<TK, TV>(childCapEmpty, NodeFlags.Internal, ownerId, insertMap);
            Unsafe.As<InternalNode1<TK, TV>>(appendedNodeObj).Data = appendedData;
            NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(appendedNodeObj));
            return appendedNodeObj;
        }

        if ((dataMap & bitpos) != 0)
        {
            ref var firstSlot =
                ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(node).Data);
            ref var existingSlot = ref Unsafe.Add(ref firstSlot, dataIdx);

            if (comparer.Equals(existingSlot.Key, key))
            {
                added = false;
                if (isMutable)
                {
                    existingSlot.Value = value;
                    return node;
                }

                var leafCap = NodeOps.GetCapacity(node.Meta);
                var updatedLeaf = NodeOps.AllocateLeaf<TK, TV>(leafCap, NodeFlags.None, ownerId, node.Map);
                var oldLeafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
                var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
                oldLeafSpan.CopyTo(newSpan);
                newSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
                return updatedLeaf;
            }

            added = true;
            var subNode = MergeDataSlots(existingSlot, key, value, hash, shift + 5, comparer, ownerId);
            var newMap = ((ulong)bitpos << 32) | (dataMap & ~bitpos);

            var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
            var newData = new DataSlot<TK, TV>[leafSpan.Length - 1];
            leafSpan[..dataIdx].CopyTo(newData);
            leafSpan[(dataIdx + 1)..].CopyTo(newData.AsSpan(dataIdx));

            var upgradedNode = NodeOps.AllocateInternal<TK, TV>(1, NodeFlags.Internal, ownerId, newMap);
            Unsafe.As<InternalNode1<TK, TV>>(upgradedNode).Data = newData;
            NodeOps.GetChildSpan<TK, TV>(upgradedNode)[0] = subNode;
            return upgradedNode;
        }

        added = true;
        var currentLeafCap = NodeOps.GetCapacity(node.Meta);
        var expandedLeaf =
            NodeOps.AllocateLeaf<TK, TV>((byte)(currentLeafCap + 1), NodeFlags.None, ownerId, node.Map | bitpos);
        var oldLeafSpan2 = NodeOps.GetLeafDataSpan<TK, TV>(node);
        var expandedSpan = NodeOps.GetLeafDataSpan<TK, TV>(expandedLeaf);

        oldLeafSpan2[..dataIdx].CopyTo(expandedSpan);
        expandedSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
        oldLeafSpan2[dataIdx..].CopyTo(expandedSpan[(dataIdx + 1)..]);

        return expandedLeaf;
    }

    public static NodeBase? RemoveTransient<TK, TV>(NodeBase? node, TK key, int hash, int shift,
        IEqualityComparer<TK> comparer, out bool removed, ulong ownerId)
    {
        if (node == null)
        {
            removed = false;
            return null;
        }

        var flags = NodeOps.GetFlags(node.Meta);
        var isMutable = NodeOps.GetOwnerId(node.Meta) == ownerId;

        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var slots = colNode.Slots;

            for (var i = 0; i < slots.Length; i++)
                if (comparer.Equals(slots[i].Key, key))
                {
                    removed = true;
                    if (slots.Length == 1) return null;

                    var updatedSlots = new DataSlot<TK, TV>[slots.Length - 1];
                    slots.AsSpan(0, i).CopyTo(updatedSlots);
                    slots.AsSpan(i + 1).CopyTo(updatedSlots.AsSpan(i));

                    if (isMutable)
                    {
                        colNode.Slots = updatedSlots;
                        return node;
                    }

                    return new CollisionNode<TK, TV>(updatedSlots, ownerId);
                }

            removed = false;
            return node;
        }

        var bit = (hash >> shift) & 0x1F;
        var bitpos = 1u << bit;
        var dataMap = (uint)node.Map;

        if (flags == NodeFlags.Internal)
        {
            var dataArray = NodeOps.GetDataArray<TK, TV>(node);
            var nodeMap = (uint)(node.Map >> 32);

            if ((dataMap & bitpos) != 0)
            {
                var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
                ref readonly var slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dataArray!), dataIdx);

                if (comparer.Equals(slot.Key, key))
                {
                    removed = true;
                    var newMap = node.Map & ~(ulong)bitpos;
                    var childCap = NodeOps.GetCapacity(node.Meta);

                    var newData = new DataSlot<TK, TV>[dataArray!.Length - 1];
                    dataArray.AsSpan(0, dataIdx).CopyTo(newData);
                    dataArray.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

                    if (childCap == 0)
                    {
                        var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, ownerId,
                            newMap);
                        newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    if (isMutable)
                    {
                        Unsafe.As<InternalNode1<TK, TV>>(node).Data = newData;
                        node.Map = newMap;
                        return node;
                    }

                    var newNodeObj =
                        NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, ownerId, newMap);
                    Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;
                    NodeOps.GetChildSpan<TK, TV>(node).CopyTo(NodeOps.GetChildSpan<TK, TV>(newNodeObj));
                    return newNodeObj;
                }
            }

            if ((nodeMap & bitpos) != 0)
            {
                var nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));
                ref var firstChild =
                    ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(node).Children);
                var childNode = Unsafe.Add(ref firstChild, nodeIdx);

                var newChild = RemoveTransient<TK, TV>(childNode, key, hash, shift + 5, comparer, out removed, ownerId);
                if (!removed) return node;

                var childCap = NodeOps.GetCapacity(node.Meta);

                if (newChild == null)
                {
                    var newMap = node.Map & ~((ulong)bitpos << 32);

                    if (childCap == 1)
                    {
                        if (dataArray == null || dataArray.Length == 0) return null;

                        var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)dataArray.Length, NodeFlags.None,
                            ownerId, newMap);
                        dataArray.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    var newNodeObj = NodeOps.AllocateInternal<TK, TV>((byte)(childCap - 1),
                        NodeFlags.Internal, ownerId, newMap);
                    Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = dataArray;

                    var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                    var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                    childSpan[..nodeIdx].CopyTo(newChildSpan);
                    childSpan[(nodeIdx + 1)..].CopyTo(newChildSpan[nodeIdx..]);
                    return newNodeObj;
                }

                if (NodeOps.GetFlags(newChild.Meta) == NodeFlags.None)
                {
                    var childCapNew = NodeOps.GetCapacity(newChild.Meta);
                    if (childCapNew == 1)
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
                            var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None,
                                ownerId, newMap);
                            newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                            return leaf;
                        }

                        var newNodeObj = NodeOps.AllocateInternal<TK, TV>((byte)(childCap - 1),
                            NodeFlags.Internal, ownerId, newMap);
                        Unsafe.As<InternalNode1<TK, TV>>(newNodeObj).Data = newData;

                        var childSpan = NodeOps.GetChildSpan<TK, TV>(node);
                        var newChildSpan = NodeOps.GetChildSpan<TK, TV>(newNodeObj);
                        childSpan[..nodeIdx].CopyTo(newChildSpan);
                        childSpan[(nodeIdx + 1)..].CopyTo(newChildSpan[nodeIdx..]);
                        return newNodeObj;
                    }
                }

                if (isMutable)
                {
                    Unsafe.Add(ref firstChild, nodeIdx) = newChild;
                    return node;
                }

                var updatedNodeObj =
                    NodeOps.AllocateInternal<TK, TV>(childCap, NodeFlags.Internal, ownerId, node.Map);
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

        if ((dataMap & bitpos) != 0)
        {
            var dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
            ref var firstSlot =
                ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref Unsafe.As<Node1<TK, TV>>(node).Data);
            ref readonly var slot = ref Unsafe.Add(ref firstSlot, dataIdx);

            if (comparer.Equals(slot.Key, key))
            {
                removed = true;
                var leafCap = NodeOps.GetCapacity(node.Meta);
                if (leafCap == 1) return null;

                var newMap = node.Map & ~(ulong)bitpos;
                var updatedLeaf =
                    NodeOps.AllocateLeaf<TK, TV>((byte)(leafCap - 1), NodeFlags.None, ownerId, newMap);

                var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
                var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
                leafSpan[..dataIdx].CopyTo(newSpan);
                leafSpan[(dataIdx + 1)..].CopyTo(newSpan[dataIdx..]);

                return updatedLeaf;
            }
        }

        removed = false;
        return node;
    }
}