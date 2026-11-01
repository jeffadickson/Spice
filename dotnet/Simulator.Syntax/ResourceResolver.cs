using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
namespace Simulator.Syntax;

/// <summary>Explicit filesystem policy; bytes count every open, including repeated includes and the root.</summary>
public sealed record ResourcePolicy(ImmutableArray<string> AllowedRoots,int MaxDepth=32,int MaxFiles=128,long MaxTotalBytes=8_388_608);
/// <summary>Decoded UTF-8 source with original-byte SHA256; an optional UTF-8 BOM is omitted from Text.</summary>
public sealed record ResolvedSource(string Path,string? Section,string Sha256,string Text);
/// <summary>Exact contiguous source mapping; all offsets are UTF-16 and zero based.</summary>
public sealed record SourceSegment(int OutputStart,int Length,int SourceIndex,int SourceStart);
/// <summary>Resolution failure at a referring source range and attempted target, when available.</summary>
public sealed record ResourceDiagnostic(string Code,string SourcePath,SourceRange? Range,string? TargetPath=null);
/// <summary>Atomic expanded source/provenance result; failures contain no partial source text or graph.</summary>
public sealed record ResourceResolution(string? Text,ImmutableArray<ResolvedSource> Sources,ImmutableArray<SourceSegment> Segments,ImmutableArray<ResourceDiagnostic> Diagnostics)
{
    /// <summary>True only when resolution finished within the explicit resource policy.</summary>
    public bool Success=>Diagnostics.IsEmpty&&Text!=null;
}
/// <summary>Opt-in bounded UTF-8 include/library resolver, independent of syntax acceptance and simulation.</summary>
public static class ResourceResolver
{
    /// <summary>Expands a titled root deck and included fragments, retaining physical per-file source segments.</summary>
    /// <remarks>Links/reparse points are refused. This is not a sandbox against hostile concurrent filesystem mutation.</remarks>
    public static ResourceResolution Resolve(string rootPath,ResourcePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);ArgumentNullException.ThrowIfNull(policy);
        if(policy.AllowedRoots.IsDefaultOrEmpty||policy.MaxDepth<0||policy.MaxDepth>128||policy.MaxFiles<1||policy.MaxFiles>4096||policy.MaxTotalBytes<1||policy.MaxTotalBytes>67_108_864)
            throw new ArgumentOutOfRangeException(nameof(policy));
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        var roots=policy.AllowedRoots.Select(Path.GetFullPath).ToArray();
        var sources=new List<ResolvedSource>();var segments=new List<SourceSegment>();var output=new StringBuilder();
        var active=new HashSet<string>(comparer);var fileCount=0;long bytes=0;
        try
        {
            foreach(var root in roots){if(root.StartsWith("\\\\",StringComparison.Ordinal))throw Fail("denied_path",root,null,root);if(!Directory.Exists(root))throw Fail("missing_allowed_root",root,null,root);RejectLinks(root,root,null);}
            Load(Path.GetFullPath(rootPath),null,0,false,rootPath,null);
            return new(output.ToString(),sources.ToImmutableArray(),segments.ToImmutableArray(),[]);
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
                var parsed=DeckParser.Parse((fragment?title:"")+text,collectDeclarations:true);
                if(parsed.Status=="resource_rejected")throw Fail("resource_parse_budget",path,null,path);
                if(parsed.Status!="accepted_structure"||parsed.Declarations==null)throw Fail("resource_syntax",path,null,path);
                var catalog=parsed.Declarations;
                var start=0;var end=text.Length;
                var ending=parsed.Statements.FirstOrDefault(s=>s.Kind=="endCard");
                if(ending!=null)
                {
                    if(fragment)throw Fail("end_in_fragment",path,null,path);
                    if(catalog.Scopes.Skip(1).Any(s=>ending.Range.Start>=s.Range.Start&&ending.Range.Start<s.Range.Start+s.Range.Length))throw Fail("end_in_subcircuit",path,null,path);
                    end=ending.Range.Start+ending.Range.Length;
                    while(end<text.Length&&text[end] is not ('\r' or '\n'))end++;
                    if(end<text.Length&&text[end]=='\r')end++;
                    if(end<text.Length&&text[end]=='\n')end++;
                }
                if(catalog.Resources.Any(r=>r.Range.Start-shift<end)&&catalog.DeferredContexts.Any(c=>c.Range.Start-shift<end))throw Fail("conditional_resources",path,null,path);
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
                    segments.Add(new(output.Length,length,index,offset));output.Append(text,offset,length);
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
    private static string Unquote(string text)=>text.Length>=2&&text[0] is '\'' or '"'&&text[^1]==text[0]?text[1..^1]:text;
    private static ResourceFailure Fail(string code,string origin,SourceRange? range,string? target)=>new(new(code,origin,range,target));
    private sealed class ResourceFailure(ResourceDiagnostic diagnostic):Exception(diagnostic.Code)
    {internal ResourceDiagnostic Diagnostic{get;}=diagnostic;}
}
