using Simulator.Benchmarks;
using Xunit;
public sealed class SuiteContracts
{
    [Fact]
    public void TrailingRootSeparatorStillAcceptsOwnedInputs() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "fixture", "Simulator.TransportFixture.dll"),
            Suites.SafeInput(AppContext.BaseDirectory, "fixture/Simulator.TransportFixture.dll"));
    private static CaseDefinition Valid => new("rc", "ac", "rc.cir", "rc.json", "v(out)", "V", [10,100], 1e-6, 1e-6, "linear RC, peak phasors");
    [Fact]
    public void ValidBoundedSuiteIsAccepted() => Suites.Validate(new("1", [Valid]), 1, 3);
    [Theory]
    [InlineData(-1, 3)] [InlineData(6, 3)] [InlineData(1, 0)] [InlineData(1, 21)]
    public void RunBudgetsRejectUnboundedWork(int warmups, int iterations) =>
        Assert.Throws<InvalidDataException>(() => Suites.Validate(new("1", [Valid]), warmups, iterations));
    [Theory]
    [InlineData("kind")] [InlineData("coordinates")] [InlineData("tolerance")] [InlineData("id")] [InlineData("unit")]
    public void IndependentCaseFieldsCannotBeSilentlyIgnored(string field)
    {
        var c = field switch {
            "kind" => Valid with { Kind = "unknown" },
            "coordinates" => Valid with { Coordinates = [10,10] },
            "tolerance" => Valid with { AbsoluteTolerance = double.NaN },
            "id" => Valid with { Id = "../escape" },
            _ => Valid with { Unit = "unknown" }
        };
        Assert.Throws<InvalidDataException>(() => Suites.Validate(new("1", [c]), 1, 3));
    }
    [Fact]
    public void DuplicateIdsAndTraversalAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Suites.Validate(new("1", [Valid,Valid with { Id="RC" }]), 1, 3));
        Assert.Throws<InvalidDataException>(() => Suites.SafeInput(AppContext.BaseDirectory, "../outside"));
    }
}
