using System.Diagnostics;
using System.Reflection;
using Map;
using Xunit.Abstractions;

namespace Tests;

// Hashes through a function, so tests can choose which keys collide.
internal sealed class HashFuncComparer(Func<int, int> hash) : IEqualityComparer<int>
{
    public bool Equals(int x, int y) => x == y;
    public int GetHashCode(int x) => hash(x);
}

// Walks a map's trie and checks the collision-node invariants.
internal static class TrieInspector
{
    // MapEnumerator's stack holds 8 frames: depths 0..7.
    private const int MaxDepth = 7;

    public static NodeBase? Root<TK, TV>(Map<TK, TV> map) =>
        (NodeBase?)typeof(Map<TK, TV>)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(map);

    // Every collision node holds >= 2 keys of one full hash; no node lies deeper than the
    // enumerator can follow. Returns the number of collision nodes and the deepest node.
    public static (int collisionNodes, int maxDepth) Check<TK, TV>(Map<TK, TV> map)
    {
        var collisionNodes = 0;
        var maxDepth = 0;
        var root = Root(map);
        if (root != null) Walk<TK, TV>(root, 0, map.Comparer, ref collisionNodes, ref maxDepth);
        return (collisionNodes, maxDepth);
    }

    private static void Walk<TK, TV>(NodeBase node, int depth, IEqualityComparer<TK> comparer,
        ref int collisionNodes, ref int maxDepth)
    {
        Assert.True(depth <= MaxDepth, $"node at depth {depth}");
        maxDepth = Math.Max(maxDepth, depth);

        var flags = NodeOps.GetFlags(node.Meta);
        if (flags == NodeFlags.Collision)
        {
            collisionNodes++;
            var slots = ((CollisionNode<TK, TV>)node).Slots;
            Assert.True(slots.Length >= 2, $"collision node with {slots.Length} slot(s)");
            var hash = comparer.GetHashCode(slots[0].Key!);
            foreach (var slot in slots) Assert.Equal(hash, comparer.GetHashCode(slot.Key!));
            return;
        }

        if (flags != NodeFlags.Internal) return;
        foreach (var child in NodeOps.GetChildSpan<TK, TV>(node))
            Walk<TK, TV>(child, depth + 1, comparer, ref collisionNodes, ref maxDepth);
    }
}

public class CollisionTests(ITestOutputHelper output)
{
    private const int Distinct = 20_000;

    // Negative keys -1..-30 hash to 0, 32 or 64: ten per hash, colliding with keys 0, 32 and 64
    // and sharing their low bits with every key that is 0 mod 32. Other keys hash to themselves.
    private static readonly HashFuncComparer Colliding = new(x => x < 0 ? -x % 3 * 32 : x);

    private static IEnumerable<int> CollidingKeys() => Enumerable.Range(1, 30).Select(i => -i);

    // Colliding keys first, so later keys pass the collision nodes on the way in.
    private static int[] AllKeys() => CollidingKeys().Concat(Enumerable.Range(0, Distinct)).ToArray();

    private static void AssertHolds(Map<int, int> map, IReadOnlyCollection<int> keys)
    {
        Assert.Equal(keys.Count, map.Count);
        foreach (var k in keys)
        {
            Assert.True(map.TryGetValue(k, out var v), $"key {k} missing");
            Assert.Equal(k * 2, v);
        }

        // Same hash as present keys, never inserted.
        Assert.False(map.ContainsKey(-33));
        Assert.False(map.ContainsKey(1_000_032));

        var seen = new HashSet<int>();
        foreach (var kvp in map) Assert.True(seen.Add(kvp.Key), $"key {kvp.Key} enumerated twice");
        Assert.Equal(keys.Count, seen.Count);

        var iterated = 0;
        map.Iter((_, _) => ++iterated > 0);
        Assert.Equal(keys.Count, iterated);

        TrieInspector.Check(map);
    }

    // Removes every colliding key and every third other key, checking along the way.
    private static void AssertRemoves(Map<int, int> map, int[] keys)
    {
        var remaining = new HashSet<int>(keys);
        foreach (var k in keys.Where(k => k < 0 || k % 3 == 0))
        {
            map = map.Remove(k);
            remaining.Remove(k);
        }

        AssertHolds(map, remaining);
        Assert.Equal(0, TrieInspector.Check(map).collisionNodes);

        foreach (var k in remaining) map = map.Remove(k);
        Assert.True(map.IsEmpty);
        Assert.Null(TrieInspector.Root(map));
    }

    [Fact]
    public void Immutable_CollisionNodesHoldOneHash()
    {
        var keys = AllKeys();
        var map = new Map<int, int>(Colliding);
        foreach (var k in keys) map = map.Set(k, k * 2);

        AssertHolds(map, keys);
        Assert.Equal(3, TrieInspector.Check(map).collisionNodes);
        AssertRemoves(map, keys);
    }

    [Fact]
    public void Immutable_ShuffledInsertOrder()
    {
        var keys = AllKeys();
        new Random(7).Shuffle(keys);
        var map = new Map<int, int>(Colliding);
        foreach (var k in keys) map = map.Set(k, k * 2);

        AssertHolds(map, keys);
        Assert.Equal(3, TrieInspector.Check(map).collisionNodes);
    }

    [Fact]
    public void Transient_CollisionNodesHoldOneHash()
    {
        var keys = AllKeys();
        var transient = new Map<int, int>(Colliding).ToTransient();
        foreach (var k in keys) transient.Set(k, k * 2);
        var map = transient.ToImmutable();

        AssertHolds(map, keys);
        Assert.Equal(3, TrieInspector.Check(map).collisionNodes);

        var removing = map.ToTransient();
        foreach (var k in keys.Where(k => k < 0)) removing.Remove(k);
        var removed = removing.ToImmutable();
        AssertHolds(removed, keys.Where(k => k >= 0).ToArray());
        Assert.Equal(0, TrieInspector.Check(removed).collisionNodes);
        // The source map is untouched.
        AssertHolds(map, keys);
    }

    [Theory]
    [InlineData(Distinct)] // scatter build
    [InlineData(40_000)] // sorted build
    public void Builder_CollisionNodesHoldOneHash(int distinct)
    {
        var keys = CollidingKeys().Concat(Enumerable.Range(0, distinct)).ToArray();
        var builder = new MapBuilder<int, int>(Colliding);
        foreach (var k in keys) builder.Add(k, k * 2);
        var map = builder.ToImmutable();

        AssertHolds(map, keys);
        Assert.Equal(3, TrieInspector.Check(map).collisionNodes);

        // Keys added to a built map land in the right place.
        map = map.Set(-31, -62).Set(-36, -72);
        Assert.Equal(-62, map[-31]);
        Assert.Equal(-72, map[-36]);
        TrieInspector.Check(map);
    }

    [Fact]
    public void Merge_CollisionNodesHoldOneHash()
    {
        var left = new MapBuilder<int, int>(Colliding);
        var right = new Map<int, int>(Colliding);
        foreach (var k in AllKeys())
            if (k % 2 == 0) left.Add(k, k * 2);
            else right = right.Set(k, k * 2);

        var merged = left.ToImmutable().Merge(right);
        AssertHolds(merged, AllKeys());
        Assert.Equal(3, TrieInspector.Check(merged).collisionNodes);
    }

    [Fact]
    public void CollisionAtDeepestLevel_IsEnumerated()
    {
        // 1 and 2 share hash 0; 3 differs from them only in bit 31, the last bit the trie reads.
        var comparer = new HashFuncComparer(x => x == 3 ? int.MinValue : 0);
        var keys = new[] { 1, 2, 3 };

        var set = new Map<int, int>(comparer).Set(1, 2).Set(2, 4).Set(3, 6);
        var built = MapModule.FromEnumerable(keys.Select(k => (k, k * 2)), comparer);

        foreach (var map in new[] { set, built })
        {
            Assert.Equal(keys.Order(), map.Select(kvp => kvp.Key).Order());
            foreach (var k in keys) Assert.Equal(k * 2, map[k]);
            var (collisionNodes, _) = TrieInspector.Check(map);
            Assert.Equal(1, collisionNodes);
        }

        // Collision node below the shift-30 node: the enumerator's last frame.
        Assert.Equal(7, TrieInspector.Check(set).maxDepth);
    }

    [Fact]
    public void Builder_DuplicateKeys_LastWins()
    {
        var map = MapModule.FromEnumerable(new[] { (1, "a"), (2, "b"), (1, "c") });
        Assert.Equal(2, map.Count);
        Assert.Equal("c", map[1]);
        Assert.Equal("b", map[2]);
        Assert.Equal((0, 0), TrieInspector.Check(map));

        var single = MapModule.FromEnumerable(new[] { (5, "a"), (5, "b"), (5, "c") });
        Assert.Single(single);
        Assert.Equal("c", single[5]);
        Assert.Equal((0, 0), TrieInspector.Check(single));
        Assert.Empty(single.Remove(5));
    }

    [Fact]
    public void Builder_DuplicateKeysAmongCollisions_LastWins()
    {
        // -1 and -4 share a hash; -2 has its own.
        var map = MapModule.FromEnumerable(
            new[] { (-1, 1), (-4, 4), (-1, 10), (-2, 2), (-2, 20), (7, 7) }, Colliding);

        Assert.Equal(4, map.Count);
        Assert.Equal(10, map[-1]);
        Assert.Equal(4, map[-4]);
        Assert.Equal(20, map[-2]);
        Assert.Equal(7, map[7]);
        Assert.Equal(1, TrieInspector.Check(map).collisionNodes);
    }

    [Fact]
    public void Builder_LargeWithDuplicates_LastWins()
    {
        const int n = 40_000;
        var builder = new MapBuilder<int, int>();
        for (var i = 0; i < n; i++) builder.Add(i, 0);
        for (var i = 0; i < n; i += 2) builder.Add(i, i);
        var map = builder.ToImmutable();

        Assert.Equal(n, map.Count);
        for (var i = 0; i < n; i++) Assert.Equal(i % 2 == 0 ? i : 0, map[i]);
        Assert.Equal(0, TrieInspector.Check(map).collisionNodes);
    }

    [Fact]
    public void LookupPastCollision_Timing()
    {
        const int n = 200_000;
        var comparer = new HashFuncComparer(x => x == 1_000_000 ? 0 : x);
        var map = new Map<int, int>(comparer).Set(0, 0).Set(1_000_000, 1);
        for (var i = 1; i < n; i++) map = map.Set(i, i);
        Assert.Equal(1, TrieInspector.Check(map).collisionNodes);

        var best = double.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < n; k += 32) Assert.True(map.ContainsKey(k));
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        output.WriteLine($"{n / 32} lookups of keys = 0 mod 32: {best:F2} ms");
    }
}
