using FsCheck.Xunit;
using Simulator.Benchmarks;
using Xunit;

public sealed class TableContracts
{
    [Fact]
    public void InvalidAxisCannotPassTheAccuracyGate() =>
        Assert.False(Tables.Within(new(double.PositiveInfinity, 1, 0), new(double.PositiveInfinity, 1, 0), 1e-6, 0));
    [Fact]
    public void NamedAcRetainsSignedComplexValues()
    {
        var p = Assert.Single(Tables.Parse("Index frequency v(out)\n0 10 1.25, -2.5\n", "ac", "v(out)"));
        Assert.Equal(new Point(10, 1.25, -2.5), p);
        Assert.False(Tables.Within(p, new(10, 1.25, 2.5), 1e-8, 1e-8));
    }
    [Fact]
    public void TransientInterpolationIsExplicitAndNeverExtrapolates()
    {
        var rows = Tables.Parse("Index time v(out)\n0 0 0\n1 1 2\n", "transient", "v(out)");
        Assert.Equal(new Point(0.25, 0.5, 0), Tables.At(rows, 0.25, true));
        Assert.Throws<InvalidDataException>(() => Tables.At(rows, -0.1, true));
        Assert.Throws<InvalidDataException>(() => Tables.At(rows, 2, true));
        Assert.Throws<InvalidDataException>(() => Tables.At(rows, 0.25, false));
    }
    [Theory]
    [InlineData("Index time v(other)\n0 0 1\n")]
    [InlineData("Index time v(out)\n0 -1 1\n")]
    [InlineData("Index time v(out)\n0 0 NaN\n")]
    [InlineData("Index time v(out)\n0 0 1\n1 0 2\n")]
    [InlineData("Index time v(out)\n0 1 1\n1 0 2\n")]
    public void MalformedTablesCannotSupplyAccuracyEvidence(string text) =>
        Assert.Throws<InvalidDataException>(() => Tables.Parse(text, "transient", "v(out)"));

    [Property(MaxTest = 128, Replay = "372,20261031")]
    public void TableValuesRoundTripAndPerturbationsFail(int raw)
    {
        var value = (double)(raw % 10000);
        var rows = Tables.Parse("Index v(out)\n0 " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n", "op", "v(out)");
        Assert.Equal(new Point(0, value, 0), Assert.Single(rows));
        Assert.False(Tables.Within(rows[0], new(0, value + 1, 0), 1e-8, 1e-8));
    }
}
