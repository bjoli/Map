/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */


using System.Runtime.CompilerServices;

namespace Map;

// The DataSlotBuffer and NodeBaseBuffer structs are tricks
// to get stack-allocated arrays of a fixed size (32).
// This is faster than heap allocation for temporary buffers.
[InlineArray(32)]
internal struct DataSlotBuffer<TK, TV> 
{ 
    private DataSlot<TK, TV> _element0; 
}

[InlineArray(32)]
internal struct NodeBaseBuffer 
{ 
    private NodeBase _element0; 
}

// A temporary storage for key-value pairs and their hash codes.
internal struct BuilderEntry<TK, TV>
{
    public int Hash;
    public TK Key;
    public TV Value;
}

/// <summary>
/// The MapBuilder is a mutable helper for creating an immutable Map.
/// It's more efficient to add items to this builder first and then
/// convert it to a Map in one go, rather than creating a new Map for each addition.
/// </summary>
public sealed class MapBuilder<TK, TV>
{
    private BuilderEntry<TK, TV>[] _entries;
    private int _count;
    private readonly IEqualityComparer<TK> _comparer;

    /// <summary>
    /// Creates a new MapBuilder.
    /// You can optionally provide a custom comparer for keys and an initial capacity.
    /// </summary>
    public MapBuilder(IEqualityComparer<TK>? comparer = null, int initialCapacity = 16)
    {
        // We allocate an array to hold the entries.
        // GC.AllocateUninitializedArray is a performance trick to avoid zeroing out the memory.
        _entries = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(initialCapacity);
        _comparer = comparer ?? EqualityComparer<TK>.Default;
    }

    /// <summary>
    /// Adds a key-value pair to the builder.
    /// If the internal array is full, it will be resized.
    /// </summary>
    public void Add(TK key, TV value)
    {
        // If we've run out of space, double the size of the array.
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        // Calculate the hash for the key and store the entry.
        _entries[_count] = new BuilderEntry<TK, TV>
        {
            Hash = _comparer.GetHashCode(key!),
            Key = key,
            Value = value
        };
        
        _count++;
    }

    /// <summary>
    /// Converts the builder's contents into an immutable Map.
    /// This is the main "build" step.
    /// </summary>
    public Map<TK, TV> ToImmutable()
    {
        if (_count == 0) return Map<TK, TV>.Empty;

        // First, we sort all the entries by their hash codes.
        // This is crucial for building the CHAMP trie efficiently.
        // As my kindergarten teacher always said:
        // "If you can use a radix sort, you probably should".
        SortByChampHash(_entries, _count);

        var span = _entries.AsSpan(0, _count);
        int finalCount = 0;
        // Then, we recursively build the nodes of the trie.
        NodeBase root = BuildNode(span, 0, _comparer, ref finalCount);

        // Finally, we create the immutable Map with the new trie.
        return new Map<TK, TV>(root, _comparer, finalCount);
    }

    /// <summary>
    /// This is a highly optimized Radix Sort implementation.
    /// It sorts the entries based on their hash codes, which is essential for the CHAMP trie structure.
    /// It sorts the array by looking at 5-bit chunks of the hash code at a time.
    /// </summary>
    private static void SortByChampHash(BuilderEntry<TK, TV>[] entries, int count)
{
    // Pre-allocate a scratch buffer of identical size without paying the CPU penalty 
    // of zeroing out memory. We use this to scatter sorted elements back and forth.
    var temp = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(count);
    
    // Represent both arrays as ultra-fast, lightweight stack descriptors (Spans)
    var source = entries.AsSpan(0, count);
    var dest = temp.AsSpan(0, count);

    // A 32-bit integer is split into 7 chunks to align with CHAMP's trie depths.
    // The first chunk takes 2 bits (32 - 30 = 2 bits -> values 0-3).
    // The remaining 6 chunks take 5 bits each (values 0-31), matching our 32-way branching factor.
    ReadOnlySpan<byte> shifts = [30, 25, 20, 15, 10, 5, 0];
    ReadOnlySpan<byte> masks  = [0x03, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F];

    // Array to keep track of how many items belong in each bucket for the current pass.
    // Fixed size of 32 elements because our maximum mask value (0x1F) is 31.
    Span<int> counts = stackalloc int[32];

    // Radix Sort works by sorting one "digit" (or bit-chunk) at a time.
    // We start at the most significant bits (shift 30) and work down to the least (shift 0).
    // Sorting from top to bottom guarantees that items are grouped by their trie path prefixes.
    for (int p = 0; p < shifts.Length; p++)
    {
        int shift = shifts[p];
        int mask = masks[p];

        // I didn't make up the terminology, this part is called "histogramming"
        // Scan the source array and count how many elements fall into each bucket 
        // based purely on the current bit-chunk we are looking at.
        for (int i = 0; i < count; i++)
        {
            int bucket = (source[i].Hash >> shift) & mask;
            counts[bucket]++;
        }
        
        // Convert our histogram counts into starting array indices.
        // For example, if bucket 0 has 5 items and bucket 1 has 3 items:
        // Bucket 0 starts writing at index 0. Bucket 1 starts writing at index 5.
        int offset = 0;
        for (int i = 0; i < 32; i++)
        {
            int c = counts[i];
            counts[i] = offset; // Store the exact starting position for this bucket in 'dest'
            offset += c;        // Accumulate the width to find the start of the next bucket
        }
        
        // Read through the source array a second time. Look at each element's bit-chunk,
        // find its designated destination index from our 'counts' table, copy it into 'dest',
        // and immediately increment that specific bucket's tracker so the next item slots in right after it.
        for (int i = 0; i < count; i++)
        {
            int bucket = (source[i].Hash >> shift) & mask;
            dest[counts[bucket]++] = source[i];
        }
    
        // Reset our histogram table to zero on the stack before we process the next bit-chunk.
        counts.Clear();

        // Swap our local span definitions. What was just written to 'dest' becomes the new 'source'
        // for the next pass, and the old source becomes the new scratch workspace buffer ('dest').
        var tempSpan = source;
        source = dest;
        dest = tempSpan;
    }

    // Because we swap pointers 7 times (an odd number), the final, fully-sorted 
    // sequence will naturally end up inside the 'temp' array instead of the original 'entries' array.
    // If our final 'source' view points to 'temp', we flush it back to 'entries' via a fast block copy.
    if (source != entries)
    {
        source.CopyTo(entries);
    }
}

    /// <summary>
    /// Recursively builds the nodes of the CHAMP trie.
    /// It takes a span of sorted entries and a shift value (for the hash bits).
    /// </summary>
    private static NodeBase BuildNode(Span<BuilderEntry<TK, TV>> span, int shift, IEqualityComparer<TK> comparer, ref int finalCount)
    {
        // If there's only one item, it's a leaf node.
        if (span.Length == 1)
        {
            ref var entry = ref span[0];
            uint bitpos0 = 1u << ((entry.Hash >> shift) & 0x1F);
            var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, OwnerId.None, bitpos0);
            NodeOps.GetLeafDataSpan<TK, TV>(leaf)[0] = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
            finalCount++;
            return leaf;
        }

        // If all items have the same hash, we have a hash collision.
        if (span[0].Hash == span[^1].Hash)
        {
            return BuildCollisionNode(span, comparer, ref finalCount);
        }

        uint dataMap = 0;
        uint nodeMap = 0;
        
        // These are our temporary, stack-allocated buffers for data and child nodes.
        var dataBuffer = new DataSlotBuffer<TK, TV>();
        var nodeBuffer = new NodeBaseBuffer();
        Span<DataSlot<TK, TV>> tempDatas = dataBuffer;
        Span<NodeBase> tempNodes = nodeBuffer;
        
        int dataCount = 0;
        int nodeCount = 0;

        int i = 0;
        while (i < span.Length)
        {
            // We group items by the current 5-bit chunk of their hash.
            int currentBit = (span[i].Hash >> shift) & 0x1F;
            uint bitpos = 1u << currentBit;

            int groupEnd = i + 1;
            while (groupEnd < span.Length && ((span[groupEnd].Hash >> shift) & 0x1F) == currentBit)
            {
                groupEnd++;
            }

            var groupSpan = span.Slice(i, groupEnd - i);

            if (groupSpan.Length == 1)
            {
                // If a group has only one item, it's stored directly in this node.
                dataMap |= bitpos;
                tempDatas[dataCount++] = new DataSlot<TK, TV> { Key = groupSpan[0].Key, Value = groupSpan[0].Value };
                finalCount++;
            }
            else
            {
                // If a group has multiple items, we create a child node for them.
                nodeMap |= bitpos;
                tempNodes[nodeCount++] = BuildNode(groupSpan, shift + 5, comparer, ref finalCount);
            }

            i = groupEnd;
        }

        ulong finalMap = ((ulong)nodeMap << 32) | dataMap;

        // If there are no child nodes, we can create a more compact leaf node.
        if (nodeCount == 0)
        {
            var leaf = (NodeBase)NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, OwnerId.None, finalMap);
            tempDatas[..dataCount].CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
            return leaf;
        }

        // Otherwise, we create an internal node with both data and child nodes.
        return new InternalNode<TK, TV>(tempDatas[..dataCount].ToArray(), tempNodes[..nodeCount].ToArray(), OwnerId.None)
        {
            Map = finalMap
        };
    }

    /// <summary>
    /// Handles the case where multiple keys have the same hash code.
    /// It creates a special "collision" node that just stores the items in a list.
    /// </summary>
    private static NodeBase BuildCollisionNode(Span<BuilderEntry<TK, TV>> span, IEqualityComparer<TK> comparer, ref int finalCount)
    {
        var slots = new List<DataSlot<TK, TV>>(span.Length);
        
        for (int i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];
            bool found = false;
            
            // We need to check for duplicate keys within the collision list.
            for (int j = 0; j < slots.Count; j++)
            {
                if (comparer.Equals(slots[j].Key, entry.Key))
                {
                    // If a key is already in the list, we just update its value.
                    slots[j] = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                // If it's a new key, add it to the list.
                slots.Add(new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value });
                finalCount++;
            }
        }

        return new CollisionNode<TK, TV>(slots.ToArray(), OwnerId.None);
    }
}
