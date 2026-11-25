namespace Spice.Ir;

public sealed record ValidationDiagnostic(
    string Code,
    string Message,
    string? SubjectId = null);

/// <summary>
/// Single fail-closed Unified Device Descriptor registry shared across
/// Spice.Schematic, ERC, StarLark-cs authoring, SpiceNetlistEmitter,
/// XyceBackend, and NativeCppBackend (ARCH-SCHEM-001).
/// </summary>
public sealed class DeviceRegistry
{
    private readonly Dictionary<string, DeviceDescriptor> _descriptors;

    public static DeviceRegistry Default { get; } = CreateDefault();

    public DeviceRegistry(IEnumerable<DeviceDescriptor> descriptors)
    {
        _descriptors = new Dictionary<string, DeviceDescriptor>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            if (!_descriptors.TryAdd(d.KindId, d))
            {
                throw new ArgumentException($"Duplicate device descriptor '{d.KindId}'.", nameof(descriptors));
            }
        }
    }

    public IReadOnlyCollection<DeviceDescriptor> All => _descriptors.Values;

    public bool TryGet(string kindId, out DeviceDescriptor? descriptor) =>
        _descriptors.TryGetValue(kindId, out descriptor);

    public IReadOnlyList<ValidationDiagnostic> ValidateInstance(DeviceInstanceIr instance)
    {
        var errors = new List<ValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(instance.Id))
        {
            errors.Add(new ValidationDiagnostic("invalid_id", "Device instance ID must be non-empty."));
            return errors;
        }

        if (!_descriptors.TryGetValue(instance.KindId, out var desc))
        {
            errors.Add(new ValidationDiagnostic(
                "unknown_device_kind",
                $"Device '{instance.Id}' references unregistered KindId '{instance.KindId}'.",
                instance.Id));
            return errors;
        }

        // Validate pins fail-closed (except variable-terminal OSDI and Subcircuit devices).
        var isVariableTerminal =
            string.Equals(desc.KindId, "spice.osdi", StringComparison.Ordinal) ||
            string.Equals(desc.KindId, "spice.subckt", StringComparison.Ordinal);
        if (!isVariableTerminal)
        {
            var expectedPins = new HashSet<string>(desc.Pins.Select(p => p.Name), StringComparer.Ordinal);
            foreach (var pin in desc.Pins)
            {
                if (!instance.PinConnections.TryGetValue(pin.Name, out var net) || string.IsNullOrWhiteSpace(net))
                {
                    errors.Add(new ValidationDiagnostic(
                        "missing_pin",
                        $"Device '{instance.Id}' ({desc.KindId}) is missing required pin '{pin.Name}'.",
                        instance.Id));
                }
            }
            foreach (var pinName in instance.PinConnections.Keys)
            {
                if (!expectedPins.Contains(pinName))
                {
                    errors.Add(new ValidationDiagnostic(
                        "unknown_pin",
                        $"Device '{instance.Id}' ({desc.KindId}) specifies unknown pin '{pinName}'.",
                        instance.Id));
                }
            }
        }
        else
        {
            if (instance.PinConnections.Count == 0)
            {
                errors.Add(new ValidationDiagnostic(
                    "missing_pin",
                    $"Device '{instance.Id}' ({desc.KindId}) must connect at least one terminal.",
                    instance.Id));
            }
            if (string.Equals(desc.KindId, "spice.subckt", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(instance.ModelName))
            {
                errors.Add(new ValidationDiagnostic(
                    "missing_subckt_model",
                    $"Subcircuit instance '{instance.Id}' must specify a subcircuit ModelName.",
                    instance.Id));
            }
        }

        // Validate parameters fail-closed (no unknown params, all required present, all finite).
        var paramMeta = desc.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var p in desc.Parameters)
        {
            if (p.Required && !instance.Parameters.ContainsKey(p.Name))
            {
                errors.Add(new ValidationDiagnostic(
                    "missing_parameter",
                    $"Device '{instance.Id}' ({desc.KindId}) is missing required parameter '{p.Name}'.",
                    instance.Id));
            }
        }

        foreach (var (paramName, value) in instance.Parameters)
        {
            if (!double.IsFinite(value))
            {
                errors.Add(new ValidationDiagnostic(
                    "non_finite_parameter",
                    $"Device '{instance.Id}' parameter '{paramName}' must be finite.",
                    instance.Id));
            }
            if (!isVariableTerminal &&
                !paramMeta.ContainsKey(paramName))
            {
                errors.Add(new ValidationDiagnostic(
                    "unknown_parameter",
                    $"Device '{instance.Id}' ({desc.KindId}) specifies unknown parameter '{paramName}'.",
                    instance.Id));
            }
        }

        return errors;
    }

    public IReadOnlyList<ValidationDiagnostic> ValidateCircuit(CircuitIr circuit)
    {
        var errors = new List<ValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(circuit.Name))
        {
            errors.Add(new ValidationDiagnostic("invalid_circuit_name", "Circuit name must be non-empty."));
        }
        if (!circuit.Nodes.Contains("0", StringComparer.Ordinal))
        {
            errors.Add(new ValidationDiagnostic("missing_ground", "Circuit must include ground reference node '0'."));
        }

        var nodeSet = new HashSet<string>(circuit.Nodes, StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in circuit.Devices)
        {
            if (!seenIds.Add(dev.Id))
            {
                errors.Add(new ValidationDiagnostic(
                    "duplicate_device_id",
                    $"Duplicate device instance ID '{dev.Id}'.",
                    dev.Id));
            }

            errors.AddRange(ValidateInstance(dev));

            foreach (var (pin, net) in dev.PinConnections)
            {
                if (!string.IsNullOrWhiteSpace(net) && !nodeSet.Contains(net))
                {
                    errors.Add(new ValidationDiagnostic(
                        "undeclared_node",
                        $"Device '{dev.Id}' pin '{pin}' connects to undeclared node '{net}'.",
                        dev.Id));
                }
            }
        }

        return errors;
    }

    private static DeviceRegistry CreateDefault() => new([
        new DeviceDescriptor(
            "spice.resistor", "Resistor",
            [new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive")],
            [new ParameterDescriptor("ohms", "Ohm", Required: true)],
            "sym.resistor"),
        new DeviceDescriptor(
            "spice.capacitor", "Capacitor",
            [new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive")],
            [new ParameterDescriptor("farads", "F", Required: true), new ParameterDescriptor("ic_volts", "V")],
            "sym.capacitor"),
        new DeviceDescriptor(
            "spice.inductor", "Inductor",
            [new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive")],
            [new ParameterDescriptor("henries", "H", Required: true), new ParameterDescriptor("ic_amps", "A")],
            "sym.inductor"),
        new DeviceDescriptor(
            "spice.vsrc", "Voltage Source",
            [new PinDescriptor("p", "positive"), new PinDescriptor("n", "negative")],
            [
                new ParameterDescriptor("dc_volts", "V", DefaultValue: 0.0),
                new ParameterDescriptor("ac_mag", "V", DefaultValue: 0.0),
                new ParameterDescriptor("ac_phase_deg", "deg", DefaultValue: 0.0),
                new ParameterDescriptor("sin_offset", "V"),
                new ParameterDescriptor("sin_amp", "V"),
                new ParameterDescriptor("sin_freq_hz", "Hz"),
                new ParameterDescriptor("pulse_v1", "V"),
                new ParameterDescriptor("pulse_v2", "V"),
                new ParameterDescriptor("pulse_td", "s"),
                new ParameterDescriptor("pulse_tr", "s"),
                new ParameterDescriptor("pulse_tf", "s"),
                new ParameterDescriptor("pulse_pw", "s"),
                new ParameterDescriptor("pulse_per", "s")
            ],
            "sym.vsrc"),
        new DeviceDescriptor(
            "spice.isrc", "Current Source",
            [new PinDescriptor("p", "positive"), new PinDescriptor("n", "negative")],
            [
                new ParameterDescriptor("dc_amps", "A", DefaultValue: 0.0),
                new ParameterDescriptor("ac_mag", "A", DefaultValue: 0.0),
                new ParameterDescriptor("ac_phase_deg", "deg", DefaultValue: 0.0)
            ],
            "sym.isrc"),
        new DeviceDescriptor(
            "spice.vccs", "Voltage-Controlled Current Source",
            [
                new PinDescriptor("p", "output_pos"), new PinDescriptor("n", "output_neg"),
                new PinDescriptor("cp", "ctrl_pos"), new PinDescriptor("cn", "ctrl_neg")
            ],
            [new ParameterDescriptor("siemens", "S", Required: true)],
            "sym.vccs"),
        new DeviceDescriptor(
            "spice.vcvs", "Voltage-Controlled Voltage Source",
            [
                new PinDescriptor("p", "output_pos"), new PinDescriptor("n", "output_neg"),
                new PinDescriptor("cp", "ctrl_pos"), new PinDescriptor("cn", "ctrl_neg")
            ],
            [new ParameterDescriptor("gain", "V/V", Required: true)],
            "sym.vcvs"),
        new DeviceDescriptor(
            "spice.diode", "SPICE Diode",
            [new PinDescriptor("p", "anode"), new PinDescriptor("n", "cathode")],
            [
                new ParameterDescriptor("is_amps", "A", DefaultValue: 1e-14),
                new ParameterDescriptor("n_emission", "", DefaultValue: 1.0),
                new ParameterDescriptor("rs_ohms", "Ohm", DefaultValue: 0.0),
                new ParameterDescriptor("bv_volts", "V", DefaultValue: 0.0),
                new ParameterDescriptor("ibv_amps", "A", DefaultValue: 1e-3),
                new ParameterDescriptor("cjo_farads", "F", DefaultValue: 0.0),
                new ParameterDescriptor("vj_volts", "V", DefaultValue: 1.0),
                new ParameterDescriptor("m_grading", "", DefaultValue: 0.5),
                new ParameterDescriptor("tt_s", "s", DefaultValue: 0.0),
                new ParameterDescriptor("area", "", DefaultValue: 1.0),
                new ParameterDescriptor("m_mult", "", DefaultValue: 1.0)
            ],
            "sym.diode"),
        new DeviceDescriptor(
            "spice.mosfet", "MOSFET (Level 1 / BSIM3 / BSIM4)",
            [
                new PinDescriptor("d", "drain"), new PinDescriptor("g", "gate"),
                new PinDescriptor("s", "source"), new PinDescriptor("b", "bulk")
            ],
            [
                new ParameterDescriptor("w_m", "m", DefaultValue: 1e-6),
                new ParameterDescriptor("l_m", "m", DefaultValue: 1e-6),
                new ParameterDescriptor("level", "", DefaultValue: 54.0),
                new ParameterDescriptor("vth0_volts", "V", DefaultValue: 0.4)
            ],
            "sym.mosfet"),
        new DeviceDescriptor(
            "spice.vdmos", "Power VDMOS Transistor",
            [new PinDescriptor("d", "drain"), new PinDescriptor("g", "gate"), new PinDescriptor("s", "source")],
            [
                new ParameterDescriptor("m_mult", "", DefaultValue: 1.0),
                new ParameterDescriptor("vto_volts", "V", DefaultValue: 3.0),
                new ParameterDescriptor("kp_a_per_v2", "A/V^2", DefaultValue: 10.0),
                new ParameterDescriptor("rd_ohms", "Ohm", DefaultValue: 0.05),
                new ParameterDescriptor("rs_ohms", "Ohm", DefaultValue: 0.02),
                new ParameterDescriptor("rg_ohms", "Ohm", DefaultValue: 2.0)
            ],
            "sym.vdmos"),
        new DeviceDescriptor(
            "spice.bjt", "Gummel-Poon BJT",
            [new PinDescriptor("c", "collector"), new PinDescriptor("b", "base"), new PinDescriptor("e", "emitter")],
            [
                new ParameterDescriptor("area", "", DefaultValue: 1.0),
                new ParameterDescriptor("m_mult", "", DefaultValue: 1.0),
                new ParameterDescriptor("bf", "", DefaultValue: 100.0),
                new ParameterDescriptor("is_amps", "A", DefaultValue: 1e-16),
                new ParameterDescriptor("rb_ohms", "Ohm", DefaultValue: 0.0),
                new ParameterDescriptor("rc_ohms", "Ohm", DefaultValue: 0.0),
                new ParameterDescriptor("re_ohms", "Ohm", DefaultValue: 0.0)
            ],
            "sym.bjt"),
        new DeviceDescriptor(
            "spice.jfet", "Shichman-Hodges JFET",
            [new PinDescriptor("d", "drain"), new PinDescriptor("g", "gate"), new PinDescriptor("s", "source")],
            [
                new ParameterDescriptor("area", "", DefaultValue: 1.0),
                new ParameterDescriptor("vto_volts", "V", DefaultValue: -2.0),
                new ParameterDescriptor("beta_a_per_v2", "A/V^2", DefaultValue: 1e-4)
            ],
            "sym.jfet"),
        new DeviceDescriptor(
            "spice.mesfet", "GaAs MESFET (Statz / Curtice)",
            [new PinDescriptor("d", "drain"), new PinDescriptor("g", "gate"), new PinDescriptor("s", "source")],
            [
                new ParameterDescriptor("area", "", DefaultValue: 1.0),
                new ParameterDescriptor("m_mult", "", DefaultValue: 1.0),
                new ParameterDescriptor("level", "", DefaultValue: 1.0),
                new ParameterDescriptor("vto_volts", "V", DefaultValue: -1.5),
                new ParameterDescriptor("beta_a_per_v2", "A/V^2", DefaultValue: 0.05)
            ],
            "sym.mesfet"),
        new DeviceDescriptor(
            "spice.switch", "Voltage-Controlled Switch (SW)",
            [
                new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive"),
                new PinDescriptor("cp", "ctrl_pos"), new PinDescriptor("cn", "ctrl_neg")
            ],
            [
                new ParameterDescriptor("vt_volts", "V", DefaultValue: 2.5),
                new ParameterDescriptor("vh_volts", "V", DefaultValue: 0.2),
                new ParameterDescriptor("ron_ohms", "Ohm", DefaultValue: 10.0),
                new ParameterDescriptor("roff_ohms", "Ohm", DefaultValue: 1e6),
                new ParameterDescriptor("hysteresis", "", DefaultValue: 0.0)
            ],
            "sym.switch"),
        new DeviceDescriptor(
            "spice.cswitch", "Current-Controlled Switch (CSW)",
            [new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive")],
            [
                new ParameterDescriptor("it_amps", "A", DefaultValue: 5e-3),
                new ParameterDescriptor("ih_amps", "A", DefaultValue: 1e-3),
                new ParameterDescriptor("ron_ohms", "Ohm", DefaultValue: 10.0),
                new ParameterDescriptor("roff_ohms", "Ohm", DefaultValue: 1e6),
                new ParameterDescriptor("hysteresis", "", DefaultValue: 0.0)
            ],
            "sym.cswitch"),
        new DeviceDescriptor(
            "spice.core", "Saturable Magnetic Core (Jiles-Atherton / Chan)",
            [new PinDescriptor("p", "passive"), new PinDescriptor("n", "passive")],
            [
                new ParameterDescriptor("turns", "", DefaultValue: 20.0),
                new ParameterDescriptor("area_m2", "m^2", DefaultValue: 1e-4),
                new ParameterDescriptor("path_m", "m", DefaultValue: 0.05),
                new ParameterDescriptor("gap_m", "m", DefaultValue: 0.0),
                new ParameterDescriptor("r_winding_ohms", "Ohm", DefaultValue: 0.1),
                new ParameterDescriptor("l_leak_henries", "H", DefaultValue: 0.0),
                new ParameterDescriptor("ic_amps", "A")
            ],
            "sym.core"),
        new DeviceDescriptor(
            "spice.subckt", "Custom Subcircuit / Macro (.SUBCKT)",
            [],
            [],
            "sym.subckt"),
        new DeviceDescriptor(
            "spice.osdi", "OSDI Compiled Verilog-A Model",
            [],
            [],
            "sym.osdi")
    ]);
}
