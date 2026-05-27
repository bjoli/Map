/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */

using System.Collections.Immutable;

namespace Map;


// This contains the parts of IImmutableDictionary that are already implemented in the main class
// but do not satisfy the types. 
public sealed partial class Map<TK, TV> :
    IImmutableDictionary<TK, TV>
    where TK : notnull
{
    bool IImmutableDictionary<TK, TV>.TryGetKey(TK key, out TK value)
    {
        return TryGetKey(key, out value);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.Add(TK key, TV val)
    {
        return Add(key, val);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.AddRange(IEnumerable<KeyValuePair<TK, TV>> pairs)
    {
        return AddRange(pairs);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.Clear()
    {
        return Empty;
    }

    bool IImmutableDictionary<TK, TV>.Contains(KeyValuePair<TK, TV> kvp)
    {
        return Exists((k, v) => k.Equals(kvp.Key) && v!.Equals(kvp.Value));
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.Remove(TK key)
    {
        return Remove(key);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.RemoveRange(IEnumerable<TK> keys)
    {
        return RemoveRange(keys);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.SetItem(TK key, TV value)
    {
        return Set(key, value);
    }

    IImmutableDictionary<TK, TV> IImmutableDictionary<TK, TV>.SetItems(IEnumerable<KeyValuePair<TK, TV>> kvPairs)
    {
        var newMap = Mutate(transient =>
        {
            foreach (var kvp in kvPairs) transient.Set(kvp.Key, kvp.Value);
        });
        return newMap;
    }
}