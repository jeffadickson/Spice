using System.Numerics;
using System.Reflection;
using System.Text.Json;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;

// Bypass the parser/export wrapper to distinguish a solver fault from an
// observable adapter fault. No candidate implementation is patched here.
var source=new VoltageSource("V1","in","0",0);
source.SetParameter("acmag",1.0);
var circuit=new Circuit(source,new Resistor("R1","in","out",1000),new Inductor("L1","out","0",1));
var ac=new AC("direct",new[]{100d});
var current=new ComplexCurrentExport(ac,"L1");
Complex? measured=null;
foreach(var flag in ac.Run(circuit))measured=current.Value;
var expected=Complex.One/new Complex(1000,2*Math.PI*100);
var constants=typeof(Constants).GetFields(BindingFlags.Static|BindingFlags.Public)
    .Where(f=>f.FieldType==typeof(double)).ToDictionary(f=>f.Name,f=>(double)f.GetValue(null)!);
var temperature=300.15;
// Reflect the public physical constants from the exact installed assembly;
// do not assume that an older SPICE implementation uses current SI constants.
var kOverQ=constants["Boltzmann"]/constants["Charge"];
var historicalPrediction=-1e-12*(Math.Exp(.6/(kOverQ*temperature))-1);
Console.WriteLine(JsonSerializer.Serialize(new{
    assembly=typeof(Circuit).Assembly.FullName,
    rl_current=new{actual_real=measured?.Real,actual_imag=measured?.Imaginary,expected_real=expected.Real,expected_imag=expected.Imaginary,
        passed=measured.HasValue&&Complex.Abs(measured.Value-expected)<1e-10},
    constants,diode_using_library_constants=historicalPrediction,
    exported_component_types=typeof(Circuit).Assembly.GetExportedTypes()
        .Where(t=>t.Namespace=="SpiceSharp.Components").Select(t=>t.Name).Order().ToArray(),
    limitations="One direct API circuit and one constants calculation; not broad model or solver qualification."
},new JsonSerializerOptions{WriteIndented=true}));
