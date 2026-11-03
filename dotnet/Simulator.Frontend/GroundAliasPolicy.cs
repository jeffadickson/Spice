namespace Simulator.Frontend;

/// <summary>Explicit ground-name semantics for selected ngspice preparation, never a source-text replacement rule.</summary>
public enum GroundAliasPolicy
{
    /// <summary>Use ordinary ngspice behavior, or PSpice behavior when an active VSWITCH requires that native compatibility mode.</summary>
    Automatic,
    /// <summary>Exact case-insensitive gnd node references become zero before port substitution.</summary>
    Ngspice,
    /// <summary>Normalize gnd except in a qualified subcircuit body whose header names gnd.</summary>
    Pspice,
    /// <summary>Match an externally set no_auto_gnd: gnd is an ordinary node, subject to explicit globals and ports.</summary>
    Disabled
}
