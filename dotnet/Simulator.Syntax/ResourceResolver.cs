using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
namespace Simulator.Syntax;

/// <summary>Explicit filesystem policy; bytes count every open, including repeated includes and the root.</summary>
public sealed record ResourcePolicy(ImmutableArray<string> AllowedRoots,int MaxDepth=32,int MaxFiles=128,long MaxTotalBytes=8_388_608)
{
    /// <summary>Explicit recognition limits for every loaded file; fragment prefix characters/tokens count toward them. Defaults remain interactive.</summary>
    public ParserLimits ParsingLimits { get; init; } = ParserLimits.Default;
}
/// <summary>Decoded UTF-8 source with original-byte SHA256; an optional UTF-8 BOM is omitted from Text.</summary>
public sealed record ResolvedSource(string Path,string? Section,string Sha256,string Text);
/// <summary>Exact contiguous source mapping; all offsets are UTF-16 and zero based.</summary>
public sealed record SourceSegment(int OutputStart,int Length,int SourceIndex,int SourceStart);
/// <summary>Physical fragment terminator omitted according to the ngspice include profile.</summary>
public sealed record FragmentTerminator(int SourceIndex,SourceRange Range);
/// <summary>Resolution failure at a referring source range and attempted target, when available.</summary>
public sealed record ResourceDiagnostic(string Code,string SourcePath,SourceRange? Range,string? TargetPath=null)
{
    /// <summary>At most three underlying recognition diagnostics, with physical file line numbers; empty for non-parser failures.</summary>
    public ImmutableArray<SyntaxDiagnostic> SyntaxDiagnostics { get; init; } = [];
}
/// <summary>Atomic expanded source/provenance result; failures contain no partial source text or graph.</summary>
public sealed record ResourceResolution(string? Text,ImmutableArray<ResolvedSource> Sources,ImmutableArray<SourceSegment> Segments,ImmutableArray<ResourceDiagnostic> Diagnostics)
{
    // Only the resolver can bind recognition to exact immutable text and bounds.
    // A public record copy with changed Text must fall back to recognition.
    internal (string Text,ParserLimits Limits,DeckSyntax Syntax)? RecognizedRoot { get; init; }
    /// <summary>Explicitly inert fragment .end cards; original source bytes remain in Sources.</summary>
    public ImmutableArray<FragmentTerminator> FragmentTerminators {get;init;}=[];
    /// <summary>True only when resolution finished within the explicit resource policy.</summary>
    public bool Success=>Diagnostics.IsEmpty&&Text!=null;
    /// <summary>Recognizes resolved text, reusing root recognition only for identical text, limits and metadata. Failed resolution cannot be parsed.</summary>
    public DeckSyntax Parse(ParserLimits? limits=null,bool collectFeatures=false,bool collectDeclarations=false)
    {
        if(!Success)throw new InvalidOperationException("Resource resolution did not succeed.");
        limits??=ParserLimits.Default;
        if(collectDeclarations&&!collectFeatures&&RecognizedRoot is {} root
            &&string.Equals(Text,root.Text,StringComparison.Ordinal)
            &&limits.MaxInputCharacters==root.Limits.MaxInputCharacters
            &&limits.MaxTokens==root.Limits.MaxTokens&&limits.UseSllFirst==root.Limits.UseSllFirst)
            return root.Syntax;
        return DeckParser.Parse(Text!,limits,collectFeatures,collectDeclarations);
    }
}
/// <summary>Opt-in bounded UTF-8 include/library resolver, independent of syntax acceptance and simulation.</summary>
public static class ResourceResolver
{
    /// <summary>Expands a titled root deck and included fragments, retaining physical per-file source segments.</summary>
    /// <remarks>Links/reparse points are refused. This is not a sandbox against hostile concurrent filesystem mutation.</remarks>
    public static ResourceResolution Resolve(string rootPath,ResourcePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);ArgumentNullException.ThrowIfNull(policy);ArgumentNullException.ThrowIfNull(policy.ParsingLimits);
        if(policy.AllowedRoots.IsDefaultOrEmpty||policy.MaxDepth<0||policy.MaxDepth>128||policy.MaxFiles<1||policy.MaxFiles>4096||policy.MaxTotalBytes<1||policy.MaxTotalBytes>67_108_864)
            throw new ArgumentOutOfRangeException(nameof(policy));
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        var roots=policy.AllowedRoots.Select(Path.GetFullPath).ToArray();
        var sources=new List<ResolvedSource>();var segments=new List<SourceSegment>();var output=new StringBuilder();var terminators=new List<FragmentTerminator>();
        var active=new HashSet<string>(comparer);var fileCount=0;long bytes=0;
        (string Text,ParserLimits Limits,DeckSyntax Syntax)? recognizedRoot=null;
        try
        {
            foreach(var root in roots){if(root.StartsWith("\\\\",StringComparison.Ordinal))throw Fail("denied_path",root,null,root);if(!Directory.Exists(root))throw Fail("missing_allowed_root",root,null,root);RejectLinks(root,root,null);}
            Load(Path.GetFullPath(rootPath),null,0,false,rootPath,null);
            var expanded=output.ToString();
            return new(expanded,sources.ToImmutableArray(),segments.ToImmutableArray(),[]){FragmentTerminators=terminators.ToImmutableArray(),
                RecognizedRoot=recognizedRoot is {} candidate&&string.Equals(expanded,candidate.Text,StringComparison.Ordinal)?candidate:null};
        }
        catch(ResourceFailure failure){return new(null,[],[],[failure.Diagnostic]);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {return new(null,[],[],[new("resource_io",rootPath,null)]);}

        void Load(string path,string? section,int depth,bool fragment,string origin,SourceRange? reference)
        {
            if(depth>policy.MaxDepth)throw Fail("depth_budget",origin,reference,path);
            if(path.StartsWith("\\\\",StringComparison.Ordinal)||!roots.Any(root=>path.Equals(root,comparison)||path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,comparison)))
                throw Fail("denied_path",origin,reference,path);
            RejectLinks(path,origin,reference);
            // Section-aware identity permits distinct selected sections in one
            // file but prevents either direct or indirect expansion cycles.
            var key=path+"\0"+(section?.ToUpperInvariant()??"");
            if(!active.Add(key))throw Fail("resource_cycle",origin,reference,path);
            if(++fileCount>policy.MaxFiles)throw Fail("file_budget",origin,reference,path);
            try
            {
                byte[] raw;
                try
                {
                    using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                    var length=stream.Length;
                    if(length>policy.MaxTotalBytes-bytes)throw Fail("byte_budget",origin,reference,path);
                    raw=new byte[checked((int)length)];stream.ReadExactly(raw);
                    if(stream.ReadByte()!=-1||stream.Length!=length)throw Fail("changed_resource",origin,reference,path);
                    bytes+=length;
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException){throw Fail("resource_io",origin,reference,path);}
                string text;
                try{var bom=raw.Length>=3&&raw[0]==0xef&&raw[1]==0xbb&&raw[2]==0xbf?3:0;text=new UTF8Encoding(false,true).GetString(raw,bom,raw.Length-bom);}
                catch(DecoderFallbackException){throw Fail("invalid_utf8",origin,reference,path);}
                var index=sources.Count;sources.Add(new(path,section,Convert.ToHexStringLower(SHA256.HashData(raw)),text));
                const string title="Resource fragment\n";var shift=fragment?title.Length:0;
                var parsed=DeckParser.Parse((fragment?title:"")+text,policy.ParsingLimits,collectDeclarations:true);
                if(parsed.Status=="resource_rejected")throw ParseFailure("resource_parse_budget",path,parsed,fragment);
                if(parsed.Status!="accepted_structure"||parsed.Declarations==null)throw ParseFailure("resource_syntax",path,parsed,fragment);
                if(!fragment)recognizedRoot=(text,policy.ParsingLimits,parsed);
                var catalog=parsed.Declarations;
                var start=0;var end=text.Length;
                var fragmentEnds=fragment?catalog.Cards.Where(c=>c.Kind=="endCard").OrderBy(c=>c.Range.Start).ToArray():[];
                if(fragmentEnds.Any(c=>c.ScopeId!=0))throw Fail("end_in_subcircuit",path,null,path);
                var ending=parsed.Statements.FirstOrDefault(s=>s.Kind=="endCard");
                if(ending!=null&&!fragment)
                {
                    if(catalog.Scopes.Skip(1).Any(s=>ending.Range.Start>=s.Range.Start&&ending.Range.Start<s.Range.Start+s.Range.Length))throw Fail("end_in_subcircuit",path,null,path);
                    end=ending.Range.Start+ending.Range.Length;
                    while(end<text.Length&&text[end] is not ('\r' or '\n'))end++;
                    if(end<text.Length&&text[end]=='\r')end++;
                    if(end<text.Length&&text[end]=='\n')end++;
                    // Keep already-validated blank padding after the root .end.
                    // It has no cards to suppress and retaining its exact source
                    // allows the root recognition/provenance to remain reusable.
                    // Nonblank tails retain the established truncation policy.
                    if(catalog.Resources.IsEmpty&&text.AsSpan(end).IndexOfAnyExcept(" \t\r\n")<0)end=text.Length;
                }
                // .csparam exports a control vector after preprocessing; it does not
                // choose file branches. Its parameter semantics remain deferred.
                if(catalog.Resources.Any(r=>r.Range.Start-shift<end)&&catalog.DeferredContexts.Any(c=>c.Kind!=".csparam"&&c.Range.Start-shift<end))throw Fail("conditional_resources",path,null,path);
                var definitions=catalog.Resources.Where(r=>r.Kind=="library_card"&&r.Section==null&&r.Range.Start-shift<end).OrderBy(r=>r.Range.Start).ToArray();
                var closings=catalog.LibraryEnds.Where(c=>c.Range.Start-shift<end).OrderBy(s=>s.Range.Start).ToArray();
                if(definitions.Any(d=>d.ScopeId!=0)||closings.Any(c=>c.ScopeId!=0))throw Fail("nested_library_section",path,null,path);
                if(section!=null)
                {
                    var sections=new List<(string Name,int Start,int End)>();var events=definitions.Select(d=>(Start:d.Range.Start,Header:(DeclaredResource?)d,Close:(LibrarySectionEnd?)null))
                        .Concat(closings.Select(c=>(Start:c.Range.Start,Header:(DeclaredResource?)null,Close:(LibrarySectionEnd?)c))).OrderBy(e=>e.Start);
                    DeclaredResource? open=null;
                    foreach(var item in events)
                    {
                        if(item.Header!=null){if(open!=null)throw Fail("library_section_structure",path,null,path);open=item.Header;}
                        else
                        {
                            if(open==null||!item.Close!.Supported||(item.Close.Name!=null&&!StringComparer.OrdinalIgnoreCase.Equals(item.Close.Name.Text,Unquote(open.Target.Text))))throw Fail("library_section_structure",path,null,path);
                            sections.Add((Unquote(open.Target.Text),open.Range.Start+open.Range.Length-shift,item.Close!.Range.Start-shift));open=null;
                        }
                    }
                    if(open!=null)throw Fail("library_section_structure",path,null,path);
                    var matches=sections.Where(s=>StringComparer.OrdinalIgnoreCase.Equals(s.Name,section)).ToArray();
                    if(matches.Length!=1)throw Fail(matches.Length==0?"missing_library_section":"ambiguous_library_section",origin,reference,path);
                    start=matches[0].Start;end=matches[0].End;
                }
                else if(definitions.Length!=0||closings.Length!=0)throw Fail("library_selection_required",path,null,path);
                var cursor=start;
                foreach(var resource in catalog.Resources.Where(r=>r.Range.Start-shift>=start&&r.Range.Start-shift<end).OrderBy(r=>r.Range.Start))
                {
                    var at=resource.Range.Start-shift;
                    Append(cursor,at-cursor);
                    var target=Unquote(resource.Target.Text);
                    var localRange=resource.Range with{Start=at,Line=resource.Range.Line-(fragment?1:0)};
                    if(resource.Kind=="library_card"&&resource.Section==null)throw Fail("library_selection_required",path,localRange,path);
                    string child;
                    try{child=Path.GetFullPath(target,Path.GetDirectoryName(path)!);}
                    catch(Exception e) when(e is ArgumentException or NotSupportedException){throw Fail("invalid_resource_path",path,localRange,target);}
                    Load(child,resource.Section?.Text,depth+1,true,path,localRange);
                    cursor=at+resource.Range.Length;
                }
                Append(cursor,end-cursor);
                void Append(int offset,int length)
                {
                    if(length<=0)return;
                    // ngspice inp_readall comments fragment .end cards and keeps
                    // reading. Omit only their generated ranges, preserving all
                    // subsequent content and exact source mappings of retained text.
                    var finish=offset+length;var cursor=offset;
                    foreach(var endingCard in fragmentEnds.Where(c=>c.Range.Start-shift>=offset&&c.Range.Start-shift<finish))
                    {
                        var at=endingCard.Range.Start-shift;
                        Copy(cursor,at-cursor);
                        terminators.Add(new(index,endingCard.Range with{Start=at,Line=endingCard.Range.Line-1}));
                        cursor=at+endingCard.Range.Length;
                    }
                    Copy(cursor,finish-cursor);
                    void Copy(int at,int count){if(count<=0)return;segments.Add(new(output.Length,count,index,at));output.Append(text,at,count);}
                }
            }
            finally{active.Remove(key);}
        }
        void RejectLinks(string path,string origin,SourceRange? reference)
        {
            // Check every existing ancestor, including the allowed root itself.
            // Ordinary '..' is normalized before this check and confinement.
            for(var current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw Fail("link_resource",origin,reference,path);
        }
    }
    // Included text is recognized with one synthetic title line. Expose physical
    // diagnostics rather than leaking that implementation offset to callers.
    private static ResourceFailure ParseFailure(string code,string path,DeckSyntax parsed,bool fragment)=>new(
        new(code,path,null,path) { SyntaxDiagnostics=parsed.Diagnostics.Take(3)
            .Select(d=>d with {Line=fragment&&d.Line>0?Math.Max(1,d.Line-1):d.Line,
                Message=d.Message.Length>512?d.Message[..512]:d.Message}).ToImmutableArray() });
    private static string Unquote(string text)=>text.Length>=2&&text[0] is '\'' or '"'&&text[^1]==text[0]?text[1..^1]:text;
    private static ResourceFailure Fail(string code,string origin,SourceRange? range,string? target)=>new(new(code,origin,range,target));
    private sealed class ResourceFailure(ResourceDiagnostic diagnostic):Exception(diagnostic.Code)
    {internal ResourceDiagnostic Diagnostic{get;}=diagnostic;}
}
