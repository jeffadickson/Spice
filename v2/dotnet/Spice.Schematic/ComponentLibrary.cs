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

    /// <summary>
    /// Bulk imports SPICE .LIB, .MOD, and .SUB library files (.MODEL cards and .SUBCKT blocks)
    /// into the persistent user library with optional dry-run preview and overwrite control
    /// (ARCH-SCHEM-005).
    /// </summary>
    public LibraryImportReport ImportSpiceLibraryText(
        string spiceText,
        string? defaultCategory = null,
        bool overwriteExisting = false,
        bool dryRun = false,
        DeviceRegistry? registry = null)
    {
        var parsed = SpiceLibraryImporter.ParseSpiceLibraryText(spiceText, defaultCategory, registry);
        if (dryRun)
        {
            return parsed with { DryRun = true };
        }

        var diagnostics = new List<LibraryImportDiagnostic>(parsed.Diagnostics);
        var savedComponents = new List<CustomComponentDefinition>();
        int imported = 0;
        int skipped = 0;

        foreach (var candidate in parsed.Components)
        {
            bool exists;
            lock (_gate)
            {
                exists = _custom.ContainsKey(candidate.Id);
            }
            if (exists && !overwriteExisting)
            {
                skipped++;
                diagnostics.Add(new LibraryImportDiagnostic(
                    "warning",
                    "duplicate_part_skipped",
                    $"Custom library already contains '{candidate.Id}'; enable overwrite to replace it.",
                    candidate.AttachedModel?.ModelName ?? candidate.Name));
                continue;
            }

            try
            {
                var persisted = UpsertCustom(candidate, registry);
                savedComponents.Add(persisted);
                imported++;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new LibraryImportDiagnostic(
                    "error",
                    "import_persist_failed",
                    ex.Message,
                    candidate.AttachedModel?.ModelName ?? candidate.Name));
            }
        }

        int errCount = diagnostics.Count(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new LibraryImportReport(
            DryRun: false,
            TotalFound: parsed.TotalFound,
            ImportedCount: imported,
            SkippedCount: skipped,
            ErrorCount: errCount,
            Components: savedComponents,
            Diagnostics: diagnostics);
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
        "spice.core" => "L",
        "spice.vsrc" => "V",
        "spice.isrc" => "I",
        "spice.diode" => "D",
        "spice.mosfet" => "M",
        "spice.vdmos" => "M",
        "spice.bjt" => "Q",
        "spice.jfet" => "J",
        "spice.mesfet" => "Z",
        "spice.switch" => "S",
        "spice.cswitch" => "W",
        "spice.vcvs" => "E",
        "spice.vccs" => "G",
        "spice.subckt" => "X",
        "spice.osdi" => "X",
        _ => "U"
    };

    private static string InferSymbolStyle(string kindId) => kindId switch
    {
        "spice.diode" => "diode",
        "spice.mosfet" => "nmos",
        "spice.vdmos" => "vdmos",
        "spice.bjt" => "npn",
        "spice.jfet" => "jfet",
        "spice.mesfet" => "mesfet",
        "spice.switch" => "switch",
        "spice.cswitch" => "cswitch",
        "spice.core" => "core",
        "spice.subckt" => "ic_box",
        "spice.osdi" => "ic_box",
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
        // ==================== FOUNDATION DIODES (6) ====================
        new CustomComponentDefinition(
            Id: "foundation.diode.1n4148",
            Name: "1N4148 Fast Diode",
            Category: "Foundation Diodes",
            Description: "Standard small-signal high-speed silicon switching diode (100V, RS=0.568 Ohm).",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "diode",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 2.52e-9, ["n_emission"] = 1.752, ["rs_ohms"] = 0.568, ["bv_volts"] = 100.0, ["cjo_farads"] = 4e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "D1N4148",
                ".MODEL D1N4148 D(IS=2.52e-9 N=1.752 RS=0.568 BV=100 IBV=100u CJO=4p VJ=0.6 M=0.333 TT=5.76n)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.1n4007",
            Name: "1N4007 Power Rectifier",
            Category: "Foundation Diodes",
            Description: "1000V 1A general-purpose silicon rectifier diode.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "diode",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 7.02e-9, ["n_emission"] = 1.77, ["rs_ohms"] = 0.042, ["bv_volts"] = 1000.0, ["cjo_farads"] = 26e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "D1N4007",
                ".MODEL D1N4007 D(IS=7.02e-9 N=1.77 RS=0.042 BV=1000 IBV=5u CJO=26p VJ=0.7 M=0.333 TT=4.32u)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.1n5819",
            Name: "1N5819 Schottky Diode",
            Category: "Foundation Diodes",
            Description: "40V 1A low forward-voltage Schottky barrier rectifier diode.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "schottky",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 2.2e-5, ["n_emission"] = 1.05, ["rs_ohms"] = 0.045, ["bv_volts"] = 40.0, ["cjo_farads"] = 110e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "D1N5819",
                ".MODEL D1N5819 D(IS=2.2e-5 N=1.05 RS=0.045 BV=40 IBV=1m CJO=110p VJ=0.4 M=0.45 EG=0.69 XTI=2)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.bzx84c5v1",
            Name: "BZX84C5V1 5.1V Zener",
            Category: "Foundation Diodes",
            Description: "5.1V precision Zener voltage-regulator diode with reverse breakdown and TBV1 tempco.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "zener",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 1e-12, ["n_emission"] = 1.05, ["rs_ohms"] = 4.0, ["bv_volts"] = 5.1, ["ibv_amps"] = 5e-3, ["cjo_farads"] = 120e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "BZX84C5V1",
                ".MODEL BZX84C5V1 D(IS=1e-12 N=1.05 RS=4.0 BV=5.1 IBV=5m NBV=1.0 TBV1=5e-4 CJO=120p VJ=0.75 M=0.33)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.bzx84c3v3",
            Name: "BZX84C3V3 3.3V Zener",
            Category: "Foundation Diodes",
            Description: "3.3V low-voltage Zener diode with negative breakdown tempco.",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "zener",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 1e-12, ["n_emission"] = 1.05, ["rs_ohms"] = 8.0, ["bv_volts"] = 3.3, ["ibv_amps"] = 5e-3, ["cjo_farads"] = 180e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "BZX84C3V3",
                ".MODEL BZX84C3V3 D(IS=1e-12 N=1.05 RS=8.0 BV=3.3 IBV=5m NBV=1.1 TBV1=-4e-4 CJO=180p VJ=0.75 M=0.33)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.diode.led_red",
            Name: "LED Red 1.8V",
            Category: "Foundation Diodes",
            Description: "Standard red GaAsP indicator LED (Vf ~ 1.8V at 10 mA).",
            BaseDeviceKindId: "spice.diode",
            SpicePrefix: "D",
            SymbolStyle: "led",
            Pins: [new CustomPinDefinition("p", -36, 0, "anode"), new CustomPinDefinition("n", 36, 0, "cathode")],
            DefaultParameters: new Dictionary<string, double> { ["is_amps"] = 1e-20, ["n_emission"] = 1.85, ["rs_ohms"] = 4.0, ["bv_volts"] = 5.0, ["cjo_farads"] = 15e-12 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "LED_RED",
                ".MODEL LED_RED D(IS=1e-20 N=1.85 RS=4.0 BV=5.0 IBV=10u CJO=15p EG=1.9 XTI=3)"),
            IsBuiltIn: true),

        // ==================== FOUNDATION BJTs (7) ====================
        new CustomComponentDefinition(
            Id: "foundation.bjt.2n2222",
            Name: "2N2222 NPN BJT",
            Category: "Foundation Transistors",
            Description: "General-purpose NPN Gummel-Poon BJT with full RB/RBM/IRB/RC/RE parasitics and transit time.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "npn",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 255.9, ["is_amps"] = 1.434e-14, ["rb_ohms"] = 10.0, ["rc_ohms"] = 1.0, ["re_ohms"] = 0.2 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "Q2N2222",
                ".MODEL Q2N2222 NPN(IS=1.434e-14 BF=255.9 BR=6.092 VAF=74.03 IKF=0.2847 RB=10 RBM=2 IRB=1e-4 RC=1.0 RE=0.2 CJE=22p VJE=0.75 MJE=0.377 CJC=7.3p VJC=0.75 MJC=0.341 XCJC=0.5 TF=0.411n XTF=3 VTF=1.7 ITF=0.6 TR=46.9n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.2n3904",
            Name: "2N3904 NPN BJT",
            Category: "Foundation Transistors",
            Description: "40V 200mA low-noise general-purpose NPN small-signal transistor.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "npn",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 300.0, ["is_amps"] = 6.734e-15, ["rb_ohms"] = 10.0, ["rc_ohms"] = 1.0, ["re_ohms"] = 0.25 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "Q2N3904",
                ".MODEL Q2N3904 NPN(IS=6.734e-15 BF=300 BR=0.737 VAF=74.03 IKF=0.0668 RB=10 RC=1.0 RE=0.25 CJE=4.49p MJE=0.259 CJC=3.64p MJC=0.308 TF=0.301n TR=239.5n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.2n3906",
            Name: "2N3906 PNP BJT",
            Category: "Foundation Transistors",
            Description: "Complementary 40V 200mA PNP Gummel-Poon small-signal transistor.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "pnp",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 180.7, ["is_amps"] = 1.41e-15, ["rb_ohms"] = 10.0, ["rc_ohms"] = 1.2, ["re_ohms"] = 0.3 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "Q2N3906",
                ".MODEL Q2N3906 PNP(IS=1.41e-15 BF=180.7 BR=4.977 VAF=18.7 IKF=0.08 RB=10 RC=1.2 RE=0.3 CJE=8.06p CJC=5.82p TF=0.55n TR=100n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.bc547b",
            Name: "BC547B NPN BJT",
            Category: "Foundation Transistors",
            Description: "High-gain low-noise European NPN audio & signal transistor (hFE ~ 330).",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "npn",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 330.0, ["is_amps"] = 2.39e-14, ["rb_ohms"] = 15.0, ["rc_ohms"] = 0.8, ["re_ohms"] = 0.3 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "QBC547B",
                ".MODEL QBC547B NPN(IS=2.39e-14 BF=330 BR=5.0 VAF=63.2 IKF=0.14 RB=15 RC=0.8 RE=0.3 CJE=11.5p CJC=4.5p TF=0.45n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.bc557b",
            Name: "BC557B PNP BJT",
            Category: "Foundation Transistors",
            Description: "Complementary high-gain low-noise PNP signal transistor (hFE ~ 300).",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "pnp",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 300.0, ["is_amps"] = 1.1e-14, ["rb_ohms"] = 15.0, ["rc_ohms"] = 1.0, ["re_ohms"] = 0.35 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "QBC557B",
                ".MODEL QBC557B PNP(IS=1.1e-14 BF=300 BR=4.5 VAF=37.0 IKF=0.10 RB=15 RC=1.0 RE=0.35 CJE=12.0p CJC=6.0p TF=0.52n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.tip31c",
            Name: "TIP31C Power NPN BJT",
            Category: "Foundation Transistors",
            Description: "100V 3A medium-power NPN silicon transistor for linear/audio output stages.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "npn",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 75.0, ["is_amps"] = 1.0e-12, ["rb_ohms"] = 2.0, ["rc_ohms"] = 0.15, ["re_ohms"] = 0.05 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "QTIP31C",
                ".MODEL QTIP31C NPN(IS=1.0e-12 BF=75 BR=4.0 VAF=100 IKF=1.5 RB=2.0 RBM=0.5 IRB=0.05 RC=0.15 RE=0.05 CJE=120p CJC=60p TF=15n TR=200n XTB=1.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.bjt.tip32c",
            Name: "TIP32C Power PNP BJT",
            Category: "Foundation Transistors",
            Description: "Complementary 100V 3A medium-power PNP silicon transistor.",
            BaseDeviceKindId: "spice.bjt",
            SpicePrefix: "Q",
            SymbolStyle: "pnp",
            Pins: [new CustomPinDefinition("c", 20, -20, "collector"), new CustomPinDefinition("b", -40, 0, "base"), new CustomPinDefinition("e", 20, 20, "emitter")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["bf"] = 70.0, ["is_amps"] = 1.0e-12, ["rb_ohms"] = 2.5, ["rc_ohms"] = 0.18, ["re_ohms"] = 0.06 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "QTIP32C",
                ".MODEL QTIP32C PNP(IS=1.0e-12 BF=70 BR=4.0 VAF=85 IKF=1.4 RB=2.5 RBM=0.6 IRB=0.05 RC=0.18 RE=0.06 CJE=140p CJC=70p TF=18n TR=220n XTB=1.5)"),
            IsBuiltIn: true),

        // ==================== FOUNDATION MOSFETs & POWER VDMOS (6) ====================
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
            Id: "foundation.vdmos.2n7002",
            Name: "2N7002 N-Ch VDMOS",
            Category: "Foundation Transistors",
            Description: "60V 115mA small-signal N-channel enhancement VDMOS with integrated body diode.",
            BaseDeviceKindId: "spice.vdmos",
            SpicePrefix: "M",
            SymbolStyle: "vdmos",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["m_mult"] = 1.0, ["vto_volts"] = 2.1, ["kp_a_per_v2"] = 0.28, ["rd_ohms"] = 1.2, ["rs_ohms"] = 0.4, ["rg_ohms"] = 5.0 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "M2N7002",
                ".MODEL M2N7002 VDMOS(VTO=2.1 KP=0.28 LAMBDA=0.015 RD=1.2 RS=0.4 RG=5.0 CGS=25p CGDMAX=18p CGDMIN=3p A=0.5 CJO=15p IS=1e-11 BV=60 IBV=250u)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.vdmos.irf540n",
            Name: "IRF540N Power VDMOS",
            Category: "Foundation Transistors",
            Description: "100V 33A 44mOhm N-channel Power VDMOS with nonlinear Miller Cgd and avalanche body diode.",
            BaseDeviceKindId: "spice.vdmos",
            SpicePrefix: "M",
            SymbolStyle: "vdmos",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["m_mult"] = 1.0, ["vto_volts"] = 3.2, ["kp_a_per_v2"] = 24.0, ["rd_ohms"] = 0.032, ["rs_ohms"] = 0.012, ["rg_ohms"] = 2.2 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "IRF540N",
                ".MODEL IRF540N VDMOS(VTO=3.2 KP=24.0 LAMBDA=0.008 THETA=0.04 RD=0.032 RS=0.012 RG=2.2 RB=0.04 CGS=1.6n CGDMAX=1.2n CGDMIN=80p A=0.6 CJO=550p IS=2e-10 N=1.05 TT=115n BV=100 IBV=250u)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.vdmos.irf9540n",
            Name: "IRF9540N P-Ch Power VDMOS",
            Category: "Foundation Transistors",
            Description: "Complementary -100V -23A P-channel Power VDMOS with Miller Cgd and body diode.",
            BaseDeviceKindId: "spice.vdmos",
            SpicePrefix: "M",
            SymbolStyle: "pvdmos",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["m_mult"] = 1.0, ["vto_volts"] = -3.4, ["kp_a_per_v2"] = 12.0, ["rd_ohms"] = 0.085, ["rs_ohms"] = 0.025, ["rg_ohms"] = 2.8 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "IRF9540N",
                ".MODEL IRF9540N VDMOS(PCHAN VTO=-3.4 KP=12.0 LAMBDA=0.01 THETA=0.05 RD=0.085 RS=0.025 RG=2.8 RB=0.06 CGS=1.4n CGDMAX=1.0n CGDMIN=75p A=0.6 CJO=480p IS=2e-10 N=1.08 TT=130n BV=100 IBV=250u)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.vdmos.bss84",
            Name: "BSS84 P-Ch VDMOS",
            Category: "Foundation Transistors",
            Description: "-50V -130mA small-signal P-channel enhancement VDMOS.",
            BaseDeviceKindId: "spice.vdmos",
            SpicePrefix: "M",
            SymbolStyle: "pvdmos",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["m_mult"] = 1.0, ["vto_volts"] = -1.7, ["kp_a_per_v2"] = 0.16, ["rd_ohms"] = 3.5, ["rs_ohms"] = 1.0, ["rg_ohms"] = 6.0 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "MBSS84",
                ".MODEL MBSS84 VDMOS(PCHAN VTO=-1.7 KP=0.16 LAMBDA=0.02 RD=3.5 RS=1.0 RG=6.0 CGS=30p CGDMAX=20p CGDMIN=4p A=0.5 CJO=18p IS=1e-11 BV=50 IBV=100u)"),
            IsBuiltIn: true),

        // ==================== FOUNDATION JFETs & RF MESFET (4) ====================
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
                ".MODEL J2N5457 NJF(VTO=-1.8 BETA=1.125m LAMBDA=2.3m CGS=4.6p CGD=4.0p PB=1.0)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.jfet.j201",
            Name: "J201 Low-Noise N-JFET",
            Category: "Foundation Transistors",
            Description: "Ultra-low-pinchoff high-sensitivity audio N-channel JFET (Vto=-0.6V).",
            BaseDeviceKindId: "spice.jfet",
            SpicePrefix: "J",
            SymbolStyle: "jfet",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["vto_volts"] = -0.6, ["beta_a_per_v2"] = 1.62e-3 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "JJ201",
                ".MODEL JJ201 NJF(VTO=-0.6 BETA=1.62m LAMBDA=2.2m CGS=3.0p CGD=4.7p PB=0.5)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.jfet.2n5460",
            Name: "2N5460 P-JFET",
            Category: "Foundation Transistors",
            Description: "General-purpose P-channel amplifier and analog switch JFET.",
            BaseDeviceKindId: "spice.jfet",
            SpicePrefix: "J",
            SymbolStyle: "jfet",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["vto_volts"] = -2.2, ["beta_a_per_v2"] = 0.65e-3 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "J2N5460",
                ".MODEL J2N5460 PJF(VTO=-2.2 BETA=0.65m LAMBDA=3.5m CGS=5.0p CGD=4.2p PB=1.0)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.mesfet.ne71000",
            Name: "NE71000 GaAs RF MESFET",
            Category: "Foundation Transistors",
            Description: "Low-noise X/Ku-band GaAs Statz MESFET (NMF LEVEL=1) with Schottky gate.",
            BaseDeviceKindId: "spice.mesfet",
            SpicePrefix: "Z",
            SymbolStyle: "mesfet",
            Pins: [new CustomPinDefinition("d", 20, -20, "drain"), new CustomPinDefinition("g", -40, 0, "gate"), new CustomPinDefinition("s", 20, 20, "source")],
            DefaultParameters: new Dictionary<string, double> { ["area"] = 1.0, ["level"] = 1.0, ["vto_volts"] = -1.4, ["beta_a_per_v2"] = 0.045 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "NE71000",
                ".MODEL NE71000 NMF(LEVEL=1 VTO=-1.4 BETA=0.045 B=0.3 ALPHA=3.5 LAMBDA=0.04 RD=2.5 RS=2.0 CGS=0.32p CGD=0.045p PB=0.8 IS=1e-13)"),
            IsBuiltIn: true),

        // ==================== FOUNDATION SWITCHES & MAGNETICS (4) ====================
        new CustomComponentDefinition(
            Id: "foundation.switch.sw_ideal",
            Name: "Voltage Switch (SW)",
            Category: "Foundation Switches & Magnetics",
            Description: "Smooth C1 voltage-controlled analog switch (VT=2.5V, VH=0.2V, RON=1 Ohm, ROFF=10 MOhm).",
            BaseDeviceKindId: "spice.switch",
            SpicePrefix: "S",
            SymbolStyle: "switch",
            Pins:
            [
                new CustomPinDefinition("p", 40, -20, "passive"),
                new CustomPinDefinition("n", 40, 20, "passive"),
                new CustomPinDefinition("cp", -40, -20, "ctrl_pos"),
                new CustomPinDefinition("cn", -40, 20, "ctrl_neg")
            ],
            DefaultParameters: new Dictionary<string, double> { ["vt_volts"] = 2.5, ["vh_volts"] = 0.2, ["ron_ohms"] = 1.0, ["roff_ohms"] = 1e7, ["hysteresis"] = 0.0 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "SW_IDEAL",
                ".MODEL SW_IDEAL SW(VT=2.5 VH=0.2 RON=1.0 ROFF=10Meg)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.switch.sw_hyst",
            Name: "Hysteretic Switch (Schmitt)",
            Category: "Foundation Switches & Magnetics",
            Description: "True two-branch hysteretic voltage switch (HYST=1, VT=2.5V, VH=0.5V).",
            BaseDeviceKindId: "spice.switch",
            SpicePrefix: "S",
            SymbolStyle: "switch",
            Pins:
            [
                new CustomPinDefinition("p", 40, -20, "passive"),
                new CustomPinDefinition("n", 40, 20, "passive"),
                new CustomPinDefinition("cp", -40, -20, "ctrl_pos"),
                new CustomPinDefinition("cn", -40, 20, "ctrl_neg")
            ],
            DefaultParameters: new Dictionary<string, double> { ["vt_volts"] = 2.5, ["vh_volts"] = 0.5, ["ron_ohms"] = 1.0, ["roff_ohms"] = 1e7, ["hysteresis"] = 1.0 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "SW_HYST",
                ".MODEL SW_HYST SW(VT=2.5 VH=0.5 RON=1.0 ROFF=10Meg HYST=1)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.cswitch.csw_ideal",
            Name: "Current Switch (CSW)",
            Category: "Foundation Switches & Magnetics",
            Description: "Current-controlled switch sensing a voltage source branch current (IT=5mA, IH=1mA).",
            BaseDeviceKindId: "spice.cswitch",
            SpicePrefix: "W",
            SymbolStyle: "cswitch",
            Pins: [new CustomPinDefinition("p", -36, 0, "passive"), new CustomPinDefinition("n", 36, 0, "passive")],
            DefaultParameters: new Dictionary<string, double> { ["it_amps"] = 5e-3, ["ih_amps"] = 1e-3, ["ron_ohms"] = 1.0, ["roff_ohms"] = 1e7, ["hysteresis"] = 0.0 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "CSW_IDEAL",
                ".MODEL CSW_IDEAL CSW(IT=5m IH=1m RON=1.0 ROFF=10Meg)"),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.core.ferrite_ja",
            Name: "Ferrite Saturable Core",
            Category: "Foundation Switches & Magnetics",
            Description: "Jiles-Atherton saturable MnZn ferrite magnetic core inductor (L+CORE with 0.2mm air gap).",
            BaseDeviceKindId: "spice.core",
            SpicePrefix: "L",
            SymbolStyle: "core",
            Pins: [new CustomPinDefinition("p", -36, 0, "passive"), new CustomPinDefinition("n", 36, 0, "passive")],
            DefaultParameters: new Dictionary<string, double> { ["turns"] = 24.0, ["area_m2"] = 1.2e-4, ["path_m"] = 0.05, ["gap_m"] = 2e-4, ["r_winding_ohms"] = 0.15, ["l_leak_henries"] = 2e-6 },
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.SpiceModelCard,
                "FERRITE_3C90",
                ".MODEL FERRITE_3C90 CORE(MS=3.8e5 A=65 K=35 C=0.22 ALPHA=1e-4 AREA=1.2e-4 PATH=0.05 GAP=2e-4 RW=0.15 LLEAK=2u)"),
            IsBuiltIn: true),

        // ==================== FOUNDATION SUBCIRCUITS (7) ====================
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
            Id: "foundation.subckt.tl072",
            Name: "TL072 JFET Op-Amp",
            Category: "Foundation Subcircuits",
            Description: "High-input-impedance (1 TOhm) JFET-input audio operational amplifier macromodel (GBW=3MHz).",
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
                "TL072_MACRO",
                """
                .SUBCKT TL072_MACRO inp inn out
                  Rin inp inn 1e12
                  Cin inp inn 3p
                  E1 int 0 inp inn 200000
                  Rp int pole 10k
                  Cp pole 0 1.061u
                  Ebuf drv 0 pole 0 1.0
                  Rout drv out 125
                .ENDS TL072_MACRO
                """),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.lm358",
            Name: "LM358 Op-Amp (Macro)",
            Category: "Foundation Subcircuits",
            Description: "Single-supply general-purpose dual op-amp macromodel (Aol=100dB, GBW=1MHz).",
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
                "LM358_MACRO",
                """
                .SUBCKT LM358_MACRO inp inn out
                  Rin inp inn 2Meg
                  E1 int 0 inp inn 100000
                  Rp int pole 10k
                  Cp pole 0 1.5915u
                  Ebuf drv 0 pole 0 1.0
                  Rout drv out 150
                .ENDS LM358_MACRO
                """),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.comparator",
            Name: "Analog Comparator",
            Category: "Foundation Subcircuits",
            Description: "Fast rail-to-rail analog comparator macromodel using smooth Slice A SW switching.",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "opamp",
            Pins:
            [
                new CustomPinDefinition("inp", -40, -20, "input_pos"),
                new CustomPinDefinition("inn", -40, 20, "input_neg"),
                new CustomPinDefinition("vcc", 0, -20, "power"),
                new CustomPinDefinition("out", 40, 0, "output")
            ],
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                "ANALOG_COMP",
                """
                .SUBCKT ANALOG_COMP inp inn vcc out
                  .MODEL COMP_SW_HI SW(VT=0.0 VH=0.005 RON=25 ROFF=10Meg)
                  .MODEL COMP_SW_LO SW(VT=0.0 VH=0.005 RON=10Meg ROFF=25)
                  Rin inp inn 10Meg
                  Shi vcc out inp inn COMP_SW_HI
                  Slo out 0 inp inn COMP_SW_LO
                .ENDS ANALOG_COMP
                """),
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.reg_7805",
            Name: "7805 5V Regulator (Macro)",
            Category: "Foundation Subcircuits",
            Description: "Fixed 5.0V linear voltage regulator macromodel with Zener reference and emitter-follower output.",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "ic_box",
            Pins:
            [
                new CustomPinDefinition("vin", -40, 0, "input"),
                new CustomPinDefinition("gnd", 0, 20, "ground"),
                new CustomPinDefinition("vout", 40, 0, "output")
            ],
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                "REG_7805",
                """
                .SUBCKT REG_7805 vin gnd vout
                  .MODEL DZ5V0 D(IS=1e-12 N=1.05 RS=1.0 BV=5.0 IBV=5m)
                  Rbias vin ref 470
                  Dz gnd ref DZ5V0
                  Ereg drv gnd ref gnd 1.0
                  Rout drv vout 0.05
                .ENDS REG_7805
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
            IsBuiltIn: true),

        new CustomComponentDefinition(
            Id: "foundation.subckt.center_tap_xfmr",
            Name: "Center-Tapped Transformer",
            Category: "Foundation Subcircuits",
            Description: "1:1+1 coupled center-tapped power/audio transformer macromodel (k=0.995).",
            BaseDeviceKindId: "spice.subckt",
            SpicePrefix: "X",
            SymbolStyle: "ic_box",
            Pins:
            [
                new CustomPinDefinition("p1", -40, -20, "primary_pos"),
                new CustomPinDefinition("p2", -40, 20, "primary_neg"),
                new CustomPinDefinition("s1", 40, -20, "sec_top"),
                new CustomPinDefinition("ct", 40, 0, "sec_ct"),
                new CustomPinDefinition("s2", 40, 20, "sec_bot")
            ],
            DefaultParameters: new Dictionary<string, double>(),
            AttachedModel: new AttachedModelDefinition(
                AttachedModelKind.Subcircuit,
                "CENTER_TAP_XFMR",
                """
                .SUBCKT CENTER_TAP_XFMR p1 p2 s1 ct s2
                  Rp p1 p1i 0.5
                  Lp p1i p2 10m
                  Ls1 s1 ct 10m
                  Ls2 ct s2 10m
                  K1 Lp Ls1 0.995
                  K2 Lp Ls2 0.995
                  K3 Ls1 Ls2 0.995
                .ENDS CENTER_TAP_XFMR
                """),
            IsBuiltIn: true)
    ];
}
