/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025-2026 Linus Björnstam
 *
 */


using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Map;


internal static class NodeOps
{
    public static (byte capacity, NodeFlags flags, ulong ownerId) UnpackMeta(ulong meta)
    {
        byte capacity = (byte)(meta & 0xFF);
        NodeFlags flags = (NodeFlags)((meta >> 8) & 0xFF);
        ulong ownerId = meta >> 16;
        return (capacity, flags, ownerId);
    }

    public static byte GetCapacity(ulong meta)
    {
        return  (byte)(meta & 0xFF);
    }
    
    public static NodeFlags GetFlags(ulong meta)
    {
        return (NodeFlags)((meta >> 8) & 0xFF);
    }
    
    public static ulong GetOwnerId(ulong meta)
    {
        return meta >> 16;
    }
    
    public static ulong PackMeta(byte capacity, NodeFlags flags, ulong ownerId)
    {
        // Masks ownerId to strictly 48 bits before shifting
        return capacity
               | ((ulong)(byte)flags << 8) 
               | ((ownerId & 0x0000FFFFFFFFFFFF) << 16); 
    }

    // This is probably the fastest way I have managed to figure out to do this by now. 
    // Using an array of delegates is not faster, nor is using makeGenericType. If you really
    // want to have a go at making this faster, skip ALL nodes and have one node with a variable tail. 
    // and use some kind of unsafe voodoo. 
    public static object AllocateLeaf<TK, TV>(byte capacity, NodeFlags flags, ulong owner, ulong map)
    {
        ulong meta = PackMeta(capacity, flags, owner);

        return capacity switch
        {
            1 => new Node1<TK, TV> { Meta= meta, Map = map },
            2 => new Node2<TK, TV> { Meta = meta, Map = map },
            3 => new Node3<TK, TV> { Meta = meta, Map = map },
            4 => new Node4<TK, TV> { Meta = meta, Map = map },
            5 => new Node5<TK, TV> { Meta = meta, Map = map },
            6 => new Node6<TK, TV> { Meta = meta, Map = map },
            7 => new Node7<TK, TV> { Meta = meta, Map = map },
            8 => new Node8<TK, TV> { Meta = meta, Map = map },
            9 => new Node9<TK, TV> { Meta = meta, Map = map },
            10 => new Node10<TK, TV> { Meta = meta, Map = map },
            11 => new Node11<TK, TV> { Meta = meta, Map = map },
            12 => new Node12<TK, TV> { Meta = meta, Map = map },
            13 => new Node13<TK, TV> { Meta = meta, Map = map },
            14 => new Node14<TK, TV> { Meta = meta, Map = map },
            15 => new Node15<TK, TV> { Meta = meta, Map = map },
            16 => new Node16<TK, TV> { Meta = meta, Map = map },
            17 => new Node17<TK, TV> { Meta = meta, Map = map },
            18 => new Node18<TK, TV> { Meta = meta, Map = map },
            19 => new Node19<TK, TV> { Meta = meta, Map = map },
            20 => new Node20<TK, TV> { Meta = meta, Map = map },
            21 => new Node21<TK, TV> { Meta = meta, Map = map },
            22 => new Node22<TK, TV> { Meta = meta, Map = map },
            23 => new Node23<TK, TV> { Meta = meta, Map = map },
            24 => new Node24<TK, TV> { Meta = meta, Map = map },
            25 => new Node25<TK, TV> { Meta = meta, Map = map },
            26 => new Node26<TK, TV> { Meta = meta, Map = map },
            27 => new Node27<TK, TV> { Meta = meta, Map = map },
            28 => new Node28<TK, TV> { Meta = meta, Map = map },
            29 => new Node29<TK, TV> { Meta = meta, Map = map },
            30 => new Node30<TK, TV> { Meta = meta, Map = map },
            31 => new Node31<TK, TV> { Meta = meta, Map = map },
            32 => new Node32<TK, TV> { Meta = meta, Map = map },
            _ => throw new ArgumentOutOfRangeException(nameof(capacity))
        };
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<DataSlot<TK, TV>> GetLeafDataSpan<TK, TV>(NodeBase node)
    {
        int capacity = GetCapacity(node.Meta);
        var typedNode = Unsafe.As<Node1<TK, TV>>(node);
        ref var first = ref Unsafe.As<LeafSlot1<TK, TV>, DataSlot<TK, TV>>(ref typedNode.Data);
        return MemoryMarshal.CreateSpan(ref first, capacity);
    }
    
}
