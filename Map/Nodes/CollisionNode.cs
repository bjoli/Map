namespace Map;

// Holds only keys whose full hash equals Hash. A key with another hash splits the node instead
// of joining it (see TrieOps.SplitCollision).
internal sealed class CollisionNode<TK, TV> : NodeBase
{
    public readonly int Hash;
    public DataSlot<TK, TV>[] Slots;

    public CollisionNode(DataSlot<TK, TV>[] slots, int hash)
    {
        Slots = slots;
        Hash = hash;
        // capacity is not used by colissionnodes.
        Meta = NodeOps.PackMeta(0, NodeFlags.Collision, 0);
    }

    public CollisionNode(DataSlot<TK, TV>[] slots, int hash, ulong ownerId)
    {
        Slots = slots;
        Hash = hash;
        // capacity is not used by colissionnodes.
        Meta = NodeOps.PackMeta(0, NodeFlags.Collision, ownerId);
    }
}
