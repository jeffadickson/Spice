using FsCheck;
using FsCheck.Xunit;
using System.Text.Json;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class DeclarationTests
{
    [Fact]
    public void Lookup_handles_forward_declarations_ancestors_case_and_local_shadows()
    {
        var parsed=DeckParser.Parse("Title\n.subckt cell p n gain=2\n.model local npn\n.subckt child a b\n.ends child\n.ends cell\n.model local d\n.model global pmos\n.end\n",collectDeclarations:true);
        var catalog=Assert.IsType<DeclarationCatalog>(parsed.Declarations);
        Assert.Equal("catalogued",catalog.Status);
        Assert.Equal(3,catalog.Scopes.Length);
        var local=catalog.Lookup(DeclarationKind.Model,"LOCAL",2);
        Assert.Equal(NameLookupStatus.Found,local.Status);
        Assert.Equal("npn",Assert.Single(local.Candidates).ModelFamily);
        Assert.Equal("pmos",Assert.Single(catalog.Lookup(DeclarationKind.Model,"GLOBAL",2).Candidates).ModelFamily);
        Assert.Equal("d",Assert.Single(catalog.Lookup(DeclarationKind.Model,"local",0).Candidates).ModelFamily);
        Assert.Equal(NameLookupStatus.Found,catalog.Lookup(DeclarationKind.Subcircuit,"CHILD",1).Status);
        Assert.Equal(NameLookupStatus.Missing,catalog.Lookup(DeclarationKind.Subcircuit,"child",0).Status);
        Assert.Null(DeckParser.Parse("Title\n.end\n").Declarations);
    }

    [Fact]
    public void Ordered_parameters_and_header_defaults_remain_unevaluated_source_syntax()
    {
        var text="Title\r.subckt cell p n gain={base*2}\r.param gain=3 gain=4\r.ends cell\r.model d1 d (is=1p n={factor})\r.end\r";
        var catalog=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
        var parameters=catalog.LookupParameter("GAIN",1);
        Assert.Equal(NameLookupStatus.Found,parameters.Status);
        Assert.Equal(new[]{"{base*2}","3","4"},parameters.Candidates.Select(p=>p.Syntax.Value.Text));
        Assert.Equal(new[]{"subcircuit_default","param_card","param_card"},parameters.Candidates.Select(p=>p.Origin));
        var defaults=catalog.Symbols.Single(s=>s.Kind==DeclarationKind.Subcircuit);
        Assert.Equal(new[]{"p","n","gain={base*2}"},defaults.HeaderArguments.Select(a=>a.Text));
        var model=catalog.Symbols.Single(s=>s.Kind==DeclarationKind.Model);
        Assert.Equal(new[]{"is","n"},model.Parameters.Select(p=>p.Name));
        Assert.Equal("{factor}",model.Parameters[1].Value.Text);
        Assert.Equal(3,parameters.Candidates[1].Syntax.Range.Line);
        foreach(var p in parameters.Candidates)Assert.Equal(p.Syntax.Value.Text,text.Substring(p.Syntax.Value.Range.Start,p.Syntax.Value.Range.Length));
    }

    [Fact]
    public void Same_scope_duplicates_are_ambiguous_and_keep_both_source_locations()
    {
        var catalog=DeckParser.Parse("Title\n.model m d\n.model M npn\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal("ambiguous_declarations",catalog.Status);
        var result=catalog.Lookup(DeclarationKind.Model,"m",0);
        Assert.Equal(NameLookupStatus.Ambiguous,result.Status);
        Assert.Equal(new[]{2,3},result.Candidates.Select(c=>c.Range.Line));
        Assert.Single(catalog.Diagnostics);
        Assert.Equal("duplicate_definition",catalog.Diagnostics[0].Code);
        Assert.Equal(3,catalog.Diagnostics[0].Range.Line);Assert.Equal(2,catalog.Diagnostics[0].Related.Line);
    }

    [Fact]
    public void External_cards_defer_missing_names_and_never_load_files_or_execute_commands()
    {
        var catalog=DeckParser.Parse("Title\n.include 'definitely-missing.inc'\n.lib 'missing.lib' corner\n.model local d\n.control\n.model hidden npn\nshell fail\n.endc\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(2,catalog.Resources.Length);
        Assert.Equal(NameLookupStatus.Deferred,catalog.Lookup(DeclarationKind.Model,"absent",0).Status);
        Assert.True(catalog.Lookup(DeclarationKind.Model,"local",0).Provisional);
        Assert.Single(catalog.Symbols);
        Assert.Equal(NameLookupStatus.Deferred,catalog.LookupParameter("gain",0).Status);
    }

    [Theory]
    [InlineData("Title\n.model m\n")]
    [InlineData("Title\n.subckt\n")]
    [InlineData("Title\n.missing a\n")]
    public void Rejected_inputs_publish_no_partial_catalog_or_features(string text)
    {
        var result=DeckParser.Parse(text,collectFeatures:true,collectDeclarations:true);
        Assert.Null(result.Declarations);Assert.Empty(result.Features);
    }

    [Fact]
    public void Lookup_validates_scope_and_name_instead_of_assuming_root()
    {
        var catalog=DeckParser.Parse("Title\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Throws<ArgumentOutOfRangeException>(()=>catalog.Lookup(DeclarationKind.Model,"m",1));
        Assert.Throws<ArgumentException>(()=>catalog.LookupParameter(" ",0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>catalog.Lookup((DeclarationKind)99,"m",0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>catalog.LookupParameter("m",-1));
    }

    [Property(MaxTest=128,Replay="701,20261101")]
    public bool Nested_lookup_is_invariant_under_renaming_and_prediction_strategy(PositiveInt value)
    {
        var name="m"+(value.Get%10000);
        var text=$"Title\n.subckt a p n\n.subckt b p n\n.ends b\n.ends a\n.model {name} d\n.end\n";
        var fast=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
        var full=DeckParser.Parse(text,new ParserLimits(useSllFirst:false),collectDeclarations:true).Declarations!;
        return fast.Lookup(DeclarationKind.Model,name.ToUpperInvariant(),2).Status==NameLookupStatus.Found&&
            JsonSerializer.Serialize(fast.Symbols)==JsonSerializer.Serialize(full.Symbols)&&fast.Scopes.SequenceEqual(full.Scopes);
    }

    [Fact]
    public void Parameter_shadows_do_not_choose_a_value_and_namespaces_remain_distinct()
    {
        var catalog=DeckParser.Parse("Title\n.param gain=1\n.model same d\n.subckt same p n gain=2\n.param GAIN=3\n.subckt child p n\n.param gain=4\n.ends child\n.ends same\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(new[]{"2","3"},catalog.LookupParameter("gain",1).Candidates.Select(p=>p.Syntax.Value.Text));
        Assert.Equal("4",Assert.Single(catalog.LookupParameter("gain",2).Candidates).Syntax.Value.Text);
        Assert.Equal("1",Assert.Single(catalog.LookupParameter("gain",0).Candidates).Syntax.Value.Text);
        Assert.Equal(NameLookupStatus.Found,catalog.Lookup(DeclarationKind.Model,"same",0).Status);
        Assert.Equal(NameLookupStatus.Found,catalog.Lookup(DeclarationKind.Subcircuit,"same",0).Status);
    }

    [Fact]
    public void Opaque_targets_and_conditional_declarations_prevent_final_absence_claims()
    {
        var catalog=DeckParser.Parse("Title\n.param f(x)=1\n.if (choose==1)\n.model m d\n.endif\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Null(Assert.Single(catalog.Parameters).Syntax.Name);
        Assert.Equal(NameLookupStatus.Deferred,catalog.LookupParameter("unknown",0).Status);
        Assert.NotEmpty(catalog.DeferredContexts);
        Assert.True(catalog.Lookup(DeclarationKind.Model,"m",0).Provisional);
        Assert.Equal(NameLookupStatus.Deferred,catalog.Lookup(DeclarationKind.Model,"unknown",0).Status);
    }

    [Fact]
    public void Closing_arguments_are_not_subcircuit_defaults_or_ports()
    {
        var catalog=DeckParser.Parse("Title\n.subckt cell p n\n.ends cell gain=9\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(new[]{"p","n"},Assert.Single(catalog.Symbols).HeaderArguments.Select(a=>a.Text));
        Assert.Empty(catalog.Parameters);
        Assert.Equal(NameLookupStatus.Missing,catalog.LookupParameter("gain",1).Status);
    }

    [Fact]
    public void Sibling_models_do_not_leak_and_duplicate_subcircuits_remain_ambiguous()
    {
        var catalog=DeckParser.Parse("Title\n.subckt a p n\n.model m d\n.ends a\n.subckt a p n\n.model m npn\n.ends a\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(NameLookupStatus.Ambiguous,catalog.Lookup(DeclarationKind.Subcircuit,"a",0).Status);
        Assert.Equal(NameLookupStatus.Missing,catalog.Lookup(DeclarationKind.Model,"m",0).Status);
        Assert.Equal("d",Assert.Single(catalog.Lookup(DeclarationKind.Model,"m",1).Candidates).ModelFamily);
        Assert.Equal("npn",Assert.Single(catalog.Lookup(DeclarationKind.Model,"m",2).Candidates).ModelFamily);
    }

    [Fact]
    public void Resource_refusals_expose_no_catalog()
    {
        Assert.Null(DeckParser.Parse("Title\n.end\n",new ParserLimits(2),collectDeclarations:true).Declarations);
    }

    [Property(MaxTest=256,Replay="702,20261101")]
    public bool Malformed_inputs_do_not_expose_partial_or_out_of_range_catalogs(NonNull<string> input)
    {
        var text="Title\n"+input.Get[..Math.Min(input.Get.Length,128)]+"\n";
        var result=DeckParser.Parse(text,collectFeatures:true,collectDeclarations:true);
        if(result.Status!="accepted_structure")return result.Declarations==null&&result.Features.IsEmpty;
        return result.Declarations!=null&&result.Declarations.Scopes.All(s=>s.Range.Start>=0&&s.Range.Start+s.Range.Length<=text.Length&&
            (s.ParentId==null?s.Id==0:s.ParentId<s.Id));
    }

    [Fact]
    public void Duplicate_heavy_catalog_summary_checks_every_definition_and_assignment()
    {
        var text="Title\n"+string.Concat(Enumerable.Repeat(".model m d\n.param gain=1\n",1000))+".end\n";
        var catalog=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
        var summary=DeclarationSummary.From(catalog);
        Assert.Equal(1000,summary.Models);Assert.Equal(1000,summary.ScopeParameters);
        Assert.Equal(999,summary.Ambiguities);Assert.Equal(2000,summary.SelfLookups);
        Assert.Equal(0,summary.SelfLookupFailures);
        Assert.Throws<ArgumentNullException>(()=>DeclarationSummary.From(null!));
    }

    [Fact]
    public void Summary_preserves_independent_counts_and_provisional_contexts()
    {
        var text="Title\n.subckt cell p n gain=2\n.model m npn (bf=100 is=1p)\n.ends cell\n.model root d (is=1p)\n.param outer=3\n.include 'missing.inc'\n.if (choose==1)\n.endif\n.end\n";
        var catalog=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
        var summary=DeclarationSummary.From(catalog);
        Assert.Equal("catalogued",summary.Status);Assert.Equal(2,summary.Scopes);
        Assert.Equal(2,summary.Models);Assert.Equal(1,summary.Subcircuits);
        Assert.Equal(2,summary.ScopeParameters);Assert.Equal(3,summary.AssignedModelParameters);
        Assert.Equal(1,summary.Resources);Assert.Equal(2,summary.DeferredContexts);
        Assert.Equal(0,summary.Ambiguities);Assert.Equal(5,summary.SelfLookups);
        Assert.Equal(0,summary.SelfLookupFailures);
        Assert.Equal(catalog.Symbols.Length,catalog.Symbols.Select(s=>s.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(".ELSEIF (choose==2)",".elseif")]
    [InlineData(".ELSE",".else")]
    [InlineData(".CSPARAM gain=1",".csparam")]
    [InlineData(".SNDPARAM rate=1",".sndparam")]
    public void Alternate_declaration_contexts_are_preserved_and_defer_lookup(string body,string kind)
    {
        var catalog=DeckParser.Parse("Title\n"+body+"\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(kind,Assert.Single(catalog.DeferredContexts).Kind);
        Assert.Equal(NameLookupStatus.Deferred,catalog.Lookup(DeclarationKind.Model,"missing",0).Status);
    }

    [Fact]
    public void Opaque_parameter_targets_and_plain_assignments_have_distinct_certainty()
    {
        var opaque=DeckParser.Parse("Title\n.param f(x)=1\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(NameLookupStatus.Deferred,opaque.LookupParameter("missing",0).Status);
        var plain=DeckParser.Parse("Title\n.param gain=1\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal(NameLookupStatus.Missing,plain.LookupParameter("missing",0).Status);
        Assert.False(plain.LookupParameter("gain",0).Provisional);
    }

    [Fact]
    public void Declaration_and_resource_ranges_retain_exact_original_spelling()
    {
        var text="Title\n.model m d (is=1p)\n.include 'a.inc'\n.lib 'b.lib' typical\n.end\n";
        var catalog=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
        var model=Assert.Single(catalog.Symbols);
        Assert.Equal(".model m d (is=1p)",text.Substring(model.Range.Start,model.Range.Length));
        Assert.Equal("is=1p",text.Substring(model.Parameters[0].Range.Start,model.Parameters[0].Range.Length));
        Assert.Equal(new[]{"include","library_card"},catalog.Resources.Select(r=>r.Kind));
        Assert.Equal(new[]{"'a.inc'","'b.lib'"},catalog.Resources.Select(r=>r.Target.Text));
        Assert.Equal(".include 'a.inc'",text.Substring(catalog.Resources[0].Range.Start,catalog.Resources[0].Range.Length));
        Assert.Equal(".lib 'b.lib' typical",text.Substring(catalog.Resources[1].Range.Start,catalog.Resources[1].Range.Length));
    }
}
