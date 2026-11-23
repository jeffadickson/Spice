using System.Globalization;
using System.Text;

namespace Spice.Ir;

/// <summary>
/// Deterministic Canonical IR to SPICE3f5 / Xyce netlist emitter shared by
/// XyceBackend, NativeCliBackend, and Spice.Schematic export (ARCH-API-001, ARCH-SCHEM-005).
/// </summary>
public static class SpiceNetlistEmitter
{
    private static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    public static string EmitNetlist(
        SimulationRequestIr request,
        bool includeXycePrintCsv = false,
        DeviceRegistry? registry = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var errors = reg.ValidateCircuit(request.Circuit);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot emit netlist for invalid CircuitIr: " +
                string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}")));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"* Spice V2 Canonical IR: {request.Circuit.Name}");

        var emittedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Circuit.Models is not null)
        {
            foreach (var m in request.Circuit.Models)
            {
                if (!emittedModels.Add(m.Name)) continue;
                if (!string.IsNullOrWhiteSpace(m.OsdiLibraryPath))
                {
                    sb.AppendLine($".OSDI \"{m.OsdiLibraryPath}\"");
                }
                if (!string.IsNullOrWhiteSpace(m.RawSpiceDefinition))
                {
                    sb.AppendLine(m.RawSpiceDefinition.Trim());
                    continue;
                }
                var kvs = string.Join(" ", m.Parameters.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
                if (!string.IsNullOrWhiteSpace(m.OsdiModelName))
                {
                    var prefix = $"model={m.OsdiModelName}";
                    kvs = string.IsNullOrWhiteSpace(kvs) ? prefix : $"{prefix} {kvs}";
                }
                sb.AppendLine($".MODEL {m.Name} {m.ModelType}({kvs})");
            }
        }

        foreach (var dev in request.Circuit.Devices)
        {
            switch (dev.KindId)
            {
                case "spice.resistor":
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'R')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {Fmt(dev.Parameters["ohms"])}");
                    break;
                case "spice.capacitor":
                {
                    var ic = dev.Parameters.TryGetValue("ic_volts", out var icv) ? $" IC={Fmt(icv)}" : string.Empty;
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'C')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {Fmt(dev.Parameters["farads"])}{ic}");
                    break;
                }
                case "spice.inductor":
                {
                    var ic = dev.Parameters.TryGetValue("ic_amps", out var ici) ? $" IC={Fmt(ici)}" : string.Empty;
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'L')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {Fmt(dev.Parameters["henries"])}{ic}");
                    break;
                }
                case "spice.vsrc":
                    sb.AppendLine(EmitSourceLine(EnsurePrefix(dev.Id, 'V'), dev, "dc_volts"));
                    break;
                case "spice.isrc":
                    sb.AppendLine(EmitSourceLine(EnsurePrefix(dev.Id, 'I'), dev, "dc_amps"));
                    break;
                case "spice.vccs":
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'G')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {dev.PinConnections["cp"]} {dev.PinConnections["cn"]} {Fmt(dev.Parameters["siemens"])}");
                    break;
                case "spice.vcvs":
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'E')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {dev.PinConnections["cp"]} {dev.PinConnections["cn"]} {Fmt(dev.Parameters["gain"])}");
                    break;
                case "spice.diode":
                {
                    var modelName = string.IsNullOrWhiteSpace(dev.ModelName) ? $"MOD_{dev.Id}" : dev.ModelName;
                    if (emittedModels.Add(modelName))
                    {
                        var isVal = dev.Parameters.GetValueOrDefault("is_amps", 1e-14);
                        var nVal = dev.Parameters.GetValueOrDefault("n_emission", 1.0);
                        var cjo = dev.Parameters.GetValueOrDefault("cjo_farads", 0.0);
                        var vj = dev.Parameters.GetValueOrDefault("vj_volts", 1.0);
                        var m = dev.Parameters.GetValueOrDefault("m_grading", 0.5);
                        var tt = dev.Parameters.GetValueOrDefault("tt_s", 0.0);
                        sb.AppendLine($".MODEL {modelName} D(IS={Fmt(isVal)} N={Fmt(nVal)} CJO={Fmt(cjo)} VJ={Fmt(vj)} M={Fmt(m)} TT={Fmt(tt)})");
                    }
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'D')} {dev.PinConnections["p"]} {dev.PinConnections["n"]} {modelName}");
                    break;
                }
                case "spice.mosfet":
                {
                    var modelName = string.IsNullOrWhiteSpace(dev.ModelName) ? $"MOS_{dev.Id}" : dev.ModelName;
                    if (emittedModels.Add(modelName))
                    {
                        var level = (int)Math.Round(dev.Parameters.GetValueOrDefault("level", 54.0));
                        var vth0 = dev.Parameters.GetValueOrDefault("vth0_volts", 0.4);
                        var mosType = vth0 < 0.0 ? "PMOS" : "NMOS";
                        sb.AppendLine($".MODEL {modelName} {mosType}(LEVEL={level} VTH0={Fmt(vth0)} TOXE=2.2n U0=420 VSAT=1.1e5)");
                    }
                    var w = dev.Parameters.GetValueOrDefault("w_m", 1e-6);
                    var l = dev.Parameters.GetValueOrDefault("l_m", 65e-9);
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'M')} {dev.PinConnections["d"]} {dev.PinConnections["g"]} {dev.PinConnections["s"]} {dev.PinConnections["b"]} {modelName} W={Fmt(w)} L={Fmt(l)}");
                    break;
                }
                case "spice.bjt":
                {
                    var modelName = string.IsNullOrWhiteSpace(dev.ModelName) ? $"BJT_{dev.Id}" : dev.ModelName;
                    if (emittedModels.Add(modelName))
                    {
                        var bf = dev.Parameters.GetValueOrDefault("bf", 100.0);
                        var isVal = dev.Parameters.GetValueOrDefault("is_amps", 1e-16);
                        sb.AppendLine($".MODEL {modelName} NPN(BF={Fmt(bf)} IS={Fmt(isVal)})");
                    }
                    var area = dev.Parameters.GetValueOrDefault("area", 1.0);
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'Q')} {dev.PinConnections["c"]} {dev.PinConnections["b"]} {dev.PinConnections["e"]} {modelName} {Fmt(area)}");
                    break;
                }
                case "spice.jfet":
                {
                    var modelName = string.IsNullOrWhiteSpace(dev.ModelName) ? $"JFET_{dev.Id}" : dev.ModelName;
                    if (emittedModels.Add(modelName))
                    {
                        var vto = dev.Parameters.GetValueOrDefault("vto_volts", -2.0);
                        var beta = dev.Parameters.GetValueOrDefault("beta_a_per_v2", 1e-4);
                        sb.AppendLine($".MODEL {modelName} NJF(VTO={Fmt(vto)} BETA={Fmt(beta)})");
                    }
                    var area = dev.Parameters.GetValueOrDefault("area", 1.0);
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'J')} {dev.PinConnections["d"]} {dev.PinConnections["g"]} {dev.PinConnections["s"]} {modelName} {Fmt(area)}");
                    break;
                }
                case "spice.subckt":
                {
                    var subName = dev.ModelName ?? throw new InvalidOperationException($"Subcircuit '{dev.Id}' missing ModelName.");
                    var orderedPins = dev.PinOrder is { Count: > 0 }
                        ? dev.PinOrder.Select(p => dev.PinConnections[p])
                        : dev.PinConnections.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);
                    var paramList = dev.Parameters.Count > 0
                        ? " " + string.Join(" ", dev.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={Fmt(kv.Value)}"))
                        : string.Empty;
                    sb.AppendLine($"{EnsurePrefix(dev.Id, 'X')} {string.Join(" ", orderedPins)} {subName}{paramList}");
                    break;
                }
                default:
                    throw new NotSupportedException($"Netlist emission does not support device kind '{dev.KindId}'.");
            }
        }

        if (request.Circuit.Directives is not null)
        {
            foreach (var dir in request.Circuit.Directives)
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    sb.AppendLine(dir.Trim());
                }
            }
        }

        var opts = request.Options ?? new Dictionary<string, double>();
        var probeNodes = request.Circuit.Nodes
            .Where(n => !string.Equals(n, "0", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        switch (request.Analysis)
        {
            case AnalysisKind.OperatingPoint:
                sb.AppendLine(".OP");
                if (includeXycePrintCsv && probeNodes.Count > 0)
                {
                    sb.AppendLine(".PRINT DC FORMAT=CSV " + string.Join(" ", probeNodes.Select(n => $"V({n})")));
                }
                break;
            case AnalysisKind.DcSweep:
            {
                var src = string.IsNullOrWhiteSpace(request.SweepSourceId) ? "V1" : request.SweepSourceId;
                var start = opts.GetValueOrDefault("start", 0.0);
                var stop = opts.GetValueOrDefault("stop", 1.0);
                var step = opts.GetValueOrDefault("step", 0.1);
                sb.AppendLine($".DC {src} {Fmt(start)} {Fmt(stop)} {Fmt(step)}");
                if (includeXycePrintCsv && probeNodes.Count > 0)
                {
                    sb.AppendLine(".PRINT DC FORMAT=CSV " + string.Join(" ", probeNodes.Select(n => $"V({n})")));
                }
                break;
            }
            case AnalysisKind.Ac:
            {
                var pts = (int)Math.Max(1, Math.Round(opts.GetValueOrDefault("points_per_decade", 10.0)));
                var fStart = opts.GetValueOrDefault("f_start_hz", 10.0);
                var fStop = opts.GetValueOrDefault("f_stop_hz", 1.0e6);
                sb.AppendLine($".AC DEC {pts} {Fmt(fStart)} {Fmt(fStop)}");
                if (includeXycePrintCsv && probeNodes.Count > 0)
                {
                    sb.AppendLine(".PRINT AC FORMAT=CSV " + string.Join(" ", probeNodes.Select(n => $"VR({n}) VI({n})")));
                }
                break;
            }
            case AnalysisKind.Transient:
            {
                var step = opts.GetValueOrDefault("step_s", 1.0e-6);
                var stop = opts.GetValueOrDefault("stop_s", 1.0e-3);
                var uic = opts.GetValueOrDefault("uic", 0.0) > 0.5 ? " UIC" : string.Empty;
                sb.AppendLine($".TRAN {Fmt(step)} {Fmt(stop)}{uic}");
                if (includeXycePrintCsv && probeNodes.Count > 0)
                {
                    sb.AppendLine(".PRINT TRAN FORMAT=CSV " + string.Join(" ", probeNodes.Select(n => $"V({n})")));
                }
                break;
            }
            default:
                throw new NotSupportedException($"Netlist emission does not yet support analysis '{request.Analysis}'.");
        }

        sb.AppendLine(".END");
        return sb.ToString();
    }

    private static string EnsurePrefix(string id, char prefix) =>
        id.Length > 0 && char.ToUpperInvariant(id[0]) == prefix ? id : $"{prefix}{id}";

    private static string EmitSourceLine(string name, DeviceInstanceIr dev, string dcParam)
    {
        var sb = new StringBuilder();
        sb.Append($"{name} {dev.PinConnections["p"]} {dev.PinConnections["n"]}");
        var dc = dev.Parameters.GetValueOrDefault(dcParam, 0.0);
        sb.Append($" DC {Fmt(dc)}");

        if (dev.Parameters.TryGetValue("ac_mag", out var acMag) && acMag != 0.0)
        {
            var acPhase = dev.Parameters.GetValueOrDefault("ac_phase_deg", 0.0);
            sb.Append($" AC {Fmt(acMag)} {Fmt(acPhase)}");
        }

        if (dev.Parameters.TryGetValue("sin_freq_hz", out var sinFreq) && sinFreq > 0.0)
        {
            var off = dev.Parameters.GetValueOrDefault("sin_offset", 0.0);
            var amp = dev.Parameters.GetValueOrDefault("sin_amp", 1.0);
            sb.Append($" SIN({Fmt(off)} {Fmt(amp)} {Fmt(sinFreq)})");
        }
        else if (dev.Parameters.TryGetValue("pulse_v2", out var pv2))
        {
            var pv1 = dev.Parameters.GetValueOrDefault("pulse_v1", 0.0);
            var td = dev.Parameters.GetValueOrDefault("pulse_td", 0.0);
            var tr = dev.Parameters.GetValueOrDefault("pulse_tr", 1e-9);
            var tf = dev.Parameters.GetValueOrDefault("pulse_tf", 1e-9);
            var pw = dev.Parameters.GetValueOrDefault("pulse_pw", 1e-3);
            var per = dev.Parameters.GetValueOrDefault("pulse_per", 2e-3);
            sb.Append($" PULSE({Fmt(pv1)} {Fmt(pv2)} {Fmt(td)} {Fmt(tr)} {Fmt(tf)} {Fmt(pw)} {Fmt(per)})");
        }

        return sb.ToString();
    }
}
