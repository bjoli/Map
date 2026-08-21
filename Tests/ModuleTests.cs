using Map;

namespace Tests;

/// <summary>
///     The static interface Bjolang binds to. What is pinned here is the *shape*: a callback
///     takes the key and the value as two arguments, and a tuple appears only where a value
///     rather than an argument list crosses the boundary.
/// </summary>
public class ModuleTests
{
    private static Map<string, int> Sample()
    {
        return MapModule.FromEnumerable(new[] { ("a", 1), ("b", 2), ("c", 3) });
    }

    [Fact]
    public void Reading()
    {
        var map = Sample();

        Assert.Equal(3, MapModule.Count(map));
        Assert.False(MapModule.IsEmpty(map));
        Assert.True(MapModule.IsEmpty(MapModule.Empty<string, int>()));
        Assert.Equal(2, MapModule.Ref(map, "b"));
        Assert.Equal(9, MapModule.RefOr(map, "z", 9));
        Assert.Equal((true, 1), MapModule.TryGetValue(map, "a"));
        Assert.Equal((false, 0), MapModule.TryGetValue(map, "z"));
        Assert.True(MapModule.ContainsKey(map, "c"));
        Assert.Equal(new[] { "a", "b", "c" }, MapModule.Keys(map).OrderBy(k => k));
        Assert.Equal(new[] { 1, 2, 3 }, MapModule.Values(map).OrderBy(v => v));
        Assert.Equal(
            new[] { ("a", 1), ("b", 2), ("c", 3) },
            MapModule.AsEnumerable(map).OrderBy(e => e.Item1));
    }

    [Fact]
    public void Writing()
    {
        var map = Sample();

        Assert.Equal(4, MapModule.Count(MapModule.Set(map, "d", 4)));
        Assert.Equal(3, MapModule.Count(MapModule.Set(map, "a", 9)));
        Assert.Equal(9, MapModule.Ref(MapModule.Set(map, "a", 9), "a"));
        Assert.Throws<ArgumentException>(() => MapModule.Add(map, "a", 9));
        Assert.Equal(2, MapModule.Count(MapModule.Remove(map, "a")));

        var extended = MapModule.SetRange(map, new[] { ("d", 4), ("e", 5) });
        Assert.Equal(5, MapModule.Count(extended));

        var trimmed = MapModule.RemoveRange(extended, new[] { "d", "e" });
        Assert.Equal(3, MapModule.Count(trimmed));

        // The right-hand map wins where both have a key.
        var other = MapModule.FromEnumerable(new[] { ("c", 30), ("d", 40) });
        var merged = MapModule.Merge(map, other);
        Assert.Equal(4, MapModule.Count(merged));
        Assert.Equal(30, MapModule.Ref(merged, "c"));

        // ...unless a resolver says otherwise. It takes the key and both values.
        var summed = MapModule.MergeWith<string, int>((_, mine, theirs) => mine + theirs, map, other);
        Assert.Equal(33, MapModule.Ref(summed, "c"));
        Assert.Equal(40, MapModule.Ref(summed, "d"));
    }

    [Fact]
    public void HigherOrder_CallbacksTakeKeyAndValueSeparately()
    {
        var map = Sample();

        var tagged = MapModule.Map<string, int, string>((k, v) => k + v, map);
        Assert.Equal("b2", MapModule.Ref(tagged, "b"));

        var doubled = MapModule.MapValues<string, int, int>(v => v * 2, map);
        Assert.Equal(4, MapModule.Ref(doubled, "b"));

        var big = MapModule.Filter<string, int>((_, v) => v > 1, map);
        Assert.Equal(2, MapModule.Count(big));
        Assert.False(MapModule.ContainsKey(big, "a"));

        var total = MapModule.Fold<string, int, int>((acc, _, v) => acc + v, 0, map);
        Assert.Equal(6, total);

        var seen = 0;
        MapModule.ForEach<string, int>((_, v) => seen += v, map);
        Assert.Equal(6, seen);

        Assert.True(MapModule.Iter<string, int>((_, v) => v < 10, map));
        Assert.False(MapModule.Iter<string, int>((_, v) => v < 3, map));

        Assert.True(MapModule.Exists<string, int>((k, _) => k == "c", map));
        Assert.False(MapModule.Exists<string, int>((k, _) => k == "z", map));

        Assert.Equal((true, "b", 2), MapModule.TryFind<string, int>((k, _) => k == "b", map));
        Assert.Equal((false, null, 0), MapModule.TryFind<string, int>((k, _) => k == "z", map));
    }

    [Fact]
    public void Cursor_AdvancesInCursorDone()
    {
        var cursor = MapModule.Cursor(Sample());
        var seen = new List<(string, int)>();

        while (!MapModule.CursorDone(cursor)) seen.Add(MapModule.CursorCurrent(cursor));

        Assert.Equal(new[] { ("a", 1), ("b", 2), ("c", 3) }, seen.OrderBy(e => e.Item1));
    }

    [Fact]
    public void Builder()
    {
        var builder = MapBuilderModule.Empty<string, int>();

        MapBuilderModule.Add(builder, "a", 1);
        MapBuilderModule.AddRange(builder, new[] { ("b", 2), ("a", 9) });

        // Appends, not entries: the duplicate key is resolved when the map is built.
        Assert.Equal(3, MapBuilderModule.Count(builder));

        var built = MapBuilderModule.Build(builder);
        Assert.Equal(2, MapModule.Count(built));
        Assert.Equal(9, MapModule.Ref(built, "a"));

        var refilled = MapBuilderModule.FromMap(Sample());
        Assert.Equal(3, MapBuilderModule.Count(refilled));
        Assert.Equal(3, MapModule.Count(MapBuilderModule.Build(refilled)));
    }

    [Fact]
    public void Transient_RoundTripsThroughTheModule()
    {
        var transient = TransientMapModule.FromPersistent(Sample());

        TransientMapModule.Set(transient, "d", 4);
        TransientMapModule.Remove(transient, "a");

        var rebuilt = TransientMapModule.ToPersistent(transient);
        Assert.Equal(3, MapModule.Count(rebuilt));
        Assert.Equal(4, MapModule.Ref(rebuilt, "d"));
        Assert.False(MapModule.ContainsKey(rebuilt, "a"));
    }
}
