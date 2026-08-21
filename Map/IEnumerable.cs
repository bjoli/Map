/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2024-2026 Linus Björnstam
 *
 */

using System.Collections;

namespace Map;

// Explicit implementation of IEnumerable to satisfy the interfaces

public sealed partial class Map<TK,TV>  :
    IEnumerable<KeyValuePair<TK,TV>>
{
    
    IEnumerator<KeyValuePair<TK, TV>> IEnumerable<KeyValuePair<TK, TV>>.GetEnumerator()
    {
        return new MapEnumerator<TK, TV>(_root);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new MapEnumerator<TK, TV>(_root);
    }

    IEnumerable<TK> IReadOnlyDictionary<TK, TV>.Keys => Keys;
    IEnumerable<TV> IReadOnlyDictionary<TK, TV>.Values => Values;
}