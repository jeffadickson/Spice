using Simulator.RequestedQualification;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class CorpusPointBudgetTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("100001")]
    public async Task Invalid_explicit_point_budgets_are_refused_before_filesystem_work(string points)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>RequestedCorpus.RunAsync(["missing-checkout","missing-corpus","missing-engine","missing-oracle","missing-inventory","missing-output","60","lu",points]));
    }
    [Fact]
    public async Task Native_supported_point_ceiling_is_an_explicit_caller_choice()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(()=>RequestedCorpus.RunAsync(["missing-checkout","missing-corpus","missing-engine","missing-oracle","missing-inventory","missing-output","60","faer","100000"]));
    }
}
