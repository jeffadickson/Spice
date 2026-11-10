using Simulator.Frontend;
using Simulator.RequestedQualification;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class PoleZeroQualificationTests
{
    private const string Raw = """
        Plotname: Pole-Zero Analysis
        Flags: complex
        No. Variables: 3
        No. Points: 1
        Variables:
        0 v(zero(2)) voltage
        1 v(pole(1)) voltage
        2 v(zero(1)) voltage
        Values:
        0 -10,0
          -20,0
          -10,0
        """;

    [Fact]
    public void MatchingRootsKeepMultiplicityAndPhysicalUnits()
    {
        var comparison = PoleZeroQualification.Compare(new([-20], [-10, -10], null), AsciiRaw.Read(Raw));
        Assert.True(comparison.Agreed);
        Assert.Equal(3, comparison.Observables.Length);
        Assert.All(comparison.Observables, root => Assert.Equal("1/s", root.Unit));
    }

    [Theory]
    [InlineData("v(zero(2))", "v(zero(3))")]
    [InlineData("-10,0", "-10,1")]
    [InlineData("Pole-Zero Analysis", "AC Analysis")]
    public void InvalidRootIdentityOrDomainCannotAgree(string from, string to)
    {
        var comparison = PoleZeroQualification.Compare(new([-20], [-10, -10], null),
            AsciiRaw.Read(Raw.Replace(from, to, StringComparison.Ordinal)));
        Assert.False(comparison.Agreed);
        Assert.NotEmpty(comparison.Gaps);
    }

    [Fact]
    public void RealNumericalDisagreementIsDistinctFromMissingRoots()
    {
        var comparison = PoleZeroQualification.Compare(new([-21], [-10, -10], null), AsciiRaw.Read(Raw));
        Assert.Equal("numerical_disagreement", comparison.Status);
        Assert.Equal(1, comparison.Observables.Single(root => root.NativeSignal == "pole(1)").Failed);
    }
}
