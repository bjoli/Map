using System.Collections.Immutable;
using Map;

namespace Tests;

public class MapTests
{
    [Fact]
    public void RegularOps_EmptyMap_ShouldBehaveSanely()
    {
        var map = Map<int, string>.Empty;

        Assert.Equal(0, map.Count);
        Assert.True(map.IsEmpty);
        Assert.False(map.ContainsKey(42));
        Assert.False(map.TryGetValue(42, out _));
        Assert.Throws<KeyNotFoundException>(() => map[42]);
    }

    [Fact]
    public void RegularOps_AddAndSet_ShouldInsertAndModifyContiguously()
    {
        var map0 = Map<int, string>.Empty;
        var map1 = map0.Add(1, "value_1");
        var map2 = map1.Add(2, "value_2");

        // Verify structural immutability isolation
        Assert.Equal(0, map0.Count);
        Assert.Equal(1, map1.Count);
        Assert.Equal(2, map2.Count);

        // Verify Lookups
        Assert.Equal("value_1", map2[1]);
        Assert.Equal("value_2", map2[2]);
        Assert.True(map2.ContainsKey(1));

        // Verify Value Overwrite
        var map3 = map2.Add(1, "value_1_updated");
        Assert.Equal(2, map3.Count);
        Assert.Equal("value_1_updated", map3[1]);
    }

    [Fact]
    public void RegularOps_Remove_ShouldDeleteAndTriggerCompaction()
    {
        var map = Map<int, string>.Empty
            .Add(1, "one")
            .Add(2, "two")
            .Add(3, "three");

        var removed1 = map.Remove(2);
        Assert.Equal(2, removed1.Count);
        Assert.False(removed1.ContainsKey(2));
        Assert.True(removed1.ContainsKey(1));

        // Non-existent key removal should maintain exact reference optimization
        var removedNone = removed1.Remove(99);
        Assert.Same(removed1, removedNone);

        // Clear down to empty invariants
        var emptyResult = removed1.Remove(1).Remove(3);
        Assert.Equal(0, emptyResult.Count);
        Assert.True(emptyResult.IsEmpty);
    }

    [Fact]
    public void TransientOps_ShouldMutateInPlaceAndIsolateOnFreeze()
    {
        var map = Map<int, string>.Empty.Add(1, "one");
        var transient = map.ToTransient();

        // Perform fast transient mutations
        transient.Add(2, "two");
        transient.Add(3, "three");
        
        Assert.True(transient.TryGetValue(2, out var val));
        Assert.Equal("two", val);

        transient.Remove(1);

        // Freeze the configuration
        var immutable = transient.ToImmutable();

        // Verify isolation boundaries
        Assert.Equal(1, map.Count); // Original remains untouched
        Assert.False(immutable.ContainsKey(1));
        Assert.Equal("two", immutable[2]);
        Assert.Equal("three", immutable[3]);
    }

    [Fact]
    public void FuzzTest_DifferentialWithImmutableDictionary()
    {
        // Deterministic seed for reproducible testing sequences
        var rand = new Random(1337); 
        var truth = ImmutableDictionary<int, string>.Empty;
        var map = Map<int, string>.Empty;

        const int OperationsCount = 500000;
        var trackedKeys = new List<int>();

        for (int i = 0; i < OperationsCount; i++)
        {
            int op = rand.Next(100);

            if (op < 55) // 55% Insert/Update mutations
            {
                int key = rand.Next(1, 20000);
                string val = $"v_{i}";

                if (!truth.ContainsKey(key))
                {
                    trackedKeys.Add(key);
                }

                truth = truth.SetItem(key, val);
                map = map.Add(key, val);
            }
            else if (op < 90) // 35% Removals
            {
                if (trackedKeys.Count > 0)
                {
                    int poolIndex = rand.Next(trackedKeys.Count);
                    int key = trackedKeys[poolIndex];
                    trackedKeys.RemoveAt(poolIndex);

                    truth = truth.Remove(key);
                    map = map.Remove(key);
                }
            }
            else // 10% Batch Transient Mutations
            {
                var transient = map.ToTransient();
                int batchSize = rand.Next(5, 30);

                for (int j = 0; j < batchSize; j++)
                {
                    int subOp = rand.Next(2);
                    if (subOp == 0) // Add inside transient frame
                    {
                        int key = rand.Next(20001, 40000); // Disjoint key space
                        string val = $"t_{i}_{j}";
                        
                        transient.Add(key, val);
                        truth = truth.SetItem(key, val);
                    }
                    else if (trackedKeys.Count > 0) // Remove inside transient frame
                    {
                        int poolIndex = rand.Next(trackedKeys.Count);
                        int key = trackedKeys[poolIndex];
                        trackedKeys.RemoveAt(poolIndex);

                        transient.Remove(key);
                        truth = truth.Remove(key);
                    }
                }

                map = transient.ToImmutable();
            }

            // Periodic verification check across full tree contents
            if (i % 10000 == 0)
            {
                VerifyStateEquality(truth, map);
            }
        }

        // Final thorough validation check
        VerifyStateEquality(truth, map);
    }

    private static void VerifyStateEquality(ImmutableDictionary<int, string> truth, Map<int, string> map)
    {
        // Assert deep value lookups match exactly
        foreach (var kvp in truth)
        {
            Assert.True(map.TryGetValue(kvp.Key, out var actualValue));
            Assert.Equal(kvp.Value, actualValue);
        }

        // Assert no phantom keys exist in the structure
        int countedElements = 0;
        foreach (var kvp in map)
        {
            countedElements++;
            Assert.True(truth.TryGetValue(kvp.Key, out var expectedValue));
            Assert.Equal(expectedValue, kvp.Value);
        }

        Assert.Equal(truth.Count, countedElements);
    }
}