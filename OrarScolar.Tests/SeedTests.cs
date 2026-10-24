using OrarScolar.Data;
using OrarScolar.Solver;

namespace OrarScolar.Tests;

// T8
public class SeedTests
{
    public static TheoryData<int, int> Cases()
    {
        var d = new TheoryData<int, int>();
        foreach (var n in new[] { 3, 8, 16 }) for (int s = 1; s <= 5; s++) d.Add(n, s);
        return d;
    }

    [Theory, MemberData(nameof(Cases))]
    public void Witness_IsValid_AndPreprocessorAccepts(int classes, int seed)
    {
        var (data, witness) = SeedGenerator.Create(classes, seed);
        var x = SolverInput.FromEntities(data);
        Assert.Equal(x.N, witness.Count);
        var (slotOf, roomOf) = x.FromEntries(witness);
        Assert.Equal(0, HardChecker.Count(x, slotOf, roomOf));
        Assert.Empty(Preprocessor.Run(x, useAc3: true).Errors);
        if (classes == 16)
        {
            Assert.Equal(16, data.Classes.Count);
            Assert.Equal(35, data.Teachers.Count);
            Assert.Equal(25, data.Rooms.Count);
        }
    }
}
