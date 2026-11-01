using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class FeatureTests
{
    [Theory]
    [InlineData("R1 1 0 1k","r")]
    [InlineData("V1 1 0 1","v")]
    [InlineData("D1 1 0 diode","d")]
    [InlineData("Q1 1 2 0 transistor","q")]
    [InlineData("M1 1 2 0 0 transistor","m")]
    [InlineData("J1 1 2 0 transistor","j")]
    [InlineData("E1 1 0 2 0 3","e")]
    [InlineData("F1 1 0 V1 2","f")]
    [InlineData("S1 1 0 2 0 sw","s")]
    [InlineData("W1 1 0 V1 sw","w")]
    [InlineData("K1 L1 L2 0.9","k")]
    [InlineData("X1 1 0 child","x")]
    [InlineData("B1 1 0 V=1","b")]
    [InlineData("T1 1 0 2 0 Z0=50 TD=1n","t")]
    [InlineData("A1 in out model","a")]
    [InlineData("N1 out model","n")]
    [InlineData("P1 1 0 2 0 model","p")]
    [InlineData("Y1 1 0 2 0 model","y")]
    [InlineData("U1 1 0 2 model","u")]
    public void Each_declared_device_production_has_a_canonical_family(string body,string family)
    {
        var parsed=DeckParser.Parse("Title\n"+body+"\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Equal(family,Assert.Single(parsed.Features.Where(f=>f.Category=="device")).Name);
    }

    [Theory]
    [InlineData(".op","op")]
    [InlineData(".ac dec 10 1 1meg","ac")]
    [InlineData(".dc V1 0 1 0.1","dc")]
    [InlineData(".tran 1n 1u","tran")]
    public void Analysis_productions_have_canonical_names(string body,string name)
    {
        var parsed=DeckParser.Parse("Title\n"+body+"\n.end\n",collectFeatures:true);
        Assert.Equal(name,Assert.Single(parsed.Features.Where(f=>f.Category=="analysis")).Name);
    }

    [Theory]
    [InlineData(".subckt child a b\n.ends child", "subcircuit", ".subckt")]
    [InlineData(".control\nplot v(1)\n.endc", "control", ".control")]
    [InlineData(".param a=1", "paramcard", ".param")]
    [InlineData(".func f(a) a+1", "function", ".func")]
    [InlineData(".model m d", "", ".model")]
    public void Structure_and_directive_views_are_complete_without_false_tags(string body,string structure,string directive)
    {
        var parsed=DeckParser.Parse("Title\n"+body+"\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Equal(structure.Length==0?Array.Empty<string>():[structure],parsed.Features.Where(f=>f.Category=="structure").Select(f=>f.Name));
        Assert.Equal(new[]{directive,".end"},parsed.Features.Where(f=>f.Category=="directive").Select(f=>f.Name));
    }
    [Fact]
    public void Inventory_observes_nested_cards_models_profiles_and_source_forms_once()
    {
        var text="Title\n.subckt cell a b\nR1 a b 1k\n.ends cell\nX1 1 0 cell\nV1 1 0 pulse(0 1 0 1n 1n 1u 2u)\n.model mm NMOS (level=54 vth0=0.4)\n.tran 1n 1u\n.end\n";
        var parsed=DeckParser.Parse(text,collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Single(parsed.Features.Where(f=>f.Category=="device"&&f.Name=="r"));
        Assert.Contains(parsed.Features,f=>f.Category=="model_level"&&f.Name=="nmos:54");
        Assert.Contains(parsed.Features,f=>f.Category=="model_parameter"&&f.Name=="nmos:vth0");
        Assert.Contains(parsed.Features,f=>f.Category=="source_form"&&f.Name=="pulse");
        Assert.Contains(parsed.Features,f=>f.Category=="analysis"&&f.Name=="tran");
        var resistor=parsed.Features.Single(f=>f.Category=="device"&&f.Name=="r");
        Assert.Equal(3,resistor.Range.Line);
        Assert.Equal("R1 a b 1k",text.Substring(resistor.Range.Start,resistor.Range.Length));
        Assert.Empty(DeckParser.Parse(text).Features);
    }

    [Fact]
    public void Opaque_languages_and_comments_do_not_become_parsed_expressions_or_devices()
    {
        var parsed=DeckParser.Parse("Title\n* .model fake npn\nB1 1 0 v='v(in)+sin(time)'\n.control\n* .model fake pmos\ntran 1n 1u\n.endc\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.DoesNotContain(parsed.Features,f=>f.Category=="model"||f.Category=="call");
        Assert.Contains(parsed.Features,f=>f.Category=="opaque"&&f.Name=="quoted_text");
        Assert.Single(parsed.Features.Where(f=>f.Category=="control_command"&&f.Name=="tran"));
    }

    [Fact]
    public void Invalid_and_resource_refused_decks_do_not_publish_partial_feature_evidence()
    {
        Assert.Empty(DeckParser.Parse("Title\nR1 1 0\n",collectFeatures:true).Features);
        Assert.Empty(DeckParser.Parse("Title\nR1 1 0 1k\n",new ParserLimits(3),true).Features);
    }

    [Theory]
    [InlineData("  B1 1 0 V=1", "b")]
    [InlineData("\tU1 LOGICEXP(1,1) DPWR DGND A OUT IO_STD LOGIC: OUT={A=='1}", "u")]
    public void Composite_tokens_do_not_turn_indentation_into_device_names(string card,string family)
    {
        var parsed=DeckParser.Parse("Title\n"+card+"\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Equal(family,Assert.Single(parsed.Features.Where(f=>f.Category=="device")).Name);
    }

    [Fact]
    public void Function_signatures_and_nested_calls_do_not_pollute_canonical_feature_names()
    {
        var parsed=DeckParser.Parse("Title\n.func foo(a) a+1\nV1 1 0 pulse(foo(1) 1 0 1n 1n 1u 2u)\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Contains(parsed.Features,f=>f.Category=="directive"&&f.Name==".func");
        Assert.DoesNotContain(parsed.Features,f=>f.Category=="source_form"&&f.Name=="foo");
        Assert.Contains(parsed.Features,f=>f.Category=="call"&&f.Name=="foo");
    }

    [Property(MaxTest=128,Replay="681,20261101")]
    public bool Observations_agree_across_prediction_strategies(NonNegativeInt n)
    {
        var text=$"Title\rR1 1 0 {n.Get%10000+1}\r.param a={{1+2*3}}\r.end\r";
        var fast=DeckParser.Parse(text,collectFeatures:true);
        var full=DeckParser.Parse(text,new ParserLimits(useSllFirst:false),true);
        return fast.Features.SequenceEqual(full.Features)&&fast.Features.Any(f=>f.Range.Line==2);
    }

    [Theory]
    [InlineData(".model m nmos", "model_level", "nmos:unspecified")]
    [InlineData(".model m nmos level={a}", "model_level", "nmos:expression")]
    [InlineData(".model m nmos level=1 level=2", "model_level", "nmos:multiple")]
    [InlineData(".include 'missing.lib'", "external_resource", "include")]
    [InlineData(".lib 'missing.lib' typical", "external_resource", "library")]
    [InlineData(".func f(a) a>0?a:0", "expression", "conditional")]
    [InlineData(".param a={b&&c}", "expression", "boolean")]
    [InlineData(".param a={-b}", "expression", "unary")]
    [InlineData(".param a={sin(b)}", "call", "sin")]
    [InlineData("a1 [%vd(in 0)] [out] model", "xspice_port", "differential")]
    [InlineData("a1 %v(in) model", "xspice_port", "single")]
    [InlineData("a1 %custom(in) model", "xspice_port", "custom")]
    public void Observes_named_structures_without_resolving_them(string body,string category,string name)
    {
        var parsed=DeckParser.Parse("Title\n"+body+"\n.end\n",collectFeatures:true);
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Contains(parsed.Features,f=>f.Category==category&&f.Name==name);
    }

    [Property(MaxTest=256,Replay="682,20261101")]
    public bool Malformed_input_never_publishes_partial_or_out_of_range_observations(NonNull<string> input)
    {
        var text="Title\n"+input.Get[..Math.Min(input.Get.Length,128)]+"\n";
        var result=DeckParser.Parse(text,collectFeatures:true);
        return result.Status!="accepted_structure"?result.Features.IsEmpty:
            result.Features.All(f=>f.Range.Start>=0&&f.Range.Length>=0&&f.Range.Start+f.Range.Length<=text.Length&&f.Range.Line>=1);
    }
}
