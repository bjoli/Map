using Map;

namespace Tests;

/// <summary>
///     The struct cursor must give the entries the enumerator gives, in the same order, for
///     every shape of trie: a leaf root, internal nodes with and without entries of their own,
///     collision nodes, and nodes that removals left behind.
/// </summary>
public class CursorTests
{
    private static List<(TK, TV)> Walk<TK, TV>(Map<TK, TV> map)
    {
        var seen = new List<(TK, TV)>();
        for (var c = MapModule.Cursor(map); !MapModule.CursorDone(c); c = MapModule.CursorNext(c))
            seen.Add(MapModule.CursorCurrent(c));
        return seen;
    }

    private static List<(TK, TV)> Enumerate<TK, TV>(Map<TK, TV> map)
    {
        var seen = new List<(TK, TV)>();
        foreach (var kv in map) seen.Add((kv.Key, kv.Value));
        return seen;
    }

    private static void AssertSameWalk<TK, TV>(Map<TK, TV> map)
    {
        var walked = Walk(map);
        Assert.Equal(Enumerate(map), walked);
        Assert.Equal(map.Count, walked.Count);
    }

    private static Map<int, int> Ints(int n, IEqualityComparer<int>? comparer = null)
    {
        var map = new Map<int, int>(comparer ?? EqualityComparer<int>.Default);
        for (var i = 0; i < n; i++) map = map.Set(i, i * 3);
        return map;
    }

    [Fact]
    public void Empty()
    {
        Assert.True(MapModule.CursorDone(MapModule.Cursor(MapModule.Empty<string, int>())));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void IntsInEnumeratorOrder(int n)
    {
        AssertSameWalk(Ints(n));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    [InlineData(50_000)]
    public void StringsInEnumeratorOrder(int n)
    {
        var map = MapModule.Empty<string, string>();
        for (var i = 0; i < n; i++) map = map.Set("k" + i, "v" + i);
        AssertSameWalk(map);
    }

    [Fact]
    public void Collisions()
    {
        // Every key in one of three hashes: collision nodes at the bottom of the trie.
        AssertSameWalk(Ints(300, new HashFuncComparer(x => x % 3)));
        // All keys in one hash.
        AssertSameWalk(Ints(50, new HashFuncComparer(_ => 0)));
        // Hashes that share long prefixes: deep chains of internal nodes.
        AssertSameWalk(Ints(200, new HashFuncComparer(x => x << 20)));
        AssertSameWalk(Ints(200, new HashFuncComparer(x => (x & 1) == 0 ? int.MinValue | x : x << 27)));
    }

    [Fact]
    public void AfterRemovals()
    {
        // Removals leave internal nodes with no entries of their own.
        var map = Ints(5000);
        for (var i = 0; i < 5000; i += 3) map = map.Remove(i);
        AssertSameWalk(map);
        for (var i = 1; i < 5000; i += 2) map = map.Remove(i);
        AssertSameWalk(map);
    }

    [Fact]
    public void FromTransient()
    {
        var transient = MapModule.Empty<int, int>().ToTransient();
        for (var i = 0; i < 3000; i++) transient.Set(i * 7, i);
        AssertSameWalk(transient.ToImmutable());
    }

    [Fact]
    public void ACursorIsAValue()
    {
        var map = Ints(500);
        var first = MapModule.Cursor(map);
        var entry = MapModule.CursorCurrent(first);
        var c = first;
        for (var i = 0; i < 100; i++) c = MapModule.CursorNext(c);

        Assert.Equal(entry, MapModule.CursorCurrent(first));
        Assert.Equal(Walk(map).Skip(100).First(), MapModule.CursorCurrent(c));
    }
}
