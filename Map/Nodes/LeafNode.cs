using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace Map;

// So, the main idea of this was stolen from the vector implementation in scala, where every shift level
// has its own node type. This works less well in c#, but using unsafe casts to span, we can avoid dealing
// with the kind combinatorics explosion of actually having the runtime deal with all the types.
//
// The casting is done simply by looking at the length. 

[InlineArray(1)]
[NonUserCode]
internal struct LeafSlot1<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(2)]
[NonUserCode]
internal struct LeafSlot2<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(3)]
[NonUserCode]
internal struct LeafSlot3<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(4)]
[NonUserCode]
internal struct LeafSlot4<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(5)]
[NonUserCode]
internal struct LeafSlot5<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(6)]
[NonUserCode]
internal struct LeafSlot6<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(7)]
[NonUserCode]
internal struct LeafSlot7<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(8)]
[NonUserCode]
internal struct LeafSlot8<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(9)]
[NonUserCode]
internal struct LeafSlot9<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(10)]
[NonUserCode]
internal struct LeafSlot10<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(11)]
[NonUserCode]
internal struct LeafSlot11<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(12)]
[NonUserCode]
internal struct LeafSlot12<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(13)]
[NonUserCode]
internal struct LeafSlot13<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(14)]
[NonUserCode]
internal struct LeafSlot14<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(15)]
[NonUserCode]
internal struct LeafSlot15<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(16)]
[NonUserCode]
internal struct LeafSlot16<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(17)]
[NonUserCode]
internal struct LeafSlot17<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(18)]
[NonUserCode]
internal struct LeafSlot18<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(19)]
[NonUserCode]
internal struct LeafSlot19<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(20)]
[NonUserCode]
internal struct LeafSlot20<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(21)]
[NonUserCode]
internal struct LeafSlot21<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(22)]
[NonUserCode]
internal struct LeafSlot22<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(23)]
[NonUserCode]
internal struct LeafSlot23<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(24)]
[NonUserCode]
internal struct LeafSlot24<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(25)]
[NonUserCode]
internal struct LeafSlot25<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(26)]
[NonUserCode]
internal struct LeafSlot26<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(27)]
[NonUserCode]
internal struct LeafSlot27<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(28)]
[NonUserCode]
internal struct LeafSlot28<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(29)]
[NonUserCode]
internal struct LeafSlot29<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(30)]
[NonUserCode]
internal struct LeafSlot30<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(31)]
[NonUserCode]
internal struct LeafSlot31<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[InlineArray(32)]
[NonUserCode]
internal struct LeafSlot32<TK, TV>
{
    private DataSlot<TK, TV> _element0;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node1<TK, TV> : NodeBase
{
    public LeafSlot1<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node2<TK, TV> : NodeBase
{
    public LeafSlot2<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node3<TK, TV> : NodeBase
{
    public LeafSlot3<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node4<TK, TV> : NodeBase
{
    public LeafSlot4<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node5<TK, TV> : NodeBase
{
    public LeafSlot5<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node6<TK, TV> : NodeBase
{
    public LeafSlot6<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node7<TK, TV> : NodeBase
{
    public LeafSlot7<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node8<TK, TV> : NodeBase
{
    public LeafSlot8<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node9<TK, TV> : NodeBase
{
    public LeafSlot9<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node10<TK, TV> : NodeBase
{
    public LeafSlot10<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node11<TK, TV> : NodeBase
{
    public LeafSlot11<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node12<TK, TV> : NodeBase
{
    public LeafSlot12<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node13<TK, TV> : NodeBase
{
    public LeafSlot13<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node14<TK, TV> : NodeBase
{
    public LeafSlot14<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node15<TK, TV> : NodeBase
{
    public LeafSlot15<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node16<TK, TV> : NodeBase
{
    public LeafSlot16<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node17<TK, TV> : NodeBase
{
    public LeafSlot17<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node18<TK, TV> : NodeBase
{
    public LeafSlot18<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node19<TK, TV> : NodeBase
{
    public LeafSlot19<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node20<TK, TV> : NodeBase
{
    public LeafSlot20<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node21<TK, TV> : NodeBase
{
    public LeafSlot21<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node22<TK, TV> : NodeBase
{
    public LeafSlot22<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node23<TK, TV> : NodeBase
{
    public LeafSlot23<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node24<TK, TV> : NodeBase
{
    public LeafSlot24<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node25<TK, TV> : NodeBase
{
    public LeafSlot25<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node26<TK, TV> : NodeBase
{
    public LeafSlot26<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node27<TK, TV> : NodeBase
{
    public LeafSlot27<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node28<TK, TV> : NodeBase
{
    public LeafSlot28<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node29<TK, TV> : NodeBase
{
    public LeafSlot29<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node30<TK, TV> : NodeBase
{
    public LeafSlot30<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node31<TK, TV> : NodeBase
{
    public LeafSlot31<TK, TV> Data;
}

[StructLayout(LayoutKind.Sequential)]
[NonUserCode]
internal sealed class Node32<TK, TV> : NodeBase
{
    public LeafSlot32<TK, TV> Data;
}