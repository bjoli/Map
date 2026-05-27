using Map;

namespace Tests;

public class PersistentMapSimpleLargeTests
{
    private const int LargeCount = 33_000;

    [Fact]
    public void Merge_LargeData_AbsolutelyNoKeyOverlap()
    {
        // Arrange
        var map1 = Map<int, int>.Empty;
        var map2 = Map<int, int>.Empty;
        var oracle = new Dictionary<int, int>();

        // Evens to Map1, Odds to Map2 -> Complete interleaving but 0 key collisions
        for (var i = 0; i < LargeCount; i++)
        {
            var evenKey = i * 2;
            var oddKey = i * 2 + 1;

            map1 = map1.Set(evenKey, 1);
            oracle[evenKey] = 1;

            map2 = map2.Set(oddKey, 0);
            oracle[oddKey] = 0;
        }

        // Act
        var merged = map1.Merge(map2);

        // Assert
        Assert.Equal(oracle.Count, merged.Count);
        foreach (var kvp in oracle)
        {
            Assert.True(merged.TryGetValue(kvp.Key, out var actual), $"Missing key {kvp.Key}");
            Assert.Equal(kvp.Value, actual);
        }
    }

    [Fact]
    public void Merge_LargeData_CompleteKeyOverlap_AllOnesAndZeros()
    {
        // Arrange
        var map1 = Map<int, int>.Empty;
        var map2 = Map<int, int>.Empty;
        var oracle = new Dictionary<int, int>();

        // Both maps contain identical keys [0 ... 33000)
        for (var i = 0; i < LargeCount; i++)
        {
            map1 = map1.Set(i, 1); // Left has 1s
            map2 = map2.Set(i, 0); // Right has 0s
            oracle[i] = 0; // Prefer Right defaults to 0
        }

        // Act
        var merged = map1.Merge(map2);

        // Assert
        Assert.Equal(LargeCount, merged.Count);
        for (var i = 0; i < LargeCount; i++)
        {
            Assert.True(merged.TryGetValue(i, out var actual), $"Missing key {i}");
            Assert.Equal(0, actual); // Must be overwritten cleanly by right side 0
        }
    }

    [Fact]
    public void Merge_LargeData_CompleteKeyOverlap_SumResolver()
    {
        // Arrange
        var map1 = Map<int, int>.Empty;
        var map2 = Map<int, int>.Empty;

        for (var i = 0; i < LargeCount; i++)
        {
            map1 = map1.Set(i, 1);
            map2 = map2.Set(i, 1);
        }

        // Act
        var merged = map1.Merge(map2, (k, left, right) => left + right);

        // Assert
        Assert.Equal(LargeCount, merged.Count);
        for (var i = 0; i < LargeCount; i++)
        {
            Assert.True(merged.TryGetValue(i, out var actual), $"Missing key {i}");
            Assert.Equal(2, actual); // 1 + 1 = 2
        }
    }
}