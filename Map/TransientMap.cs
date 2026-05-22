/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025-2026 Linus Björnstam
 *
 */

using System.Runtime.CompilerServices;

namespace Map;

public sealed class TransientMap<TK, TV>
{
    private NodeBase? _root;
    private readonly IEqualityComparer<TK> _comparer;
    private ulong _ownerId;
    private int _count;

    internal TransientMap(NodeBase? root, IEqualityComparer<TK> comparer)
    {
        _root = root;
        _comparer = comparer;
        _ownerId = OwnerId.Next();
        _count = 0;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TK key, out TV value)
    {
        if (_root == null)
        {
            value = default!;
            return false;
        }

        int hash = _comparer.GetHashCode(key!);
        return TrieOps.TryGetValue(_root, key, hash, _comparer, out value);
    }

    public void Add(TK key, TV value)
    {
        int hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.InsertTransient(_root, key, value, hash, 0, _comparer, _ownerId, out bool added);
        if (added) _count++;
    }
    
    public void Remove(TK key)
    {
        if (_root == null) return;

        int hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.RemoveTransient<TK,TV>(_root, key, hash, 0, _comparer, out bool removed, _ownerId);
        if (removed) _count--;
    }

    public Map<TK, TV> ToImmutable()
    {
        // Disowns all the nodes we mutated.
        _ownerId = OwnerId.Next();
        return new Map<TK, TV>(_root, _comparer, _count);
    }
}
