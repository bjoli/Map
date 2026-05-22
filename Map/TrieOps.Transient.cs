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

internal static partial class TrieOps
{
    // Well, if you thought the immutable Insert, this is actually the same, but it checks if the instance of TransientMap that 
    // had Insert called on it owns the node. 
    public static NodeBase InsertTransient<TK, TV>(NodeBase? node, TK key, TV value, int hash, int shift, IEqualityComparer<TK> comparer, ulong ownerId, out bool added)
    {
        // The following handles the empty case like this: create a shiny new leaf node.
        // stamp it with our `ownerId` so we know we're allowed to mutate it later.
        if (node == null)
        {
            uint bitpos0 = 1u << ((hash >> shift) & 0x1F);
            var newNode = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, ownerId, bitpos0);
            NodeOps.GetLeafDataSpan<TK,TV>(newNode)[0] = new DataSlot<TK, TV> { Key = key, Value = value };
            added = true;
            return newNode;
        }

        var flags = NodeOps.GetFlags(node.Meta);
        bool isMutable = NodeOps.GetOwnerId(node.Meta) == ownerId;

        // CollisionNode!  hunt for the key. If we find it and 
        // we own the node, just smash the new value into the array. If we don't own it, 
        // we have to copy the array over to a new node we *do* own.
        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var slots = colNode.Slots;

            for (int i = 0; i < slots.Length; i++)
            {
                if (comparer.Equals(slots[i].Key, key))
                {
                    added = false;
                    if (isMutable)
                    {
                        slots[i].Value = value; // Mutate existing array
                        return node;
                    }
                    var updatedSlots = new DataSlot<TK, TV>[slots.Length];
                    slots.AsSpan().CopyTo(updatedSlots);
                    updatedSlots[i] = new DataSlot<TK, TV> { Key = key, Value = value };
                    return new CollisionNode<TK, TV>(updatedSlots, ownerId);
                }
            }
            // Add a new value to the collision node
            added = true;
            var appendedSlots = new DataSlot<TK, TV>[slots.Length + 1];
            slots.AsSpan().CopyTo(appendedSlots);
            appendedSlots[slots.Length] = new DataSlot<TK, TV> { Key = key, Value = value };

            if (isMutable)
            {
                colNode.Slots = appendedSlots; // Swap array internally
                return node;
            }
            return new CollisionNode<TK, TV>(appendedSlots, ownerId);
        }

        int bit = (hash >> shift) & 0x1F;
        uint bitpos = 1u << bit;
        uint dataMap = (uint)node.Map;
        int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));

        // Internal Node! if we own the node, we can swap out 
        // child pointers or overwrite data slots directly. If a data slot needs to be promoted 
        // to a child node (due to a collision), we can update the node's arrays in place.
        if (flags == NodeFlags.Internal)
        {
            var internalNode = Unsafe.As<InternalNode<TK, TV>>(node);
            uint nodeMap = (uint)(node.Map >> 32);
            int nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));

            if ((dataMap & bitpos) != 0)
            {
                if (comparer.Equals(internalNode.Data[dataIdx].Key, key))
                {
                    added = false;
                    if (isMutable)
                    {
                        internalNode.Data[dataIdx].Value = value;
                        return node;
                    }

                    var newData = new DataSlot<TK, TV>[internalNode.Data.Length];
                    internalNode.Data.AsSpan().CopyTo(newData);
                    newData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
                    return new InternalNode<TK, TV>(newData, internalNode.Nodes, ownerId) { Map = node.Map };
                }

                var subNode = MergeDataSlots(internalNode.Data[dataIdx], key, value, hash, shift + 5, comparer, ownerId);
                ulong newMap = ((ulong)(nodeMap | bitpos) << 32) | (dataMap & ~bitpos);

                var shrunkData = new DataSlot<TK, TV>[internalNode.Data.Length - 1];
                internalNode.Data.AsSpan(0, dataIdx).CopyTo(shrunkData);
                internalNode.Data.AsSpan(dataIdx + 1).CopyTo(shrunkData.AsSpan(dataIdx));

                var expandedNodes = new NodeBase[internalNode.Nodes.Length + 1];
                internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(expandedNodes);
                expandedNodes[nodeIdx] = subNode;
                internalNode.Nodes.AsSpan(nodeIdx).CopyTo(expandedNodes.AsSpan(nodeIdx + 1));
                added = true;
                if (isMutable)
                {
                    internalNode.Data = shrunkData;
                    internalNode.Nodes = expandedNodes;
                    internalNode.Map = newMap;
                    return node;
                }
                return new InternalNode<TK, TV>(shrunkData, expandedNodes, ownerId) { Map = newMap };
            }

            if ((nodeMap & bitpos) != 0)
            {
                var childNode = internalNode.Nodes[nodeIdx];
                var newChildNode = InsertTransient(childNode, key, value, hash, shift + 5, comparer, ownerId, out added);

                if (isMutable)
                {
                    internalNode.Nodes[nodeIdx] = newChildNode;
                    return node;
                }

                if (ReferenceEquals(childNode, newChildNode)) return node;

                var newNodes = new NodeBase[internalNode.Nodes.Length];
                internalNode.Nodes.AsSpan().CopyTo(newNodes);
                newNodes[nodeIdx] = newChildNode;
                return new InternalNode<TK, TV>(internalNode.Data, newNodes, ownerId) { Map = node.Map };
            }


            added = true;
            var appendedData = new DataSlot<TK, TV>[internalNode.Data.Length + 1];
            internalNode.Data.AsSpan(0, dataIdx).CopyTo(appendedData);
            appendedData[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
            internalNode.Data.AsSpan(dataIdx).CopyTo(appendedData.AsSpan(dataIdx + 1));
            
            ulong insertMap = node.Map | bitpos;
            
            if (isMutable)
            {
                internalNode.Data = appendedData;
                internalNode.Map = insertMap;
                return node;
            }
            return new InternalNode<TK, TV>(appendedData, internalNode.Nodes, ownerId) { Map = insertMap };
        }

        // Leaf nodes. if the spot is already taken, we update it in place.
        // Sadly, if we need to add a *new* item to a leaf, we still have to allocate a whole new leaf, because currently
        // I only do exact fit nodes. 
        
        var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);

        if ((dataMap & bitpos) != 0)
        {
            if (comparer.Equals(leafSpan[dataIdx].Key, key))
            {
                added = false;
                if (isMutable)
                {
                    NodeOps.GetLeafDataSpan<TK, TV>(node)[dataIdx].Value = value; // Mutate span directly
                    return node;
                }
                var updatedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)leafSpan.Length, NodeFlags.None, ownerId, node.Map);
                var newSpan = NodeOps.GetLeafDataSpan<TK, TV>(updatedLeaf);
                leafSpan.CopyTo(newSpan);
                newSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
                return updatedLeaf;
            }

            added = true;
            var subNode = MergeDataSlots(leafSpan[dataIdx], key, value, hash, shift + 5, comparer, ownerId);
            ulong newMap = ((ulong)bitpos << 32) | (dataMap & ~bitpos);
            
            var newData = new DataSlot<TK, TV>[leafSpan.Length - 1];
            leafSpan[..dataIdx].CopyTo(newData);
            leafSpan[(dataIdx + 1)..].CopyTo(newData.AsSpan(dataIdx));

            // Upgrading from Leaf to Internal always requires allocation
            return new InternalNode<TK, TV>(newData, new[] { subNode }, ownerId) { Map = newMap };
        }

        added = true;
        // Inline expansion always requires allocation since memory size is bound to the LeafType
        var expandedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)(leafSpan.Length + 1), NodeFlags.None, ownerId, node.Map | bitpos);
        var expandedSpan = NodeOps.GetLeafDataSpan<TK, TV>(expandedLeaf);
        
        leafSpan[..dataIdx].CopyTo(expandedSpan);
        expandedSpan[dataIdx] = new DataSlot<TK, TV> { Key = key, Value = value };
        leafSpan[dataIdx..].CopyTo(expandedSpan[(dataIdx + 1)..]);
        
        return expandedLeaf;
    }
    
    public static NodeBase? RemoveTransient<TK, TV>(NodeBase? node, TK key, int hash, int shift, IEqualityComparer<TK> comparer, out bool removed, ulong ownerId)
    {
        if (node == null)
        {
            removed = false;
            return null;
        }

        var flags = NodeOps.GetFlags(node.Meta);
        bool isMutable = NodeOps.GetOwnerId(node.Meta) == ownerId;

        if (flags == NodeFlags.Collision)
        {
            var colNode = Unsafe.As<CollisionNode<TK, TV>>(node);
            var slots = colNode.Slots;
            
            for (int i = 0; i < slots.Length; i++)
            {
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
            }
            
            removed = false;
            return node;
        }

        int bit = (hash >> shift) & 0x1F;
        uint bitpos = 1u << bit;
        uint dataMap = (uint)node.Map;

        if (flags == NodeFlags.Internal)
        {
            var internalNode = Unsafe.As<InternalNode<TK, TV>>(node);
            uint nodeMap = (uint)(node.Map >> 32);

            if ((dataMap & bitpos) != 0)
            {
                int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
                
                if (comparer.Equals(internalNode.Data[dataIdx].Key, key))
                {
                    removed = true;
                    ulong newMap = node.Map & ~((ulong)bitpos);
                    
                    var newData = new DataSlot<TK, TV>[internalNode.Data.Length - 1];
                    internalNode.Data.AsSpan(0, dataIdx).CopyTo(newData);
                    internalNode.Data.AsSpan(dataIdx + 1).CopyTo(newData.AsSpan(dataIdx));

                    if (internalNode.Nodes.Length == 0)
                    {
                        var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, ownerId, newMap);
                        newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    if (isMutable)
                    {
                        internalNode.Data = newData;
                        internalNode.Map = newMap;
                        return node;
                    }
                    return new InternalNode<TK, TV>(newData, internalNode.Nodes, ownerId) { Map = newMap };
                }
            }
            
            if ((nodeMap & bitpos) != 0)
            {
                int nodeIdx = BitOperations.PopCount(nodeMap & (bitpos - 1));
                var childNode = internalNode.Nodes[nodeIdx];
                var newChild = RemoveTransient<TK,TV>(childNode, key, hash, shift + 5, comparer, out removed, ownerId);

                if (!removed) return node;

                if (newChild == null)
                {
                    ulong newMap = node.Map & ~(((ulong)bitpos) << 32);

                    if (internalNode.Nodes.Length == 1)
                    {
                        if (internalNode.Data.Length == 0) return null;

                        var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)internalNode.Data.Length, NodeFlags.None, ownerId, newMap);
                        internalNode.Data.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                        return leaf;
                    }

                    var newNodes = new NodeBase[internalNode.Nodes.Length - 1];
                    internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(newNodes);
                    internalNode.Nodes.AsSpan(nodeIdx + 1).CopyTo(newNodes.AsSpan(nodeIdx));
                    
                    if (isMutable)
                    {
                        internalNode.Nodes = newNodes;
                        internalNode.Map = newMap;
                        return node;
                    }
                    return new InternalNode<TK, TV>(internalNode.Data, newNodes, ownerId) { Map = newMap };
                }

                // CHAMP Compaction
                if (NodeOps.GetFlags(newChild.Meta) == NodeFlags.None)
                {
                    byte childCap = NodeOps.GetCapacity(newChild.Meta);
                    if (childCap == 1)
                    {
                        var singleData = NodeOps.GetLeafDataSpan<TK, TV>(newChild)[0];
                        
                        ulong newMap = (node.Map & ~(((ulong)bitpos) << 32)) | bitpos;
                        int newDataIdx = BitOperations.PopCount((uint)newMap & (bitpos - 1));

                        var newData = new DataSlot<TK, TV>[internalNode.Data.Length + 1];
                        internalNode.Data.AsSpan(0, newDataIdx).CopyTo(newData);
                        newData[newDataIdx] = singleData;
                        internalNode.Data.AsSpan(newDataIdx).CopyTo(newData.AsSpan(newDataIdx + 1));

                        var newNodes = new NodeBase[internalNode.Nodes.Length - 1];
                        internalNode.Nodes.AsSpan(0, nodeIdx).CopyTo(newNodes);
                        internalNode.Nodes.AsSpan(nodeIdx + 1).CopyTo(newNodes.AsSpan(nodeIdx));

                        if (newNodes.Length == 0)
                        {
                            var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)newData.Length, NodeFlags.None, ownerId, newMap);
                            newData.CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
                            return leaf;
                        }

                        if (isMutable)
                        {
                            internalNode.Data = newData;
                            internalNode.Nodes = newNodes;
                            internalNode.Map = newMap;
                            return node;
                        }
                        return new InternalNode<TK, TV>(newData, newNodes, ownerId) { Map = newMap };
                    }
                }

                if (isMutable)
                {
                    internalNode.Nodes[nodeIdx] = newChild;
                    return node;
                }
                var updatedNodes = new NodeBase[internalNode.Nodes.Length];
                internalNode.Nodes.AsSpan().CopyTo(updatedNodes);
                updatedNodes[nodeIdx] = newChild;
                return new InternalNode<TK, TV>(internalNode.Data, updatedNodes, ownerId) { Map = node.Map };
            }

            removed = false;
            return node;
        }

        // --- Leaf Node Phase ---
        if ((dataMap & bitpos) != 0)
        {
            int dataIdx = BitOperations.PopCount(dataMap & (bitpos - 1));
            var leafSpan = NodeOps.GetLeafDataSpan<TK, TV>(node);
            
            if (comparer.Equals(leafSpan[dataIdx].Key, key))
            {
                removed = true;
                if (leafSpan.Length == 1) return null;

                ulong newMap = node.Map & ~((ulong)bitpos);
                // Cannot shrink Leaf types in-place; must allocate the properly sized struct
                var updatedLeaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)(leafSpan.Length - 1), NodeFlags.None, ownerId, newMap);
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
