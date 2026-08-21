/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Linus Björnstam
 *
 */

namespace Map;

/// <summary>
///     The static interface to <see cref="TransientMap{TK, TV}" />: the map's own interface with
///     the writes turned inside out.
///
///     The same names in the same argument order as <see cref="MapModule" />, mutating in place
///     and answering nothing. It is the fast way to make a map out of *another* map, because it
///     owns the nodes it has touched and <see cref="ToPersistent{TK,TV}" /> hands the result back
///     in O(1).
/// </summary>
public static class TransientMapModule
{
    // ---------------------------------------------------------
    // Construction
    // ---------------------------------------------------------

    // transientmap-empty
    public static TransientMap<TK, TV> Empty<TK, TV>(IEqualityComparer<TK>? comparer = null)
    {
        return MapModule.Empty<TK, TV>(comparer).ToTransient();
    }

    // map->transientmap
    public static TransientMap<TK, TV> FromPersistent<TK, TV>(Map<TK, TV> map)
    {
        return map.ToTransient();
    }

    /// <summary>
    ///     The map as it stands. The transient stays usable and disowns what it handed over, so a
    ///     later write copies rather than corrupting the snapshot.
    /// </summary>
    // transientmap->map
    public static Map<TK, TV> ToPersistent<TK, TV>(TransientMap<TK, TV> map)
    {
        return map.ToImmutable();
    }

    public static TransientMap<TK, TV> FromEnumerable<TK, TV>(
        IEnumerable<(TK key, TV value)> source,
        IEqualityComparer<TK>? comparer = null)
    {
        var transient = MapModule.Empty<TK, TV>(comparer).ToTransient();
        foreach (var (key, value) in source) transient.Set(key, value);
        return transient;
    }

    /// <summary>A walk of the map as it stands. Finish it before the next write.</summary>
    public static IEnumerable<(TK, TV)> AsEnumerable<TK, TV>(TransientMap<TK, TV> map)
    {
        var enumerator = map.GetEnumerator();
        while (enumerator.MoveNext()) yield return enumerator.CurrentEntry;
    }

    // ---------------------------------------------------------
    // Reading
    // ---------------------------------------------------------

    // transientmap-length
    public static int Count<TK, TV>(TransientMap<TK, TV> map) => map.Count;

    // transientmap-empty?
    public static bool IsEmpty<TK, TV>(TransientMap<TK, TV> map) => map.IsEmpty;

    // transientmap-ref
    public static TV Ref<TK, TV>(TransientMap<TK, TV> map, TK key) => map.Get(key);

    // transientmap-ref-or
    public static TV RefOr<TK, TV>(TransientMap<TK, TV> map, TK key, TV fallback)
    {
        return map.TryGetValue(key, out var value) ? value : fallback;
    }

    // transientmap-try-ref, before the Option is put on in Bjolang
    public static (bool found, TV value) TryGetValue<TK, TV>(TransientMap<TK, TV> map, TK key)
    {
        var found = map.TryGetValue(key, out var value);
        return (found, value);
    }

    // transientmap-contains?
    public static bool ContainsKey<TK, TV>(TransientMap<TK, TV> map, TK key) => map.ContainsKey(key);

    // ---------------------------------------------------------
    // Writing — in place, answering nothing
    // ---------------------------------------------------------

    // transientmap-set!
    public static void Set<TK, TV>(TransientMap<TK, TV> map, TK key, TV value)
    {
        map.Set(key, value);
    }

    // transientmap-remove!
    public static void Remove<TK, TV>(TransientMap<TK, TV> map, TK key)
    {
        map.Remove(key);
    }

    // transientmap-set-all!
    public static void SetRange<TK, TV>(TransientMap<TK, TV> map, IEnumerable<(TK key, TV value)> range)
    {
        foreach (var (key, value) in range) map.Set(key, value);
    }

    // transientmap-remove-all!
    public static void RemoveRange<TK, TV>(TransientMap<TK, TV> map, IEnumerable<TK> range)
    {
        foreach (var key in range) map.Remove(key);
    }

    /// <summary>
    ///     Drops every entry <paramref name="predicate" /> rejects.
    ///
    ///     The doomed keys are collected before any of them is removed: a write mutates the nodes
    ///     a walk in progress is standing on.
    /// </summary>
    // transientmap-filter!
    public static void FilterInPlace<TK, TV>(Func<TK, TV, bool> predicate, TransientMap<TK, TV> map)
    {
        var doomed = new List<TK>();
        map.Iter((key, value) =>
        {
            if (!predicate(key, value)) doomed.Add(key);
            return true;
        });

        foreach (var key in doomed) map.Remove(key);
    }
}
