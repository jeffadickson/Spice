using System.Globalization;
using System.Text;
using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Diagnostic emitted per item during bulk .LIB / .MOD / .SUB SPICE library import.
/// </summary>
public sealed record LibraryImportDiagnostic(
    string Severity,
    string Code,
    string Message,
    string? ItemName = null,
    int? LineNumber = null);

/// <summary>
/// Result envelope of bulk SPICE .LIB / .MOD / .SUB import (supports dry-run preview mode).
/// </summary>
public sealed record LibraryImportReport(
    bool DryRun,
    int TotalFound,
    int ImportedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<CustomComponentDefinition> Components,
    IReadOnlyList<LibraryImportDiagnostic> Diagnostics);

/// <summary>
/// Bulk parser and importer converting SPICE .LIB, .MOD, and .SUB files containing
/// .MODEL cards and .SUBCKT ... .ENDS definitions into validated Spice V2 CustomComponentDefinitions.
/// </summary>
public static class SpiceLibraryImporter
{
    private sealed record LogicalLine(int LineNumber, string Text);

    public static LibraryImportReport ParseSpiceLibraryText(
        string spiceText,
        string? defaultCategory = null,
        DeviceRegistry? registry = null)
    {
        var diagnostics = new List<LibraryImportDiagnostic>();
        var components = new List<CustomComponentDefinition>();
        if (string.IsNullOrWhiteSpace(spiceText))
        {
            diagnostics.Add(new LibraryImportDiagnostic("error", "empty_input", "SPICE library text is empty."));
            return new LibraryImportReport(true, 0, 0, 0, 1, components, diagnostics);
        }

        var reg = registry ?? DeviceRegistry.Default;
        var logical = JoinContinuationLines(spiceText);
        var baseCategory = string.IsNullOrWhiteSpace(defaultCategory) ? "Imported SPICE" : defaultCategory.Trim();
        string? currentSection = null;
        int totalFound = 0;

        for (int i = 0; i < logical.Count; ++i)
        {
            var line = logical[i];
            var trimmed = line.Text.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var firstSpace = FindWhitespace(trimmed);
            var head = (firstSpace < 0 ? trimmed : trimmed[..firstSpace]).ToUpperInvariant();

            if (head == ".LIB")
            {
                var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..].Trim();
                // Single-token `.LIB SECTION_NAME` starts a library section; ignore two-token `.LIB file section` includes.
                var toks = Tokenize(rest);
                if (toks.Count == 1)
                {
                    currentSection = toks[0];
                }
                continue;
            }

            if (head == ".ENDL")
            {
                currentSection = null;
                continue;
            }

            if (head == ".MODEL")
            {
                totalFound++;
                var cat = string.IsNullOrWhiteSpace(currentSection)
                    ? baseCategory
                    : $"{baseCategory} / {currentSection}";
                if (TryBuildModelComponent(line, trimmed, cat, reg, out var part, out var err) && part is not null)
                {
                    components.Add(part);
                }
                else if (err is not null)
                {
                    diagnostics.Add(err);
                }
                continue;
            }

            if (head == ".SUBCKT")
            {
                totalFound++;
                var startLine = line.LineNumber;
                var bodyLines = new List<string> { trimmed };
                int depth = 1;
                int j = i + 1;
                while (j < logical.Count && depth > 0)
                {
                    var subTrim = logical[j].Text.Trim();
                    bodyLines.Add(subTrim);
                    if (subTrim.StartsWith(".SUBCKT", StringComparison.OrdinalIgnoreCase) &&
                        (subTrim.Length == 7 || char.IsWhiteSpace(subTrim[7])))
                    {
                        depth++;
                    }
                    else if (subTrim.StartsWith(".ENDS", StringComparison.OrdinalIgnoreCase) &&
                             (subTrim.Length == 5 || char.IsWhiteSpace(subTrim[5])))
                    {
                        depth--;
                    }
                    j++;
                }

                if (depth != 0)
                {
                    diagnostics.Add(new LibraryImportDiagnostic(
                        "error",
                        "unterminated_subckt",
                        "Missing matching '.ENDS' for '.SUBCKT' block.",
                        ExtractSecondToken(trimmed),
                        startLine));
                    break;
                }

                i = j - 1;
                var cat = string.IsNullOrWhiteSpace(currentSection)
                    ? $"{baseCategory} Subcircuits"
                    : $"{baseCategory} / {currentSection}";
                if (TryBuildSubcktComponent(startLine, trimmed, bodyLines, cat, reg, out var part, out var err) && part is not null)
                {
                    components.Add(part);
                }
                else if (err is not null)
                {
                    diagnostics.Add(err);
                }
            }
        }

        if (totalFound == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(new LibraryImportDiagnostic(
                "warning",
                "no_models_found",
                "No .MODEL cards or .SUBCKT blocks were found in the supplied text."));
        }

        int errCount = diagnostics.Count(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new LibraryImportReport(
            DryRun: true,
            TotalFound: totalFound,
            ImportedCount: components.Count,
            SkippedCount: 0,
            ErrorCount: errCount,
            Components: components,
            Diagnostics: diagnostics);
    }

    private static bool TryBuildModelComponent(
        LogicalLine line,
        string rawModelCard,
        string category,
        DeviceRegistry reg,
        out CustomComponentDefinition? component,
        out LibraryImportDiagnostic? error)
    {
        component = null;
        error = null;

        var tokens = Tokenize(rawModelCard);
        if (tokens.Count < 3)
        {
            error = new LibraryImportDiagnostic(
                "error", "malformed_model", ".MODEL directive requires at least model name and type.", null, line.LineNumber);
            return false;
        }

        var modelName = tokens[1];
        var typeTok = tokens[2];
        var lp = typeTok.IndexOf('(');
        var modelType = (lp >= 0 ? typeTok[..lp] : typeTok).Trim().ToUpperInvariant();
        var paramBody = new StringBuilder();
        if (lp >= 0)
        {
            var inside = typeTok[(lp + 1)..];
            if (inside.EndsWith(')')) inside = inside[..^1];
            paramBody.Append(inside);
        }
        for (int k = 3; k < tokens.Count; ++k)
        {
            var tok = tokens[k].Trim();
            if (tok.StartsWith('(')) tok = tok[1..];
            if (tok.EndsWith(')')) tok = tok[..^1];
            if (string.IsNullOrWhiteSpace(tok)) continue;
            if (paramBody.Length > 0) paramBody.Append(' ');
            paramBody.Append(tok);
        }

        var paramText = paramBody.ToString();
        var parsedParams = ParseModelKeyValues(paramText);

        string baseKind;
        string prefix;
        string symbolStyle;
        IReadOnlyList<CustomPinDefinition> pins;
        var defaults = new Dictionary<string, double>(StringComparer.Ordinal);

        switch (modelType)
        {
            case "D":
                baseKind = "spice.diode";
                prefix = "D";
                pins = [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")];
                symbolStyle = InferDiodeSymbol(modelName, parsedParams);
                if (parsedParams.TryGetValue("is", out var dIs)) defaults["is_amps"] = dIs;
                if (parsedParams.TryGetValue("n", out var dN)) defaults["n_emission"] = dN;
                if (parsedParams.TryGetValue("rs", out var dRs)) defaults["rs_ohms"] = dRs;
                if (parsedParams.TryGetValue("bv", out var dBv)) defaults["bv_volts"] = Math.Abs(dBv);
                if (parsedParams.TryGetValue("ibv", out var dIbv)) defaults["ibv_amps"] = Math.Abs(dIbv);
                if (parsedParams.TryGetValue("cjo", out var dCjo) || parsedParams.TryGetValue("cj0", out dCjo))
                    defaults["cjo_farads"] = dCjo;
                break;

            case "NPN":
            case "PNP":
                baseKind = "spice.bjt";
                prefix = "Q";
                symbolStyle = modelType == "PNP" ? "pnp" : "npn";
                pins =
                [
                    new CustomPinDefinition("c", 20, -20, "collector"),
                    new CustomPinDefinition("b", -40, 0, "base"),
                    new CustomPinDefinition("e", 20, 20, "emitter")
                ];
                defaults["area"] = 1.0;
                if (parsedParams.TryGetValue("bf", out var qBf)) defaults["bf"] = qBf;
                if (parsedParams.TryGetValue("is", out var qIs)) defaults["is_amps"] = qIs;
                if (parsedParams.TryGetValue("rb", out var qRb)) defaults["rb_ohms"] = qRb;
                if (parsedParams.TryGetValue("rc", out var qRc)) defaults["rc_ohms"] = qRc;
                if (parsedParams.TryGetValue("re", out var qRe)) defaults["re_ohms"] = qRe;
                break;

            case "NMOS":
            case "PMOS":
                baseKind = "spice.mosfet";
                prefix = "M";
                symbolStyle = modelType == "PMOS" ? "pmos" : "nmos";
                pins =
                [
                    new CustomPinDefinition("d", 20, -20, "drain"),
                    new CustomPinDefinition("g", -40, 0, "gate"),
                    new CustomPinDefinition("s", 20, 20, "source"),
                    new CustomPinDefinition("b", 40, 0, "bulk")
                ];
                defaults["w_m"] = 1e-6;
                defaults["l_m"] = 1e-6;
                if (parsedParams.TryGetValue("level", out var mLvl)) defaults["level"] = mLvl;
                if (parsedParams.TryGetValue("vth0", out var mVth) || parsedParams.TryGetValue("vto", out mVth))
                    defaults["vth0_volts"] = mVth;
                break;

            case "VDMOS":
                baseKind = "spice.vdmos";
                prefix = "M";
                var isPchan = paramText.Split([' ', '\t', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => t.Equals("pchan", StringComparison.OrdinalIgnoreCase));
                symbolStyle = isPchan ? "pvdmos" : "vdmos";
                pins =
                [
                    new CustomPinDefinition("d", 20, -20, "drain"),
                    new CustomPinDefinition("g", -40, 0, "gate"),
                    new CustomPinDefinition("s", 20, 20, "source")
                ];
                defaults["m_mult"] = 1.0;
                if (parsedParams.TryGetValue("vto", out var vmVto) || parsedParams.TryGetValue("vth0", out vmVto))
                    defaults["vto_volts"] = vmVto;
                if (parsedParams.TryGetValue("kp", out var vmKp)) defaults["kp_a_per_v2"] = vmKp;
                if (parsedParams.TryGetValue("rd", out var vmRd)) defaults["rd_ohms"] = vmRd;
                if (parsedParams.TryGetValue("rs", out var vmRs)) defaults["rs_ohms"] = vmRs;
                if (parsedParams.TryGetValue("rg", out var vmRg)) defaults["rg_ohms"] = vmRg;
                break;

            case "NJF":
            case "PJF":
                baseKind = "spice.jfet";
                prefix = "J";
                symbolStyle = "jfet";
                pins =
                [
                    new CustomPinDefinition("d", 20, -20, "drain"),
                    new CustomPinDefinition("g", -40, 0, "gate"),
                    new CustomPinDefinition("s", 20, 20, "source")
                ];
                defaults["area"] = 1.0;
                if (parsedParams.TryGetValue("vto", out var jVto)) defaults["vto_volts"] = jVto;
                if (parsedParams.TryGetValue("beta", out var jBeta)) defaults["beta_a_per_v2"] = jBeta;
                break;

            case "NMF":
            case "PMF":
            case "MESFET":
                baseKind = "spice.mesfet";
                prefix = "Z";
                symbolStyle = "mesfet";
                pins =
                [
                    new CustomPinDefinition("d", 20, -20, "drain"),
                    new CustomPinDefinition("g", -40, 0, "gate"),
                    new CustomPinDefinition("s", 20, 20, "source")
                ];
                defaults["area"] = 1.0;
                if (parsedParams.TryGetValue("level", out var zLvl)) defaults["level"] = zLvl;
                if (parsedParams.TryGetValue("vto", out var zVto)) defaults["vto_volts"] = zVto;
                if (parsedParams.TryGetValue("beta", out var zBeta)) defaults["beta_a_per_v2"] = zBeta;
                break;

            case "SW":
            case "VSWITCH":
                baseKind = "spice.switch";
                prefix = "S";
                symbolStyle = "switch";
                pins =
                [
                    new CustomPinDefinition("p", 40, -20, "passive"),
                    new CustomPinDefinition("n", 40, 20, "passive"),
                    new CustomPinDefinition("cp", -40, -20, "ctrl_pos"),
                    new CustomPinDefinition("cn", -40, 20, "ctrl_neg")
                ];
                if (parsedParams.TryGetValue("vt", out var swVt)) defaults["vt_volts"] = swVt;
                if (parsedParams.TryGetValue("vh", out var swVh)) defaults["vh_volts"] = Math.Abs(swVh);
                if (parsedParams.TryGetValue("ron", out var swRon)) defaults["ron_ohms"] = swRon;
                if (parsedParams.TryGetValue("roff", out var swRoff)) defaults["roff_ohms"] = swRoff;
                if (parsedParams.TryGetValue("hyst", out var swHyst) || parsedParams.TryGetValue("hysteresis", out swHyst))
                    defaults["hysteresis"] = swHyst != 0.0 ? 1.0 : 0.0;
                break;

            case "CSW":
            case "ISWITCH":
                baseKind = "spice.cswitch";
                prefix = "W";
                symbolStyle = "cswitch";
                pins =
                [
                    new CustomPinDefinition("p", -36, 0, "passive"),
                    new CustomPinDefinition("n", 36, 0, "passive")
                ];
                if (parsedParams.TryGetValue("it", out var cswIt)) defaults["it_amps"] = cswIt;
                if (parsedParams.TryGetValue("ih", out var cswIh)) defaults["ih_amps"] = Math.Abs(cswIh);
                if (parsedParams.TryGetValue("ron", out var cswRon)) defaults["ron_ohms"] = cswRon;
                if (parsedParams.TryGetValue("roff", out var cswRoff)) defaults["roff_ohms"] = cswRoff;
                break;

            case "CORE":
                baseKind = "spice.core";
                prefix = "L";
                symbolStyle = "core";
                pins =
                [
                    new CustomPinDefinition("p", -36, 0, "passive"),
                    new CustomPinDefinition("n", 36, 0, "passive")
                ];
                defaults["turns"] = 20.0;
                if (parsedParams.TryGetValue("area", out var cArea)) defaults["area_m2"] = cArea;
                if (parsedParams.TryGetValue("path", out var cPath)) defaults["path_m"] = cPath;
                if (parsedParams.TryGetValue("gap", out var cGap)) defaults["gap_m"] = cGap;
                if (parsedParams.TryGetValue("rw", out var cRw)) defaults["r_winding_ohms"] = cRw;
                if (parsedParams.TryGetValue("lleak", out var cLleak)) defaults["l_leak_henries"] = cLleak;
                break;

            default:
                error = new LibraryImportDiagnostic(
                    "error",
                    "unsupported_model_type",
                    $"Unsupported .MODEL type '{modelType}' for model '{modelName}'.",
                    modelName,
                    line.LineNumber);
                return false;
        }

        var safeId = $"imported.{modelType.ToLowerInvariant()}.{SanitizeId(modelName)}";
        var candidate = ComponentLibraryStore.WithContentHash(new CustomComponentDefinition(
            Id: safeId,
            Name: $"{modelName} ({modelType})",
            Category: category,
            Description: $"Imported SPICE .MODEL {modelName} ({modelType}).",
            BaseDeviceKindId: baseKind,
            SpicePrefix: prefix,
            SymbolStyle: symbolStyle,
            Pins: pins,
            DefaultParameters: defaults,
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                modelName,
                rawModelCard.Trim(),
                Description: $"Imported from SPICE library line {line.LineNumber}"),
            IsBuiltIn: false));

        var valErrs = ComponentLibraryStore.Validate(candidate, reg);
        if (valErrs.Count > 0)
        {
            error = new LibraryImportDiagnostic(
                "error",
                valErrs[0].Code,
                valErrs[0].Message,
                modelName,
                line.LineNumber);
            return false;
        }

        component = candidate;
        return true;
    }

    private static bool TryBuildSubcktComponent(
        int startLine,
        string headerLine,
        IReadOnlyList<string> bodyLines,
        string category,
        DeviceRegistry reg,
        out CustomComponentDefinition? component,
        out LibraryImportDiagnostic? error)
    {
        component = null;
        error = null;

        var tokens = Tokenize(headerLine);
        if (tokens.Count < 3)
        {
            error = new LibraryImportDiagnostic(
                "error",
                "malformed_subckt",
                ".SUBCKT requires a name and at least one port pin.",
                tokens.Count >= 2 ? tokens[1] : null,
                startLine);
            return false;
        }

        var subName = tokens[1];
        var portNames = new List<string>();
        var defaults = new Dictionary<string, double>(StringComparer.Ordinal);
        bool inParams = false;
        for (int k = 2; k < tokens.Count; ++k)
        {
            var tok = tokens[k];
            if (tok.Equals("PARAMS:", StringComparison.OrdinalIgnoreCase))
            {
                inParams = true;
                continue;
            }
            var eq = tok.IndexOf('=');
            if (eq > 0 || inParams)
            {
                inParams = true;
                if (eq > 0 && eq + 1 < tok.Length && TryParseSpiceNumber(tok[(eq + 1)..], out var v))
                {
                    defaults[tok[..eq]] = v;
                }
                continue;
            }
            if (!portNames.Contains(tok, StringComparer.OrdinalIgnoreCase))
            {
                portNames.Add(tok);
            }
        }

        if (portNames.Count == 0)
        {
            error = new LibraryImportDiagnostic(
                "error",
                "subckt_no_ports",
                $".SUBCKT '{subName}' has no interface port pins.",
                subName,
                startLine);
            return false;
        }

        var looksOpamp = portNames.Count == 3 &&
            (subName.Contains("OPAMP", StringComparison.OrdinalIgnoreCase) ||
             subName.Contains("TL07", StringComparison.OrdinalIgnoreCase) ||
             subName.Contains("LM358", StringComparison.OrdinalIgnoreCase) ||
             subName.Contains("LM741", StringComparison.OrdinalIgnoreCase) ||
             subName.Contains("COMP", StringComparison.OrdinalIgnoreCase) ||
             (portNames.Any(p => p.Equals("inp", StringComparison.OrdinalIgnoreCase)) &&
              portNames.Any(p => p.Equals("inn", StringComparison.OrdinalIgnoreCase))));

        var pins = new List<CustomPinDefinition>();
        if (looksOpamp)
        {
            pins.Add(new CustomPinDefinition(portNames[0], -40, -20, "input_pos"));
            pins.Add(new CustomPinDefinition(portNames[1], -40, 20, "input_neg"));
            pins.Add(new CustomPinDefinition(portNames[2], 40, 0, "output"));
        }
        else
        {
            int leftCount = (portNames.Count + 1) / 2;
            int rightCount = portNames.Count - leftCount;
            for (int i = 0; i < leftCount; ++i)
            {
                double y = (i - (leftCount - 1) * 0.5) * 20.0;
                pins.Add(new CustomPinDefinition(portNames[i], -40.0, y, "inout"));
            }
            for (int i = 0; i < rightCount; ++i)
            {
                double y = (i - (rightCount - 1) * 0.5) * 20.0;
                pins.Add(new CustomPinDefinition(portNames[leftCount + i], 40.0, y, "inout"));
            }
        }

        var safeId = $"imported.subckt.{SanitizeId(subName)}";
        var fullText = string.Join("\n", bodyLines);
        var candidate = ComponentLibraryStore.WithContentHash(new CustomComponentDefinition(
            Id: safeId,
            Name: $"{subName} (.SUBCKT)",
            Category: category,
            Description: $"Imported SPICE .SUBCKT {subName} ({portNames.Count} pins).",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: looksOpamp ? "opamp" : "ic_box",
            Pins: pins,
            DefaultParameters: defaults,
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                subName,
                fullText,
                Description: $"Imported from SPICE library line {startLine}"),
            IsBuiltIn: false));

        var valErrs = ComponentLibraryStore.Validate(candidate, reg);
        if (valErrs.Count > 0)
        {
            error = new LibraryImportDiagnostic(
                "error",
                valErrs[0].Code,
                valErrs[0].Message,
                subName,
                startLine);
            return false;
        }

        component = candidate;
        return true;
    }

    private static string InferDiodeSymbol(string modelName, Dictionary<string, double> p)
    {
        if (p.TryGetValue("bv", out var bv) && Math.Abs(bv) > 0.0 && Math.Abs(bv) <= 30.0)
            return "zener";
        if (modelName.Contains("LED", StringComparison.OrdinalIgnoreCase))
            return "led";
        if (modelName.Contains("BZX", StringComparison.OrdinalIgnoreCase) ||
            modelName.Contains("1N47", StringComparison.OrdinalIgnoreCase) ||
            modelName.Contains("ZENER", StringComparison.OrdinalIgnoreCase) ||
            modelName.Contains("DZ", StringComparison.OrdinalIgnoreCase))
            return "zener";
        if (modelName.Contains("581", StringComparison.OrdinalIgnoreCase) ||
            modelName.Contains("BAT", StringComparison.OrdinalIgnoreCase) ||
            modelName.Contains("SCH", StringComparison.OrdinalIgnoreCase) ||
            (p.TryGetValue("is", out var isv) && isv >= 1.0e-6))
            return "schottky";
        return "diode";
    }

    private static List<LogicalLine> JoinContinuationLines(string text)
    {
        var rawLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                           .Replace('\r', '\n')
                           .Split('\n');
        var result = new List<LogicalLine>();
        for (int i = 0; i < rawLines.Length; ++i)
        {
            var cleaned = StripInlineComment(rawLines[i]).Trim();
            if (string.IsNullOrEmpty(cleaned) || cleaned.StartsWith('*')) continue;

            if (cleaned.StartsWith('+') && result.Count > 0)
            {
                var prev = result[^1];
                result[^1] = prev with { Text = prev.Text + " " + cleaned[1..].Trim() };
            }
            else
            {
                result.Add(new LogicalLine(i + 1, cleaned));
            }
        }
        return result;
    }

    private static string StripInlineComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; ++i)
        {
            char ch = line[i];
            if (ch == '"') inQuotes = !inQuotes;
            else if (!inQuotes && ch == ';') return line[..i];
        }
        return line;
    }

    private static int FindWhitespace(string s)
    {
        for (int i = 0; i < s.Length; ++i)
        {
            if (char.IsWhiteSpace(s[i])) return i;
        }
        return -1;
    }

    private static string? ExtractSecondToken(string line)
    {
        var toks = Tokenize(line);
        return toks.Count >= 2 ? toks[1] : null;
    }

    private static List<string> Tokenize(string text)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        int parenDepth = 0;
        bool inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                cur.Append(ch);
            }
            else if (!inQuotes && ch == '(')
            {
                parenDepth++;
                cur.Append(ch);
            }
            else if (!inQuotes && ch == ')')
            {
                if (parenDepth > 0) parenDepth--;
                cur.Append(ch);
            }
            else if (!inQuotes && parenDepth == 0 && (char.IsWhiteSpace(ch) || ch == ','))
            {
                if (cur.Length > 0)
                {
                    list.Add(cur.ToString());
                    cur.Clear();
                }
            }
            else
            {
                cur.Append(ch);
            }
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    private static Dictionary<string, double> ParseModelKeyValues(string body)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var parts = body.Split([' ', '\t', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; ++i)
        {
            var tok = parts[i];
            var eq = tok.IndexOf('=');
            if (eq > 0 && eq + 1 < tok.Length)
            {
                var k = tok[..eq].Trim();
                var vStr = tok[(eq + 1)..].Trim();
                if (TryParseSpiceNumber(vStr, out var v)) map[k] = v;
            }
            else if (eq == tok.Length - 1 && eq > 0 && i + 1 < parts.Length)
            {
                var k = tok[..eq].Trim();
                if (TryParseSpiceNumber(parts[++i], out var v)) map[k] = v;
            }
            else if (eq < 0 && i + 1 < parts.Length)
            {
                if (parts[i + 1] == "=" && i + 2 < parts.Length)
                {
                    if (TryParseSpiceNumber(parts[i + 2], out var v)) map[tok] = v;
                    i += 2;
                }
                else if (parts[i + 1].StartsWith('=') && parts[i + 1].Length > 1)
                {
                    if (TryParseSpiceNumber(parts[i + 1][1..], out var v)) map[tok] = v;
                    i += 1;
                }
            }
        }
        return map;
    }

    internal static bool TryParseSpiceNumber(string raw, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
        {
            return true;
        }
        int numEnd = 0;
        if (numEnd < s.Length && (s[numEnd] == '+' || s[numEnd] == '-')) numEnd++;
        bool seenDigit = false;
        while (numEnd < s.Length && char.IsDigit(s[numEnd])) { numEnd++; seenDigit = true; }
        if (numEnd < s.Length && s[numEnd] == '.')
        {
            numEnd++;
            while (numEnd < s.Length && char.IsDigit(s[numEnd])) { numEnd++; seenDigit = true; }
        }
        if (!seenDigit) return false;
        if (numEnd < s.Length && (s[numEnd] == 'e' || s[numEnd] == 'E'))
        {
            int expPos = numEnd + 1;
            if (expPos < s.Length && (s[expPos] == '+' || s[expPos] == '-')) expPos++;
            int expDigits = 0;
            while (expPos < s.Length && char.IsDigit(s[expPos])) { expPos++; expDigits++; }
            if (expDigits > 0) numEnd = expPos;
        }
        if (!double.TryParse(s[..numEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out var mantissa))
            return false;

        var suffix = s[numEnd..].ToLowerInvariant();
        double mult = 1.0;
        if (suffix.StartsWith("meg", StringComparison.Ordinal)) mult = 1.0e6;
        else if (suffix.StartsWith("mil", StringComparison.Ordinal)) mult = 25.4e-6;
        else if (suffix.StartsWith('t')) mult = 1.0e12;
        else if (suffix.StartsWith('g')) mult = 1.0e9;
        else if (suffix.StartsWith('k')) mult = 1.0e3;
        else if (suffix.StartsWith('m')) mult = 1.0e-3;
        else if (suffix.StartsWith('u')) mult = 1.0e-6;
        else if (suffix.StartsWith('n')) mult = 1.0e-9;
        else if (suffix.StartsWith('p')) mult = 1.0e-12;
        else if (suffix.StartsWith('f')) mult = 1.0e-15;
        else if (suffix.StartsWith('a')) mult = 1.0e-18;
        else if (suffix.Length > 0) return false;

        value = mantissa * mult;
        return double.IsFinite(value);
    }

    private static string SanitizeId(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') sb.Append(char.ToLowerInvariant(ch));
            else sb.Append('_');
        }
        return sb.Length == 0 ? "part" : sb.ToString();
    }
}
