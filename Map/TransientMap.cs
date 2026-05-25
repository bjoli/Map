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
    private readonly IEqualityComparer<TK> _comparer;
    private int _count;
    private ulong _ownerId;
    private NodeBase? _root;

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

        var hash = _comparer.GetHashCode(key!);
        return TrieOps.TryGetValue(_root, key, hash, _comparer, out value);
    }

    public void Add(TK key, TV value)
    {
        var hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.InsertTransient(_root, key, value, hash, 0, _comparer, _ownerId, out var added);
        if (added) _count++;
    }

    public void Remove(TK key)
    {
        if (_root == null) return;

        var hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.RemoveTransient<TK, TV>(_root, key, hash, 0, _comparer, out var removed, _ownerId);
        if (removed) _count--;
    }

    public Map<TK, TV> ToImmutable()
    {
        // Disowns all the nodes we mutated.
        _ownerId = OwnerId.Next();
        return new Map<TK, TV>(_root, _comparer, _count);
    }
}