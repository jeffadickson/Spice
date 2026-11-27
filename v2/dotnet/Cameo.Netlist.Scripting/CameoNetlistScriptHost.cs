using System.Globalization;
using System.Text;
using Cameo;
using Cameo.Runtime;
using Cameo.Runtime.Interop;

namespace Cameo.Netlist.Scripting;

/// <summary>
/// Options controlling hermetic Cameo script evaluation for flagship netlist generation.
/// All execution runs strictly outside any simulator hot loop (design/elaboration time only).
/// </summary>
public sealed record CameoNetlistScriptOptions(
    long MaxSteps = 2_000_000,
    int MaxScriptBytes = 2 * 1024 * 1024,
    bool TypeCheck = true,
    string FileName = "design.cameo",
    IReadOnlyDictionary<string, double>? NumericGlobals = null,
    IReadOnlyDictionary<string, string>? StringGlobals = null,
    Func<string, NetlistPartDefaults?>? LibraryResolver = null);

/// <summary>
/// Deterministic result of evaluating a .cameo netlist script into a canonical,
/// dual-hashed FlagshipNetlistDocument (spice.netlist.v3).
/// </summary>
public sealed record CameoScriptEvaluationResult(
    bool Succeeded,
    FlagshipNetlistDocument? Document,
    string? FormattedSpicenet,
    IReadOnlyList<FlagshipDiagnostic> Diagnostics,
    string Stdout);

/// <summary>
/// Hermetic Cameo (.cameo) scripting engine for the Cameo.Netlist flagship format.
/// Executes statically type-checked Cameo scripts with Cameo.Units (SharpUnits ISO 80000
/// physical units), Cameo.SafeNumerics, FilePolicy.DenyAll, and deterministic step budgets
/// to programmatically emit immutable FlagshipNetlistDocument instances.
/// </summary>
public static class CameoNetlistScriptHost
{
    /// <summary>
    /// Evaluates a .cameo script hermetically and returns a canonical, dual-hashed
    /// FlagshipNetlistDocument + formatted .spicenet text, or structured diagnostics on failure.
    /// Never throws for script syntax, type, runtime, or step-budget failures.
    /// </summary>
    public static CameoScriptEvaluationResult Evaluate(
        string scriptSource,
        CameoNetlistScriptOptions? options = null,
        Action<CameoEngine, NetlistScriptBuilder>? configureEngine = null)
    {
        var opts = options ?? new CameoNetlistScriptOptions();
        var diags = new List<FlagshipDiagnostic>();

        if (string.IsNullOrWhiteSpace(scriptSource))
        {
            diags.Add(new FlagshipDiagnostic("error", "empty_cameo_script", "Cameo netlist script is empty."));
            return new CameoScriptEvaluationResult(false, null, null, diags, "");
        }
        if (Encoding.UTF8.GetByteCount(scriptSource) > opts.MaxScriptBytes)
        {
            diags.Add(new FlagshipDiagnostic("error", "cameo_script_too_large", $"Cameo script exceeds bounded limit of {opts.MaxScriptBytes} bytes."));
            return new CameoScriptEvaluationResult(false, null, null, diags, "");
        }
        if (opts.MaxSteps <= 0)
        {
            diags.Add(new FlagshipDiagnostic("error", "invalid_step_budget", "MaxSteps must be strictly positive."));
            return new CameoScriptEvaluationResult(false, null, null, diags, "");
        }

        using var stdoutWriter = new StringWriter(CultureInfo.InvariantCulture);
        using var engine = CameoEngine.CreateHermetic(maxSteps: opts.MaxSteps, typeCheck: opts.TypeCheck);
        engine.State.Stdout = stdoutWriter;

        var builder = new NetlistScriptBuilder(diags, opts.LibraryResolver);
        engine.Expose("net", builder);
        configureEngine?.Invoke(engine, builder);

        if (opts.NumericGlobals is not null)
        {
            foreach (var (k, v) in opts.NumericGlobals.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                engine.DefineValue(k, v);
            }
        }
        if (opts.StringGlobals is not null)
        {
            foreach (var (k, v) in opts.StringGlobals.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                engine.DefineValue(k, v);
            }
        }

        var run = engine.TryRunString(scriptSource, opts.FileName);
        var stdout = stdoutWriter.ToString();

        switch (run.Value)
        {
            case RunCompleted:
                break;

            case CompileFailed cf:
                foreach (var d in cf.Diagnostics)
                {
                    diags.Add(new FlagshipDiagnostic("error", "cameo_compile_error", d.ToString()));
                }
                return new CameoScriptEvaluationResult(false, null, null, diags, stdout);

            case RuntimeFailed rf:
                diags.Add(new FlagshipDiagnostic("error", "cameo_runtime_error", rf.Message));
                return new CameoScriptEvaluationResult(false, null, null, diags, stdout);

            case RunAborted ra:
                diags.Add(new FlagshipDiagnostic("error", "cameo_aborted", $"Cameo script aborted: {ra.Reason}."));
                return new CameoScriptEvaluationResult(false, null, null, diags, stdout);

            case RunExited re:
                if (re.Code != 0)
                {
                    diags.Add(new FlagshipDiagnostic("error", "cameo_exited", $"Cameo script exited with code {re.Code}."));
                    return new CameoScriptEvaluationResult(false, null, null, diags, stdout);
                }
                break;
        }

        var doc = builder.BuildSnapshot();
        if (doc is null || diags.Any(d => d.Severity == "error"))
        {
            return new CameoScriptEvaluationResult(false, null, null, diags, stdout);
        }

        // Verify hierarchical elaboration fails closed before accepting the generated netlist.
        var elab = FlagshipNetlist.Elaborate(doc, opts.LibraryResolver);
        if (!elab.Succeeded)
        {
            diags.AddRange(elab.Diagnostics);
            return new CameoScriptEvaluationResult(false, null, null, diags, stdout);
        }

        var formatted = FlagshipNetlist.Format(doc);
        return new CameoScriptEvaluationResult(true, doc, formatted, diags, stdout);
    }

    /// <summary>
    /// Heuristically detects whether a text payload is a Cameo (.cameo) netlist script
    /// rather than declarative .spicenet, polyglot SVG, or JSON.
    /// </summary>
    public static bool LooksLikeCameoNetlistScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith('<')) return false;
        if (trimmed.StartsWith("netlist ", StringComparison.OrdinalIgnoreCase)) return false;
        return text.Contains("net.", StringComparison.Ordinal) ||
               trimmed.StartsWith("--", StringComparison.Ordinal) ||
               trimmed.StartsWith("fn ", StringComparison.Ordinal) ||
               trimmed.StartsWith("let ", StringComparison.Ordinal) ||
               trimmed.StartsWith("record ", StringComparison.Ordinal) ||
               trimmed.StartsWith("enum ", StringComparison.Ordinal);
    }
}

/// <summary>
/// Typed .NET host object exposed to .cameo netlist scripts as the global <c>net</c> object.
/// Only methods decorated with <see cref="ExposeAttribute"/> are visible to scripts and
/// automatically registered with Cameo's static type checker.
/// </summary>
public sealed class NetlistScriptBuilder
{
    private sealed class MutableModule
    {
        public string Name;
        public Dictionary<string, double> DefaultParameters = new(StringComparer.Ordinal);
        public List<FlagshipPortDef> Ports = [];
        public List<FlagshipPartDef> Parts = [];
        public List<FlagshipUseDef> Uses = [];
        public Dictionary<string, (bool IsGround, List<NetlistPinRef> Pins)> Nets = new(StringComparer.OrdinalIgnoreCase);
        public List<FlagshipChainDef> Chains = [];
        public List<string> Directives = [];

        public MutableModule(string name) => Name = name;

        public FlagshipModuleDef Freeze() => new(
            Name: Name,
            DefaultParameters: new Dictionary<string, double>(DefaultParameters, StringComparer.Ordinal),
            Ports: Ports.ToList(),
            Parts: Parts.ToList(),
            Uses: Uses.ToList(),
            Nets: Nets.Select(kv => new FlagshipNetDef(kv.Value.IsGround ? "GND" : kv.Key, kv.Value.Pins.ToList(), kv.Value.IsGround)).ToList(),
            Chains: Chains.ToList(),
            Directives: Directives.ToList());
    }

    private sealed class MutableRtl
    {
        public string Name;
        public string Format;
        public string? Clock;
        public List<FlagshipPortDef> Ports = [];
        public List<FlagshipMixedBridge> Bridges = [];
        public string? RawRtlJson;

        public MutableRtl(string name, string format, string? clock)
        {
            Name = name;
            Format = format;
            Clock = clock;
        }

        public FlagshipRtlBlock Freeze() => new(Name, Format, Clock, Ports.ToList(), Bridges.ToList(), RawRtlJson);
    }

    private readonly List<FlagshipDiagnostic> _diags;
    private readonly Func<string, NetlistPartDefaults?>? _libraryResolver;
    private string _designName = "untitled_cameo_design";
    private readonly MutableModule _circuit = new("__circuit__");
    private readonly List<MutableModule> _modules = [];
    private MutableModule? _activeModule;
    private readonly List<MutableRtl> _rtlBlocks = [];
    private MutableRtl? _activeRtl;
    private readonly List<NetlistModelDef> _models = [];
    private readonly List<FlagshipPlacement> _placements = [];
    private double _gridPitch = 20.0;
    private string _gridStyle = "lines";
    private bool _hasLayout;
    private double? _pcbWidthMm;
    private double? _pcbHeightMm;
    private int? _pcbLayers;
    private readonly List<FlagshipPcbRule> _pcbRules = [];
    private readonly List<FlagshipVerifyBlock> _verifications = [];

    internal NetlistScriptBuilder(
        List<FlagshipDiagnostic> diags,
        Func<string, NetlistPartDefaults?>? libraryResolver)
    {
        _diags = diags;
        _libraryResolver = libraryResolver;
    }

    private MutableModule TargetScope => _activeModule ?? _circuit;

    // -------------------- Design & Module Hierarchy --------------------

    [Expose("design")]
    public void Design(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _diags.Add(new FlagshipDiagnostic("error", "invalid_design_name", "net.design(name) requires a non-empty name."));
            return;
        }
        _designName = name.Trim();
    }

    [Expose("module")]
    public void BeginModule(string name)
    {
        if (_activeModule is not null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "nested_module_block", $"Cannot open module '{name}' while module '{_activeModule.Name}' is still open; call net.end_module() first."));
            return;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            _diags.Add(new FlagshipDiagnostic("error", "invalid_module_name", "net.module(name) requires a non-empty module name."));
            return;
        }
        var mod = new MutableModule(name.Trim());
        _modules.Add(mod);
        _activeModule = mod;
    }

    [Expose("end_module")]
    public void EndModule()
    {
        if (_activeModule is null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "unmatched_end_module", "net.end_module() called with no open module."));
            return;
        }
        _activeModule = null;
    }

    [Expose("param")]
    public void Param(string name, double defaultValue)
    {
        if (!double.IsFinite(defaultValue))
        {
            _diags.Add(new FlagshipDiagnostic("error", "non_finite_param", $"Parameter '{name}' must be finite."));
            return;
        }
        TargetScope.DefaultParameters[name.Trim()] = defaultValue;
    }

    [Expose("port")]
    public void Port(string name, string direction = "inout", int width = 1)
    {
        TargetScope.Ports.Add(new FlagshipPortDef(name.Trim(), string.IsNullOrWhiteSpace(direction) ? "inout" : direction.Trim(), Math.Max(1, width)));
    }

    [Expose("use")]
    public void UseModule(string instanceId, string moduleName, Dictionary<string, double>? args = null)
    {
        TargetScope.Uses.Add(new FlagshipUseDef(
            instanceId.Trim(),
            moduleName.Trim(),
            args is not null ? new Dictionary<string, double>(args, StringComparer.Ordinal) : new Dictionary<string, double>(StringComparer.Ordinal)));
    }

    [Expose("use_expr")]
    public void UseModuleExpr(string instanceId, string moduleName, Dictionary<string, string> argSpecs)
    {
        var numeric = new Dictionary<string, double>(StringComparer.Ordinal);
        var exprs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, raw) in argSpecs)
        {
            var v = raw.Trim();
            if (v.StartsWith('$'))
            {
                exprs[k] = v;
            }
            else if (FlagshipNetlist.TryParseQuantity(v, out var num))
            {
                numeric[k] = num;
            }
            else
            {
                _diags.Add(new FlagshipDiagnostic("error", "invalid_use_arg", $"Invalid argument '{k}={raw}' on module instance '{instanceId}'."));
            }
        }
        TargetScope.Uses.Add(new FlagshipUseDef(instanceId.Trim(), moduleName.Trim(), numeric, exprs.Count > 0 ? exprs : null));
    }

    // -------------------- General & Typed Part Constructors --------------------

    [Expose("part")]
    public void Part(string id, string kindId, string attributes = "")
    {
        var numeric = new Dictionary<string, double>(StringComparer.Ordinal);
        var exprs = new Dictionary<string, string>(StringComparer.Ordinal);
        string? model = null, lib = null, ctrl = null, fp = null, mpn = null, supplier = null, valueLabel = null;

        if (!string.IsNullOrWhiteSpace(attributes))
        {
            foreach (var (k, v) in ParseAttributePairs(attributes))
            {
                switch (k.ToLowerInvariant())
                {
                    case "model": model = v; break;
                    case "lib": lib = v; break;
                    case "ctrl": ctrl = v; break;
                    case "footprint": fp = v; break;
                    case "mpn": mpn = v; break;
                    case "supplier": supplier = v; break;
                    case "value": valueLabel = v; break;
                    default:
                        if (v.StartsWith('$'))
                        {
                            exprs[k] = v;
                        }
                        else if (FlagshipNetlist.TryParseQuantity(v, out var d))
                        {
                            numeric[k] = d;
                        }
                        else
                        {
                            _diags.Add(new FlagshipDiagnostic("error", "invalid_part_attr", $"Invalid numeric/SI attribute '{k}={v}' on part '{id}'.", SubjectId: id));
                        }
                        break;
                }
            }
        }

        TargetScope.Parts.Add(new FlagshipPartDef(
            Id: id.Trim(),
            KindId: kindId.Trim(),
            Parameters: numeric,
            ModelName: model,
            LibraryPartId: lib,
            ControlSourceId: ctrl,
            Footprint: fp,
            Mpn: mpn,
            SupplierPartNumber: supplier,
            DisplayValue: valueLabel,
            ParameterExpressions: exprs.Count > 0 ? exprs : null));
    }

    [Expose("resistor")]
    public void Resistor(string id, string nodeP, string nodeN, double ohms, string footprint = "")
    {
        AddTwoPinPart(id, "spice.resistor", nodeP, nodeN, new Dictionary<string, double> { ["ohms"] = ohms }, footprint: footprint);
    }

    [Expose("capacitor")]
    public void Capacitor(string id, string nodeP, string nodeN, double farads, string footprint = "")
    {
        AddTwoPinPart(id, "spice.capacitor", nodeP, nodeN, new Dictionary<string, double> { ["farads"] = farads }, footprint: footprint);
    }

    [Expose("inductor")]
    public void Inductor(string id, string nodeP, string nodeN, double henries, string footprint = "")
    {
        AddTwoPinPart(id, "spice.inductor", nodeP, nodeN, new Dictionary<string, double> { ["henries"] = henries }, footprint: footprint);
    }

    [Expose("vsrc")]
    public void VoltageSource(string id, string nodeP, string nodeN, double dcVolts, double acMag = 0.0)
    {
        var p = new Dictionary<string, double> { ["dc_volts"] = dcVolts };
        if (acMag != 0.0) p["ac_mag"] = acMag;
        AddTwoPinPart(id, "spice.vsrc", nodeP, nodeN, p);
    }

    [Expose("isrc")]
    public void CurrentSource(string id, string nodeP, string nodeN, double dcAmps, double acMag = 0.0)
    {
        var p = new Dictionary<string, double> { ["dc_amps"] = dcAmps };
        if (acMag != 0.0) p["ac_mag"] = acMag;
        AddTwoPinPart(id, "spice.isrc", nodeP, nodeN, p);
    }

    [Expose("diode")]
    public void Diode(string id, string anode, string cathode, string modelName = "")
    {
        AddTwoPinPart(id, "spice.diode", anode, cathode, new Dictionary<string, double>(), modelName: string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim());
    }

    [Expose("nmos")]
    public void Nmos(string id, string drain, string gate, string source, string bulk, double wMeters = 1e-6, double lMeters = 65e-9, string modelName = "")
    {
        var idTrim = id.Trim();
        TargetScope.Parts.Add(new FlagshipPartDef(
            Id: idTrim,
            KindId: "spice.mosfet",
            Parameters: new Dictionary<string, double> { ["w_m"] = wMeters, ["l_m"] = lMeters, ["vth0_volts"] = 0.4 },
            ModelName: string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim()));
        ConnectPinInternal(drain, idTrim, "d");
        ConnectPinInternal(gate, idTrim, "g");
        ConnectPinInternal(source, idTrim, "s");
        ConnectPinInternal(bulk, idTrim, "b");
    }

    [Expose("pmos")]
    public void Pmos(string id, string drain, string gate, string source, string bulk, double wMeters = 2e-6, double lMeters = 65e-9, string modelName = "")
    {
        var idTrim = id.Trim();
        TargetScope.Parts.Add(new FlagshipPartDef(
            Id: idTrim,
            KindId: "spice.mosfet",
            Parameters: new Dictionary<string, double> { ["w_m"] = wMeters, ["l_m"] = lMeters, ["vth0_volts"] = -0.4 },
            ModelName: string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim()));
        ConnectPinInternal(drain, idTrim, "d");
        ConnectPinInternal(gate, idTrim, "g");
        ConnectPinInternal(source, idTrim, "s");
        ConnectPinInternal(bulk, idTrim, "b");
    }

    [Expose("bjt")]
    public void Bjt(string id, string collector, string baseNet, string emitter, string modelName = "", double area = 1.0)
    {
        var idTrim = id.Trim();
        TargetScope.Parts.Add(new FlagshipPartDef(
            Id: idTrim,
            KindId: "spice.bjt",
            Parameters: new Dictionary<string, double> { ["area"] = area },
            ModelName: string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim()));
        ConnectPinInternal(collector, idTrim, "c");
        ConnectPinInternal(baseNet, idTrim, "b");
        ConnectPinInternal(emitter, idTrim, "e");
    }

    // -------------------- Connectivity & Chains --------------------

    [Expose("connect")]
    public void Connect(string netName, string instancePin)
    {
        var dot = instancePin.IndexOf('.');
        if (dot <= 0 || dot == instancePin.Length - 1)
        {
            _diags.Add(new FlagshipDiagnostic("error", "invalid_pin_ref", $"net.connect expects 'Instance.Pin', got '{instancePin}'."));
            return;
        }
        ConnectPinInternal(netName, instancePin[..dot].Trim(), instancePin[(dot + 1)..].Trim());
    }

    [Expose("wire")]
    public void Wire(string netName, string[] instancePins)
    {
        foreach (var pin in instancePins)
        {
            Connect(netName, pin);
        }
    }

    [Expose("ground")]
    public void Ground(string instancePin) => Connect("GND", instancePin);

    [Expose("chain")]
    public void Chain(string arrowSeparatedSteps)
    {
        var steps = arrowSeparatedSteps
            .Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (steps.Count < 2)
        {
            _diags.Add(new FlagshipDiagnostic("error", "short_chain", "net.chain requires at least two elements separated by '->'."));
            return;
        }
        TargetScope.Chains.Add(new FlagshipChainDef(steps));
    }

    // -------------------- Mixed-Signal VeloSim RTL & Bridges --------------------

    [Expose("rtl")]
    public void BeginRtl(string name, string clock = "", string format = "velosim.rtl.v1")
    {
        if (_activeRtl is not null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "nested_rtl_block", $"Cannot open rtl '{name}' while rtl '{_activeRtl.Name}' is still open; call net.end_rtl() first."));
            return;
        }
        var r = new MutableRtl(name.Trim(), string.IsNullOrWhiteSpace(format) ? "velosim.rtl.v1" : format.Trim(), string.IsNullOrWhiteSpace(clock) ? null : clock.Trim());
        _rtlBlocks.Add(r);
        _activeRtl = r;
    }

    [Expose("rtl_port")]
    public void RtlPort(string name, string direction = "input", int width = 1)
    {
        if (_activeRtl is null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "no_active_rtl", "net.rtl_port() requires an open net.rtl(...) block."));
            return;
        }
        _activeRtl.Ports.Add(new FlagshipPortDef(name.Trim(), string.IsNullOrWhiteSpace(direction) ? "input" : direction.Trim(), Math.Clamp(width, 1, 64)));
    }

    [Expose("bridge")]
    public void Bridge(
        string name,
        string kind,
        string digitalSignal,
        string analogNet,
        double vLow = 0.0,
        double vHigh = 3.3,
        double vThresh = 1.65,
        double vHyst = 0.1,
        double rOut = 50.0)
    {
        if (_activeRtl is null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "no_active_rtl", "net.bridge() requires an open net.rtl(...) block."));
            return;
        }
        _activeRtl.Bridges.Add(new FlagshipMixedBridge(
            name.Trim(),
            kind.Trim().ToLowerInvariant(),
            digitalSignal.Trim(),
            analogNet.Trim(),
            vLow,
            vHigh,
            vThresh,
            vHyst,
            rOut));
    }

    [Expose("rtl_json")]
    public void RtlJson(string rawJson)
    {
        if (_activeRtl is null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "no_active_rtl", "net.rtl_json() requires an open net.rtl(...) block."));
            return;
        }
        _activeRtl.RawRtlJson = rawJson;
    }

    [Expose("end_rtl")]
    public void EndRtl()
    {
        if (_activeRtl is null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "unmatched_end_rtl", "net.end_rtl() called with no open rtl block."));
            return;
        }
        _activeRtl = null;
    }

    // -------------------- Models, Layout, PCB, Directives, Utilities --------------------

    [Expose("model")]
    public void Model(string name, string spiceText, string kind = "SpiceModelCard", string osdiLibraryPath = "")
    {
        var parsedKind = Enum.TryParse<NetlistModelKind>(kind, ignoreCase: true, out var k)
            ? k
            : NetlistModelKind.SpiceModelCard;
        _models.Add(new NetlistModelDef(parsedKind, name.Trim(), spiceText.Trim(), string.IsNullOrWhiteSpace(osdiLibraryPath) ? null : osdiLibraryPath.Trim()));
    }

    [Expose("layout")]
    public void Layout(double gridPitch = 20.0, string gridStyle = "lines")
    {
        _gridPitch = gridPitch > 2.0 ? gridPitch : 20.0;
        _gridStyle = string.IsNullOrWhiteSpace(gridStyle) ? "lines" : gridStyle.Trim();
        _hasLayout = true;
    }

    [Expose("place")]
    public void Place(string instanceId, double x, double y, int rotationDegrees = 0, bool mirrorX = false, bool mirrorY = false)
    {
        _hasLayout = true;
        _placements.Add(new FlagshipPlacement(instanceId.Trim(), x, y, rotationDegrees, mirrorX, mirrorY));
    }

    [Expose("pcb_board")]
    public void PcbBoard(double widthMm, double heightMm, int layers = 2)
    {
        _pcbWidthMm = widthMm;
        _pcbHeightMm = heightMm;
        _pcbLayers = layers;
    }

    [Expose("pcb_rule")]
    public void PcbRule(string netName, double clearanceMm, double traceWidthMm = 0.0)
    {
        _pcbRules.Add(new FlagshipPcbRule(
            netName.Trim(),
            clearanceMm > 0.0 ? clearanceMm : null,
            traceWidthMm > 0.0 ? traceWidthMm : null));
    }

    [Expose("directive")]
    public void Directive(string spiceDirective)
    {
        if (!string.IsNullOrWhiteSpace(spiceDirective))
        {
            TargetScope.Directives.Add(spiceDirective.Trim());
        }
    }

    [Expose("verify")]
    public void Verify(string analysis, string assertion)
    {
        _verifications.Add(new FlagshipVerifyBlock(analysis.Trim(), [assertion.Trim()]));
    }

    [Expose("qty")]
    public double Qty(string siLiteral)
    {
        if (!FlagshipNetlist.TryParseQuantity(siLiteral, out var v))
        {
            throw new ArgumentException($"Invalid SI quantity literal '{siLiteral}'.");
        }
        return v;
    }

    [Expose("from_spicenet")]
    public void FromSpicenet(string spicenetText)
    {
        var parsed = FlagshipNetlist.Parse(spicenetText, _libraryResolver);
        if (!parsed.Succeeded || parsed.Document is null)
        {
            _diags.AddRange(parsed.Diagnostics);
            return;
        }
        var d = parsed.Document;
        _designName = d.Name;
        foreach (var m in d.Modules)
        {
            var mm = new MutableModule(m.Name);
            foreach (var kv in m.DefaultParameters) mm.DefaultParameters[kv.Key] = kv.Value;
            mm.Ports.AddRange(m.Ports);
            mm.Parts.AddRange(m.Parts);
            mm.Uses.AddRange(m.Uses);
            foreach (var n in m.Nets) mm.Nets[n.IsGround ? "GND" : n.Name] = (n.IsGround, n.Pins.ToList());
            mm.Chains.AddRange(m.Chains);
            mm.Directives.AddRange(m.Directives);
            _modules.Add(mm);
        }
        foreach (var kv in d.Circuit.DefaultParameters) _circuit.DefaultParameters[kv.Key] = kv.Value;
        _circuit.Ports.AddRange(d.Circuit.Ports);
        _circuit.Parts.AddRange(d.Circuit.Parts);
        _circuit.Uses.AddRange(d.Circuit.Uses);
        foreach (var n in d.Circuit.Nets) _circuit.Nets[n.IsGround ? "GND" : n.Name] = (n.IsGround, n.Pins.ToList());
        _circuit.Chains.AddRange(d.Circuit.Chains);
        _circuit.Directives.AddRange(d.Circuit.Directives);
        _models.AddRange(d.Models);
        foreach (var r in d.RtlBlocks)
        {
            var mr = new MutableRtl(r.Name, r.Format, r.Clock) { RawRtlJson = r.RawRtlJson };
            mr.Ports.AddRange(r.Ports);
            mr.Bridges.AddRange(r.Bridges);
            _rtlBlocks.Add(mr);
        }
    }

    // -------------------- Host Snapshot Finalizer --------------------

    /// <summary>
    /// Materializes a canonical, dual-hashed FlagshipNetlistDocument snapshot from the
    /// current builder state. Hidden from Cameo scripts via [Hide]; intended for host
    /// controllers such as Spice.Cameo's simulation driver.
    /// </summary>
    [Hide]
    public FlagshipNetlistDocument? BuildSnapshot()
    {
        if (_activeModule is not null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "unclosed_module", $"Module '{_activeModule.Name}' was never closed with net.end_module()."));
            return null;
        }
        if (_activeRtl is not null)
        {
            _diags.Add(new FlagshipDiagnostic("error", "unclosed_rtl", $"RTL block '{_activeRtl.Name}' was never closed with net.end_rtl()."));
            return null;
        }
        if (_circuit.Parts.Count == 0 && _circuit.Uses.Count == 0)
        {
            _diags.Add(new FlagshipDiagnostic("error", "empty_circuit", "Cameo netlist script produced no parts or module instances in the top-level circuit."));
            return null;
        }

        FlagshipLayout? layout = _hasLayout ? new FlagshipLayout(_gridPitch, _gridStyle, _placements.ToList()) : null;
        FlagshipPcbSection? pcb = (_pcbWidthMm.HasValue || _pcbHeightMm.HasValue || _pcbLayers.HasValue || _pcbRules.Count > 0)
            ? new FlagshipPcbSection(_pcbWidthMm, _pcbHeightMm, _pcbLayers, _pcbRules.Count > 0 ? _pcbRules.ToList() : null)
            : null;

        return FlagshipNetlist.WithHashes(new FlagshipNetlistDocument(
            Name: _designName,
            Circuit: _circuit.Freeze(),
            Modules: _modules.Select(m => m.Freeze()).ToList(),
            RtlBlocks: _rtlBlocks.Select(r => r.Freeze()).ToList(),
            Models: _models.ToList(),
            Layout: layout,
            Pcb: pcb,
            Verifications: _verifications.Count > 0 ? _verifications.ToList() : null));
    }

    private void AddTwoPinPart(
        string id,
        string kindId,
        string nodeP,
        string nodeN,
        Dictionary<string, double> parameters,
        string? modelName = null,
        string? footprint = null)
    {
        var idTrim = id.Trim();
        TargetScope.Parts.Add(new FlagshipPartDef(
            Id: idTrim,
            KindId: kindId,
            Parameters: parameters,
            ModelName: modelName,
            Footprint: string.IsNullOrWhiteSpace(footprint) ? null : footprint.Trim()));
        ConnectPinInternal(nodeP, idTrim, "p");
        ConnectPinInternal(nodeN, idTrim, "n");
    }

    private void ConnectPinInternal(string netName, string instanceId, string pinName)
    {
        var trimmed = netName.Trim();
        bool isGnd = string.Equals(trimmed, "0", StringComparison.Ordinal) ||
                     string.Equals(trimmed, "GND", StringComparison.OrdinalIgnoreCase);
        var key = isGnd ? "GND" : trimmed;
        if (!TargetScope.Nets.TryGetValue(key, out var bucket))
        {
            bucket = (isGnd, new List<NetlistPinRef>());
            TargetScope.Nets[key] = bucket;
        }
        var pinRef = new NetlistPinRef(instanceId, pinName);
        if (!bucket.Pins.Contains(pinRef)) bucket.Pins.Add(pinRef);
    }

    private static IEnumerable<(string Key, string Value)> ParseAttributePairs(string raw)
    {
        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && (char.IsWhiteSpace(raw[i]) || raw[i] == ',' || raw[i] == ';')) i++;
            if (i >= raw.Length) break;
            int kStart = i;
            while (i < raw.Length && raw[i] != '=' && !char.IsWhiteSpace(raw[i])) i++;
            var key = raw[kStart..i].Trim();
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length || raw[i] != '=') continue;
            i++;
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            string val;
            if (i < raw.Length && raw[i] == '"')
            {
                i++;
                int vStart = i;
                while (i < raw.Length && raw[i] != '"') i++;
                val = raw[vStart..i];
                if (i < raw.Length) i++;
            }
            else
            {
                int vStart = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i]) && raw[i] != ',' && raw[i] != ';') i++;
                val = raw[vStart..i];
            }
            if (!string.IsNullOrEmpty(key)) yield return (key, val);
        }
    }
}
