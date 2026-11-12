using System.Numerics;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ComplexControlTests
{
    private static ControlCommandSyntax[] Commands(string body)=>DeckParser.Parse("Complex reporting\n.control\n"+body+"\n.endc\n.end\n",collectDeclarations:true)
        .Declarations!.ControlBlocks.Single().Commands.ToArray();

    [Fact]
    public void Complex_arithmetic_matches_independent_loaded_rc_formula_and_vector_substitution()
    {
        var f=new[]{100000d,150000d,200000d};
        var output=f.Select(hz=>{var x=2*Math.PI*hz*2e-6;return new Complex(1.5/(1+x*x),-1.5*x/(1+x*x));}).ToArray();
        var before=output.ToArray();
        var commands=Commands("let s=2*pi*i*frequency\nlet gold=1.5/(1+s*2u)\nlet err=abs(v(out)/gold-1)\necho error $&err\nif vecmax(err) > 1e-12\nquit 1\nelse\nquit 0\nend");
        var report=ControlVectorExecution.ExecuteComplex(commands,new Dictionary<string,Complex[]>{["frequency"]=f.Select(v=>new Complex(v,0)).ToArray(),["v(out)"]=output});
        Assert.Equal(0,report.ExitCode);Assert.Empty(report.Diagnostics);
        Assert.Equal(3,report.Locals["err"].Length);
        Assert.All(report.Locals["err"],v=>Assert.InRange(v.Real,0,1e-12));
        Assert.Equal(before,output);Assert.Single(report.Output);
    }

    [Fact]
    public void Plot_boundaries_replace_physical_vectors_and_preserve_explicit_locals()
    {
        var commands=Commands("let count=0\nop\nif v(out) eq 4\nlet count=count+1\nend\nac lin 1 1 1\nif abs(v(out)) eq 2\nlet count=count+1\nend\nif count ne 2\nquit 1\nelse\nquit 0\nend");
        var analyses=commands.Where(c=>c.Kind=="analysis").ToArray();
        var plots=new Dictionary<int,IReadOnlyDictionary<string,Complex[]>>{
            [analyses[0].Range.Start]=new Dictionary<string,Complex[]>{["v(out)"]=[4]},
            [analyses[1].Range.Start]=new Dictionary<string,Complex[]>{["v(out)"]=[new(0,-2)]}};
        var report=ControlVectorExecution.ExecutePlots(commands,plots);
        Assert.Equal(0,report.ExitCode);Assert.Equal(new ControlComplexValue(2,0),Assert.Single(report.Locals["count"]));
        plots.Remove(analyses[1].Range.Start);
        Assert.Equal("missing_control_analysis_plot",Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecutePlots(commands,plots)).Message);
    }

    [Theory]
    [InlineData("let bad=vecmax(v(out))")]
    [InlineData("if v(out) > 0\nquit 0\nend")]
    public void Nonreal_ordering_is_an_explicit_failure(string body)
        =>Assert.Equal("control_real_required",Assert.Throws<InvalidDataException>(()=>
            ControlVectorExecution.ExecuteComplex(Commands(body),new Dictionary<string,Complex[]>{["v(out)"]=[Complex.ImaginaryOne]})).Message);

    [Fact]
    public void Real_only_entry_point_does_not_enable_imaginary_constants()
        =>Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.Execute(Commands("let z=i"),new Dictionary<string,double[]>()));

    [Fact]
    public void Nonconstant_plot_vectors_do_not_leak_into_the_next_analysis()
    {
        var commands=Commands("op\nlet remembered=v(out)\nac lin 1 1 1\nlet leaked=remembered");
        var analyses=commands.Where(c=>c.Kind=="analysis").ToArray();
        var plots=new Dictionary<int,IReadOnlyDictionary<string,Complex[]>>{
            [analyses[0].Range.Start]=new Dictionary<string,Complex[]>{["v(out)"]=[1,2]},
            [analyses[1].Range.Start]=new Dictionary<string,Complex[]>{["v(out)"]=[3]}};
        Assert.Equal("unknown_control_vector:remembered",Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecutePlots(commands,plots)).Message);
    }

    [Fact]
    public void Finite_complex_components_serialize_without_derived_overflow()
    {
        var report=ControlVectorExecution.ExecuteComplex(Commands("let kept=v(out)"),
            new Dictionary<string,Complex[]>{["v(out)"]=[new(1.7e308,1.7e308)]});
        var json=System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Magnitude",json,StringComparison.Ordinal);
    }

    [Fact]
    public void Builtin_arithmetic_does_not_require_whitespace()
    {
        var report=ControlVectorExecution.ExecuteComplex(Commands("let z=pi+i"),new Dictionary<string,Complex[]>());
        Assert.Equal(new ControlComplexValue(Math.PI,1),Assert.Single(report.Locals["z"]));
    }

    [FsCheck.Xunit.Property(MaxTest=128,Replay="5002,2026111201")]
    public void Randomized_complex_rc_values_match_rectangular_analytical_reference(ushort coordinate)
    {
        var frequency=1+100d*coordinate;var x=2*Math.PI*frequency*2e-6;
        var reference=new Complex(1.5/(1+x*x),-1.5*x/(1+x*x));
        var result=ControlVectorExecution.ExecuteComplex(Commands("let z=1.5/(1+2*pi*i*frequency*2u)"),
            new Dictionary<string,Complex[]>{["frequency"]=[frequency]});
        var actual=Assert.Single(result.Locals["z"]);
        Assert.InRange(Complex.Abs(new Complex(actual.Real,actual.Imag)-reference)/reference.Magnitude,0,1e-12);
    }

    [Fact]
    public void Invalid_complex_input_shape_and_domain_publish_no_report()
    {
        foreach(var input in new[]{Array.Empty<Complex>(),new[]{new Complex(double.NaN,0)},new Complex[4097]})
            Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecuteComplex(Commands("let z=x"),new Dictionary<string,Complex[]>{["x"]=input}));
        Assert.Equal("control_vector_shape",Assert.Throws<InvalidDataException>(()=>
            ControlVectorExecution.ExecuteComplex(Commands("let z=x+y"),new Dictionary<string,Complex[]>{["x"]=[1,2],["y"]=[1,2,3]})).Message);
        Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecuteComplex(Commands("let z=1/0"),new Dictionary<string,Complex[]>()));
    }

    [Fact]
    public void Null_plot_vectors_are_structured_data_failures()
    {
        var commands=Commands("op");var offset=commands.Single().Range.Start;
        var plots=new Dictionary<int,IReadOnlyDictionary<string,Complex[]>>{
            [offset]=new Dictionary<string,Complex[]>{["v(out)"]=null!}};
        Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecutePlots(commands,plots));
    }
}
