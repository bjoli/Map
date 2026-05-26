/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Linus Björnstam
 *
 */

using System.Runtime.InteropServices;

namespace Map;


// This should not be needed, since dotnet does pack things along boundaries. 
// I had a weird edge case though, where a small value type and a reference type ended up with the pointer 
// being unaligned and shit got slow
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct DataSlot<TK, TV>
{
    public TK Key;
    public TV Value;

    // Helper constructors
    public static DataSlot<TK, TV> Data(TK key, TV value)
    {
        return new DataSlot<TK, TV> { Key = key, Value = value };
    }
}

[Flags]
internal enum NodeFlags : byte
{
    None = 0,
    Internal = 1,
    Collision = 2
}

// This is just a thread safe way to generate ids for the map. id = 0 is reserved for 
// immutable objects.
internal static class OwnerId
{
    // This is a pretty conservative batch size. If I set my 16 core 9950x processor to waste as many IDs at it can
    // it would run out of IDs in about 195 days, if the heat it puts out wouldn't kill it before. 
    private const int BatchSize = 100;

    /// <summary>
    ///     Represents an invalid / uninitialised OwnerId.
    /// </summary>
    public const ulong None = 0;

    // The maximum ID that has been reserved globally.
    // Starts at 0, so the first batch reserves IDs 1–100.
    private static long _globalHighWaterMark;

    // Per‑thread state: current ID to hand out and how many remain in the local batch.
    [ThreadStatic] private static long _localCurrentId;

    [ThreadStatic] private static int _localRemaining;

    /// <summary>
    ///     Generates the next unique <see cref="ulong" /> OwnerId.
    ///     Mostly non‑blocking (thread‑local), hits <see cref="Interlocked" /> only once per 100 IDs.
    /// </summary>
    public static ulong Next()
    {
        // Fast path: we still have IDs in our thread‑local batch (zero locking overhead).
        if (_localRemaining > 0)
        {
            _localRemaining--;
            var val = ++_localCurrentId;
            return (ulong)val;
        }

        // Slow path: we exhausted our batch (or this is the thread's first call).
        return NextBatch();
    }

    private static ulong NextBatch()
    {
        // Atomically reserve a new block of IDs from the global counter.
        // Only one thread contends for this cache line at a time.
        var reservedEnd = Interlocked.Add(ref _globalHighWaterMark, BatchSize);

        // Calculate the start of our new range.
        var reservedStart = reservedEnd - BatchSize + 1;

        // Reset the local cache.
        // _localCurrentId is set to (start - 1) so the very next increment lands on 'reservedStart'.
        _localCurrentId = reservedStart - 1;
        _localRemaining = BatchSize;

        // Perform the first generation inside the new batch (same logic as the fast path).
        _localRemaining--;
        var val = ++_localCurrentId;
        return (ulong)val;
    }
}