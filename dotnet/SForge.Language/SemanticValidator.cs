namespace SForge.Language;

/// <summary>Validates cross-declaration invariants before a future lowering phase creates simulator IR.</summary>
public static class SForgeSemanticValidator
{
    public static IReadOnlyList<Diagnostic> Validate(Document document)
    {
        var diagnostics = new List<Diagnostic>();
        var circuits = document.Declarations.OfType<Circuit>().ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var circuit in circuits.Values)
        {
            var nodes = new HashSet<string>(circuit.Nodes, StringComparer.Ordinal);
            if (!nodes.Contains("gnd")) diagnostics.Add(new(circuit.Span, "circuit must declare gnd"));
            foreach (var device in circuit.Devices)
            {
                if (!nodes.Contains(device.Positive) || !nodes.Contains(device.Negative)) diagnostics.Add(new(device.Span, "device terminal references an undeclared node"));
                if (device is Resistor resistor && resistor.Resistance.Unit is not "Ohm" and not "kOhm" and not "MOhm") diagnostics.Add(new(device.Span, "resistance requires an Ohm-family unit"));
                if (device is Capacitor capacitor && capacitor.Capacitance.Unit is not "F" and not "uF" and not "nF" and not "pF") diagnostics.Add(new(device.Span, "capacitance requires an F-family unit"));
                if (device is VoltageSource source && source.Dc.Unit is not "V" and not "mV") diagnostics.Add(new(device.Span, "source dc requires a voltage unit"));
            }
        }
        foreach (var run in document.Declarations.OfType<Run>())
        {
            if (!circuits.ContainsKey(run.Circuit)) diagnostics.Add(new(run.Span, "run references an undeclared circuit"));
            if (run.Analysis is Transient transient)
            {
                if (transient.Stop.Unit is not "s" and not "ms" and not "us") diagnostics.Add(new(transient.Span, "transient stop requires a time unit"));
                if (transient.MaxStep.Unit is not "s" and not "ms" and not "us") diagnostics.Add(new(transient.Span, "transient max_step requires a time unit"));
            }
        }
        return diagnostics;
    }
}
