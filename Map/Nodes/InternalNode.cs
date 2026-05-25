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

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace Map;

// New Internal Node for branches
internal sealed class InternalNode<TK, TV> : NodeBase
{
    public  DataSlot<TK, TV>[] Data;
    public  NodeBase[] Nodes;

    public InternalNode(DataSlot<TK, TV>[] data, NodeBase[] nodes)
    {
        Data = data;
        Nodes = nodes;
        Meta = NodeOps.PackMeta(0, NodeFlags.Internal, 0);
    }

    public InternalNode(DataSlot<TK, TV>[] data, NodeBase[] nodes, ulong owner)
    {
        Data = data;
        Nodes = nodes;
        Meta = NodeOps.PackMeta(0, NodeFlags.Internal, owner);
    }
}