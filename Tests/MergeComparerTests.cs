using Map;

namespace Tests;

public class MergeComparerTests
{
    private static Map<string, int> Make(IEqualityComparer<string> comparer, params (string, int)[] entries) =>
        MapModule.FromEnumerable(entries, comparer);

    [Fact]
    public void Merge_RightIgnoreCase_IntoDefault()
    {
        var left = Make(EqualityComparer<string>.Default, ("a", 1), ("B", 2));
        var right = Make(StringComparer.OrdinalIgnoreCase, ("A", 10), ("c", 3), ("b", 20));

        var merged = left.Merge(right);

        Assert.Same(left.Comparer, merged.Comparer);
        Assert.Equal(5, merged.Count);
        Assert.Equal(1, merged["a"]);
        Assert.Equal(10, merged["A"]);
        Assert.Equal(2, merged["B"]);
        Assert.Equal(20, merged["b"]);
        Assert.Equal(3, merged["c"]);
        Assert.False(merged.ContainsKey("C"));
    }

    [Fact]
    public void Merge_RightDefault_IntoIgnoreCase()
    {
        var left = Make(StringComparer.OrdinalIgnoreCase, ("a", 1), ("B", 2));
        var right = Make(EqualityComparer<string>.Default, ("A", 10), ("c", 3), ("b", 20));

        var merged = left.Merge(right);

        Assert.Same(left.Comparer, merged.Comparer);
        Assert.Equal(3, merged.Count);
        Assert.Equal(10, merged["a"]);
        Assert.Equal(20, merged["B"]);
        Assert.Equal(3, merged["C"]);
        foreach (var key in new[] { "a", "A", "b", "B", "c", "C" }) Assert.True(merged.ContainsKey(key));
    }

    [Fact]
    public void MergeWith_DifferentComparers_ResolverGetsKeyLeftRight()
    {
        var left = Make(StringComparer.OrdinalIgnoreCase, ("a", 1), ("B", 2));
        var right = Make(EqualityComparer<string>.Default, ("A", 10), ("c", 3));
        var calls = new List<(string, int, int)>();

        var merged = MapModule.MergeWith((k, l, r) =>
        {
            calls.Add((k, l, r));
            return l * 100 + r;
        }, left, right);

        Assert.Equal([("A", 1, 10)], calls);
        Assert.Equal(3, merged.Count);
        Assert.Equal(110, merged["a"]);
        Assert.Equal(2, merged["b"]);
        Assert.Equal(3, merged["C"]);
    }

    [Fact]
    public void MergeWith_SameComparer_ResolverGetsKeyLeftRight()
    {
        var left = Make(StringComparer.OrdinalIgnoreCase, ("a", 1), ("B", 2));
        var right = Make(StringComparer.OrdinalIgnoreCase, ("A", 10), ("c", 3));
        var calls = new List<(int, int)>();

        var merged = MapModule.MergeWith((_, l, r) =>
        {
            calls.Add((l, r));
            return l * 100 + r;
        }, left, right);

        Assert.Equal([(1, 10)], calls);
        Assert.Equal(110, merged["A"]);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Merge_EmptyLeft_KeepsLeftComparer()
    {
        var right = Make(StringComparer.OrdinalIgnoreCase, ("A", 1), ("b", 2));

        var fromDefaultEmpty = Map<string, int>.Empty.Merge(right);
        Assert.Same(EqualityComparer<string>.Default, fromDefaultEmpty.Comparer);
        Assert.Equal(2, fromDefaultEmpty.Count);
        Assert.True(fromDefaultEmpty.ContainsKey("A"));
        Assert.False(fromDefaultEmpty.ContainsKey("a"));

        var ignoreCaseEmpty = new Map<string, int>(StringComparer.OrdinalIgnoreCase);
        var fromIgnoreCaseEmpty = ignoreCaseEmpty.Merge(Make(EqualityComparer<string>.Default, ("X", 1)));
        Assert.Same(StringComparer.OrdinalIgnoreCase, fromIgnoreCaseEmpty.Comparer);
        Assert.True(fromIgnoreCaseEmpty.ContainsKey("x"));
    }

    [Fact]
    public void Merge_EmptyRight_ReturnsLeft()
    {
        var left = Make(StringComparer.OrdinalIgnoreCase, ("a", 1));
        Assert.Same(left, left.Merge(Map<string, int>.Empty));
    }

    [Fact]
    public void Merge_DifferentComparers_LargeMaps()
    {
        var left = new MapBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
        var right = new MapBuilder<string, int>();
        for (var i = 0; i < 5_000; i++) left.Add("k" + i, i);
        for (var i = 2_500; i < 7_500; i++) right.Add("K" + i, -i);

        var merged = left.ToImmutable().Merge(right.ToImmutable());

        Assert.Equal(7_500, merged.Count);
        for (var i = 0; i < 7_500; i++) Assert.Equal(i < 2_500 ? i : -i, merged["k" + i]);
    }
}
