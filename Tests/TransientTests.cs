using Map;

namespace Tests;

public class TransientTests
{
    /// <summary>
    ///     A transient taken from a non-empty map used to start its count at zero, so every map
    ///     built from anything but <c>Empty.ToTransient()</c> came out with a wrong <c>Count</c>
    ///     — negative, even, once more keys were removed than added.
    /// </summary>
    [Fact]
    public void Transient_FromNonEmptyMap_KeepsTheCount()
    {
        var map = Map<int, int>.Empty;
        for (var i = 0; i < 100; i++) map = map.Set(i, i);

        var transient = map.ToTransient();

        Assert.Equal(100, transient.Count);
        Assert.Equal(100, transient.ToImmutable().Count);
    }

    [Fact]
    public void Transient_WritesMoveTheCount()
    {
        var map = Map<int, int>.Empty;
        for (var i = 0; i < 10; i++) map = map.Set(i, i);

        var transient = map.ToTransient();

        transient.Set(10, 10);
        Assert.Equal(11, transient.Count);

        // A key already there is a replacement, not an addition.
        transient.Set(0, 99);
        Assert.Equal(11, transient.Count);

        transient.Remove(0);
        Assert.Equal(10, transient.Count);

        // A key that is not there removes nothing.
        transient.Remove(1000);
        Assert.Equal(10, transient.Count);

        var rebuilt = transient.ToImmutable();
        Assert.Equal(10, rebuilt.Count);
        Assert.False(rebuilt.ContainsKey(0));
        Assert.Equal(10, rebuilt[10]);
    }

    [Fact]
    public void Transient_DoesNotDisturbTheMapItCameFrom()
    {
        var map = Map<int, int>.Empty;
        for (var i = 0; i < 50; i++) map = map.Set(i, i);

        var transient = map.ToTransient();
        for (var i = 0; i < 50; i++) transient.Set(i, i * 2);

        Assert.Equal(50, map.Count);
        Assert.Equal(1, map[1]);
        Assert.Equal(2, transient.ToImmutable()[1]);
    }

    [Fact]
    public void Transient_ReadsSeeTheWrites()
    {
        var transient = TransientMapModule.Empty<string, int>();

        TransientMapModule.Set(transient, "a", 1);
        TransientMapModule.SetRange(transient, new[] { ("b", 2), ("c", 3) });

        Assert.Equal(3, TransientMapModule.Count(transient));
        Assert.True(TransientMapModule.ContainsKey(transient, "b"));
        Assert.Equal((true, 3), TransientMapModule.TryGetValue(transient, "c"));
        Assert.Equal((false, 0), TransientMapModule.TryGetValue(transient, "z"));
        Assert.Equal(7, TransientMapModule.RefOr(transient, "z", 7));

        TransientMapModule.FilterInPlace<string, int>((_, v) => v > 1, transient);

        Assert.Equal(2, TransientMapModule.Count(transient));
        Assert.False(TransientMapModule.ContainsKey(transient, "a"));

        TransientMapModule.RemoveRange(transient, new[] { "b", "c" });
        Assert.True(TransientMapModule.IsEmpty(transient));
    }
}
