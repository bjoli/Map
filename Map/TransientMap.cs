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

/// <summary>
///     A mutable, transient version of a <see cref="Map{TK, TV}" /> that can be efficiently modified
///     before being converted back to an immutable map.
/// </summary>
public sealed class TransientMap<TK, TV> where TK : notnull
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

    /// <summary>
    ///     Attempts to get the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <param name="value">
    ///     When this method returns, contains the value associated with the specified key, if the key is found;
    ///     otherwise, the default value for the type of the <paramref name="value" /> parameter.
    /// </param>
    /// <returns><c>true</c> if the key was found; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TK key, out TV value)
    {
        if (_root == null)
        {
            value = default!;
            return false;
        }

        var hash = _comparer.GetHashCode(key);
        return TrieOps.TryGetValue(_root, key, hash, _comparer, out value);
    }

    /// <summary>
    ///     Adds an element with the provided key and value to the map. If the key already exists,
    ///     the existing value is updated.
    /// </summary>
    /// <param name="key">The object to use as the key of the element to add.</param>
    /// <param name="value">The object to use as the value of the element to add.</param>
    public void Set(TK key, TV value)
    {
        var hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.InsertTransient(_root, key, value, hash, 0, _comparer, _ownerId, out var added);
        if (added) _count++;
    }
    
    public void Add(TK key, TV value)
    {
        var hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        if (TrieOps.TryGetKey<TK,TV>(_root, key, hash, _comparer, out _))
        {
            throw new ArgumentException($"The key {key} is already registered.", nameof(key));
        }
        _root = TrieOps.InsertTransient(_root, key, value, hash, 0, _comparer, _ownerId, out  _);
        _count++;
    }
    
    

    /// <summary>
    ///     Removes the element with the specified key from the map.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    public void Remove(TK key)
    {
        if (_root == null) return;

        var hash = _comparer.GetHashCode(key ?? throw new ArgumentNullException(nameof(key)));
        _root = TrieOps.RemoveTransient<TK, TV>(_root, key, hash, 0, _comparer, out var removed, _ownerId);
        if (removed) _count--;
    }

    /// <summary>
    ///     Creates an immutable <see cref="Map{TK, TV}" /> from the contents of this transient map.
    ///     This operation is an O(1) operation and does not involve copying the data.
    /// </summary>
    /// <returns>An immutable map.</returns>
    public Map<TK, TV> ToImmutable()
    {
        // Disowns all the nodes we mutated.
        _ownerId = OwnerId.Next();
        return new Map<TK, TV>(_root, _comparer, _count);
    }
}