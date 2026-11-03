using FsCheck;
using FsCheck.Fluent;
using Microsoft.FSharp.Core;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class ResistorModelTests
{
    private static ResistorPreparation Prepare(string card="R1 a 0 rm",string model=".model rm R(R=500)",string parameters="")
    {
        var syntax=DeckParser.Parse("resistor\n"+parameters+"\n"+card+"\n"+model+"\n.end\n",collectDeclarations:true);Assert.Equal("accepted_structure",syntax.Status);var catalog=syntax.Declarations!;
        return ResistorModelLowering.Prepare(catalog,Assert.Single(catalog.Primitives),ParameterEvaluator.Evaluate(catalog));
    }
    [Theory]
    [InlineData("R1 a 0 rm",".model rm R(R=500)",500)]
    [InlineData("R1 a 0 rm L=12u W=2u",".model rm R(RSH=100)",600)]
    [InlineData("R1 a 0 rm L=14u W=2u",".model rm R(RSH=100 SHORT=1u NARROW=.5u)",1200)]
    [InlineData("R1 a 0 rm L=10u W=2u",".model rm R(RSH=100 SHORT=-1u NARROW=-1u)",300)]
    public void Constant_and_sheet_models_match_analytical_resistances(string card,string model,double expected)
    {var result=Prepare(card,model);Assert.True(result.Success);Assert.InRange(Math.Abs(result.Resistor!.Ohms-expected),0,1e-10);}
    [Theory]
    [InlineData("R1 a 0 rm",1200)]
    [InlineData("R1 a 0 {rm}",1200)]
    [InlineData("R1 a 0 1k",1000)]
    public void Existing_numeric_and_parameter_values_precede_same_named_models(string card,double expected)
    {var result=Prepare(card,".model rm R(R=500)\n.model 1k R(R=500)",".param rm=1200");Assert.True(result.Success);Assert.Equal(expected,result.Resistor!.Ohms);Assert.Equal(ResistorProfile.Scalar,result.Resistor.Profile);}
    [Theory]
    [InlineData("R1 a 0 rm",".model rm R(R=0)")]
    [InlineData("R1 a 0 rm",".model rm R(R=-1)")]
    [InlineData("R1 a 0 rm",".model rm R")]
    [InlineData("R1 a 0 rm",".model rm C(C=1u)")]
    [InlineData("R1 a 0 rm",".model rm R(R=500 TC1=0)")]
    [InlineData("R1 a 0 rm",".model rm R(R=500 RSH=100)")]
    [InlineData("R1 a 0 rm",".model rm R(R=500 R=600)")]
    [InlineData("R1 a 0 rm",".model rm R(R={missing})")]
    [InlineData("R1 a 0 rm",".model rm R(R=500)\n.model rm R(R=600)")]
    [InlineData("R1 a 0 rm L=1u W=1u",".model rm R(R=500)")]
    [InlineData("R1 a 0 rm L=1u",".model rm R(RSH=100)")]
    [InlineData("R1 a 0 rm L=0 W=1u",".model rm R(RSH=100)")]
    [InlineData("R1 a 0 rm L=2u W=1u",".model rm R(RSH=100 SHORT=1u)")]
    [InlineData("R1 a 0 rm L=2u W=1u",".model rm R(RSH=100 NARROW=.5u)")]
    [InlineData("R1 a 0 rm L=2u W=1u M=1",".model rm R(RSH=100)")]
    [InlineData("R1 a 0 rm L=2u W=1u W=2u",".model rm R(RSH=100)")]
    [InlineData("R1 a 0 rm L=2u W=1u TC1=0",".model rm R(RSH=100)")]
    [InlineData("R1 a 0 rm L=1e308 W=1e-308",".model rm R(RSH=100)")]
    public void Unsupported_parameters_geometry_binding_and_physics_refuse_atomically(string card,string model)
    {var result=Prepare(card,model);Assert.False(result.Success);Assert.Null(result.Resistor);Assert.NotEmpty(result.Diagnostics);}
    [Fact]
    public void Model_values_use_declaration_scope_and_geometry_uses_actual_call_overrides()
    {
        var syntax=DeckParser.Parse("t\n.param rv=500 sh=100\n.model fixed R(R={rv})\n.model sheet R(RSH={sh})\n.subckt child a b rv=4000 sh=200 l=12u w=1u\nR1 a b fixed\nR2 a b sheet L={l} W={w}\n.ends\nX1 a 0 child w=2u\n.end\n",collectDeclarations:true);
        var catalog=syntax.Declarations!;var call=Assert.Single(InstanceBinder.Bind(catalog).Instances);var environment=ParameterEvaluator.ForInstance(catalog,call,ParameterEvaluator.Evaluate(catalog));
        var fixedR=ResistorModelLowering.Prepare(catalog,catalog.Primitives.Single(p=>p.Name=="R1"),environment);var sheet=ResistorModelLowering.Prepare(catalog,catalog.Primitives.Single(p=>p.Name=="R2"),environment);
        Assert.True(fixedR.Success);Assert.Equal(500,fixedR.Resistor!.Ohms);Assert.True(sheet.Success);Assert.InRange(Math.Abs(sheet.Resistor!.Ohms-600),0,1e-10);
    }
    [Fact]
    public void Foreign_objects_and_unsupported_opaque_tails_are_refused()
    {
        var catalog=DeckParser.Parse("t\nR1 a 0 rm\n.model rm R(R=500)\n.end\n",collectDeclarations:true).Declarations!;var primitive=Assert.Single(catalog.Primitives);
        var foreign=DeckParser.Parse("t\n.end\n",collectDeclarations:true).Declarations!;
        Assert.False(ResistorModelLowering.Prepare(foreign,primitive,ParameterEvaluator.Evaluate(foreign)).Success);
        Assert.False(ResistorModelLowering.Prepare(catalog,primitive,ParameterEvaluator.Evaluate(foreign)).Success);
        Assert.False(Prepare(model:".model rm R(R=500 opaque)").Success);
        Assert.False(Prepare(card:"R1 a 0 rm L=1u W=1u",parameters:".param rm=1200").Success);
    }
    [Fact]
    public void Generated_geometry_assignments_preserve_bare_CR_physical_locations()
    {
        var catalog=DeckParser.Parse("t\rR1 a 0 rm\r+ L=12u W=2u\r.model rm R(RSH=100)\r.end\r",collectDeclarations:true).Declarations!;
        var primitive=Assert.Single(catalog.Primitives);var geometry=primitive.ResistorModel!.Parameters;
        Assert.Equal(3,geometry[0].Range.Line);Assert.Equal("12u",geometry[0].Value.Text);Assert.Equal(5,geometry[0].Value.Range.Column);
        Assert.True(ResistorModelLowering.Prepare(catalog,primitive,ParameterEvaluator.Evaluate(catalog)).Success);
    }
    [Fact]
    public void Sheet_geometry_property_campaign()
    {
        var config=Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(FSharpOption<Replay>.Some(new(new Rnd(20261106UL),null)));
        Check.One(config,Prop.ForAll<byte,byte,byte>((l,w,r)=>{var length=l+1;var width=w+1;var sheet=r+1;var result=Prepare($"R1 a 0 rm L={length}u W={width}u",$".model rm R(RSH={sheet})");return result.Success&&Math.Abs(result.Resistor!.Ohms-((double)length/width*sheet))<1e-8;}));
    }
}