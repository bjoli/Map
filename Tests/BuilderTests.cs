using Map;

namespace Tests;

public class BuilderTests
{
    [Fact]
    public void BuilderShouldBuildSmallMapsCorrectly()
    {
        var my = new MapBuilder<int, int>();
        for (var i = 0; i < 21400; i++) my.Add(i, i);

        var imm = my.ToImmutable();
        for (var i = 0; i < 21400; i++) Assert.Equal(i, imm[i]);
    }

    [Fact]
    public void BuilderShouldBuildLargeMapsCorrectly()
    {
        var my = new MapBuilder<int, int>();
        for (var i = 0; i < 1500000; i++) my.Add(i, i);

        var imm = my.ToImmutable();
        for (var i = 0; i < 1500000; i++) Assert.Equal(i, imm[i]);
    }
}