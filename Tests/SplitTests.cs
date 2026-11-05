using System.Numerics;
using Map;

namespace Tests;

// Map.Split: the parts hold each entry once, are maps of their own, and leave the map as it was.
public class SplitTests
{
    private static Map<int, int> Of(IEnumerable<int> keys, IEqualityComparer<int>? comparer = null)
    {
        var map = comparer == null ? Map<int, int>.Empty : new Map<int, int>(comparer);
        foreach (var k in keys) map = map.Set(k, k * 2);
        return map;
    }

    // Each key lies under the bits of its hash: the bit of a data entry or a child at its level
    // is the hash's 5 bits there. A collision node holds one full hash. Counts and bitmaps agree.
    private static int CheckNode(NodeBase node, int shift, IEqualityComparer<int> comparer)
    {
        var flags = NodeOps.GetFlags(node.Meta);
        if (flags == NodeFlags.Collision)
        {
            var slots = ((CollisionNode<int, int>)node).Slots;
            Assert.True(slots.Length >= 2);
            return slots.Length;
        }

        var dataMap = (uint)node.Map;
        ReadOnlySpan<DataSlot<int, int>> data = flags == NodeFlags.None
            ? NodeOps.GetLeafDataSpan<int, int>(node)
            : NodeOps.GetDataArray<int, int>(node);
        Assert.Equal(BitOperations.PopCount(dataMap), data.Length);
        var i = 0;
        for (var m = dataMap; m != 0; m &= m - 1)
        {
            var bit = BitOperations.TrailingZeroCount(m);
            Assert.Equal(bit, (comparer.GetHashCode(data[i++].Key) >>> shift) & 0x1F);
        }

        var n = data.Length;
        if (flags != NodeFlags.Internal) return n;
        var nodeMap = (uint)(node.Map >> 32);
        var children = NodeOps.GetChildSpan<int, int>(node);
        Assert.Equal(BitOperations.PopCount(nodeMap), children.Length);
        Assert.Equal(0u, dataMap & nodeMap);
        i = 0;
        for (var m = nodeMap; m != 0; m &= m - 1)
        {
            var child = children[i++];
            var bit = BitOperations.TrailingZeroCount(m);
            foreach (var key in Keys(child))
                Assert.Equal(bit, (comparer.GetHashCode(key) >>> shift) & 0x1F);
            n += CheckNode(child, shift + 5, comparer);
        }

        return n;
    }

    private static IEnumerable<int> Keys(NodeBase node)
    {
        var keys = new List<int>();
        TrieOps.Iter<int, int>(node, (k, _) =>
        {
            keys.Add(k);
            return true;
        });
        return keys;
    }

    private static void CheckParts(Map<int, int> map, int[] keys, int k)
    {
        var parts = map.Split(k);
        Assert.InRange(parts.Length, 1, Math.Max(1, k));
        if (keys.Length >= k) Assert.True(parts.Length >= Math.Min(k, 2) || keys.Length < 2);

        var seen = new HashSet<int>();
        foreach (var part in parts)
        {
            var root = TrieInspector.Root(part);
            Assert.Equal(part.Count, root == null ? 0 : CheckNode(root, 0, map.Comparer));
            Assert.Equal(part.Count, part.Count());
            Assert.Same(map.Comparer, part.Comparer);
            foreach (var (key, value) in part)
            {
                Assert.True(seen.Add(key), $"key {key} is in two parts");
                Assert.Equal(key * 2, value);
                Assert.True(part.TryGetValue(key, out var v));
                Assert.Equal(key * 2, v);
            }
        }

        Assert.Equal(keys.Length, seen.Count);
        foreach (var key in keys) Assert.Contains(key, seen);

        // A key is found only in its own part.
        foreach (var part in parts)
            foreach (var key in keys.Take(200))
                Assert.Equal(part.ContainsKey(key), part.Any(e => e.Key == key));

        // Merged back, the parts are the map.
        var merged = parts.Aggregate(new Map<int, int>(map.Comparer), (acc, p) => acc.Merge(p));
        Assert.True(merged.Equals(map));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(33)]
    [InlineData(1000)]
    [InlineData(20_000)]
    [InlineData(100_000)]
    public void Split_PartsHoldEachEntryOnce(int n)
    {
        var keys = Enumerable.Range(0, n).Select(i => i * 7919 + 3).ToArray();
        var map = Of(keys);
        foreach (var k in new[] { 1, 2, 3, 7, 96, 1000 }) CheckParts(map, keys, k);
    }

    [Fact]
    public void Split_WithCollisions()
    {
        var comparer = new HashFuncComparer(x => x < 0 ? -x % 3 * 32 : x);
        var keys = Enumerable.Range(1, 30).Select(i => -i).Concat(Enumerable.Range(0, 20_000)).ToArray();
        var map = Of(keys, comparer);
        foreach (var k in new[] { 2, 7, 96, 5000 }) CheckParts(map, keys, k);
    }

    [Fact]
    public void Split_ManyKeysOnFewHashBits()
    {
        // Hashes that agree in their low 10 bits put every key two levels down.
        var comparer = new HashFuncComparer(x => x << 10);
        var keys = Enumerable.Range(0, 5000).ToArray();
        var map = Of(keys, comparer);
        foreach (var k in new[] { 2, 7, 96 }) CheckParts(map, keys, k);
    }

    [Fact]
    public void Split_PartsAreClose()
    {
        var map = Of(Enumerable.Range(0, 100_000));
        foreach (var k in new[] { 2, 8, 96 })
        {
            var parts = map.Split(k);
            Assert.Equal(k, parts.Length);
            Assert.All(parts, p => Assert.InRange(p.Count, 100_000 / k / 2, 100_000 / k * 2));
        }
    }

    [Fact]
    public void Split_OneOrEmptyIsTheMap()
    {
        var map = Of(Enumerable.Range(0, 100));
        Assert.Same(map, Assert.Single(map.Split(1)));
        Assert.Same(map, Assert.Single(map.Split(0)));
        var empty = Map<int, int>.Empty;
        Assert.Same(empty, Assert.Single(empty.Split(8)));
    }

    [Fact]
    public void Split_PartsChangeLikeMaps()
    {
        var keys = Enumerable.Range(0, 20_000).ToArray();
        var map = Of(keys);
        var parts = map.Split(16);
        foreach (var part in parts)
        {
            var own = part.Select(e => e.Key).ToArray();
            var p = part;
            foreach (var key in own.Where((_, i) => i % 2 == 0)) p = p.Remove(key);
            p = p.Set(-1, -2);
            Assert.Equal(own.Length - (own.Length + 1) / 2 + 1, p.Count);
            Assert.True(p.ContainsKey(-1));
            foreach (var (key, i) in own.Select((key, i) => (key, i)))
                Assert.Equal(i % 2 != 0, p.ContainsKey(key));

            var t = part.ToTransient();
            foreach (var key in own) t.Remove(key);
            Assert.Equal(0, t.Count);
            Assert.Equal(own.Length, part.Count);
        }

        Assert.Equal(keys.Length, map.Count);
        foreach (var key in keys) Assert.True(map.ContainsKey(key));
    }
}
