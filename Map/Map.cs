/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */

using System.Runtime.CompilerServices;

namespace Map;

// No `where TK : notnull`. The constraint is unenforceable at the Bjolang
// boundary — the generated C# carries no `where` clauses at all — and every
// Bjolang type argument is non-null anyway, so requiring it here only produced
// CS8714 at each generic call site.
public sealed partial class Map<TK, TV> :
    IEquatable<Map<TK, TV>>
{
    public static readonly Map<TK, TV> Empty = new(null, EqualityComparer<TK>.Default);
    private readonly IEqualityComparer<TK> _comparer;
    private readonly NodeBase? _root;

    public Map(IEqualityComparer<TK> comparer)
    {
        _root = null;
        _comparer = comparer;
        Count = 0;
    }

    public Map()
    {
        _root = null;
        _comparer = EqualityComparer<TK>.Default;
        Count = 0;
    }

    private Map(NodeBase? root, IEqualityComparer<TK> comparer)
    {
        _root = root;
        _comparer = comparer;
        Count = 0;
    }

    internal Map(NodeBase? root, IEqualityComparer<TK> comparer, int count)
    {
        _root = root;
        _comparer = comparer;
        Count = count;
    }

    public bool IsEmpty => Count == 0;

    /// <summary>
    ///     The equality the keys are filed under. Exposed so that a derived map — a filter, a
    ///     mapping of the values — can be built under the same one rather than silently
    ///     falling back to the default.
    /// </summary>
    public IEqualityComparer<TK> Comparer => _comparer;

    /// <summary>
    ///     The comparer's hash of a key.
    ///
    ///     The suppression is the whole reason this is a method: with no <c>notnull</c> on
    ///     <typeparamref name="TK" /> the compiler cannot see that a key is non-null, and
    ///     <see cref="IEqualityComparer{T}.GetHashCode" /> disallows one. A null key is a
    ///     caller error, not a case handled here.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HashOf(TK key)
    {
        return _comparer.GetHashCode(key!);
    }

    public MapKeyCollection<TK, TV> Keys => new(_root, Count);
    public MapValueCollection<TK, TV> Values => new(_root, Count);

    public int Count { get; }

    public TV this[TK key] => Get(key);


    public bool ContainsKey(TK key)
    {
        return TryGetValue(key, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TK key, out TV value)
    {
        if (_root == null)
        {
            value = default!;
            return false;
        }

        var hash = HashOf(key);
        return TrieOps.TryGetValue(_root, key, hash, _comparer, out value);
    }


    /// Empty, but still comparing keys the way this map does. `Empty` is the
    /// shared instance and carries the default comparer, so returning it here
    /// would change what the map means the moment something was put back in.
    public Map<TK, TV> Clear()
    {
        return ReferenceEquals(_comparer, EqualityComparer<TK>.Default)
            ? Empty
            : new Map<TK, TV>(_comparer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKey(TK key, out TK value)
    {
        if (_root == null)
        {
            value = default!;
            return false;
        }

        var hash = HashOf(key);
        return TrieOps.TryGetKey<TK, TV>(_root, key, hash, _comparer, out value);
    }

    public TV Get(TK key)
    {
        if (TryGetValue(key, out var value)) return value;

        throw new KeyNotFoundException($"The key '{key}' was not present in the map.");
    }
    // Todo: add and set should be using the same TrieOps method, which can conditionally
    // overwrite the value or raise an exception.
    public Map<TK, TV> Set(TK key, TV value)
    {
        var hash = HashOf(key);
        var newRoot = TrieOps.Insert(_root, key, value, hash, 0, _comparer, out var added);

        // If nothing was added NOR CHANGED, we can just return the same persistentmap
        if (ReferenceEquals(_root, newRoot)) return this;

        return new Map<TK, TV>(newRoot, _comparer, added ? Count + 1 : Count);
    }

    public Map<TK, TV> Add(TK key, TV value)
    {
        if (ContainsKey(key))
            throw new ArgumentException($"The key '{key}' is already in the map.");
        var hash = HashOf(key);
        var newRoot = TrieOps.Insert(_root, key, value, hash, 0, _comparer, out var added);

        return new Map<TK, TV>(newRoot, _comparer, added ? Count + 1 : Count);
    }

    public Map<TK, TV> AddRange(IEnumerable<KeyValuePair<TK, TV>> range)
    {
        var newMap = Mutate(transient =>
        {
            foreach (var kvp in range) transient.Set(kvp.Key, kvp.Value);
        });
        return newMap;
    }

    public Map<TK, TV> RemoveRange(IEnumerable<TK> range)
    {
        var newMap = Mutate(transient =>
        {
            foreach (var k in range) transient.Remove(k);
        });
        return newMap;
    }


    public Map<TK, TV> Remove(TK key)
    {
        if (_root == null) return this;

        var hash = HashOf(key);
        var newRoot = TrieOps.Remove<TK, TV>(_root!, key, hash, 0, _comparer, out var removed);

        if (!removed) return this;

        // `Clear`, not `Empty`: removing the last key must not swap the
        // comparer out from under the map.
        if (newRoot == null) return Clear();

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
            var keyHash = HashOf(kvp.Key);
            var valHash = kvp.Value == null ? 0 : valueComparer.GetHashCode(kvp.Value);

            hash ^= HashCode.Combine(keyHash, valHash);
        }

        return hash;
    }

    public TransientMap<TK, TV> ToTransient()
    {
        return new TransientMap<TK, TV>(_root, _comparer, Count);
    }

    /// <summary>
    ///     Executes a delegate on the elements of the map
    /// </summary>
    /// <returns>True if the iteration completed all elements, or False if aborted early.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Iter(Func<TK, TV, bool> action)
    {
        return TrieOps.Iter(_root, action);
    }

    /// <summary>
    ///     Filters the map, retaining only elements that satisfy the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to test each entry for a condition.</param>
    /// <returns>A new <see cref="Map{TK, TV}" /> containing the elements that satisfy the condition.</returns>
    public Map<TK, TV> Filter(Func<TK, TV, bool> predicate)
    {
        var builder = new MapBuilder<TK, TV>(_comparer);
        Iter((k, v) =>
        {
            if (predicate(k, v)) builder.Add(k, v);
            return true;
        });
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Transforms the elements of the map using the specified function.
    /// </summary>
    /// <typeparam name="TNk">The type of the new keys.</typeparam>
    /// <typeparam name="TNv">The type of the new values.</typeparam>
    /// <param name="action">A function to transform each key-value pair.</param>
    /// <param name="comparer">An optional equality comparer for the new keys.</param>
    /// <returns>A new <see cref="Map{NK, NV}" /> containing the transformed elements.</returns>
    public Map<TNk, TNv> MapCar<TNk, TNv>(Func<TK, TV, (TNk, TNv)> action, IEqualityComparer<TNk>? comparer = null)
    {
        var builder = new MapBuilder<TNk, TNv>(comparer);
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
        // `Iter` reports whether it ran to the *end*, so a walk that never stopped is a walk
        // that never matched. The outer negation is what turns "no match" into an answer.
        return !Iter((k, v) => !pred(k, v));
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
    ///     Merges another map into this one using a conflict resolution strategy.
    /// </summary>
    /// <param name="other">The other map to merge.</param>
    /// <param name="conflictResolver">
    ///     An optional thunk called when keys conflict: (key, leftValue, rightValue) => resolvedValue.
    ///     Pass null to default to picking the right value (other overwrites this).
    /// </param>
    public Map<TK, TV> Merge(Map<TK, TV> other, Func<TK, TV, TV, TV>? conflictResolver = null)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        var newRoot = TrieOps.Merge(_root, other._root, 0, _comparer, conflictResolver);

        if (ReferenceEquals(_root, newRoot)) return this;
        if (ReferenceEquals(other._root, newRoot)) return other;

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
        return new MapEnumerator<TK, TV>(_root);
    }
}