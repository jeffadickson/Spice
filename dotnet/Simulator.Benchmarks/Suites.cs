namespace Simulator.Benchmarks;
internal static class Suites
{
    internal static void Validate(SuiteDefinition suite, int warmups, int iterations)
    {
        if (suite.SchemaVersion != "1" || suite.Cases is null || suite.Cases.Length is < 1 or > 16 ||
            warmups is < 0 or > 5 || iterations is < 1 or > 20)
            throw new InvalidDataException("Invalid suite schema, case count or repetition budget.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in suite.Cases)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 64 || !char.IsAsciiLetterOrDigit(c.Id[0]) ||
                c.Id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.')) || !ids.Add(c.Id) ||
                c.Kind is not ("op" or "ac" or "transient") || c.Unit is not ("V" or "A") ||
                string.IsNullOrWhiteSpace(c.Deck) || string.IsNullOrWhiteSpace(c.Request) ||
                string.IsNullOrWhiteSpace(c.Observable) || string.IsNullOrWhiteSpace(c.Policy) || c.Policy.Length > 1024 ||
                !double.IsFinite(c.AbsoluteTolerance) || c.AbsoluteTolerance <= 0 || !double.IsFinite(c.RelativeTolerance) || c.RelativeTolerance < 0 ||
                c.Coordinates is null || c.Coordinates.Length is < 1 or > 32 || c.Coordinates.Any(v => !double.IsFinite(v) || v < 0) ||
                c.Coordinates.Zip(c.Coordinates.Skip(1)).Any(pair => pair.First >= pair.Second))
                throw new InvalidDataException("Malformed or duplicate benchmark case.");
            if ((c.Kind == "op" && (c.Coordinates.Length != 1 || c.Coordinates[0] != 0)) ||
                (c.Kind == "ac" && c.Coordinates[0] == 0))
                throw new InvalidDataException("Invalid analysis coordinates.");
        }
    }

    internal static string SafeInput(string root, string relative)
    {
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Expected an existing root and relative input.");
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(relative, root);
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Input escaped the benchmark root.");
        if (!File.Exists(path)) throw new FileNotFoundException("Benchmark input missing.", path);
        return path;
    }
}
