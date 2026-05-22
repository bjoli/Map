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

public sealed class Map<TK, TV>
{
    internal readonly NodeBase? Root;
    private readonly IEqualityComparer<TK> _comparer;
    private int _count;

    public static readonly Map<TK, TV> Empty = new(null, EqualityComparer<TK>.Default);
    
    public Map(IEqualityComparer<TK> comparer)
    {
        Root = null;
        _comparer = comparer;
        _count = 0;
    }
    
    public Map()
    {
        Root = null;
        _comparer = EqualityComparer<TK>.Default;
        _count = 0;
    }

    internal Map(NodeBase? root, IEqualityComparer<TK> comparer)
    {
        Root = root;
        _comparer = comparer;
        _count = 0;
    }

    internal Map(NodeBase? root, IEqualityComparer<TK> comparer, int count)
    {
        Root = root;
        _comparer = comparer;
        _count = count;
    }

    public int Count => _count;
    public bool IsEmpty => Count == 0;

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

        int hash = _comparer.GetHashCode(key!);
        return TrieOps.TryGetValue(Root, key, hash, _comparer, out value);
    }

    public TV Get(TK key)
    {
        if (TryGetValue(key, out TV value))
        {
            return value;
        }
        
        throw new KeyNotFoundException($"The key '{key}' was not present in the map.");
    }
    
    public TV this[TK key] => Get(key);

    public Map<TK, TV> Add(TK key, TV value)
    {
        int hash = _comparer.GetHashCode(key!);
        NodeBase newRoot = TrieOps.Insert(Root, key, value, hash, 0, _comparer, out bool added);
        
        // If nothing was added NOR CHANGED, we can just return the same persistentmap
        if (ReferenceEquals(Root, newRoot))
        {
            return this;
        }

        return new Map<TK, TV>(newRoot, _comparer, added ? _count + 1 : _count);
    }
    
    
    public Map<TK, TV> Remove(TK key)
    {
        if (Root == null)
        {
            return this;
        }

        int hash = _comparer.GetHashCode(key!);
        NodeBase? newRoot = TrieOps.Remove<TK,TV>(Root!, key!, hash, 0, _comparer, out bool removed);

        if (!removed)
        {
            return this;
        }

        if (newRoot == null)
        {
            return Empty;
        }
    
        return new Map<TK, TV>(newRoot, _comparer, removed ? _count - 1 : _count);
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
        {
            if (!other.TryGetValue(kvp.Key, out TV otherValue) || !valueComparer.Equals(kvp.Value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is Map<TK, TV> other && Equals(other);

    public override int GetHashCode()
    {
        if (Count == 0) return 0;

        int hash = 0;
        var valueComparer = EqualityComparer<TV>.Default;

        // XOR is commutative, guaranteeing the same hash regardless of internal tree structure
        foreach (var kvp in this)
        {
            int keyHash = kvp.Key == null ? 0 : _comparer.GetHashCode(kvp.Key);
            int valHash = kvp.Value == null ? 0 : valueComparer.GetHashCode(kvp.Value);
            
            hash ^= HashCode.Combine(keyHash, valHash);
        }

        return hash;
    }
    
    public TransientMap<TK, TV> ToTransient() => new TransientMap<TK, TV>(Root, _comparer);
    public MapEnumerator<TK, TV> GetEnumerator() => new MapEnumerator<TK, TV>(Root);


    /// <summary>
    /// Executes a delegate on the elements of the map
    /// </summary>
    /// <returns>True if the iteration completed all elements, or False if aborted early.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Iter(Func<TK, TV, bool> action)
    {
        return TrieOps.Iter<TK, TV>(Root, action);
    }

    public Map<TK,TV> Filter(Func<TV,TV,bool> action)
    {
        var builder = new MapBuilder<TK,TV>(_comparer);
        this.Iter((k, v) => {builder.Add(k,v); return true;});
        return builder.ToImmutable();
    }


    public Map<NK, NV> MapCar<NK,NV>(Func<TK,TV, (NK, NV)> action, IEqualityComparer<NK>? comparer = null) {
        var builder = new MapBuilder<NK, NV>(comparer);
        this.Iter((k, v) => {
            (var nk, var nv) = action(k, v);
            builder.Add(nk, nv);
            return true;
        });

        return builder.ToImmutable();
    }

    public TState FoldValues<TState>(TState seed, Func<TState,TV,TState> action) {
        this.Iter((_, v) => {
            seed = action(seed, v);
            return true;
        });
        return seed;
    }

    public bool Exists(Func<TK,TV, bool> pred) {
        return Iter((k,v) => {return !pred(k,v);});
    }

    public TK FindKey(Func<TK, bool> pred)
    {
        TK key = default(TK);
        bool found =false;
        this.Iter((k,v) => {
            if (pred(k))
            {
                found = true;
                key = k;
                return false;
            }
            return true;
        });
        if(!found)
            throw new KeyNotFoundException("Key not found in map");
                
        return key;
    }

    public void ForEach(Action<TK,TV> action)
    {
        this.Iter((k, v) => {action(k,v); return true;});

    }
    /// <summary>
    /// Executes a struct-based action over the map's elements. 
    /// Iteration stops immediately if the action returns false.
    /// </summary>
    /// <returns>True if the iteration completed all elements, or False if aborted early.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IterFast<TAction>(ref TAction action) where TAction : struct, IKeyValueAction<TK, TV>
    {
        return TrieOps.IterFast<TK, TV, TAction>(Root, ref action);
    }
    
    
    public Map<TK,TV> Mutate(Action<TransientMap<TK,TV>> action) {
        var transient = this.ToTransient();
        action(transient);
        return transient.ToImmutable();

    }
    
    /// <summary>
    /// Merges another map into this one. Defaults to 'Prefer Right' where values in 'other' overwrite this map.
    /// </summary>
    public Map<TK, TV> Merge(Map<TK, TV> other)
    {
        return Merge(other, null);
    }

    /// <summary>
    /// Merges another map into this one using a conflict resolution strategy.
    /// </summary>
    /// <param name="other">The other map to merge.</param>
    /// <param name="conflictResolver">
    /// An optional thunk called when keys conflict: (key, leftValue, rightValue) => resolvedValue.
    /// Pass null to default to picking the right value (other overwrites this).
    /// </param>
    public Map<TK, TV> Merge(Map<TK, TV> other, Func<TK, TV, TV, TV>? conflictResolver)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (this.IsEmpty) return other;
        if (other.IsEmpty) return this;

        NodeBase? newRoot = TrieOps.Merge<TK, TV>(this.Root, other.Root, 0, _comparer, conflictResolver);
    
        if (ReferenceEquals(this.Root, newRoot)) return this;
        if (ReferenceEquals(other.Root, newRoot)) return other;

        // Recalculate size allocation-free via IterFast
        var counter = 0;
        TrieOps.Iter<TK,TV>(newRoot, (k, v) => { counter++;
            return true;
        });

        return new Map<TK, TV>(newRoot, _comparer, counter);
    }



}
