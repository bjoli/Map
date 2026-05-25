namespace Map;



internal sealed class CollisionNode<TK, TV> : NodeBase
{
    public DataSlot<TK, TV>[] Slots;

    public CollisionNode(DataSlot<TK, TV>[] slots)
    {
        Slots = slots;
        // capacity is not used by colissionnodes.
        Meta = NodeOps.PackMeta(0, NodeFlags.Collision, 0);
    }
    
    public CollisionNode(DataSlot<TK, TV>[] slots, ulong ownerId)
    {
        Slots = slots;
        // capacity is not used by colissionnodes.
        Meta = NodeOps.PackMeta(0, NodeFlags.Collision, ownerId);
    }
    
    
}