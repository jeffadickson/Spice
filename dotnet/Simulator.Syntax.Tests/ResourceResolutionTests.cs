using System.Text;
using Xunit;
namespace Simulator.Syntax.Tests;
public sealed class ResourceResolutionTests
{
    // The runner sets CODEX_ARTIFACT_ROOT/TEMP to its E: task directory. On a
    // different platform the standard temporary root is an explicit fallback.
    private sealed class Files:IDisposable
    {
        internal string Root{get;}
        private readonly string parent;
        internal Files(){parent=Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT")??Path.GetTempPath();if(!Directory.Exists(parent))throw new DirectoryNotFoundException(parent);Root=Path.Combine(parent,"spice-resources-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);}
        internal string Put(string name,string text){var p=Path.Combine(Root,name);var dir=Path.GetDirectoryName(p)!;if(!Directory.Exists(dir))Directory.CreateDirectory(dir);File.WriteAllText(p,text,new UTF8Encoding(false,true));return p;}
        public void Dispose(){if(Path.GetDirectoryName(Path.GetFullPath(Root))==Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar))Directory.Delete(Root,true);}
    }
    [Fact]
    public void Relative_fragments_expand_without_losing_first_line_and_keep_exact_source_maps()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\r\n.include 'sub/values.inc'\r\nR1 a 0 {r}\r\n.end\r\n");
        f.Put("sub/values.inc","* λ source\r\n.param r=1k\r\n.include 'model.inc'\r\n");f.Put("sub/model.inc",".model dm d\n");
        var result=ResourceResolver.Resolve(root,new([f.Root]));
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));Assert.Equal(3,result.Sources.Length);
        var parsed=DeckParser.Parse(result.Text!,collectDeclarations:true);Assert.Equal("accepted_structure",parsed.Status);
        Assert.Empty(parsed.Declarations!.Resources);Assert.Single(parsed.Declarations.Symbols);Assert.Equal(1000,ParameterEvaluator.Evaluate(parsed.Declarations).Values["r"]);
        var offset=0;foreach(var segment in result.Segments){Assert.Equal(offset,segment.OutputStart);Assert.Equal(result.Sources[segment.SourceIndex].Text.Substring(segment.SourceStart,segment.Length),result.Text!.Substring(segment.OutputStart,segment.Length));offset+=segment.Length;}
        Assert.Equal(result.Text!.Length,offset);
    }
    [Fact]
    public void Only_the_requested_library_section_is_loaded_and_its_ports_and_overrides_bind()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.lib 'cells.lib' typical\n.param base=3\nX1 a b load resistance={base*1k}\n.end\n");
        f.Put("cells.lib",".lib unused\n.include 'must-not-open.inc'\n.endl unused\n.lib typical\n.subckt load p n resistance=1k\nR1 p n {resistance}\n.ends\n.endl typical\n");
        var resolved=ResourceResolver.Resolve(root,new([f.Root]));Assert.True(resolved.Success);
        Assert.DoesNotContain("must-not-open",resolved.Text!);var catalog=DeckParser.Parse(resolved.Text!,collectDeclarations:true).Declarations!;
        var call=Assert.Single(InstanceBinder.Bind(catalog).Instances);Assert.Equal(BindingStatus.Bound,call.Status);
        Assert.Equal(3000,ParameterEvaluator.ForInstance(catalog,call,ParameterEvaluator.Evaluate(catalog)).Values["resistance"]);
    }
    [Theory]
    [InlineData(".include '../outside.inc'","denied_path")]
    [InlineData(".include 'missing.inc'","resource_io")]
    [InlineData(".lib 'cells.lib' absent","missing_library_section")]
    public void Failure_publishes_no_partial_graph_or_expanded_text(string directive,string code)
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n"+directive+"\n.end\n");f.Put("cells.lib",".lib typical\n.param a=1\n.endl\n");
        var result=ResourceResolver.Resolve(root,new([f.Root]));Assert.False(result.Success);Assert.Null(result.Text);Assert.Empty(result.Sources);Assert.Empty(result.Segments);Assert.Contains(result.Diagnostics,d=>d.Code==code);
    }
    [Fact]
    public void Cycles_encodings_conditionals_and_all_resource_budgets_have_explicit_failures()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.include 'a.inc'\n.end\n");f.Put("a.inc",".include 'root.cir'\n");
        Assert.Equal("resource_cycle",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
        Assert.Equal("file_budget",Assert.Single(ResourceResolver.Resolve(root,new([f.Root],MaxFiles:1)).Diagnostics).Code);
        Assert.Equal("byte_budget",Assert.Single(ResourceResolver.Resolve(root,new([f.Root],MaxTotalBytes:1)).Diagnostics).Code);
        f.Put("a.inc",".include 'b.inc'\n");f.Put("b.inc",".param a=1\n");Assert.Equal("depth_budget",Assert.Single(ResourceResolver.Resolve(root,new([f.Root],MaxDepth:1)).Diagnostics).Code);
        File.WriteAllBytes(Path.Combine(f.Root,"a.inc"),[0xff]);Assert.Equal("invalid_utf8",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
        f.Put("root.cir","Title\n.if enabled\n.include 'a.inc'\n.endif\n.end\n");Assert.Equal("conditional_resources",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
    }
    [Fact]
    public void Library_section_ambiguity_and_unclosed_sections_are_rejected()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.lib 'cells.lib' typical\n.end\n");
        f.Put("cells.lib",".lib typical\n.param a=1\n.endl\n.lib typical\n.param a=2\n.endl\n");
        Assert.Equal("ambiguous_library_section",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
        f.Put("cells.lib",".lib typical\n.param a=1\n.endl wrong\n");Assert.Equal("library_section_structure",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
        f.Put("cells.lib",".lib typical\n.param a=1\n");Assert.Equal("library_section_structure",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
    }
    [Fact]
    public void The_root_end_card_prevents_inactive_tail_resource_access()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.end\n.include 'must-not-open.inc'\n");
        var result=ResourceResolver.Resolve(root,new([f.Root]));
        Assert.True(result.Success);Assert.DoesNotContain("must-not-open",result.Text!);Assert.Single(result.Sources);
    }
    [Fact]
    public void End_cards_in_fragments_are_explicitly_unsupported()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.include 'stop.inc'\n.end\n");f.Put("stop.inc",".end\n");
        Assert.Equal("end_in_fragment",Assert.Single(ResourceResolver.Resolve(root,new([f.Root])).Diagnostics).Code);
    }
    private sealed class LinkFactAttribute:FactAttribute
    {
        public LinkFactAttribute()
        {
            var folder=Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT")??Path.GetTempPath();
            if(OperatingSystem.IsWindows()&&new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).DriveFormat.Equals("exFAT",StringComparison.OrdinalIgnoreCase))
                Skip="Configured artifact volume is exFAT; symbolic-link creation is unavailable. Replay on a supported native filesystem.";
        }
    }
    [LinkFact]
    public void A_file_link_cannot_escape_an_allowed_root()
    {
        using var f=new Files();var root=f.Put("allowed/root.cir","Title\n.include 'link.inc'\n.end\n");var outside=f.Put("outside.inc",".param secret=99\n");
        File.CreateSymbolicLink(Path.Combine(f.Root,"allowed/link.inc"),outside);
        Assert.Equal("link_resource",Assert.Single(ResourceResolver.Resolve(root,new([Path.Combine(f.Root,"allowed")])).Diagnostics).Code);
    }    [Fact]
    public void Flat_library_selection_never_discards_an_enclosing_subcircuit_scope()
    {
        using var f=new Files();var root=f.Put("root.cir","Title\n.lib 'cells.lib' typical\n.end\n");
        f.Put("cells.lib",".subckt outer p n\n.lib typical\n.param local=1\n.endl typical\n.ends\n");
        var result=ResourceResolver.Resolve(root,new([f.Root]));
        Assert.False(result.Success);Assert.Null(result.Text);Assert.Equal("nested_library_section",Assert.Single(result.Diagnostics).Code);
    }}
