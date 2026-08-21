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
///     The static interface to <see cref="MapBuilder{TK, TV}" />: append, then build once.
///
///     A builder is the bulk path. Appends land in a flat buffer and are scattered into the trie
///     by hash in one pass at the end, where setting a key on the map itself pays a copy down the
///     trie per entry. It is the fast way to make a map out of *nothing*; a transient is the fast
///     way to make one out of another map.
/// </summary>
public static class MapBuilderModule
{
    // mapbuilder-empty
    public static MapBuilder<TK, TV> Empty<TK, TV>(IEqualityComparer<TK>? comparer = null)
    {
        return new MapBuilder<TK, TV>(comparer);
    }

    // mapbuilder-empty, with room reserved
    public static MapBuilder<TK, TV> Empty<TK, TV>(int capacity, IEqualityComparer<TK>? comparer = null)
    {
        return new MapBuilder<TK, TV>(comparer, capacity < 1 ? 1 : capacity);
    }

    // map->mapbuilder
    public static MapBuilder<TK, TV> FromMap<TK, TV>(Map<TK, TV> map)
    {
        var builder = new MapBuilder<TK, TV>(map.Comparer, map.Count < 1 ? 1 : map.Count);
        map.Iter((key, value) =>
        {
            builder.Add(key, value);
            return true;
        });
        return builder;
    }

    // mapbuilder-add!
    public static void Add<TK, TV>(MapBuilder<TK, TV> builder, TK key, TV value)
    {
        builder.Add(key, value);
    }

    /// <summary>
    ///     Every entry of <paramref name="range" />, appended. An entry is a
    ///     <c>(key, value)</c> tuple: it crosses the boundary as a value rather than as an
    ///     argument list, so it is the shape any language with tuples can already destructure.
    /// </summary>
    // mapbuilder-add-all!
    public static void AddRange<TK, TV>(MapBuilder<TK, TV> builder, IEnumerable<(TK key, TV value)> range)
    {
        foreach (var (key, value) in range) builder.Add(key, value);
    }

    /// <summary>
    ///     How many entries have been *appended*. A key appended twice is resolved when the map is
    ///     built, last one winning, so this is not the size of the map to come.
    /// </summary>
    // mapbuilder-length
    public static int Count<TK, TV>(MapBuilder<TK, TV> builder)
    {
        return builder.Count;
    }

    // mapbuilder->map
    public static Map<TK, TV> Build<TK, TV>(MapBuilder<TK, TV> builder)
    {
        return builder.ToImmutable();
    }
}
