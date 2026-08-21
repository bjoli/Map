/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Linus Björnstam
 *
 */

using System.Runtime.CompilerServices;

namespace Map;

/// <summary>
///     The static interface to <see cref="Map{TK, TV}" />.
///
///     Every operation takes the map explicitly, and the higher-order ones take the function
///     first and the map last. Folds take the accumulator first, as a left fold does everywhere
///     else in this codebase.
///
///     A callback is handed the key and the value as two arguments. That is what the trie's own
///     walk produces, so nothing is packed and unpacked to make the call. The exceptions are the
///     places where a *value* crosses the boundary rather than an argument list: a bulk write
///     takes <c>(key, value)</c> tuples, a walk yields them, and
///     <see cref="CursorCurrent{TK,TV}" /> answers one — a tuple is a structural type that any
///     language with tuples already has a name for, where a
///     <see cref="KeyValuePair{TKey,TValue}" /> or an <c>out</c> parameter is a C# idiom nothing
///     else can call. A partial answer is a tuple for the same reason, with a leading
///     <c>found</c> flag.
/// </summary>
public static class MapModule
{
    // ---------------------------------------------------------
    // Construction
    // ---------------------------------------------------------

    // map-empty
    public static Map<TK, TV> Empty<TK, TV>(IEqualityComparer<TK>? comparer = null)
    {
        return comparer is null ? Map<TK, TV>.Empty : new Map<TK, TV>(comparer);
    }

    /// <summary>
    ///     The map of everything <paramref name="source" /> yields, later keys winning.
    ///
    ///     Through the builder rather than a transient: appends land in a flat buffer and are
    ///     scattered into the trie by hash in one pass at the end, which beats inserting a key at
    ///     a time.
    /// </summary>
    // seq->map
    public static Map<TK, TV> FromEnumerable<TK, TV>(
        IEnumerable<(TK key, TV value)> source,
        IEqualityComparer<TK>? comparer = null)
    {
        var builder = new MapBuilder<TK, TV>(comparer);
        foreach (var (key, value) in source) builder.Add(key, value);
        return builder.ToImmutable();
    }

    /// <summary>The entries, in the trie's own order — which is to say, in no order to rely on.</summary>
    // map->seq
    public static IEnumerable<(TK, TV)> AsEnumerable<TK, TV>(Map<TK, TV> map)
    {
        var enumerator = map.GetEnumerator();
        while (enumerator.MoveNext()) yield return enumerator.CurrentEntry;
    }

    // map-clear
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Clear<TK, TV>(Map<TK, TV> map) => map.Clear();

    // map->transientmap
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TransientMap<TK, TV> ToTransient<TK, TV>(Map<TK, TV> map) => map.ToTransient();

    // ---------------------------------------------------------
    // Reading
    // ---------------------------------------------------------

    // map-length
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<TK, TV>(Map<TK, TV> map) => map.Count;

    // map-empty?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEmpty<TK, TV>(Map<TK, TV> map) => map.IsEmpty;

    /// <summary>
    ///     The value under <paramref name="key" />, or a throw. <see cref="TryGetValue{TK,TV}" />
    ///     is the total one and <see cref="RefOr{TK,TV}" /> the one with a default.
    /// </summary>
    // map-ref
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TV Ref<TK, TV>(Map<TK, TV> map, TK key) => map.Get(key);

    // map-ref-or
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TV RefOr<TK, TV>(Map<TK, TV> map, TK key, TV fallback) =>
        map.TryGetValue(key, out var value) ? value : fallback;

    // map-try-ref, before the Option is put on in Bjolang
    public static (bool found, TV value) TryGetValue<TK, TV>(Map<TK, TV> map, TK key)
    {
        var found = map.TryGetValue(key, out var value);
        return (found, value);
    }

    // map-contains? / map-has-key?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsKey<TK, TV>(Map<TK, TV> map, TK key) => map.ContainsKey(key);

    // map-keys
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<TK> Keys<TK, TV>(Map<TK, TV> map) => map.Keys;

    // map-values
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<TV> Values<TK, TV>(Map<TK, TV> map) => map.Values;

    // ---------------------------------------------------------
    // Writing
    // ---------------------------------------------------------

    // map-set: the key's value, replaced or added
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Set<TK, TV>(Map<TK, TV> map, TK key, TV value) => map.Set(key, value);

    /// <summary>The same, but a key already there is an error rather than a replacement.</summary>
    // map-add
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Add<TK, TV>(Map<TK, TV> map, TK key, TV value) => map.Add(key, value);

    // map-remove
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Remove<TK, TV>(Map<TK, TV> map, TK key) => map.Remove(key);

    /// <summary>
    ///     Every entry of <paramref name="range" />, set in one pass. Through a transient, so the
    ///     trie is copied once rather than once per entry.
    /// </summary>
    // map-set-all
    public static Map<TK, TV> SetRange<TK, TV>(Map<TK, TV> map, IEnumerable<(TK key, TV value)> range)
    {
        var transient = map.ToTransient();
        foreach (var (key, value) in range) transient.Set(key, value);
        return transient.ToImmutable();
    }

    // map-remove-all
    public static Map<TK, TV> RemoveRange<TK, TV>(Map<TK, TV> map, IEnumerable<TK> range)
    {
        var transient = map.ToTransient();
        foreach (var key in range) transient.Remove(key);
        return transient.ToImmutable();
    }

    /// <summary>
    ///     <paramref name="other" /> laid over <paramref name="map" />: where both have a key, the
    ///     right-hand value wins. Cheap where the two have a history in common — an untouched
    ///     subtree is taken whole rather than walked.
    /// </summary>
    // map-merge
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Merge<TK, TV>(Map<TK, TV> map, Map<TK, TV> other) => map.Merge(other);

    /// <summary>
    ///     The same, with the collisions decided by <paramref name="resolver" />, which is handed
    ///     the key, the left value and the right one.
    /// </summary>
    // map-merge-with
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> MergeWith<TK, TV>(
        Func<TK, TV, TV, TV> resolver,
        Map<TK, TV> map,
        Map<TK, TV> other) => map.Merge(other, resolver);

    // ---------------------------------------------------------
    // Higher-order — function first, map last
    // ---------------------------------------------------------

    /// <summary>
    ///     A new value for every entry, filed under the key it came from. The mapper is handed the
    ///     key and the value and answers a value: what the result is filed under is the key, so
    ///     letting the mapper move one would make collisions this function has no answer for.
    /// </summary>
    // map-map
    public static Map<TK, TV2> Map<TK, TV, TV2>(Func<TK, TV, TV2> mapper, Map<TK, TV> map)
    {
        var builder = new MapBuilder<TK, TV2>(null, System.Math.Max(map.Count, 1));
        map.Iter((k, v) =>
        {
            builder.Add(k, mapper(k, v));
            return true;
        });
        return builder.ToImmutable();
    }

    /// <summary>
    ///     The functor's map: the value moves, the key rides along.
    ///
    ///     The one place a two-argument callback will not do. A functor's <c>(-> %a %b)</c> has to
    ///     replace the element type and hand back the same shape, and the only argument of
    ///     <c>(Map %k %v)</c> free to move is the value.
    /// </summary>
    // map-map-values
    public static Map<TK, TV2> MapValues<TK, TV, TV2>(Func<TV, TV2> mapper, Map<TK, TV> map)
    {
        var builder = new MapBuilder<TK, TV2>(null, System.Math.Max(map.Count, 1));
        map.Iter((k, v) =>
        {
            builder.Add(k, mapper(v));
            return true;
        });
        return builder.ToImmutable();
    }

    // map-filter
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<TK, TV> Filter<TK, TV>(Func<TK, TV, bool> predicate, Map<TK, TV> map) =>
        map.Filter(predicate);

    // map-fold: folder state map
    public static TState Fold<TK, TV, TState>(Func<TState, TK, TV, TState> folder, TState state, Map<TK, TV> map)
    {
        var current = state;
        map.Iter((k, v) =>
        {
            current = folder(current, k, v);
            return true;
        });
        return current;
    }

    // map-for-each
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<TK, TV>(Action<TK, TV> action, Map<TK, TV> map) => map.ForEach(action);

    /// <summary>
    ///     Walks until <paramref name="action" /> answers false, and reports whether it ran to the
    ///     end. The early-exit walk the predicates below are written in terms of.
    /// </summary>
    // map-iter
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Iter<TK, TV>(Func<TK, TV, bool> action, Map<TK, TV> map) => map.Iter(action);

    // map-any?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Exists<TK, TV>(Func<TK, TV, bool> predicate, Map<TK, TV> map) => map.Exists(predicate);

    /// <summary>
    ///     The first entry satisfying <paramref name="predicate" />, in the trie's own order. A
    ///     tuple with a leading flag rather than an exception: "nothing matched" is an answer a
    ///     caller can be handed rather than one it has to catch.
    /// </summary>
    // map-find, before the Option is put on in Bjolang
    public static (bool found, TK key, TV value) TryFind<TK, TV>(Func<TK, TV, bool> predicate, Map<TK, TV> map)
    {
        var foundKey = default(TK);
        var foundValue = default(TV);

        var ranToEnd = map.Iter((k, v) =>
        {
            if (!predicate(k, v)) return true;
            foundKey = k;
            foundValue = v;
            return false;
        });

        return (!ranToEnd, foundKey!, foundValue!);
    }

    // ---------------------------------------------------------
    // Walking
    // ---------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MapCursor<TK, TV> Cursor<TK, TV>(Map<TK, TV> map) => new MapCursor<TK, TV>(map);

    /// <summary>
    ///     Advances the cursor, and answers whether it ran off the end.
    ///
    ///     The advance happens here rather than in a step of its own: a walk asks "is there
    ///     more?" exactly once per entry, so folding the two together is what lets the whole
    ///     traversal allocate nothing after the cursor itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CursorDone<TK, TV>(MapCursor<TK, TV> cursor) => !cursor.Enumerator.MoveNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (TK, TV) CursorCurrent<TK, TV>(MapCursor<TK, TV> cursor) => cursor.Enumerator.CurrentEntry;
}

/// <summary>
///     A position in a walk of a <see cref="Map{TK, TV}" />.
///
///     <see cref="MapEnumerator{TK, TV}" /> is a struct, which is what keeps a <c>foreach</c>
///     allocation-free — and exactly what makes it useless to a caller that has to *hold* the
///     position, since every copy advances independently. This is that struct in a heap cell:
///     one allocation for the walk, none per entry.
/// </summary>
public sealed class MapCursor<TK, TV>
{
    public MapEnumerator<TK, TV> Enumerator;

    public MapCursor(Map<TK, TV> map)
    {
        Enumerator = map.GetEnumerator();
    }
}
