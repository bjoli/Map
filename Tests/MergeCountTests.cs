using Map;

namespace Tests;

// Merge and MergeWith checked against a Dictionary oracle: Count, contents, right-wins values,
// and the resolver seeing exactly the keys present on both sides.
public class MergeCountTests
{
    // x and x ^ 1024 hash alike, so keys on both sides of bit 10 build collision nodes.
    private sealed class Bit10Blind : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int x) => x & ~1024;
    }

    private static readonly IEqualityComparer<int>[] Comparers = [EqualityComparer<int>.Default, new Bit10Blind()];

    private static Map<int, int> Make(IEqualityComparer<int> comparer, IEnumerable<int> keys, int valueOffset)
    {
        var t = new Map<int, int>(comparer).ToTransient();
        foreach (var k in keys) t.Set(k, k + valueOffset);
        return t.ToImmutable();
    }

    private static Dictionary<int, int> ToDict(Map<int, int> m)
    {
        var d = new Dictionary<int, int>();
        foreach (var kvp in m) d.Add(kvp.Key, kvp.Value);
        return d;
    }

    private static void AssertMatches(Dictionary<int, int> expected, Map<int, int> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        var seen = 0;
        foreach (var kvp in actual)
        {
            seen++;
            Assert.True(expected.TryGetValue(kvp.Key, out var v), $"unexpected key {kvp.Key}");
            Assert.Equal(v, kvp.Value);
        }

        Assert.Equal(expected.Count, seen);
        foreach (var (k, v) in expected)
        {
            Assert.True(actual.TryGetValue(k, out var got), $"missing key {k}");
            Assert.Equal(v, got);
        }
    }

    // Merges both ways, with and without a resolver, and checks each against the oracle.
    private static void CheckBothWays(Map<int, int> a, Map<int, int> b)
    {
        Check(a, b);
        Check(b, a);
    }

    private static void Check(Map<int, int> left, Map<int, int> right)
    {
        var l = ToDict(left);
        var r = ToDict(right);

        var rightWins = new Dictionary<int, int>(l);
        foreach (var (k, v) in r) rightWins[k] = v;
        AssertMatches(rightWins, left.Merge(right));
        AssertMatches(rightWins, MapModule.Merge(left, right));

        var expectedCalls = new HashSet<(int, int, int)>();
        var resolved = new Dictionary<int, int>(l);
        foreach (var (k, v) in r)
            if (l.TryGetValue(k, out var lv))
            {
                expectedCalls.Add((k, lv, v));
                resolved[k] = lv * 3 + v;
            }
            else
            {
                resolved[k] = v;
            }

        var calls = new List<(int, int, int)>();
        var merged = MapModule.MergeWith((k, lv, rv) =>
        {
            calls.Add((k, lv, rv));
            return lv * 3 + rv;
        }, left, right);
        AssertMatches(resolved, merged);
        Assert.Equal(expectedCalls.Count, calls.Count);
        Assert.Equal(expectedCalls, calls.ToHashSet());

        // A resolver that keeps the left value leaves the left contents plus right-only keys.
        var keepLeft = new Dictionary<int, int>(r);
        foreach (var (k, v) in l) keepLeft[k] = v;
        AssertMatches(keepLeft, left.Merge(right, (_, lv, _) => lv));
    }

    public static IEnumerable<object[]> ComparerIndices() => [[0], [1]];

    [Theory]
    [MemberData(nameof(ComparerIndices))]
    public void Shapes(int comparerIndex)
    {
        var c = Comparers[comparerIndex];
        var empty = new Map<int, int>(c);
        var small = Make(c, [1, 2, 3, 1025, 1026], 0);
        var big = Make(c, Enumerable.Range(0, 5000), 0);

        // One side empty.
        CheckBothWays(empty, small);
        CheckBothWays(empty, big);
        CheckBothWays(empty, empty);

        // Disjoint.
        CheckBothWays(big, Make(c, Enumerable.Range(10_000, 3000), 7));
        CheckBothWays(small, Make(c, [4, 5, 1028, 99_999], 1));

        // Overlapping, right values differ.
        CheckBothWays(big, Make(c, Enumerable.Range(2500, 5000), 100));
        CheckBothWays(small, Make(c, [3, 1025, 2049, 7], 50));

        // Identical: the same map, and an equal map built separately.
        CheckBothWays(big, big);
        CheckBothWays(big, Make(c, Enumerable.Range(0, 5000), 0));

        // A map against modified copies of itself.
        CheckBothWays(big, big.Set(7, -1));
        CheckBothWays(big, big.Set(1031, -1).Set(77_777, 3));
        CheckBothWays(big, big.Remove(1024).Remove(0).Remove(4999));
        CheckBothWays(big, big.Remove(5).Set(123_456, 1).Set(9, 9));
        CheckBothWays(small, small.Set(1027, 0).Remove(2));

        // One entry against a big map.
        CheckBothWays(big, Make(c, [42], 1));
        CheckBothWays(big, Make(c, [1_000_000], 1));
    }

    [Fact]
    public void SharedTreeMergeKeepsLeftWhenNothingChanges()
    {
        foreach (var c in Comparers)
        {
            var big = Make(c, Enumerable.Range(0, 5000), 0);
            Assert.Same(big, big.Merge(big));
            Assert.Same(big, big.Merge(big.Remove(5)));
            Assert.Same(big, big.Merge(big.Remove(1024).Remove(0)));
            Assert.Same(big, big.Merge(Make(c, [10, 1034, 4000], 0)));
            Assert.Same(big, big.Merge(big.Set(7, 8), (_, l, _) => l));

            var changed = big.Merge(big.Set(7, 8));
            Assert.NotSame(big, changed);
            Assert.Equal(5000, changed.Count);
            Assert.Equal(8, changed[7]);
        }
    }

    [Fact]
    public void ResolverRunsOverSharedSubtrees()
    {
        foreach (var c in Comparers)
        {
            var big = Make(c, Enumerable.Range(0, 3000), 0);
            var calls = 0;
            var merged = big.Merge(big.Set(7, 100), (k, l, r) =>
            {
                calls++;
                Assert.Equal(k, l);
                Assert.Equal(k == 7 ? 100 : k, r);
                return l + r;
            });
            Assert.Equal(3000, calls);
            Assert.Equal(3000, merged.Count);
            Assert.Equal(107, merged[7]);
            Assert.Equal(20, merged[10]);
        }
    }

    // {3, 1027} is a collision node at depth 1; {1027, 2051, 4099} sit one level deeper, below
    // an internal node in the same position, so a collision node meets an internal node.
    [Fact]
    public void CollisionNodeAgainstInternalNode()
    {
        var c = new Bit10Blind();
        var col = Make(c, [3, 1027], 0);
        var deep = Make(c, [1027, 2051, 4099], 5);
        CheckBothWays(col, deep);
        CheckBothWays(col.Set(64, 1), deep.Set(96, 2));
        CheckBothWays(col, Make(c, [2051, 4099], 5));
    }

    [Fact]
    public void Randomized()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var rng = new Random(seed);
            var c = Comparers[seed % 2];
            var range = rng.Next(1, 4) switch { 1 => 64, 2 => 4096, _ => 200_000 };

            var left = Make(c, Enumerable.Range(0, rng.Next(0, 1500)).Select(_ => rng.Next(range)), 0);

            Map<int, int> right;
            if (rng.Next(2) == 0)
            {
                right = Make(c, Enumerable.Range(0, rng.Next(0, 1500)).Select(_ => rng.Next(range)),
                    rng.Next(2) * 1000);
            }
            else
            {
                // A modified copy, so the two trees share most of their subtrees.
                right = left;
                var edits = rng.Next(0, 20);
                for (var i = 0; i < edits; i++)
                {
                    var k = rng.Next(range);
                    right = rng.Next(3) == 0 ? right.Remove(k) : right.Set(k, rng.Next(3) == 0 ? k : -k - 1);
                }
            }

            Check(left, right);
            Check(right, left);
        }
    }
}
