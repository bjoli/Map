using Map;

namespace Tests;

public class PersistentMapMergeTests
{
    [Fact]
    public void Merge_WithEmptyMaps_ReturnsCorrectResult()
    {
        // Arrange
        var empty1 = Map<string, int>.Empty;
        var empty2 = Map<string, int>.Empty;
        var mapWithData = empty1.Add("A", 1).Add("B", 2);

        // Act & Assert
        // 1. Both empty
        var res1 = empty1.Merge(empty2);
        Assert.True(res1.IsEmpty);
        Assert.Equal(0, res1.Count);

        // 2. Left empty
        var res2 = empty1.Merge(mapWithData);
        Assert.Equal(2, res2.Count);
        Assert.Equal(1, res2["A"]);
        Assert.Equal(2, res2["B"]);

        // 3. Right empty
        var res3 = mapWithData.Merge(empty2);
        Assert.Equal(2, res3.Count);
        Assert.Equal(1, res3["A"]);
    }

    [Fact]
    public void Merge_NoOverlappingKeys_CombinesAllElements()
    {
        // Arrange
        var map1 = Map<string, int>.Empty.Add("A", 1).Add("B", 2);
        var map2 = Map<string, int>.Empty.Add("C", 3).Add("D", 4);

        // Act
        var merged = map1.Merge(map2);

        // Assert
        Assert.Equal(4, merged.Count);
        Assert.Equal(1, merged["A"]);
        Assert.Equal(2, merged["B"]);
        Assert.Equal(3, merged["C"]);
        Assert.Equal(4, merged["D"]);
    }

    [Fact]
    public void Merge_DefaultStrategy_OverwritesWithRightValue()
    {
        // Arrange
        var map1 = Map<string, int>.Empty.Add("A", 1).Add("B", 2);
        var map2 = Map<string, int>.Empty.Add("B", 99).Add("C", 3);

        // Act
        var merged = map1.Merge(map2); // Defaults to null thunk -> prefer right

        // Assert
        Assert.Equal(3, merged.Count);
        Assert.Equal(1, merged["A"]);
        Assert.Equal(99, merged["B"]); // Overwritten
        Assert.Equal(3, merged["C"]);
    }

    [Fact]
    public void Merge_PreferLeftThunk_KeepsLeftValueOnConflict()
    {
        // Arrange
        var map1 = Map<string, int>.Empty.Add("A", 1).Add("B", 2);
        var map2 = Map<string, int>.Empty.Add("B", 99).Add("C", 3);

        // Act
        var merged = map1.Merge(map2, (key, left, right) => left);

        // Assert
        Assert.Equal(3, merged.Count);
        Assert.Equal(1, merged["A"]);
        Assert.Equal(2, merged["B"]); // Kept original left value
        Assert.Equal(3, merged["C"]);
    }

    [Fact]
    public void Merge_CustomThunk_CombinesValues()
    {
        // Arrange
        var map1 = Map<string, string>.Empty.Add("user1", "Role:User");
        var map2 = Map<string, string>.Empty.Add("user1", "Role:Admin").Add("user2", "Role:Guest");

        // Act
        var merged = map1.Merge(map2, (key, left, right) => $"{left},{right}");

        // Assert
        Assert.Equal(2, merged.Count);
        Assert.Equal("Role:User,Role:Admin", merged["user1"]);
        Assert.Equal("Role:Guest", merged["user2"]);
    }

    [Fact]
    public void Merge_DeepStructuralOverlap_MaintainsTrieIntegrity()
    {
        // Arrange
        // Force keys into more complex internal branching structures by inserting many items
        var map1 = Map<int, int>.Empty;
        var map2 = Map<int, int>.Empty;

        for (int i = 0; i < 100; i += 2) map1 = map1.Add(i, i);       // Even keys: 0, 2, 4...
        for (int i = 1; i < 100; i += 2) map2 = map2.Add(i, i * 10);  // Odd keys:  1, 3, 5...
        
        // Add one direct conflict key to both
        map1 = map1.Add(500, 500);
        map2 = map2.Add(500, 5000);

        // Act
        var merged = map1.Merge(map2); // Prefer right

        // Assert
        Assert.Equal(101, merged.Count); // 50 evens + 50 odds + 1 overlapping key
        Assert.Equal(20, merged[20]);
        Assert.Equal(210, merged[21]);
        Assert.Equal(5000, merged[500]); // Overwritten by right value
    }

    // A helper class that forces hash collisions intentionally
    private class ForcedCollisionComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(x, y);
        // All strings get the same hashcode to force CollisionNode generation
        public int GetHashCode(string obj) => 42; 
    }

    [Fact]
    public void Merge_HashCollisionNodes_ResolvesCorrectly()
    {
        // Arrange
        var comparer = new ForcedCollisionComparer();
        var map1 = new Map<string, int>(comparer).Add("KeyA", 1).Add("KeyB", 2);
        var map2 = new Map<string, int>(comparer).Add("KeyB", 99).Add("KeyC", 3);

        // Act
        var merged = map1.Merge(map2); // Default strategy

        // Assert
        Assert.Equal(3, merged.Count);
        Assert.Equal(1, merged["KeyA"]);
        Assert.Equal(99, merged["KeyB"]); // Handled inside collision loop
        Assert.Equal(3, merged["KeyC"]);
    }
    
    [Fact]
    public void Merge_HashCollisionNodes_WithCustomThunk_ResolvesCorrectly()
    {
        // Arrange
        var comparer = new ForcedCollisionComparer();
        var map1 = new Map<string, int>(comparer).Add("KeyA", 1).Add("KeyB", 2);
        var map2 = new Map<string, int>(comparer).Add("KeyB", 3).Add("KeyC", 4);

        // Act
        var merged = map1.Merge(map2, (k, l, r) => l + r); // Sum conflicts

        // Assert
        Assert.Equal(3, merged.Count);
        Assert.Equal(5, merged["KeyB"]); // 2 + 3
    }
    
    private const int LargeDatasetSize = 33_000;

    [Fact]
    public void Merge_LargeTrees_MatchesOracle_PreferRight()
    {
        // Arrange - Create large datasets with overlapping ranges
        // Map 1: 0 to 33,000
        // Map 2: 16_500 to 49_500 (50% intersection overlap)
        var (map1, oracle1) = GenerateLargeDataset(0, LargeDatasetSize, val => val);
        var (map2, oracle2) = GenerateLargeDataset(LargeDatasetSize / 2, LargeDatasetSize, val => val * 10);

        // Build expected oracle state (prefer right: map2 overwrites map1)
        var expectedOracle = new Dictionary<int, int>(oracle1);
        foreach (var kvp in oracle2)
        {
            expectedOracle[kvp.Key] = kvp.Value;
        }

        // Act
        var mergedMap = map1.Merge(map2); // Default strategy (Prefer Right)

        // Assert
        Assert.Equal(expectedOracle.Count, mergedMap.Count);
        
        // Verify every single key against the oracle
        foreach (var kvp in expectedOracle)
        {
            Assert.True(mergedMap.TryGetValue(kvp.Key, out int actualValue), $"Key {kvp.Key} missing from merged map.");
            Assert.Equal(kvp.Value, actualValue);
        }
    }

    [Fact]
    public void Merge_LargeTrees_MatchesOracle_CustomThunk()
    {
        // Arrange
        var (map1, oracle1) = GenerateLargeDataset(0, LargeDatasetSize, val => val);
        var (map2, oracle2) = GenerateLargeDataset(LargeDatasetSize / 2, LargeDatasetSize, val => val * 10);

        Func<int, int, int, int> addResolver = (key, left, right) => left + right;

        // Build expected oracle state using the same conflict thunk
        var expectedOracle = new Dictionary<int, int>(oracle1);
        foreach (var kvp in oracle2)
        {
            if (expectedOracle.TryGetValue(kvp.Key, out int leftValue))
            {
                expectedOracle[kvp.Key] = addResolver(kvp.Key, leftValue, kvp.Value);
            }
            else
            {
                expectedOracle[kvp.Key] = kvp.Value;
            }
        }

        // Act
        var mergedMap = map1.Merge(map2, addResolver);

        // Assert
        Assert.Equal(expectedOracle.Count, mergedMap.Count);

        // Verify elements match the sum calculations
        foreach (var kvp in expectedOracle)
        {
            Assert.True(mergedMap.TryGetValue(kvp.Key, out int actualValue), $"Key {kvp.Key} missing from merged map.");
            Assert.Equal(kvp.Value, actualValue);
        }
    }

    /// <summary>
    /// Generates identical datasets across both the CHAMP implementation and a reference BCL Dictionary.
    /// </summary>
    private static (Map<int, int> Map, Dictionary<int, int> Oracle) GenerateLargeDataset(
        int startKey, 
        int count, 
        Func<int, int> valueGenerator)
    {
        var map = Map<int, int>.Empty;
        var oracle = new Dictionary<int, int>(count);

        for (int i = 0; i < count; i++)
        {
            int key = startKey + i;
            int value = valueGenerator(key);

            map = map.Add(key, value);
            oracle[key] = value;
        }

        return (map, oracle);
    }

    [Fact]
    public void  MergeOverlap()
    {
        Map<int,int> zeromap  = Map<int,int>.Empty;
        for(int i = 0; i < 20000; i++) {
            zeromap = zeromap.Add(i, 0);
        }

        var oneMap = Map<int,int>.Empty;
        for (int i = 10000; i < 30000; i++) {
            oneMap = oneMap.Add(i, 1);   
        }

        var merged = zeromap.Merge(oneMap, (k, v, v2) => 10);
        Assert.Equal(10, merged[10000]);

        int sum = 0;
        merged.Iter((k, v) => {
            sum += v;
            return true;
        });

        Assert.Equal(110000, sum);
    } 
}
