using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Persistent versioned library file envelope (*.spice-lib.json) storing user-created
/// custom components with their symbols, ordered pins, and attached SPICE/OSDI models.
/// </summary>
public sealed record ComponentLibraryFile(
    string FormatId,
    string SchemaVersion,
    IReadOnlyList<CustomComponentDefinition> Components);

/// <summary>
/// Manages built-in Foundation models and persistent user custom components with attached
/// .MODEL, .SUBCKT, or .OSDI models (ARCH-SCHEM-001, ARCH-SCHEM-005).
/// </summary>
public sealed class ComponentLibraryStore
{
    public const string LibraryFormatId = "spice.component-library.v2";
    public const string CurrentSchemaVersion = "2.1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _gate = new();
    private readonly string? _storagePath;
    private readonly Dictionary<string, CustomComponentDefinition> _builtIns;
    private readonly Dictionary<string, CustomComponentDefinition> _custom = new(StringComparer.OrdinalIgnoreCase);

    public ComponentLibraryStore(string? storagePath = null)
    {
        _storagePath = storagePath;
        _builtIns = CreateFoundationLibrary()
            .Select(WithContentHash)
            .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        LoadFromDisk();
    }

    public string? StoragePath => _storagePath;

    public static string GetDefaultLibraryPath()
    {
        var env = Environment.GetEnvironmentVariable("SPICE_V2_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".spice-v2", "library.spice-lib.json");
    }

    public IReadOnlyList<CustomComponentDefinition> ListAll()
    {
        lock (_gate)
        {
            return _builtIns.Values
                .Concat(_custom.Values)
                .OrderBy(c => c.IsBuiltIn ? 0 : 1)
                .ThenBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<CustomComponentDefinition> ListCustom()
    {
        lock (_gate)
        {
            return _custom.Values
                .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool TryGet(string partId, out CustomComponentDefinition? component)
    {
        lock (_gate)
        {
            if (_custom.TryGetValue(partId, out component)) return true;
            return _builtIns.TryGetValue(partId, out component);
        }
    }

    public bool TryFindByModelName(string modelName, out CustomComponentDefinition? component)
    {
        lock (_gate)
        {
            foreach (var c in _custom.Values.Concat(_builtIns.Values))
            {
                if (c.AttachedModel is not null &&
                    string.Equals(c.AttachedModel.ModelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    component = c;
                    return true;
                }
            }
            component = null;
            return false;
        }
    }

    /// <summary>
    /// Fail-closed validation of a custom component definition and its attached SPICE/OSDI model.
    /// </summary>
    public static IReadOnlyList<ValidationDiagnostic> Validate(
        CustomComponentDefinition part,
        DeviceRegistry? registry = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var errors = new List<ValidationDiagnostic>();

        if (string.IsNullOrWhiteSpace(part.Id))
        {
            errors.Add(new ValidationDiagnostic("empty_part_id", "Custom component Id must be non-empty."));
        }
        if (string.IsNullOrWhiteSpace(part.Name))
        {
            errors.Add(new ValidationDiagnostic("empty_part_name", "Custom component Name must be non-empty.", part.Id));
        }
        if (!reg.TryGet(part.BaseDeviceKindId, out var desc) || desc is null)
        {
            errors.Add(new ValidationDiagnostic(
                "unknown_base_device_kind",
                $"Base device kind '{part.BaseDeviceKindId}' is not registered in DeviceRegistry.",
                part.Id));
        }

        if (part.Pins is null || part.Pins.Count == 0)
        {
            errors.Add(new ValidationDiagnostic("missing_pins", "Custom component must define at least one pin.", part.Id));
        }
        else
        {
            var seenPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in part.Pins)
            {
                if (string.IsNullOrWhiteSpace(pin.Name))
                {
                    errors.Add(new ValidationDiagnostic("empty_pin_name", "Pin name cannot be empty.", part.Id));
                }
                else if (!seenPins.Add(pin.Name))
                {
                    errors.Add(new ValidationDiagnostic("duplicate_pin_name", $"Duplicate pin '{pin.Name}'.", part.Id));
                }
                if (!double.IsFinite(pin.LocalX) || !double.IsFinite(pin.LocalY))
                {
                    errors.Add(new ValidationDiagnostic("non_finite_pin_coord", $"Pin '{pin.Name}' coordinates must be finite.", part.Id));
                }
            }

            if (desc is not null &&
                desc.Pins.Count > 0 &&
                !string.Equals(desc.KindId, "spice.subckt", StringComparison.Ordinal) &&
                !string.Equals(desc.KindId, "spice.osdi", StringComparison.Ordinal))
            {
                foreach (var reqPin in desc.Pins)
                {
                    if (!seenPins.Contains(reqPin.Name))
                    {
                        errors.Add(new ValidationDiagnostic(
                            "missing_required_pin",
                            $"Base kind '{desc.KindId}' requires pin '{reqPin.Name}'.",
                            part.Id));
                    }
                }
            }
        }

        if (part.DefaultParameters is not null)
        {
            foreach (var (k, v) in part.DefaultParameters)
            {
                if (!double.IsFinite(v))
                {
                    errors.Add(new ValidationDiagnostic(
                        "non_finite_parameter",
                        $"Default parameter '{k}' must be finite.",
                        part.Id));
                }
            }
        }

        if (part.SymbolPrimitives is not null)
        {
            foreach (var prim in part.SymbolPrimitives)
            {
                var k = (prim.Kind ?? string.Empty).ToLowerInvariant();
                if (k is not ("line" or "rect" or "circle" or "polyline" or "polygon" or "text"))
                {
                    errors.Add(new ValidationDiagnostic(
                        "invalid_symbol_primitive_kind",
                        $"Unsupported symbol primitive kind '{prim.Kind}'.",
                        part.Id));
                }
                if (!double.IsFinite(prim.X1) || !double.IsFinite(prim.Y1) ||
                    !double.IsFinite(prim.X2) || !double.IsFinite(prim.Y2) ||
                    !double.IsFinite(prim.Radius) || !double.IsFinite(prim.StrokeWidth))
                {
                    errors.Add(new ValidationDiagnostic(
                        "non_finite_symbol_primitive",
                        "Symbol primitive coordinates, radius, and strokeWidth must be finite.",
                        part.Id));
                }
                if (prim.Radius < 0.0 || prim.StrokeWidth <= 0.0)
                {
                    errors.Add(new ValidationDiagnostic(
                        "invalid_symbol_primitive_dimension",
                        "Symbol primitive radius must be >= 0 and strokeWidth must be > 0.",
                        part.Id));
                }
            }
        }

        if (part.AttachedModel is not null)
        {
            var m = part.AttachedModel;
            if (string.IsNullOrWhiteSpace(m.ModelName))
            {
                errors.Add(new ValidationDiagnostic("empty_model_name", "Attached model must specify a ModelName.", part.Id));
            }
            var trimmed = (m.SpiceText ?? string.Empty).Trim();
            if (m.Kind == AttachedModelKind.SpiceModelCard)
            {
                if (!trimmed.Contains(".MODEL", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationDiagnostic(
                        "invalid_model_card",
                        "SpiceModelCard attached model text must contain a '.MODEL' statement.",
                        part.Id));
                }
            }
            else if (m.Kind == AttachedModelKind.Subcircuit)
            {
                if (!trimmed.Contains(".SUBCKT", StringComparison.OrdinalIgnoreCase) ||
                    !trimmed.Contains(".ENDS", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationDiagnostic(
                        "invalid_subckt_definition",
                        "Subcircuit attached model text must contain both '.SUBCKT' and '.ENDS'.",
                        part.Id));
                }
            }
            else if (m.Kind == AttachedModelKind.Osdi)
            {
                if (string.IsNullOrWhiteSpace(m.OsdiLibraryPath) && !trimmed.Contains(".OSDI", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationDiagnostic(
                        "missing_osdi_library_path",
                        "OSDI attached model must specify OsdiLibraryPath or a '.OSDI' directive.",
                        part.Id));
                }
            }
        }
        else if (string.Equals(part.BaseDeviceKindId, "spice.subckt", StringComparison.Ordinal))
        {
            errors.Add(new ValidationDiagnostic(
                "missing_subckt_model",
                "Custom component with BaseDeviceKindId 'spice.subckt' must attach a Subcircuit model.",
                part.Id));
        }

        return errors;
    }

    /// <summary>
    /// Validates and persists a user custom component (with its attached model) into the library.
    /// </summary>
    public CustomComponentDefinition UpsertCustom(CustomComponentDefinition part, DeviceRegistry? registry = null)
    {
        var errs = Validate(part, registry);
        if (errs.Count > 0)
        {
            throw new InvalidOperationException(
                "Custom component validation failed: " +
                string.Join("; ", errs.Select(e => $"{e.Code}: {e.Message}")));
        }

        var normalized = WithContentHash(part with
        {
            IsBuiltIn = false,
            SchemaVersion = CurrentSchemaVersion,
            Category = string.IsNullOrWhiteSpace(part.Category) ? "Custom" : part.Category.Trim(),
            SpicePrefix = string.IsNullOrWhiteSpace(part.SpicePrefix) ? InferPrefix(part.BaseDeviceKindId) : part.SpicePrefix.Trim().ToUpperInvariant(),
            SymbolStyle = string.IsNullOrWhiteSpace(part.SymbolStyle) ? InferSymbolStyle(part.BaseDeviceKindId) : part.SymbolStyle.Trim().ToLowerInvariant()
        });

        lock (_gate)
        {
            _custom[normalized.Id] = normalized;
            SaveToDisk();
        }
        return normalized;
    }

    public bool DeleteCustom(string partId)
    {
        lock (_gate)
        {
            if (!_custom.Remove(partId))
            {
                return false;
            }
            SaveToDisk();
            return true;
        }
    }

    /// <summary>
    /// Converts a schematic sheet into a reusable .SUBCKT custom component with auto-generated
    /// ordered symbol pins and saves it to the library (RSpice "Create symbol from schematic" parity).
    /// </summary>
    public CustomComponentDefinition CreateFromSchematic(
        SchematicDocument document,
        string partId,
        string name,
        string category = "Custom Subcircuits",
        string? description = null,
        DeviceRegistry? registry = null)
    {
        var compiled = SchematicCompiler.CompileToIr(document, registry, this);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException(
                "Cannot create subcircuit component from invalid schematic: " +
                string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        }

        // Determine interface pins from explicit sheet ports, falling back to non-ground nets.
        var portNames = document.RootSheet.Ports is { Count: > 0 } ports
            ? ports.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : document.RootSheet.Nets
                .Where(n => !n.IsGlobalGround && !string.Equals(n.NetName, "0", StringComparison.Ordinal))
                .Select(n => n.NetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (portNames.Count == 0)
        {
            throw new InvalidOperationException("Schematic must have at least one port or non-ground net to create a subcircuit component.");
        }

        // Always expose ground reference if used internally and not explicitly listed.
        var subcktName = SanitizeIdentifier(name);
        var pins = new List<CustomPinDefinition>();
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

        // Emit inner circuit body lines (strip .OP / .END and top comment).
        var rawNetlist = SpiceNetlistEmitter.EmitNetlist(
            new SimulationRequestIr(document.Name, compiled.Circuit, AnalysisKind.OperatingPoint),
            includeXycePrintCsv: false,
            registry: registry);

        var sb = new StringBuilder();
        sb.AppendLine($".SUBCKT {subcktName} {string.Join(" ", portNames)}");
        foreach (var rawLine in rawNetlist.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) ||
                line.StartsWith('*') ||
                line.Equals(".OP", StringComparison.OrdinalIgnoreCase) ||
                line.Equals(".END", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            sb.AppendLine("  " + line);
        }
        sb.AppendLine($".ENDS {subcktName}");

        var part = new CustomComponentDefinition(
            Id: string.IsNullOrWhiteSpace(partId) ? $"user.subckt.{subcktName.ToLowerInvariant()}" : partId.Trim(),
            Name: name.Trim(),
            Category: category,
            Description: description ?? $"Subcircuit created from schematic '{document.Name}'.",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "ic_box",
            Pins: pins,
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                subcktName,
                sb.ToString().TrimEnd(),
                Description: $"Generated from {document.Name}"));

        return UpsertCustom(part, registry);
    }

    public string ExportCustomLibraryJson()
    {
        lock (_gate)
        {
            var file = new ComponentLibraryFile(LibraryFormatId, CurrentSchemaVersion, ListCustom());
            return JsonSerializer.Serialize(file, JsonOpts);
        }
    }

    public int ImportCustomLibraryJson(string json, DeviceRegistry? registry = null)
    {
        var file = JsonSerializer.Deserialize<ComponentLibraryFile>(json, JsonOpts)
            ?? throw new InvalidDataException("Invalid component library JSON.");
        if (!string.Equals(file.FormatId, LibraryFormatId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported library formatId '{file.FormatId}'. Expected '{LibraryFormatId}'.");
        }
        if (file.SchemaVersion is not ("2.0" or "2.1"))
        {
            throw new InvalidDataException($"Unsupported library schemaVersion '{file.SchemaVersion}'.");
        }
        int imported = 0;
        foreach (var comp in file.Components)
        {
            UpsertCustom(comp, registry);
            imported++;
        }
        return imported;
    }

    private void LoadFromDisk()
    {
        if (string.IsNullOrWhiteSpace(_storagePath) || !File.Exists(_storagePath))
        {
            return;
        }
        try
        {
            var json = File.ReadAllText(_storagePath);
            var file = JsonSerializer.Deserialize<ComponentLibraryFile>(json, JsonOpts);
            if (file?.Components is null) return;
            foreach (var c in file.Components)
            {
                if (Validate(c).Count == 0)
                {
                    _custom[c.Id] = WithContentHash(c with { IsBuiltIn = false });
                }
            }
        }
        catch
        {
            // Keep store operational if disk cache is corrupted; explicit import fails closed.
        }
    }

    private void SaveToDisk()
    {
        if (string.IsNullOrWhiteSpace(_storagePath)) return;
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var file = new ComponentLibraryFile(
            LibraryFormatId,
            CurrentSchemaVersion,
            _custom.Values.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList());
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(file, JsonOpts));
    }

    public static CustomComponentDefinition WithContentHash(CustomComponentDefinition part)
    {
        var canonical = JsonSerializer.Serialize(part with { ContentSha256 = null }, JsonOpts);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return part with { ContentSha256 = hash };
    }

    private static string InferPrefix(string kindId) => kindId switch
    {
        "spice.resistor" => "R",
        "spice.capacitor" => "C",
        "spice.inductor" => "L",
        "spice.vsrc" => "V",
        "spice.isrc" => "I",
        "spice.diode" => "D",
        "spice.mosfet" => "M",
        "spice.bjt" => "Q",
        "spice.jfet" => "J",
        "spice.vcvs" => "E",
        "spice.vccs" => "G",
        "spice.subckt" => "X",
        _ => "U"
    };

    private static string InferSymbolStyle(string kindId) => kindId switch
    {
        "spice.diode" => "diode",
        "spice.mosfet" => "nmos",
        "spice.bjt" => "npn",
        "spice.jfet" => "jfet",
        "spice.subckt" => "ic_box",
        _ => "ic_box"
    };

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-') sb.Append('_');
        }
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, "SUB_");
        return sb.ToString().ToUpperInvariant();
    }

    private static IReadOnlyList<CustomComponentDefinition> CreateFoundationLibrary() =>
    [
        new CustomComponentDefinition(
            Id: "foundation.diode.1n4148",
            Name: "1N4148 Fast Diode",
            Category: "Foundation Diodes",
            Description: "Standard small-signal high-speed silicon switching diode.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "diode",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 2.52e-9, ["n_emission"] = 1.752, ["cjo_farads"] = 4e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "D1N4148",
                ".MODEL D1N4148 D(IS=2.52e-9 N=1.752 CJO=4p VJ=0.6 M=0.333 TT=5.76n)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.1n5819",
            Name: "1N5819 Schottky Diode",
            Category: "Foundation Diodes",
            Description: "Low forward-voltage Schottky barrier rectifier diode.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "schottky",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 2.2e-5, ["n_emission"] = 1.05, ["cjo_farads"] = 110e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "D1N5819",
                ".MODEL D1N5819 D(IS=2.2e-5 N=1.05 CJO=110p VJ=0.4 M=0.45)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.2n2222",
            Name: "2N2222 NPN BJT",
            Category: "Foundation Transistors",
            Description: "General-purpose NPN Gummel-Poon bipolar junction transistor.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "npn",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 255.9, ["is_amps"] = 1.434e-14 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "Q2N2222",
                ".MODEL Q2N2222 NPN(IS=1.434e-14 BF=255.9 VAF=74.03 CJE=22p CJC=7.3p)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.2n3906",
            Name: "2N3906 PNP BJT",
            Category: "Foundation Transistors",
            Description: "Complementary PNP Gummel-Poon small-signal transistor.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "pnp",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 180.7, ["is_amps"] = 1.41e-15 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "Q2N3906",
                ".MODEL Q2N3906 PNP(IS=1.41e-15 BF=180.7 VAF=18.7 CJE=8.06p CJC=5.82p)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.mos.bsim4_nch65",
            Name: "BSIM4 65nm NMOS",
            Category: "Foundation Transistors",
            Description: "Native BSIM4 (LEVEL=54) 65nm N-channel MOSFET compact model.",
            BaseDeviceKindId: "spice.mosfet",
            SpicePrefix: "M",
            SymbolStyle: "nmos",
            Pins:
            [
                new CustomPinDefinition("d", 20, -20, "drain"),
                new CustomPinDefinition("g", -40, 0, "gate"),
                new CustomPinDefinition("s", 20, 20, "source"),
                new CustomPinDefinition("b", 40, 0, "bulk")
            ],
            DefaultParameters: new Dictionary<string, double> { ["w_m"] = 1e-6, ["l_m"] = 65e-9, ["level"] = 54.0, ["vth0_volts"] = 0.35 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "NCH65",
                ".MODEL NCH65 NMOS(LEVEL=54 VTH0=0.35 TOXE=2.2n U0=420 VSAT=1.1e5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.mos.bsim4_pch65",
            Name: "BSIM4 65nm PMOS",
            Category: "Foundation Transistors",
            Description: "Native BSIM4 (LEVEL=54) 65nm P-channel MOSFET compact model.",
            BaseDeviceKindId: "spice.mosfet",
            SpicePrefix: "M",
            SymbolStyle: "pmos",
            Pins:
            [
                new CustomPinDefinition("d", 20, -20, "drain"),
                new CustomPinDefinition("g", -40, 0, "gate"),
                new CustomPinDefinition("s", 20, 20, "source"),
                new CustomPinDefinition("b", 40, 0, "bulk")
            ],
            DefaultParameters: new Dictionary<string, double> { ["w_m"] = 2.2e-6, ["l_m"] = 65e-9, ["level"] = 54.0, ["vth0_volts"] = -0.35 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "PCH65",
                ".MODEL PCH65 PMOS(LEVEL=54 VTH0=-0.35 TOXE=2.2n U0=160 VSAT=0.9e5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.jfet.2n5457",
            Name: "2N5457 N-JFET",
            Category: "Foundation Transistors",
            Description: "Low-noise N-channel Shichman-Hodges JFET.",
            BaseDeviceKindId: "spice.jfet",
            SpicePrefix: "J",
            SymbolStyle: "jfet",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["vto_volts"] = -1.8, ["beta_a_per_v2"] = 1.125e-3 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "J2N5457",
                ".MODEL J2N5457 NJF(VTO=-1.8 BETA=1.125m LAMBDA=2.3m)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.ideal_opamp",
            Name: "Ideal Op-Amp (Macro)",
            Category: "Foundation Subcircuits",
            Description: "Single-pole operational amplifier subcircuit (Aol=100dB, Rout=50 Ohm, GBW=10MHz).",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "opamp",
            Pins:
            [
                new CustomPinDefinition("inp", -40, -20, "input_pos"),
                new CustomPinDefinition("inn", -40, 20, "input_neg"),
                new CustomPinDefinition("out", 40, 0, "output")
            ],
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                "IDEAL_OPAMP",
                """
                .SUBCKT IDEAL_OPAMP inp inn out
                  Rin inp inn 10Meg
                  E1 int 0 inp inn 100000
                  Rp int pole 10k
                  Cp pole 0 15.915n
                  Ebuf drv 0 pole 0 1.0
                  Rout drv out 50
                .ENDS IDEAL_OPAMP
                """),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.rc_snubber",
            Name: "RC Snubber Network",
            Category: "Foundation Subcircuits",
            Description: "2-terminal damped RC snubber subcircuit (100 Ohm + 10 nF).",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "ic_box",
            Pins: [new CustomPinDefinition("p", -40, 0, "passive"), new CustomPinDefinition("n", 40, 0, "passive")],
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                "RC_SNUBBER",
                """
                .SUBCKT RC_SNUBBER p n
                  Rs p mid 100
                  Cs mid n 10n
                .ENDS RC_SNUBBER
                """),
            IsBuiltIn: true)
    ];
}
