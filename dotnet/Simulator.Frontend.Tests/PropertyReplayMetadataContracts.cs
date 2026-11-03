using System.Globalization;
using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Reject invalid replay metadata before an adapter can silently omit property cases.</summary>
public sealed class PropertyReplayMetadataContracts
{
    [Fact]
    public void Every_explicit_property_replay_constructs_a_valid_library_rng()
    {
        foreach(var method in typeof(PropertyReplayMetadataContracts).Assembly.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)))
        foreach(var attribute in method.GetCustomAttributes<PropertyAttribute>())
        {
            if(string.IsNullOrEmpty(attribute.Replay))continue;
            var parts=attribute.Replay.Split(',');
            Assert.True(parts.Length is 1 or 2,$"Unsupported replay form on {method.DeclaringType?.Name}.{method.Name}");
            var seed=ulong.Parse(parts[0],CultureInfo.InvariantCulture);
            // Delegate validation to the actual maintained RNG, not a second
            // approximation of SplitMix's valid-gamma rules.
            _ = parts.Length==1?new Rnd(seed):new Rnd(seed,ulong.Parse(parts[1],CultureInfo.InvariantCulture));

        }
    }
}
