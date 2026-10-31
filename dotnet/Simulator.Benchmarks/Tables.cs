using System.Runtime.CompilerServices;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
[assembly: InternalsVisibleTo("Simulator.Benchmarks.Tests")]
namespace Simulator.Benchmarks;
internal sealed record Point(double Coordinate, double Real, double Imag);
internal static class Tables
{
    // Only a named .print table is evidence. Device listings, other observables
    // and unrecognized text cannot silently become zero-valued reference data.
    internal static Point[] Parse(string text, string kind, string observable)
    {
        if (kind is not ("op" or "ac" or "transient") || text.Length > 8_388_608)
            throw new InvalidDataException("Unsupported or oversized reference table.");
        var rows = new List<Point>();
        var active = false;
        foreach (var line in text.Split('\n'))
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0 && words[0].Equals("Index", StringComparison.OrdinalIgnoreCase))
            {
                active = kind == "op" ? words.Length == 2 && Equal(words[1], observable)
                    : words.Length == 3 && Equal(words[1], kind == "ac" ? "frequency" : "time") && Equal(words[2], observable);
                continue;
            }
            if (!active || words.Length == 0 || line.Trim().All(c => c == '-')) continue;
            var data = line.Replace(",", " ", StringComparison.Ordinal).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(data[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) { active = false; continue; }
            if (index < 0 || data.Length != (kind == "op" ? 2 : kind == "ac" ? 4 : 3))
                throw new InvalidDataException("Malformed named reference row.");
            var coordinate = kind == "op" ? 0 : Number(data[1]);
            var real = Number(data[kind == "op" ? 1 : 2]);
            var imaginary = kind == "ac" ? Number(data[3]) : 0;
            if (coordinate < 0 || (kind == "ac" && coordinate == 0) || rows.Count >= 100_000)
                throw new InvalidDataException("Invalid reference axis or row budget.");
            rows.Add(new(coordinate, real, imaginary));
        }
        var result = rows.ToArray();
        Validate(result);
        if (kind == "op" && result.Length != 1) throw new InvalidDataException("Expected one operating point.");
        return result;
    }

    internal static Point At(Point[] rows, double coordinate, bool interpolate)
    {
        Validate(rows);
        if (!double.IsFinite(coordinate) || coordinate < rows[0].Coordinate || coordinate > rows[^1].Coordinate)
            throw new InvalidDataException("Comparison would extrapolate.");
        var index = Array.BinarySearch(rows, new(coordinate, 0, 0), PointComparer.Instance);
        if (index >= 0) return rows[index];
        if (!interpolate) throw new InvalidDataException("Missing exact reference coordinate.");
        var right = ~index;
        var left = rows[right - 1];
        var next = rows[right];
        var fraction = (coordinate - left.Coordinate) / (next.Coordinate - left.Coordinate);
        var point = new Point(coordinate, left.Real + fraction * (next.Real - left.Real), left.Imag + fraction * (next.Imag - left.Imag));
        if (!double.IsFinite(point.Real) || !double.IsFinite(point.Imag)) throw new InvalidDataException("Interpolation overflow.");
        return point;
    }

    internal static bool Within(Point expected, Point actual, double absolute, double relative)
    {
        if (!double.IsFinite(absolute) || absolute <= 0 || !double.IsFinite(relative) || relative < 0)
            throw new InvalidDataException("Invalid comparison tolerances.");
        var scale = new Complex(expected.Real, expected.Imag).Magnitude;
        var difference = new Complex(expected.Real - actual.Real, expected.Imag - actual.Imag).Magnitude;
        var bound = absolute + relative * scale;
        return double.IsFinite(expected.Coordinate) && expected.Coordinate == actual.Coordinate && double.IsFinite(difference) && double.IsFinite(bound) && difference <= bound;
    }

    internal static void Validate(Point[] rows)
    {
        if (rows.Length is < 1 or > 100_000 || rows.Any(p => !double.IsFinite(p.Coordinate) || p.Coordinate < 0 || !double.IsFinite(p.Real) || !double.IsFinite(p.Imag)) ||
            rows.Zip(rows.Skip(1)).Any(pair => pair.First.Coordinate >= pair.Second.Coordinate))
            throw new InvalidDataException("Missing/nonfinite/nonmonotonic named samples.");
    }
    private static bool Equal(string a, string b)
    {
        if(a.Equals(b,StringComparison.OrdinalIgnoreCase))return true;
        // Batch .print i(DEVICE) uses DEVICE#branch for V/L/E/H. Bind only this exact
        // requested supported V/L/E/H branch, never an arbitrary branch column.
        if(!b.StartsWith("i(",StringComparison.OrdinalIgnoreCase)||!b.EndsWith(')'))return false;
        var source=b[2..^1];
        return Regex.IsMatch(source,@"\A[VvLlEeHh][A-Za-z0-9_.$:-]*\z") &&
            a.Equals(source+"#branch",StringComparison.OrdinalIgnoreCase);
    }
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new InvalidDataException("Nonfinite or malformed reference number.");
    private sealed class PointComparer : IComparer<Point>
    {
        internal static readonly PointComparer Instance = new();
        public int Compare(Point? x, Point? y) => (x?.Coordinate ?? throw new InvalidDataException()).CompareTo(y?.Coordinate ?? throw new InvalidDataException());
    }
}
