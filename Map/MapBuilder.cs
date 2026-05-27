/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */


using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
///     The MapBuilder is a mutable helper for creating an immutable Map.
///     It's more efficient to add items to this builder first and then
///     convert it to a Map in one go, rather than creating a new Map for each addition.
/// </summary>
public sealed class MapBuilder<TK, TV> where TK : notnull
{
    private readonly IEqualityComparer<TK> _comparer;
    private int _count;
    private BuilderEntry<TK, TV>[] _entries;

    /// <summary>
    ///     Creates a new MapBuilder.
    ///     You can optionally provide a custom comparer for keys and an initial capacity.
    /// </summary>
    public MapBuilder(IEqualityComparer<TK>? comparer = null, int initialCapacity = 16)
    {
        // We allocate an array to hold the entries.
        // GC.AllocateUninitializedArray is a performance trick to avoid zeroing out the memory.
        _entries = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(initialCapacity);
        _comparer = comparer ?? EqualityComparer<TK>.Default;
    }

    /// <summary>
    ///     Adds a key-value pair to the builder.
    ///     If the internal array is full, it will be resized.
    /// </summary>
    public void Add(TK key, TV value)
    {
        var entries = _entries;
        var count = _count;

        // 1. Uninitialized Expansion
        if ((uint)count >= (uint)entries.Length)
        {
            var newEntries = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(entries.Length * 2);

            // Span.CopyTo lowers to a highly optimized memmove
            entries.AsSpan().CopyTo(newEntries);

            _entries = entries = newEntries;
        }

        // 2. Bounds-Check Elimination
        ref var dest = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(entries), count);

        dest.Hash = _comparer.GetHashCode(key);
        dest.Key = key;
        dest.Value = value;

        _count = count + 1;
    }

    /// <summary>
    ///     Converts the builder's contents into an immutable Map.
    ///     This is the main "build" step.
    /// </summary>
    public Map<TK, TV> ToImmutable()
    {
        if (_count == 0) return Map<TK, TV>.Empty;

        var finalCount = 0;
        NodeBase root;

        // Threshold tuned for L2 cache boundaries during 32-way scatter
        // On a ryzen 5 5900x the boundary is about 50000.
        if (_count < 30_000)
        {
            var temp = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(_count);
            root = BuildNodeRecursive(
                _entries.AsSpan(0, _count),
                temp.AsSpan(0, _count),
                0,
                _comparer,
                ref finalCount);
        }
        else
        {
            // Fallback to sequential LSD sort to prevent cache thrashing on huge datasets
            var sortedEntries = SortLargeMap(_entries, _count);
            root = BuildNode(sortedEntries.AsSpan(0, _count), 0, _comparer, ref finalCount);
        }

        return new Map<TK, TV>(root, _comparer, finalCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetMappedChampKey(uint h)
    {
        // Realigns the hash so standard mathematical order perfectly matches CHAMP depth priority.
        // LSB 5 bits become MSB, preserving chunk priority for standard sorting / byte-radix passes.
        return ((h & 0x1F) << 27) |
               (((h >> 5) & 0x1F) << 22) |
               (((h >> 10) & 0x1F) << 17) |
               (((h >> 15) & 0x1F) << 12) |
               (((h >> 20) & 0x1F) << 7) |
               (((h >> 25) & 0x1F) << 2) |
               (h >> 30);
    }

    private static BuilderEntry<TK, TV>[] SortLargeMap(BuilderEntry<TK, TV>[] entries, int count)
    {
        var sortedEntries = GC.AllocateUninitializedArray<BuilderEntry<TK, TV>>(count);

        // Rent parallel arrays to avoid allocation overhead
        var packed = ArrayPool<ulong>.Shared.Rent(count);
        var tempPacked = ArrayPool<ulong>.Shared.Rent(count);

        var source = packed.AsSpan(0, count);
        var dest = tempPacked.AsSpan(0, count);
        var entriesSpan = entries.AsSpan(0, count);

        // Pack the 32-bit mapped key into the high bits, and the index into the low bits.
        for (var i = 0; i < count; i++)
            source[i] = ((ulong)GetMappedChampKey((uint)entriesSpan[i].Hash) << 32) | (uint)i;

        // 3-Pass LSD Radix Sort targeting only the high 32 bits (11-11-10 chunking)
        Span<int> counts0 = stackalloc int[2048];
        Span<int> counts1 = stackalloc int[2048];
        Span<int> counts2 = stackalloc int[1024];

        foreach (var p in source)
        {
            counts0[(int)((p >> 32) & 0x7FF)]++;
            counts1[(int)((p >> 43) & 0x7FF)]++;
            counts2[(int)(p >> 54)]++;
        }

        int o0 = 0, o1 = 0, o2 = 0;
        for (var i = 0; i < 2048; i++)
        {
            var c0 = counts0[i];
            counts0[i] = o0;
            o0 += c0;
            var c1 = counts1[i];
            counts1[i] = o1;
            o1 += c1;
            if (i < 1024)
            {
                var c2 = counts2[i];
                counts2[i] = o2;
                o2 += c2;
            }
        }

        // Scatter 1 (Bits 32-42)
        foreach (var p in source) dest[counts0[(int)((p >> 32) & 0x7FF)]++] = p;
        // Scatter 2 (Bits 43-53)
        foreach (var p in dest) source[counts1[(int)((p >> 43) & 0x7FF)]++] = p;
        // Scatter 3 (Bits 54-63)
        foreach (var p in source) dest[counts2[(int)(p >> 54)]++] = p;

        // The sorted permutation is now in 'dest'. 
        // Move the heavy structs into their final positions in one pass.
        var destEntriesSpan = sortedEntries.AsSpan(0, count);
        for (var i = 0; i < count; i++) destEntriesSpan[i] = entriesSpan[(int)(dest[i] & 0xFFFFFFFF)];

        ArrayPool<ulong>.Shared.Return(packed);
        ArrayPool<ulong>.Shared.Return(tempPacked);

        return sortedEntries;
    }

    /// <summary>
    ///     Recursively builds the nodes of the CHAMP trie.
    ///     It takes a span of sorted entries and a shift value (for the hash bits).
    /// </summary>
    private static NodeBase BuildNode(Span<BuilderEntry<TK, TV>> span, int shift, IEqualityComparer<TK> comparer,
        ref int finalCount)
    {
        if (span.Length == 1)
        {
            ref var entry = ref span[0];
            var bitpos0 = 1u << ((entry.Hash >> shift) & 0x1F);
            var leaf = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, OwnerId.None, bitpos0);
            NodeOps.GetLeafDataSpan<TK, TV>(leaf)[0] = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
            finalCount++;
            return leaf;
        }

        if (span[0].Hash == span[^1].Hash) return BuildCollisionNode(span, comparer, ref finalCount);

        uint dataMap = 0;
        uint nodeMap = 0;

        var dataBuffer = new DataSlotBuffer<TK, TV>();
        var nodeBuffer = new NodeBaseBuffer();

        // Get raw pointers directly to the stack allocations to bypass Span boundaries entirely
        ref var dataStart = ref Unsafe.As<DataSlotBuffer<TK, TV>, DataSlot<TK, TV>>(ref dataBuffer);
        ref var nodeStart = ref Unsafe.As<NodeBaseBuffer, NodeBase>(ref nodeBuffer);

        var dataCount = 0;
        var nodeCount = 0;

        var i = 0;
        while (i < span.Length)
        {
            var currentBit = (span[i].Hash >> shift) & 0x1F;
            var bitpos = 1u << currentBit;

            var groupEnd = i + 1;
            while (groupEnd < span.Length && ((span[groupEnd].Hash >> shift) & 0x1F) == currentBit) groupEnd++;

            var groupSpan = span.Slice(i, groupEnd - i);

            if (groupSpan.Length == 1)
            {
                dataMap |= bitpos;
                Unsafe.Add(ref dataStart, dataCount++) = new DataSlot<TK, TV>
                    { Key = groupSpan[0].Key, Value = groupSpan[0].Value };
                finalCount++;
            }
            else
            {
                nodeMap |= bitpos;
                Unsafe.Add(ref nodeStart, nodeCount++) = BuildNode(groupSpan, shift + 5, comparer, ref finalCount);
            }

            i = groupEnd;
        }

        var finalMap = ((ulong)nodeMap << 32) | dataMap;

        if (nodeCount == 0)
        {
            var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, OwnerId.None, finalMap);
            MemoryMarshal.CreateReadOnlySpan(ref dataStart, dataCount).CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
            return leaf;
        }

        var newNode =
            NodeOps.AllocateInternal<TK, TV>((byte)nodeCount, NodeFlags.Internal, OwnerId.None, finalMap);

        var finalData = dataCount == 0 ? Array.Empty<DataSlot<TK, TV>>() : new DataSlot<TK, TV>[dataCount];
        if (dataCount > 0) MemoryMarshal.CreateReadOnlySpan(ref dataStart, dataCount).CopyTo(finalData);
        Unsafe.As<InternalNode1<TK, TV>>(newNode).Data = finalData;

        // Pop the inline child nodes via raw pointer arithmetic (zero bounds checking)
        ref var destChild = ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(newNode).Children);
        for (var j = 0; j < nodeCount; j++) Unsafe.Add(ref destChild, j) = Unsafe.Add(ref nodeStart, j);

        return newNode;
    }

    /// <summary>
    ///     Handles the case where multiple keys have the same hash code.
    ///     It creates a special "collision" node that just stores the items in a list.
    /// </summary>
    private static NodeBase BuildCollisionNode(Span<BuilderEntry<TK, TV>> span, IEqualityComparer<TK> comparer,
        ref int finalCount)
    {
        // List is not very efficient, but we also don't build very many collisionNodes. 
        var slots = new List<DataSlot<TK, TV>>(span.Length);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];
            var found = false;

            // We need to check for duplicate keys within the collision list.
            for (var j = 0; j < slots.Count; j++)
                if (comparer.Equals(slots[j].Key, entry.Key))
                {
                    // If a key is already in the list, we just update its value.
                    slots[j] = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
                    found = true;
                    break;
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

    // This builds the smaller (below about 30_000 elements) champs
    // Above that, we see cache thrashing.
    private static NodeBase BuildNodeRecursive(
        Span<BuilderEntry<TK, TV>> source,
        Span<BuilderEntry<TK, TV>> dest,
        int shift,
        IEqualityComparer<TK> comparer,
        ref int finalCount)
    {
        // Global edge case: The entire map only contains exactly 1 item.
        if (source.Length == 1 && shift == 0)
        {
            ref var entry = ref source[0];
            var bitpos0 = 1u << (entry.Hash & 0x1F);
            var leaf = NodeOps.AllocateLeaf<TK, TV>(1, NodeFlags.None, OwnerId.None, bitpos0);
            NodeOps.GetLeafDataSpan<TK, TV>(leaf)[0] = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
            finalCount++;
            return leaf;
        }

        // If we have shifted past 30, all 32 bits are exhausted. Any remaining items are collisions.
        if (shift > 30) return BuildCollisionNode(source, comparer, ref finalCount);

        // Histogram the current 5-bit chunk
        Span<int> counts = stackalloc int[32];
        for (var i = 0; i < source.Length; i++) counts[(source[i].Hash >> shift) & 0x1F]++;

        // Calculate offsets and pre-compute the CHAMP bitmaps
        Span<int> starts = stackalloc int[32];
        Span<int> positions = stackalloc int[32];
        var offset = 0;
        uint dataMap = 0;
        uint nodeMap = 0;

        for (var i = 0; i < 32; i++)
        {
            var c = counts[i];
            starts[i] = offset;
            positions[i] = offset; // Moving cursor for the scatter pass
            offset += c;

            if (c == 1) dataMap |= 1u << i;
            else if (c > 1) nodeMap |= 1u << i;
        }

        // Scatter elements into 'dest' (This cleanly partitions the array)
        for (var i = 0; i < source.Length; i++)
        {
            var bucket = (source[i].Hash >> shift) & 0x1F;
            dest[positions[bucket]++] = source[i];
        }

        // Build the current node
        var dataBuffer = new DataSlotBuffer<TK, TV>();
        var nodeBuffer = new NodeBaseBuffer();

        ref var dataStart = ref Unsafe.As<DataSlotBuffer<TK, TV>, DataSlot<TK, TV>>(ref dataBuffer);
        ref var nodeStart = ref Unsafe.As<NodeBaseBuffer, NodeBase>(ref nodeBuffer);

        var dataCount = 0;
        var nodeCount = 0;

        for (var i = 0; i < 32; i++)
        {
            var c = counts[i];
            if (c == 0) continue;

            if (c == 1)
            {
                // Single elements instantly terminate sorting and become data payloads
                ref var entry = ref dest[starts[i]];
                Unsafe.Add(ref dataStart, dataCount++) = new DataSlot<TK, TV> { Key = entry.Key, Value = entry.Value };
                finalCount++;
            }
            else
            {
                // Multiple elements recurse. Note how 'dest' and 'source' slices swap to ping-pong memory
                var childSource = dest.Slice(starts[i], c);
                var childDest = source.Slice(starts[i], c);

                Unsafe.Add(ref nodeStart, nodeCount++) = BuildNodeRecursive(
                    childSource, childDest, shift + 5, comparer, ref finalCount);
            }
        }

        var finalMap = ((ulong)nodeMap << 32) | dataMap;

        if (nodeCount == 0)
        {
            var leaf = NodeOps.AllocateLeaf<TK, TV>((byte)dataCount, NodeFlags.None, OwnerId.None, finalMap);
            MemoryMarshal.CreateReadOnlySpan(ref dataStart, dataCount).CopyTo(NodeOps.GetLeafDataSpan<TK, TV>(leaf));
            return leaf;
        }

        var newNode = NodeOps.AllocateInternal<TK, TV>((byte)nodeCount, NodeFlags.Internal, OwnerId.None, finalMap);

        var finalData = dataCount == 0 ? Array.Empty<DataSlot<TK, TV>>() : new DataSlot<TK, TV>[dataCount];
        if (dataCount > 0) MemoryMarshal.CreateReadOnlySpan(ref dataStart, dataCount).CopyTo(finalData);
        Unsafe.As<InternalNode1<TK, TV>>(newNode).Data = finalData;

        ref var destChild = ref Unsafe.As<NodeSlot1, NodeBase>(ref Unsafe.As<InternalNode1<TK, TV>>(newNode).Children);
        for (var j = 0; j < nodeCount; j++) Unsafe.Add(ref destChild, j) = Unsafe.Add(ref nodeStart, j);

        return newNode;
    }
}