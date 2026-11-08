using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
/// <summary>Generated two-terminal primitive metadata, retaining unevaluated source syntax.</summary>
public sealed record PrimitiveSyntax(string Name,string Family,int ScopeId,SourceSyntax P,SourceSyntax N,SourceSyntax? Value,bool Supported,SourceRange Range)
{
    /// <summary>Separately generated DC/AC clauses; null means outside this bounded excitation profile.</summary>
    public SourceExcitationSyntax? Excitation { get; init; }
    /// <summary>Generated resistor scalar or model candidate and assignment tail; scalar/parameter evaluation still has precedence.</summary>
    public ResistorModelSyntax? ResistorModel {get;init;}
    /// <summary>Unevaluated AC-only resistance in ohms for the single-assignment scalar profile.</summary>
    public SourceSyntax? AcResistance {get;init;}
    /// <summary>A leading reserved DC/AC token must satisfy the bounded clause grammar rather than become a same-named scalar parameter.</summary>
    public bool RequiresExcitationProfile { get; init; }
    /// <summary>Typed differential control terminals for a qualified linear E/G source.</summary>
    public VoltageControlSyntax? Control { get; init; }
    /// <summary>Original same-instance voltage-branch reference of an F/H source.</summary>
    public SourceSyntax? CurrentReference { get; init; }
}
/// <summary>Control voltage is V(P)-V(N); output polarity remains the primitive P-to-N convention.</summary>
public sealed record VoltageControlSyntax(SourceSyntax P,SourceSyntax N);
/// <summary>Unevaluated SI DC and AC amplitude/degree-phase fields. A null DC field in an AC-only header denotes the specified zero DC default.</summary>
public sealed record SourceExcitationSyntax(SourceSyntax? Dc,SourceSyntax? AcMagnitude,SourceSyntax? AcPhase)
{
    /// <summary>Generated positional waveform fields; timing defaults belong to an explicit transient analysis.</summary>
    public SourceWaveformSyntax? Waveform {get;init;}
}
/// <summary>Recognized source families; recognizing fields is separate from numerical admission.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SourceWaveformKind>))]
public enum SourceWaveformKind
{
    /// <summary>Finite-edge periodic pulse.</summary>
    Pulse,
    /// <summary>Possibly delayed/damped sinusoid with degree phase syntax.</summary>
    Sine,
    /// <summary>Piecewise-linear time/value pairs.</summary>
    Pwl,
    /// <summary>Exponential rise and return.</summary>
    Exponential,
    /// <summary>Single-frequency frequency modulation, with degree phase syntax.</summary>
    FrequencyModulated,
    /// <summary>Amplitude modulation, with degree phase syntax.</summary>
    AmplitudeModulated
}
/// <summary>Unevaluated ordered scalar fields from the generated waveform rule.</summary>
public sealed record SourceWaveformSyntax(SourceWaveformKind Kind,ImmutableArray<SourceSyntax> Values);
/// <summary>Every generated statement's lexical owner, for explicit active-capability checks.</summary>
public sealed record ScopedCard(int ScopeId,string Kind,SourceRange Range)
{
    /// <summary>Original generated leading-token spelling; composite lexical headers may span more than one word.</summary>
    public string Name { get; init; } = "";
    /// <summary>True only for a nonempty generated .option(s) tail composed entirely of the qualified reporting and raw-file encoding settings.</summary>
    public bool ReportingOptionsOnly { get; init; }
    /// <summary>Generated directive whose only physical setting explicitly selects the fixed 27 Celsius model temperature.</summary>
    public bool DefaultTemperature { get; init; }
    /// <summary>Generated legacy reporting width with bounded literal IN and OUT settings.</summary>
    public bool ReportingWidth { get; init; }
    /// <summary>Exact numerical settings of the pinned Berkeley FD static profile.</summary>
    public bool BsimSoiFdStaticOptions { get; init; }
    /// <summary>Wire method for a fully recognized METHOD/MAXORD card, or null.</summary>
    public string? TransientMethodOption { get; init; }
    /// <summary>Root scalar exports to the control namespace, separate from physical parameters.</summary>
    public ImmutableArray<ParameterSyntax> ControlConstants { get; init; } = [];
    /// <summary>Generated .global node list; null denotes another directive or an unsupported global header.</summary>
    public ImmutableArray<SourceSyntax>? GlobalNodes {get;init;}
}
internal static class PrimitiveExtraction
{
    internal static PrimitiveSyntax? Extract(ParserRuleContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture,Func<int,int,SourceSyntax> captureRange)
    {
        SpiceParser.TwoNodesContext nodes;SpiceParser.ParameterContext[] parameters;
        if(card is SpiceParser.PassiveContext passive){nodes=passive.twoNodes();parameters=passive.parameter();}
        else if(card is SpiceParser.SourceContext source){nodes=source.twoNodes();parameters=source.parameter();}
        else if(card is SpiceParser.IcontrolContext current)
        {
            nodes=current.twoNodes();var parser=SourceProfileExtraction.CreateParser(current.parameter());
            try{
                if(parser!=null){var profile=parser.currentControlProfile();return new(card.Start.Text,card.Start.Text[..1].ToUpperInvariant(),scope,capture(nodes.node(0)),capture(nodes.node(1)),capture(profile.value()),true,capture(card).Range){CurrentReference=capture(current.referenceName())};}
            }catch(Antlr4.Runtime.Misc.ParseCanceledException){ }
            return new(card.Start.Text,card.Start.Text[..1].ToUpperInvariant(),scope,capture(nodes.node(0)),capture(nodes.node(1)),null,false,capture(card).Range){CurrentReference=capture(current.referenceName())};
        }
        else if(card is SpiceParser.VcontrolContext control)
        {
            nodes=control.twoNodes();
            var parser=SourceProfileExtraction.CreateParser(control.parameter());
            try
            {
                if(control.tableTail()==null&&parser!=null)
                {
                    var profile=parser.linearControlProfile();
                    return new(card.Start.Text,card.Start.Text[..1].ToUpperInvariant(),scope,capture(nodes.node(0)),capture(nodes.node(1)),capture(profile.value()),true,capture(card).Range)
                    {Control=new(capture(profile.twoNodes().node(0)),capture(profile.twoNodes().node(1)))};
                }
            }
            catch(Antlr4.Runtime.Misc.ParseCanceledException){ }
            return new(card.Start.Text,card.Start.Text[..1].ToUpperInvariant(),scope,capture(nodes.node(0)),capture(nodes.node(1)),null,false,capture(card).Range);
        }
        else return null;
        var family=card.Start.Text[..1].ToUpperInvariant();
        // The general grammar represents "DC -2" as arithmetic (DC minus 2).
        // Interpret the source-only DC keyword using generated operator tokens;
        // retain the signed physical slice for the existing scalar evaluator.
        if(card is SpiceParser.SourceContext&&parameters.Length==1&&parameters[0].arithmetic() is {} arithmetic
            &&arithmetic.value()?.GetText().Equals("dc",StringComparison.OrdinalIgnoreCase)==true
            &&(arithmetic.PLUS()!=null||arithmetic.MINUS()!=null))
        {
            var sign=arithmetic.PLUS()??arithmetic.MINUS();
            return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),
                captureRange(sign.Symbol.StartIndex,arithmetic.Stop.StopIndex),true,capture(card).Range);
        }
        // Native inductors use zero current by default under UIC. Admit
        // its explicit spelling without dropping any nonzero device state.
        if(family=="L"&&parameters.Length==2&&parameters[0].value()!=null
            &&parameters[1].assignment() is {} initial
            &&initial.referenceName()?.GetText().Equals("ic",StringComparison.OrdinalIgnoreCase)==true
            &&initial.value()?.GetText()=="0")
            return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),capture(parameters[0]),true,capture(card).Range);

        var valueIndex=card is SpiceParser.SourceContext&&parameters.Length>0&&parameters[0].GetText().Equals("dc",StringComparison.OrdinalIgnoreCase)?1:0;
        var supported=parameters.Length==valueIndex+1&&parameters[valueIndex].assignment()==null&&!(card is SpiceParser.SourceContext&&parameters[valueIndex].call()!=null);
        var resistor=family=="R"?ResistorModelExtraction.Extract(parameters,capture):null;
        var ac=resistor is {Parameters.Length:1}&&resistor.Parameters[0].Name?.Equals("ac",StringComparison.OrdinalIgnoreCase)==true
            ?resistor.Parameters[0].Value:null;
        var value=supported?capture(parameters[valueIndex]):ac!=null?resistor!.Reference:null;
        return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),value,supported,capture(card).Range)
        {ResistorModel=resistor,AcResistance=ac,
         RequiresExcitationProfile=card is SpiceParser.SourceContext&&parameters.Length>0&&SourceProfileExtraction.IsReserved(parameters[0].Start.Text),
         Excitation=card is SpiceParser.SourceContext sourceCard
            ?supported&&value!=null&&!parameters[0].Start.Text.Equals("ac",StringComparison.OrdinalIgnoreCase)
                &&!(parameters.Length==1&&SourceProfileExtraction.IsReserved(parameters[0].GetText()))
                ?new(value,null,null):SourceProfileExtraction.Extract(sourceCard,capture)
            :null};
    }
}

internal static class GlobalNodeExtraction
{
    internal static ImmutableArray<SourceSyntax>? Extract(ParserRuleContext card,Func<ParserRuleContext,SourceSyntax> capture)
    {
        if(card is not SpiceParser.ExtensionContext extension||!extension.Start.Text.Equals(".global",StringComparison.OrdinalIgnoreCase))return null;
        var parser=SourceProfileExtraction.CreateParser(extension.parameter());if(parser==null)return null;
        try{return parser.globalNodesProfile().node().Select(capture).ToImmutableArray();}
        catch(Antlr4.Runtime.Misc.ParseCanceledException){return null;}
    }
}
