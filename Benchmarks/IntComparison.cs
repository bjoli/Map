using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Map;

namespace Benchmarks;

[MemoryDiagnoser]
public class IntMapBenchmarks
{
    [Params(100, 1000, 10000, 100000)]
    public int N { get; set; }

    private int[] _allKeys;
    private int[] _retrieveKeys;
    private int[] _updateKeys;
    private int[] _removeKeys;
    private int[] _mixedKeys; // Half existing, half new

    // Pre-built collections for read/update/remove tests
    private ImmutableDictionary<int, int> _immDict;
    private LanguageExt.HashMap<int, int> _extHashMap;
    private Map<int, int> _map;
 
    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        
        // Build integer keys (inserted sorted)
        _allKeys = Enumerable.Range(0, N).ToArray();

        int subsetSize = Math.Max(1, N / 10);
        
        // Subsets for different operations
        var shuffled = _allKeys.OrderBy(x => rnd.Next()).ToArray();
        _retrieveKeys = shuffled.Take(subsetSize).ToArray();
        _updateKeys = shuffled.Skip(subsetSize).Take(subsetSize).ToArray();
        _removeKeys = shuffled.Skip(subsetSize * 2).Take(subsetSize).ToArray();

        // Mixed keys: half existing, half completely new (N + 1 to N + subsetSize/2)
        var existingHalf = shuffled.Skip(subsetSize * 3).Take(subsetSize / 2).ToArray();
        var newHalf = Enumerable.Range(N + 1, subsetSize - (subsetSize / 2)).ToArray();
        _mixedKeys = existingHalf.Concat(newHalf).OrderBy(x => rnd.Next()).ToArray();

        // Pre-build collections
        _immDict = ImmutableDictionary.CreateRange(_allKeys.Select(k => new KeyValuePair<int, int>(k, k)));
        _extHashMap = LanguageExt.HashMap.empty<int, int>();
        _map = Map<int, int>.Empty;
        foreach (var k in _allKeys)
        {
            _map = _map.Add(k, k);
            _extHashMap = _extHashMap.AddOrUpdate(k, k);
        }
    }

    // --- 1. BUILD ---
    
    
    [Benchmark]
    public Map<int, int> Build_PersistentMap()
    {
        var map = Map<int, int>.Empty;
        foreach (var k in _allKeys) map = map.Add(k, k);
        return map;
    }
    
    [Benchmark]
    public Map<int, int> Build_TransientMap()
    {
        var map = Map<int, int>.Empty.ToTransient();
        foreach (var k in _allKeys) map.Add(k, k);
        return map.ToImmutable();
    }
    
    [Benchmark]
    public Map<int, int> Build_MapBuilder()
    {
        var map = new MapBuilder<int, int>(EqualityComparer<int>.Default);
        foreach (var k in _allKeys) map.Add(k, k);
        return map.ToImmutable();
    }

    [Benchmark]
    public ImmutableDictionary<int, int> Build_ImmDict()
    {
        var map = ImmutableDictionary<int, int>.Empty;
        foreach (var k in _allKeys) map = map.Add(k, k);
        return map;
    }

    [Benchmark]
    public ImmutableSortedDictionary<int, int> Build_ImmSortedDict()
    {
        var map = ImmutableSortedDictionary<int, int>.Empty;
        foreach (var k in _allKeys) map = map.Add(k, k);
        return map;
    }




    [Benchmark]
    public LanguageExt.HashMap<int, int> Build_ExtHashMap()
    {
        var map = LanguageExt.HashMap.empty<int, int>();
        foreach (var k in _allKeys) map = map.AddOrUpdate(k, k);
        return map;
    }

    // --- 2. RETRIEVAL ---
    
    [Benchmark]
    public int Retrieve_Map()
    {
        int count = 0;
        foreach (var k in _retrieveKeys)
            if (_map.TryGetValue(k, out _)) count++;
        return count;
    }

    [Benchmark]
    public int Retrieve_ImmDict()
    {
        int count = 0;
        foreach (var k in _retrieveKeys)
            if (_immDict.TryGetValue(k, out _)) count++;
        return count;
    }


    [Benchmark]
    public int Retrieve_ExtHashMap()
    {
        int count = 0;
        foreach (var k in _retrieveKeys)
            if (_extHashMap.Find(k).IsSome) count++;
        return count;
    }
    

    // --- 3. UPDATING ---

    
    
    [Benchmark]
    public Map<int, int> Update_Map()
    {
        var map = _map;
        foreach (var k in _updateKeys) map = map.Add(k, 999);
        return map;
    }
    
    [Benchmark]
    public Map<int, int> Update_TransientMap()
    {
        var map = _map.ToTransient();
        foreach (var k in _updateKeys) map.Add(k, 999);
        return map.ToImmutable();
    }

    
    
    [Benchmark]
    public ImmutableDictionary<int, int> Update_ImmDict()
    {
        var map = _immDict;
        foreach (var k in _updateKeys) map = map.SetItem(k, 999);
        return map;
    }

    [Benchmark]
    public LanguageExt.HashMap<int, int> Update_ExtHashMap()
    {
        var map = _extHashMap;
        foreach (var k in _updateKeys) map = map.SetItem(k, 999);
        return map;
    }

    // --- 4. UPDATE & SET (MIXED) ---
    
    [Benchmark]
    public Map<int, int> UpdateSet_Map()
    {
        var map = _map;
        foreach (var k in _mixedKeys) map = map.Add(k, 999);
        return map;
    }
    
    [Benchmark]
    public Map<int, int> UpdateSet_TransientMap()
    {
        var map = _map.ToTransient();
        foreach (var k in _mixedKeys) map.Add(k, 999);
        return map.ToImmutable();
    }

    [Benchmark]
    public ImmutableDictionary<int, int> UpdateSet_ImmDict()
    {
        var map = _immDict;
        foreach (var k in _mixedKeys) map = map.SetItem(k, 999);
        return map;
    }

    [Benchmark]
    public LanguageExt.HashMap<int, int> UpdateSet_ExtHashMap()
    {
        var map = _extHashMap;
        foreach (var k in _mixedKeys) map = map.AddOrUpdate(k, 999);
        return map;
    }

    // --- 5. ITERATION ---
    
    [Benchmark]
    public int Iterate_Map()
    {
        int sum = 0;
        foreach (var kvp in _map) sum += kvp.Value;
        return sum;
    }



    [Benchmark]
    public int IterateInternal_Map()
    {
        var sum = 0;
        _map.Iter((k, v) =>
        {
            sum += v;
            return true;
        });
        return sum;
    }

    [Benchmark]
    public int Iterate_ImmDict()
    {
        int sum = 0;
        foreach (var kvp in _immDict) sum += kvp.Value;
        return sum;
    }

    [Benchmark]
    public int Iterate_ExtHashMap()
    {
        int sum = 0;
        foreach (var kvp in _extHashMap) sum += kvp.Value;
        return sum;
    }

    
    // --- 6. REMOVAL ---
    
    [Benchmark]
    public Map<int, int> Remove_Map()
    {
        var map = _map;
        foreach (var k in _removeKeys) map = map.Remove(k);
        return map;
    }
    
    [Benchmark]
    public Map<int, int> Remove_TransientMap()
    {
        var map = _map.ToTransient();
        foreach (var k in _removeKeys) map.Remove(k);
        return map.ToImmutable();
    }

    [Benchmark]
    public ImmutableDictionary<int, int> Remove_ImmDict()
    {
        var map = _immDict;
        foreach (var k in _removeKeys) map = map.Remove(k);
        return map;
    }


    [Benchmark]
    public LanguageExt.HashMap<int, int> Remove_ExtHashMap()
    {
        var map = _extHashMap;
        foreach (var k in _removeKeys) map = map.Remove(k);
        return map;
    }
    
    static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(IntMapBenchmarks).Assembly).Run(args);

       
}
