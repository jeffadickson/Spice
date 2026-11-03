using Simulator.RequestedQualification;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class CorpusBlockerRankingTests
{
    [Fact]
    public void Counts_root_identities_and_content_without_multiplying_observations()
    {
        var rows = new[] {
            new BlockerObservation("a", "same", ["model", "model", "control"]),
            new BlockerObservation("mirror", "same", ["model"]),
            new BlockerObservation("b", "different", ["control"]),
            new BlockerObservation("unknown", "third", []) };
        var ranks = CorpusBlockerRanking.Rank(rows);
        var model = Assert.Single(ranks, r => r.Key == "model");
        Assert.Equal(2, model.DistinctInputs);
        Assert.Equal(1, model.UniqueContents);
        Assert.Equal(1, model.SoleObservedInputs);
        Assert.Equal(1, Assert.Single(model.Cooccurring, c => c.Key == "control").DistinctInputs);
        Assert.Equal(2, Assert.Single(ranks, r => r.Key == "control").UniqueContents);
    }

    [Fact]
    public void Duplicate_identities_are_not_silently_double_counted() =>
        Assert.Throws<ArgumentException>(() => CorpusBlockerRanking.Rank([
            new("a", "hash", ["one"]), new("a", "hash", ["two"])]));

    [Fact]
    public void Null_input_is_rejected_and_duplicate_error_explains_the_problem()
    {
        Assert.Throws<ArgumentNullException>(() => CorpusBlockerRanking.Rank(null!));
        var error = Assert.Throws<ArgumentException>(() => CorpusBlockerRanking.Rank([
            new("a", "hash", []), new("a", "hash", [])]));
        Assert.StartsWith("Duplicate root identity.", error.Message);
        Assert.Empty(CorpusBlockerRanking.Rank([new("unavailable", "hash", [])]));
    }

    [Fact]
    public void Impact_order_overlap_order_and_bounded_examples_have_independent_expected_values()
    {
        var ranks = CorpusBlockerRanking.Rank([
            new("z", "1", ["hub"]), new("y", "2", ["hub"]), new("w", "3", ["hub"]),
            new("a", "4", ["hub", "a", "b", "c", "d", "e", "f"]),
            new("b", "5", ["a"]), new("c", "6", ["hub", "a"]) ]);
        Assert.Equal(new[] {"hub", "a", "b", "c", "d", "e", "f"}, ranks.Select(r => r.Key));
        Assert.Equal(5, ranks[0].DistinctInputs);
        Assert.Equal(3, ranks[0].SoleObservedInputs);
        Assert.Equal(new[] {"a", "c", "w"}, ranks[0].Examples);
        Assert.Equal(new[] {"a", "b", "c", "d", "e"}, ranks[0].Cooccurring.Select(c => c.Key));
        Assert.Equal(new[] {2, 1, 1, 1, 1}, ranks[0].Cooccurring.Select(c => c.DistinctInputs));
    }

    [FsCheck.Xunit.Property(MaxTest = 200, Replay = "362,20261103")]
    public void Ranking_is_invariant_under_order_and_repeated_observations(int[] values)
    {
        // FsCheck owns generation, shrinking and replay. Transform bounded
        // signed integers into overlapping keys; this is not simulator fuzzing.
        var rows = values.Take(64).Select((value, i) => new BlockerObservation(
            i.ToString(), (i % 7).ToString(), Enumerable.Range(0, value & 7)
                .Select(k => "k" + ((value >> (k * 2)) & 3)).ToArray())).ToArray();
        var repeated = rows.Reverse().Select(r => r with { Keys = r.Keys.Concat(r.Keys).Reverse().ToArray() }).ToArray();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(CorpusBlockerRanking.Rank(rows)),
            System.Text.Json.JsonSerializer.Serialize(CorpusBlockerRanking.Rank(repeated)));
    }
}
