/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */

using System.Collections;
using System.Runtime.CompilerServices;

namespace Map;

public sealed class Map<TK, TV> :
    IReadOnlyDictionary<TK, TV>
//IImmutableDictionary<TK, TV>,
//IEquatable<Map<TK, TV>>,
    where TK : notnull
{
    public static readonly Map<TK, TV> Empty = new(null, EqualityComparer<TK>.Default);
    private readonly IEqualityComparer<TK> _comparer;
    internal readonly NodeBase? Root;

    public Map(IEqualityComparer<TK> comparer)
    {
        Root = null;
        _comparer = comparer;
        Count = 0;
    }

    public Map()
    {
        Root = null;
        _comparer = EqualityComparer<TK>.Default;
        Count = 0;
    }

    internal Map(NodeBase? root, IEqualityComparer<TK> comparer)
    {
        Root = root;
        _comparer = comparer;
        Count = 0;
    }

    internal Map(NodeBase? root, IEqualityComparer<TK> comparer, int count)
    {
        Root = root;
        _comparer = comparer;
        Count = count;
    }

    public bool IsEmpty => Count == 0;

    public MapKeyCollection<TK, TV> Keys => new(Root, Count);
    public MapValueCollection<TK, TV> Values => new(Root, Count);

    public int Count { get; }

    public TV this[TK key] => Get(key);


    public bool ContainsKey(TK key)
    {
        return TryGetValue(key, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TK key, out TV value)
    {
        if (Root == null)
        {
            value = default!;
            return false;
        }

        var hash = _comparer.GetHashCode(key);
        return TrieOps.TryGetValue(Root, key, hash, _comparer, out value);
    }

    // Explicit implementering för att tillfredsställa gränssnitten
    IEnumerator<KeyValuePair<TK, TV>> IEnumerable<KeyValuePair<TK, TV>>.GetEnumerator()
    {
        return new MapEnumerator<TK, TV>(Root);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new MapEnumerator<TK, TV>(Root);
    }

    // Explicit implementering för att tillfredsställa gränssnittets kontrakt
    IEnumerable<TK> IReadOnlyDictionary<TK, TV>.Keys => Keys;
    IEnumerable<TV> IReadOnlyDictionary<TK, TV>.Values => Values;

    public TV Get(TK key)
    {
        if (TryGetValue(key, out var value)) return value;

        throw new KeyNotFoundException($"The key '{key}' was not present in the map.");
    }

    public Map<TK, TV> Add(TK key, TV value)
    {
        var hash = _comparer.GetHashCode(key);
        var newRoot = TrieOps.Insert(Root, key, value, hash, 0, _comparer, out var added);

        // If nothing was added NOR CHANGED, we can just return the same persistentmap
        if (ReferenceEquals(Root, newRoot)) return this;

        return new Map<TK, TV>(newRoot, _comparer, added ? Count + 1 : Count);
    }


    public Map<TK, TV> Remove(TK key)
    {
        if (Root == null) return this;

        var hash = _comparer.GetHashCode(key);
        var newRoot = TrieOps.Remove<TK, TV>(Root!, key, hash, 0, _comparer, out var removed);

        if (!removed) return this;

        if (newRoot == null) return Empty;

        return new Map<TK, TV>(newRoot, _comparer, removed ? Count - 1 : Count);
    }

    public bool Equals(Map<TK, TV>? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (Count != other.Count) return false;
        if (Count == 0) return true;

        var valueComparer = EqualityComparer<TV>.Default;

        // The struct-based enumerator we built earlier makes this allocation-free
        foreach (var kvp in this)
            if (!other.TryGetValue(kvp.Key, out var otherValue) || !valueComparer.Equals(kvp.Value, otherValue))
                return false;

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is Map<TK, TV> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (Count == 0) return 0;

        var hash = 0;
        var valueComparer = EqualityComparer<TV>.Default;

        // XOR is commutative, guaranteeing the same hash regardless of internal tree structure
        foreach (var kvp in this)
        {
            var keyHash = _comparer.GetHashCode(kvp.Key);
            var valHash = kvp.Value == null ? 0 : valueComparer.GetHashCode(kvp.Value);

            hash ^= HashCode.Combine(keyHash, valHash);
        }

        return hash;
    }

    public TransientMap<TK, TV> ToTransient()
    {
        return new TransientMap<TK, TV>(Root, _comparer);
    }

    /// <summary>
    ///     Executes a delegate on the elements of the map
    /// </summary>
    /// <returns>True if the iteration completed all elements, or False if aborted early.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Iter(Func<TK, TV, bool> action)
    {
        return TrieOps.Iter(Root, action);
    }

    /// <summary>
    ///     Filters the map, retaining only elements that satisfy the specified predicate.
    /// </summary>
    /// <param name="action">A function to test each value for a condition.</param>
    /// <returns>A new <see cref="Map{TK, TV}" /> containing the elements that satisfy the condition.</returns>
    public Map<TK, TV> Filter(Func<TV, TV, bool> action)
    {
        var builder = new MapBuilder<TK, TV>(_comparer);
        Iter((k, v) =>
        {
            builder.Add(k, v);
            return true;
        });
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Transforms the elements of the map using the specified function.
    /// </summary>
    /// <typeparam name="NK">The type of the new keys.</typeparam>
    /// <typeparam name="NV">The type of the new values.</typeparam>
    /// <param name="action">A function to transform each key-value pair.</param>
    /// <param name="comparer">An optional equality comparer for the new keys.</param>
    /// <returns>A new <see cref="Map{NK, NV}" /> containing the transformed elements.</returns>
    public Map<NK, NV> MapCar<NK, NV>(Func<TK, TV, (NK, NV)> action, IEqualityComparer<NK>? comparer = null)
        where NK : notnull
    {
        var builder = new MapBuilder<NK, NV>(comparer);
        Iter((k, v) =>
        {
            var (nk, nv) = action(k, v);
            builder.Add(nk, nv);
            return true;
        });

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Aggregates the values of the map using the specified function.
    /// </summary>
    /// <typeparam name="TState">The type of the accumulator state.</typeparam>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="action">A function to aggregate the state and each value.</param>
    /// <returns>The final accumulated state.</returns>
    public TState FoldValues<TState>(TState seed, Func<TState, TV, TState> action)
    {
        Iter((_, v) =>
        {
            seed = action(seed, v);
            return true;
        });
        return seed;
    }

    /// <summary>
    ///     Aggregates the keys of the map using the specified function.
    /// </summary>
    /// <typeparam name="TState">The type of the accumulator state.</typeparam>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="action">A function to aggregate the state and each key.</param>
    /// <returns>The final accumulated state.</returns>
    public TState FoldKeys<TState>(TState seed, Func<TState, TK, TState> action)
    {
        Iter((k, _) =>
        {
            seed = action(seed, k);
            return true;
        });
        return seed;
    }

    /// <summary>
    ///     Determines whether the map contains elements that satisfy the specified predicate.
    /// </summary>
    /// <param name="pred">A function to test each element for a condition.</param>
    /// <returns><c>true</c> if the map contains an element that satisfies the condition; otherwise, <c>false</c>.</returns>
    public bool Exists(Func<TK, TV, bool> pred)
    {
        return Iter((k, v) => { return !pred(k, v); });
    }

    /// <summary>
    ///     Finds the first key in the map that satisfies the specified predicate.
    /// </summary>
    /// <param name="pred">A function to test each key for a condition.</param>
    /// <returns>The key that satisfies the condition.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if no key satisfies the condition.</exception>
    public TK FindKey(Func<TK, bool> pred)
    {
        var key = default(TK);
        var found = false;
        Iter((k, _) =>
        {
            if (pred(k))
            {
                found = true;
                key = k;
                return false;
            }

            return true;
        });
        if (!found)
            throw new KeyNotFoundException("Key not found in map");

        return key!;
    }

    /// <summary>
    ///     Executes the specified action on each element of the map.
    /// </summary>
    /// <param name="action">The action to execute on each key-value pair.</param>
    public void ForEach(Action<TK, TV> action)
    {
        Iter((k, v) =>
        {
            action(k, v);
            return true;
        });
    }


    /// <summary>
    ///     Creates a transient version of the map, applies the specified mutation action, and returns an immutable map.
    /// </summary>
    /// <param name="action">The action to apply to the transient map.</param>
    /// <returns>A new <see cref="Map{TK, TV}" /> with the mutations applied.</returns>
    public Map<TK, TV> Mutate(Action<TransientMap<TK, TV>> action)
    {
        var transient = ToTransient();
        action(transient);
        return transient.ToImmutable();
    }

    /// <summary>
    ///     Merges another map into this one. Defaults to 'Prefer Right' where values in 'other' overwrite this map.
    /// </summary>
    public Map<TK, TV> Merge(Map<TK, TV> other)
    {
        return Merge(other, null);
    }

    /// <summary>
    ///     Merges another map into this one using a conflict resolution strategy.
    /// </summary>
    /// <param name="other">The other map to merge.</param>
    /// <param name="conflictResolver">
    ///     An optional thunk called when keys conflict: (key, leftValue, rightValue) => resolvedValue.
    ///     Pass null to default to picking the right value (other overwrites this).
    /// </param>
    public Map<TK, TV> Merge(Map<TK, TV> other, Func<TK, TV, TV, TV>? conflictResolver)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        var newRoot = TrieOps.Merge(Root, other.Root, 0, _comparer, conflictResolver);

        if (ReferenceEquals(Root, newRoot)) return this;
        if (ReferenceEquals(other.Root, newRoot)) return other;

        // Recalculate size allocation-free via IterFast
        var counter = 0;
        TrieOps.Iter<TK, TV>(newRoot, (_, _) =>
        {
            counter++;
            return true;
        });

        return new Map<TK, TV>(newRoot, _comparer, counter);
    }

    public MapEnumerator<TK, TV> GetEnumerator()
    {
        return new MapEnumerator<TK, TV>(Root);
    }
}