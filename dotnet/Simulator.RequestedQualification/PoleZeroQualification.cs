using System.Collections.Immutable;
using System.Globalization;
using Simulator.Frontend;
using Simulator.Syntax;

namespace Simulator.RequestedQualification;

/// <summary>Independent native projection and root comparison for a prepared PZ request.</summary>
internal static class PoleZeroQualification
{
    internal static string Mask(string source, IEnumerable<ProjectionOmission> omissions)
    {
        var chars = source.ToCharArray();
        var end = 0;
        foreach (var omission in omissions.OrderBy(o => o.Range.Start))
        {
            var range = omission.Range;
            if (range.Start < end || range.Length < 0 || range.Start > chars.Length - range.Length)
                throw new InvalidDataException("invalid_pz_projection_range");
            end = range.Start + range.Length;
            for (var i = range.Start; i < end; i++)
                if (chars[i] is not ('\r' or '\n')) chars[i] = ' ';
        }
        return new string(chars);
    }

    internal static ComparisonResult Compare(PoleZeroResult result, NativeRaw raw)
    {
        ComparisonResult Refuse(string gap) => new("native_result_refused", [], [gap]);
        if (!result.Success || raw.PlotName != "Pole-Zero Analysis" || !raw.Complex || raw.Points.Length != 1)
            return Refuse("invalid_pz_native_plot");
        var poles = new SortedDictionary<int, double>();
        var zeros = new SortedDictionary<int, double>();
        var point = raw.Points[0];
        for (var i = 0; i < raw.Variables.Length; i++)
        {
            // ngspice RAW labels roots as voltage columns. Their physical
            // dimension follows the .pz plot contract, in inverse seconds.
            var name = raw.Variables[i].Name;
            var target = name.StartsWith("v(pole(", StringComparison.OrdinalIgnoreCase) ? poles :
                name.StartsWith("v(zero(", StringComparison.OrdinalIgnoreCase) ? zeros : null;
            if (target is null || !name.EndsWith("))", StringComparison.Ordinal) ||
                !int.TryParse(name.AsSpan(7, name.Length - 9), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 1 || index > 4 || !double.IsFinite(point[i].Real) || Math.Abs(point[i].Imag) > 1e-7 ||
                !target.TryAdd(index, point[i].Real))
                return Refuse("invalid_pz_native_root");
        }

        if (!poles.Keys.SequenceEqual(Enumerable.Range(1, result.Poles.Length)) ||
            !zeros.Keys.SequenceEqual(Enumerable.Range(1, result.Zeros.Length)))
            return Refuse("pz_root_count_or_identity_mismatch");

        const double absolute = 1e-7;
        const double relative = 1e-9;
        var observations = ImmutableArray.CreateBuilder<ObservableComparison>();
        Add("pole", result.Poles, poles.Values);
        Add("zero", result.Zeros, zeros.Values);
        if (observations.Count == 0) return Refuse("no_comparable_pz_roots");
        return new(observations.Any(o => o.Failed > 0) ? "numerical_disagreement" : "agreed",
            observations.ToImmutable(), []);

        void Add(string kind, ImmutableArray<double> actual, IEnumerable<double> reference)
        {
            var i = 0;
            foreach (var (a, b) in actual.Order().Zip(reference.Order()))
            {
                var name = $"{kind}({++i})";
                var error = Math.Abs(a - b);
                observations.Add(new(name, name, "1/s", 1,
                    error > absolute + relative * Math.Abs(b) ? 1 : 0, error, absolute, relative));
            }
        }
    }
}
