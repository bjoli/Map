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

namespace Map;

internal static partial class TrieOps
{
    // Look, I know this method is a bit of a monster. I apologize for the sheer amount of code here,
    // but the following handles inserting a key-value pair into the trie like this: we navigate down 
    // the tree using chunks of the hash, and depending on whether we hit an empty spot, a leaf, or an 
    // internal node, we either slot it right in or do some painful allocations to expand the node. 
    // It's not pretty, but it avoids even worse performance.
    public static NodeBase Insert<TK, TV>(NodeBase? node, TK key, TV value, int hash, int shift, IEqualityComparer<TK> comparer, out bool added)
{
    // The following handles the base case of an empty tree like this: we just allocate a single 
    // leaf node, stuff our data in there, and call it a day.
    if (node == null)
    {
        uint bitpos0 = 1u << ((hash >> shift) & 0x1F);
        var newNode = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitpos0);
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
        int length = oldSlots.Length;

        for (int i = 0; i < length; i++)
        {
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
        }

        var appendedSlots = new DataSlot<TK, TV>[length + 1];
        //Array.Copy(oldSlots, appendedSlots, length);
        oldSlots.AsSpan(0, length).CopyTo(appendedSlots);
        appendedSlots[length] = DataSlot<TK, TV>.Data(key, value);
        added = true;
        return new CollisionNode<TK, TV>(appendedSlots);
    }

    int bit = (hash >> shift) & 0x1F;
    uint bitpos = 1u << bit;
    uint dataMap = (uint)node.Map;
    int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

    // The Interal node is actually my least favourite node. Her we 
    // check the bitmaps to see if the hash fragment points to local data or a child node.
    if (flags == NodeFlags.Internal)
    {
        var internalNode = Unsafe.As<InternalNode<TK, TV>>(node);
        uint nodeMap = (uint)(node.Map >> 32);
        int nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

        // The following handles updating existing local data like this: if the spot is taken by a data slot,
        // we check if the keys match. If they don't, it's a collision at this depth, so we promote the 
        // data slot to a child node. I hope kids still think short array copying is cool.
        if ((dataMap & bitpos) != 0)
        {
            ref readonly var existingSlot = ref internalNode.Data[dataIdx];

            if (comparer.Equals(existingSlot.Key, key))
            {
                added = false;
                if (EqualityComparer<TV>.Default.Equals(existingSlot.Value, value)) return node;
                
                var newData0 = new DataSlot<TK, TV>[internalNode.Data.Length];
                //Array.Copy(internalNode.Data, newData0, internalNode.Data.Length);
                internalNode.Data.AsSpan(0, internalNode.Data.Length).CopyTo(newData0);
                newData0[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
                
                return new InternalNode<TK, TV>(newData0, internalNode.Nodes) { Map = node.Map };
            }

            // Data collision -> Promote data slot to sub-node
            var subNode = MergeDataSlots(existingSlot, key, value, hash, shift + 5, comparer);
            ulong newMap = ((ulong)(nodeMap | bitpos) << 32) | (dataMap & ~bitpos);
            
            var newData = new DataSlot<TK, TV>[internalNode.Data.Length - 1];
            //Array.Copy(internalNode.Data, 0, newData, 0, dataIdx);
            internalNode.Data.AsSpan(0, dataIdx).CopyTo(newData);
            internalNode.Data.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

            var newNodes = new NodeBase[internalNode.Nodes.Length + 1];
            // Array.Copy(internalNode.Nodes, 0, newNodes, 0, nodeIdx);
            internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(newNodes);
            newNodes[nodeIdx] = subNode;
            internalNode.Nodes.AsSpan(nodeIdx).CopyTo(newNodes.AsSpan(nodeIdx + 1));
            added = true;
            return new InternalNode<TK, TV>(newData, newNodes) { Map = newMap };
        }

        // The following handles routing to a child node: if the spot points to a child,
        // we just recurse down into it and let it figure out the rest.
        if ((nodeMap & bitpos) != 0)
        {
            var childNode = internalNode.Nodes[nodeIdx];
            var newChildNode = Insert(childNode, key, value, hash, shift + 5, comparer, out added);

            if (ReferenceEquals(childNode, newChildNode)) return node;

            var newNodes = new NodeBase[internalNode.Nodes.Length];
            //Array.Copy(internalNode.Nodes, newNodes, internalNode.Nodes.Length);
            internalNode.Nodes.AsSpan().CopyTo(newNodes);
            newNodes[nodeIdx] = newChildNode;
            
            return new InternalNode<TK, TV>(internalNode.Data, newNodes) { Map = node.Map };
        }

        // An empty slot!  we just tack our new data right into the node's
        // data array.
        added = true;
        var appendedData = new DataSlot<TK, TV>[internalNode.Data.Length + 1];
        //Array.Copy(internalNode.Data, 0, appendedData, 0, dataIdx);
        internalNode.Data.AsSpan(0, dataIdx).CopyTo(appendedData);
        appendedData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
        
        internalNode.Data.AsSpan(dataIdx).CopyTo(appendedData.AsSpan(dataIdx + 1));
        
        return new InternalNode<TK, TV>(appendedData, internalNode.Nodes) { Map = node.Map | bitpos };
    }

    // The following handles leaf nodes like this: it's basically a cut-down version of the internal node 
    // logic, since leaves don't have sub-nodes. Sorry for the code duplication.
    var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);

    if ((dataMap & bitpos) != 0)
    {
        ref readonly var existingSlot = ref leafSpan[dataIdx];

        if (comparer.Equals(existingSlot.Key, key))
        {
            added = false;
            if (EqualityComparer<TV>.Default.Equals(existingSlot.Value, value)) return node;
            
            var updatedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)leafSpan.Length, NodeFlags.None, 0, node.Map);
            var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
            leafSpan.CopyTo(newSpan);
            newSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
            return updatedLeaf;
        }

        // Leaf collision -> Must upgrade to InternalNode
        added = true;
        var subNode = MergeDataSlots(existingSlot, key, value, hash, shift + 5, comparer);
        ulong newMap = ((ulong)bitpos << 32) | (dataMap & ~bitpos);
        
        var newData = new DataSlot<TK, TV>[leafSpan.Length - 1];
        leafSpan[..dataIdx].CopyTo(newData);
        leafSpan[(dataIdx + 1)..].CopyTo(newData.AsSpan(dataIdx));

        var newNodes = new[] { subNode };
        
        return new InternalNode<TK, TV>(newData, newNodes) { Map = newMap };
    }

    // Empty slot in LeafNode -> Append inline
    added = true;
    var expandedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)(leafSpan.Length + 1), NodeFlags.None, 0, node.Map | bitpos);
    var expandedSpan = NodeOps.GetLeafDataSpan<TK, TV>(expandedLeaf);
    
    leafSpan[..dataIdx].CopyTo(expandedSpan);
    expandedSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
    leafSpan[dataIdx..].CopyTo(expandedSpan[(dataIdx + 1)..]);

    return expandedLeaf;
}

    // The following handles merging two values that hash to the same spot. 
    // it figures out how deep we have to go before their hashes finally differ, and builds 
    // up the necessary nodes along the way.
    private static NodeBase MergeDataSlots<TK, TV>(DataSlot<TK, TV> existingSlot, TK newKey, TV newValue, int newHash,
        int shift, IEqualityComparer<TK> comparer, ulong ownerId = 0)
    {
        int existingHash = comparer.GetHashCode(existingSlot.Key!);

        // 1. Full 32-bit hash collision
        if (existingHash == newHash)
        {
            return new CollisionNode<TK, TV>(new[]
            {
                existingSlot,
                new DataSlot<TK, TV> { Key = newKey, Value = newValue }
            }, ownerId);
        }

        int existingBit = (existingHash >> shift) & 0x1F;
        int newBit = (newHash >> shift) & 0x1F;

        // 2. Hashes diverge at this specific bit depth
        if (existingBit != newBit)
        {
            uint dataMap = (1u << existingBit) | (1u << newBit);
            var leafNode = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(2, NodeFlags.None, ownerId, dataMap);
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
        uint nodeMap = 1u << existingBit;
        var subNode = MergeDataSlots(existingSlot, newKey, newValue, newHash, shift + 5, comparer, ownerId);
    
    return new InternalNode<TK, TV>(Array.Empty<DataSlot<TK, TV>>(), new[] { subNode }, ownerId)
    {
        Map = (ulong)nodeMap << 32
    };
}
    
// Key lookup! it zips down the tree, decoding the bitmap
// at each level to figure out exactly which array index holds our data or next node.
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryGetValue<TK, TV>(NodeBase? node, TK key, int hash, IEqualityComparer<TK> comparer, out TV value)
{
    int shift = 0;
    NodeBase? current = node;

    while (current != null)
    {
        ulong meta = current.Meta;
        // Directly pull flags from bit 8 without calling out to helper functions.
        // I don't know why, but this produces better code after the kit has done it's work. 
        byte flags = (byte)((meta >> 8) & 0xFF);

        if (flags == (byte)NodeFlags.Collision)
        {
            var slots = Unsafe.As<CollisionNode<TK, TV>>(current).Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (comparer.Equals(slots[i].Key, key))
                {
                    value = slots[i].Value;
                    return true;
                }
            }
            break;
        }

        int bit = (hash >> shift) & 0x1F;
        uint bitpos = 1u << bit;
        ulong map = current.Map;
        uint dataMap = (uint)map;

        if (flags == (byte)NodeFlags.Internal)
        {
            var internalNode = Unsafe.As<InternalNode<TK, TV>>(current);
            
            if ((dataMap & bitpos) != 0)
            {
                int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
                ref readonly var slot = ref internalNode.Data[dataIdx];

                if (comparer.Equals(slot.Key, key))
                {
                    value = slot.Value;
                    return true;
                }
                break;
            }

            uint nodeMap = (uint)(map >> 32);
            if ((nodeMap & bitpos) != 0)
            {
                int nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1)); 
                current = internalNode.Nodes[nodeIdx];
                shift += 5;
                continue;
            }
            break;
        }

        // Use inline bit masks to pull index position instantly
        // There are no flags here, meaning we must be at a leaf.
        if ((dataMap & bitpos) != 0)
        {
            int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
            ref readonly var slot = ref NodeOps.GetLeafDataSpan<TK, TV>(current)[dataIdx];

            if (comparer.Equals(slot.Key, key))
            {
                value = slot.Value;
                return true;
            }
        }
        
        break; // Leaves never have sub-nodes
    }

    value = default!;
    return false;
}


// The following handles deleting a key from the trie. it hunts down the node, strips out
// the data slot, and then painstakingly re-compacts the tree if a node becomes too sparse. 
// It's a complete nightmare to read, and for that, I am not that sorry.
public static NodeBase? Remove<TK, TV>(NodeBase? node, TK key, int hash, int shift, IEqualityComparer<TK> comparer, out bool removed)
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
        
        for (int i = 0; i < slots.Length; i++)
        {
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
        }
        
        removed = false;
        return node;
    }

    int bit = (hash >> shift) & 0x1F;
    uint bitpos = 1u << bit;
    uint dataMap = (uint)node.Map;

    // Phase 2: Internal Node
    if (flags == NodeFlags.Internal)
    {
        var internalNode = Unsafe.As<InternalNode<TK, TV>>(node);
        uint nodeMap = (uint)(node.Map >> 32);

        // Case 2a: Key is in the Data array
        if ((dataMap & bitpos) != 0)
        {
            int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
            
            if (comparer.Equals(internalNode.Data[dataIdx].Key, key))
            {
                removed = true;
                ulong newMap = node.Map & ~((ulong)bitpos);
                
                if (internalNode.Data.Length == 1 && internalNode.Nodes.Length == 0)
                    return null; 

                var newData = new DataSlot<TK, TV>[internalNode.Data.Length - 1];
                //Array.Copy(internalNode.Data, 0, newData, 0, dataIdx);
                internalNode.Data.AsSpan(0, dataIdx).CopyTo(newData);
                internalNode.Data.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

                // Downgrade to LeafNode if no sub-nodes remain
                if (internalNode.Nodes.Length == 0)
                {
                    var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, 0, newMap);
                    newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                    return leaf;
                }

                return new InternalNode<TK, TV>(newData, internalNode.Nodes) { Map = newMap };
            }
        }
        
        // Case 2b: Key is routed to a Sub-Node
        if ((nodeMap & bitpos) != 0)
        {
            int nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));
            var childNode = internalNode.Nodes[nodeIdx];
            NodeBase? newChild = Remove<TK,TV>(childNode, key, hash, shift + 5, comparer, out removed);

            if (!removed) return node;

            // Sub-node became completely empty
            if (newChild == null)
            {
                ulong newMap = node.Map & ~(((ulong)bitpos) << 32);

                if (internalNode.Nodes.Length == 1)
                {
                    if (internalNode.Data.Length == 0) return null;

                    var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)internalNode.Data.Length, NodeFlags.None, 0, newMap);
                    internalNode.Data.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                    return leaf;
                }

                var newNodes = new NodeBase[internalNode.Nodes.Length - 1];
                //Array.Copy(internalNode.Nodes, 0, newNodes, 0, nodeIdx);
                internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(newNodes);
                internalNode.Nodes.AsSpan(nodeIdx + 1).CopyTo(newNodes.AsSpan(nodeIdx));
                return new InternalNode<TK, TV>(internalNode.Data, newNodes) { Map = newMap };
            }

            // CHAMP Compaction: If sub-node shrunk to a single data element, pull it up to this node
            if (NodeOps.GetFlags(newChild.Meta) == NodeFlags.None)
            {
                byte childCap = NodeOps.GetCapacity(newChild.Meta);
                if (childCap == 1)
                {
                    var singleData = NodeOps.GetLeafDataSpan<TK, TV>(newChild)[0];
                    
                    // Flip the bit from NodeMap to DataMap
                    ulong newMap = (node.Map & ~(((ulong)bitpos) << 32)) | bitpos;
                    int newDataIdx = BitOperations.PopCount((uint)newMap & (bitpos - 1));

                    var newData = new DataSlot<TK, TV>[internalNode.Data.Length + 1];
                    //Array.Copy(internalNode.Data, 0, newData, 0, newDataIdx);
                    internalNode.Data.AsSpan(0, newDataIdx).CopyTo(newData);
                    newData[newDataIdx] = singleData;
                    internalNode.Data.AsSpan(newDataIdx).CopyTo(newData.AsSpan(newDataIdx + 1));

                    var newNodes = new NodeBase[internalNode.Nodes.Length - 1];
                    // Array.Copy(internalNode.Nodes, 0, newNodes, 0, nodeIdx);
                    internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(newNodes);
                    //Array.Copy(internalNode.Nodes, nodeIdx + 1, newNodes, nodeIdx, internalNode.Nodes.Length - nodeIdx - 1);
                    internalNode.Nodes.AsSpan(nodeIdx + 1, internalNode.Nodes.Length - nodeIdx - 1)
                        .CopyTo(newNodes.AsSpan(nodeIdx));
                    if (newNodes.Length == 0)
                    {
                        var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, 0, newMap);
                        newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    return new InternalNode<TK, TV>(newData, newNodes) { Map = newMap };
                }
            }

            // Normal sub-node replacement
            var updatedNodes = new NodeBase[internalNode.Nodes.Length];
            //Array.Copy(internalNode.Nodes, updatedNodes, internalNode.Nodes.Length);
            internalNode.Nodes.AsSpan(0, internalNode.Nodes.Length).CopyTo(updatedNodes);
            updatedNodes[nodeIdx] = newChild;
            return new InternalNode<TK, TV>(internalNode.Data, updatedNodes) { Map = node.Map };
        }

        removed = false;
        return node;
    }

    // Phase 3: Leaf Node
    if ((dataMap & bitpos) != 0)
    {
        int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
        var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
        
        if (comparer.Equals(leafSpan[dataIdx].Key, key))
        {
            removed = true;
            if (leafSpan.Length == 1) return null;

            ulong newMap = node.Map & ~((ulong)bitpos);
            var updatedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)(leafSpan.Length - 1), NodeFlags.None, 0, newMap);
            var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
            
            leafSpan[..dataIdx].CopyTo(newSpan);
            leafSpan[(dataIdx + 1)..].CopyTo(newSpan[dataIdx..]);
            
            return updatedLeaf;
        }
    }

    removed = false;
    return node;
}

    public static bool IterFast<TK, TV, TAction>(NodeBase? node, ref TAction action) 
        where TAction : struct, IKeyValueAction<TK, TV>
    {
        if (node == null) return true;

        var flags = NodeOps.GetFlags(node.Meta);

        if (flags == NodeFlags.None)
        {
            var span = NodeOps.GetLeafDataSpan<TK, TV>(node);
            for (int i = 0; i < span.Length; i++)
            {
                if (!action.Invoke(span[i].Key, span[i].Value)) return false;
            }
            return true;
        }
        
        if (flags == NodeFlags.Internal)
        {
            var internalNode = Unsafe.As<InternalNode<TK, TV>>(node);
            
            for (int i = 0; i < internalNode.Data.Length; i++)
            {
                if (!action.Invoke(internalNode.Data[i].Key, internalNode.Data[i].Value)) return false;
            }
            
            for (int i = 0; i < internalNode.Nodes.Length; i++)
            {
                if (!IterFast<TK, TV, TAction>(internalNode.Nodes[i], ref action)) return false;
            }
            
            return true;
        }

        // CollisionNode
        var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
        for (int i = 0; i < colNode.Slots.Length; i++)
        {
            if (!action.Invoke(colNode.Slots[i].Key, colNode.Slots[i].Value)) return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Pure structural merge of two CHAMP nodes
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
            NodeBase current = node2;
            foreach (var slot in col1.Slots)
            {
                int h = comparer.GetHashCode(slot.Key!);
                if (TryGetValue(node2, slot.Key, h, comparer, out TV existingVal2))
                {
                    TV resolvedVal = conflictResolver != null ? conflictResolver(slot.Key, slot.Value, existingVal2) : existingVal2;
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
            NodeBase current = node1;
            foreach (var slot in col2.Slots)
            {
                int h = comparer.GetHashCode(slot.Key!);
                if (TryGetValue(node1, slot.Key, h, comparer, out TV existingVal1))
                {
                    TV resolvedVal = conflictResolver != null ? conflictResolver(slot.Key, existingVal1, slot.Value) : slot.Value;
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
            uint map1 = (uint)node1.Map;
            uint map2 = (uint)node2.Map;
            if ((map1 & map2) == 0)
            {
                uint combinedMap = map1 | map2;
                int totalCount = BitOperations.PopCount(combinedMap);
                var newLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)totalCount, NodeFlags.None, 0, combinedMap);
                var destSpan = NodeOps.GetLeafDataSpan<TK, TV>(newLeaf);
                var span = NodeOps.GetLeafDataSpan<TK, TV>(node1);
                var span0 = NodeOps.GetLeafDataSpan<TK, TV>(node2);

                int idx1 = 0, idx2 = 0, destIdx = 0;
                uint tempMap = combinedMap;
                while (tempMap != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(tempMap);
                    uint bitpos = 1u << bit;
                    if ((map1 & bitpos) != 0)
                        destSpan[destIdx++] = span[idx1++];
                    else
                        destSpan[destIdx++] = span0[idx2++];
                    tempMap &= ~bitpos;
                }
                return newLeaf;
            }
        }

        uint dataMap1 = (uint)node1.Map;
        uint nodeMap1 = (flags1 == NodeFlags.Internal) ? (uint)(node1.Map >> 32) : 0;
        var span1 = (flags1 == NodeFlags.Internal) 
            ? Unsafe.As<InternalNode<TK, TV>>(node1).Data.AsSpan() 
            : NodeOps.GetLeafDataSpan<TK, TV>(node1);
        var nodes1 = (flags1 == NodeFlags.Internal) 
            ? Unsafe.As<InternalNode<TK, TV>>(node1).Nodes 
            : Array.Empty<NodeBase>();

        uint dataMap2 = (uint)node2.Map;
        uint nodeMap2 = (flags2 == NodeFlags.Internal) ? (uint)(node2.Map >> 32) : 0;
        var span2 = (flags2 == NodeFlags.Internal) 
            ? Unsafe.As<InternalNode<TK, TV>>(node2).Data.AsSpan() 
            : NodeOps.GetLeafDataSpan<TK, TV>(node2);
        var nodes2 = (flags2 == NodeFlags.Internal) 
            ? Unsafe.As<InternalNode<TK, TV>>(node2).Nodes 
            : Array.Empty<NodeBase>();

        uint allBits = dataMap1 | nodeMap1 | dataMap2 | nodeMap2;

        var pooledData = ArrayPool<DataSlot<TK, TV>>.Shared.Rent(32);
        var pooledNodes = ArrayPool<NodeBase>.Shared.Rent(32);

        int dataCount = 0;
        int nodeCount = 0;
        ulong finalDataMap = 0;
        ulong finalNodeMap = 0;

        uint tempBits = allBits;
        while (tempBits != 0)
        {
            int bit = BitOperations.TrailingZeroCount(tempBits);
            uint bitpos = 1u << bit;
            tempBits &= ~bitpos;

            bool hasData1 = (dataMap1 & bitpos) != 0;
            bool hasNode1 = (nodeMap1 & bitpos) != 0;
            bool hasData2 = (dataMap2 & bitpos) != 0;
            bool hasNode2 = (nodeMap2 & bitpos) != 0;

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
                    TV resolvedVal = conflictResolver != null ? conflictResolver(d1.Key, d1.Value, d2.Value) : d2.Value;
                    pooledData[dataCount++] = DataSlot<TK, TV>.Data(d1.Key, resolvedVal); 
                    finalDataMap |= bitpos;
                }
                else
                {
                    int h2 = comparer.GetHashCode(d2.Key!);
                    var subNode = MergeDataSlots(d1, d2.Key, d2.Value, h2, shift + 5, comparer);
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
                int h1 = comparer.GetHashCode(d1.Key!);
                uint bitposNext = 1u << ((h1 >> (shift + 5)) & 0x1F);
                var microLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitposNext);
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
                int h2 = comparer.GetHashCode(d2.Key!);
                uint bitposNext = 1u << ((h2 >> (shift + 5)) & 0x1F);
                var microLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, 0, bitposNext);
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
            resultNode = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, 0, finalDataMap);
            pooledData.AsSpan(0, dataCount).CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(resultNode));
        }
        else
        {
            var finalData = new DataSlot<TK, TV>[dataCount];
            pooledData.AsSpan(0, dataCount).CopyTo(finalData);

            var finalNodes = new NodeBase[nodeCount];
            pooledNodes.AsSpan(0, nodeCount).CopyTo(finalNodes);

            resultNode = new InternalNode<TK, TV>(finalData, finalNodes)
            {
                Map = finalDataMap | (finalNodeMap << 32)
            };
        }

        ArrayPool<DataSlot<TK, TV>>.Shared.Return(pooledData);
        ArrayPool<NodeBase>.Shared.Return(pooledNodes);

        return resultNode;
    }
    
    
    public static bool Iter<K, V>(NodeBase? node, Func<K, V, bool> action)
    {
        if (node == null) return true;

        var flags = NodeOps.GetFlags(node.Meta);

        if (flags == NodeFlags.None)
        {
            var span = NodeOps.GetLeafDataSpan<K, V>(node);
            for (int i = 0; i < span.Length; i++)
            {
                if (!action(span[i].Key, span[i].Value)) return false;
            }
            return true;
        }

        if (flags == NodeFlags.Internal)
        {
            var internalNode = Unsafe.As<InternalNode<K, V>>(node);

            for (int i = 0; i < internalNode.Data.Length; i++)
            {
                if (!action(internalNode.Data[i].Key, internalNode.Data[i].Value)) return false;
            }

            for (int i = 0; i < internalNode.Nodes.Length; i++)
            {
                if (!Iter<K, V>(internalNode.Nodes[i], action)) return false;
            }

            return true;
        }

        // CollisionNode
        var colNode = Unsafe.As<CollisionNode<K, V>>(node);
        for (int i = 0; i < colNode.Slots.Length; i++)
        {
            if (!action(colNode.Slots[i].Key, colNode.Slots[i].Value)) return false;
        }

        return true;
    }

}
