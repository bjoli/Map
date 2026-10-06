using Map;

namespace Tests;

public class TransientAliasingTests
{
    [Fact]
    public void Transient_OverwriteInCopiedInternalNode_LeavesSourceMapAlone()
    {
        // 32 lands under the same root bit as 0, so setting it copies the root into the
        // transient. Setting 1 afterwards then writes into the copied root's data array.
        var m = Map<int, string>.Empty.Set(1, "a").Set(0, "b").Set(32, "c");
        var t = m.ToTransient();
        t.Set(32, "c2");
        t.Set(1, "a2");

        Assert.Equal("a", m[1]);
        Assert.Equal("b", m[0]);
        Assert.Equal("c", m[32]);
        Assert.Equal("a2", t.Get(1));
        Assert.Equal("c2", t.Get(32));
    }

    [Fact]
    public void Transient_RemoveThenOverwrite_LeavesSourceMapAlone()
    {
        var m = Map<int, string>.Empty.Set(1, "a").Set(0, "b").Set(32, "c").Set(64, "d");
        var t = m.ToTransient();
        t.Remove(64);
        t.Set(1, "a2");

        Assert.Equal("a", m[1]);
        Assert.Equal("d", m[64]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Transient_RandomWrites_NeverChangeTheSourceMap(int seed)
    {
        var rng = new Random(seed);

        for (var round = 0; round < 20; round++)
        {
            var size = rng.Next(1, 5000);
            var keySpace = size * 2;
            var map = Map<int, int>.Empty;
            var snapshot = new Dictionary<int, int>();

            for (var i = 0; i < size; i++)
            {
                var k = RandomKey(rng, keySpace);
                var v = rng.Next();
                map = map.Set(k, v);
                snapshot[k] = v;
            }

            var transient = map.ToTransient();
            var expected = new Dictionary<int, int>(snapshot);
            var ops = rng.Next(1, 3 * size);
            for (var i = 0; i < ops; i++)
            {
                var k = RandomKey(rng, keySpace);
                if (rng.Next(3) == 0)
                {
                    transient.Remove(k);
                    expected.Remove(k);
                }
                else
                {
                    var v = rng.Next();
                    transient.Set(k, v);
                    expected[k] = v;
                }
            }

            AssertSame(snapshot, map);
            Assert.Equal(expected.Count, transient.Count);
            foreach (var (k, v) in expected) Assert.Equal(v, transient.Get(k));

            // A second transient over the frozen result must not disturb it either.
            var frozen = transient.ToImmutable();
            var again = frozen.ToTransient();
            foreach (var k in expected.Keys.Take(200)) again.Set(k, -1);
            foreach (var k in expected.Keys.Skip(200).Take(200)) again.Remove(k);
            AssertSame(expected, frozen);
            AssertSame(snapshot, map);
        }
    }

    // Half the keys are multiples of 32/1024 so they share low hash bits and push nodes deeper.
    private static int RandomKey(Random rng, int keySpace)
    {
        return rng.Next(4) switch
        {
            0 => rng.Next(keySpace / 32 + 1) * 32 + rng.Next(2),
            1 => rng.Next(keySpace / 1024 + 1) * 1024 + rng.Next(3),
            _ => rng.Next(keySpace)
        };
    }

    private static void AssertSame(Dictionary<int, int> expected, Map<int, int> map)
    {
        Assert.Equal(expected.Count, map.Count);
        foreach (var (k, v) in expected)
        {
            Assert.True(map.TryGetValue(k, out var actual), $"missing key {k}");
            Assert.Equal(v, actual);
        }

        var seen = 0;
        map.Iter((k, v) =>
        {
            Assert.Equal(expected[k], v);
            seen++;
            return true;
        });
        Assert.Equal(expected.Count, seen);
    }
}

public class CollisionMergeTests
{
    // 3 and 1027 hash alike; 35 shares 3's root bit but not its second-level bits.
    private sealed class Bit10Blind : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int x) => x & ~1024;
    }

    private static (Map<int, string> L, Map<int, string> R) Maps()
    {
        var c = new Bit10Blind();
        var l = new Map<int, string>(c).Set(3, "L3").Set(1027, "L1027");
        var r = new Map<int, string>(c).Set(3, "R3").Set(35, "R35");
        return (l, r);
    }

    [Fact]
    public void Merge_CollisionNodeOnLeft_RightValueWins()
    {
        var (l, r) = Maps();
        var merged = l.Merge(r);

        Assert.Equal(3, merged.Count);
        Assert.Equal("R3", merged[3]);
        Assert.Equal("L1027", merged[1027]);
        Assert.Equal("R35", merged[35]);
    }

    [Fact]
    public void Merge_CollisionNodeOnRight_RightValueWins()
    {
        var (l, r) = Maps();
        var merged = r.Merge(l);

        Assert.Equal(3, merged.Count);
        Assert.Equal("L3", merged[3]);
        Assert.Equal("L1027", merged[1027]);
        Assert.Equal("R35", merged[35]);
    }

    [Fact]
    public void MergeWith_CollisionNode_CallsResolverOnce()
    {
        var (l, r) = Maps();
        var calls = new List<(int, string, string)>();
        var merged = MapModule.MergeWith((k, a, b) =>
        {
            calls.Add((k, a, b));
            return a + b;
        }, l, r);

        Assert.Equal([(3, "L3", "R3")], calls);
        Assert.Equal("L3R3", merged[3]);

        calls.Clear();
        merged = MapModule.MergeWith((k, a, b) =>
        {
            calls.Add((k, a, b));
            return a + b;
        }, r, l);

        Assert.Equal([(3, "R3", "L3")], calls);
        Assert.Equal("R3L3", merged[3]);
    }
}
