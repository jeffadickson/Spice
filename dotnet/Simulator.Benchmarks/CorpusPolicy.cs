using System.Globalization;
using System.Text.RegularExpressions;
namespace Simulator.Benchmarks;
internal static class CorpusPolicy
{
    internal static bool IsDeck(string path,string boundedText) =>
        new[]{".cir",".sp",".ckt",".net",".deck",".spice",".inp"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase)
        || (System.IO.Path.GetExtension(path).Length==0 && Regex.IsMatch(boundedText,@"(?im)^\s*\.end\s*$"));

    internal static string[] Features(string text)
    {
        var features=new SortedSet<string>(StringComparer.Ordinal);
        foreach(var line in text.Split('\n').Skip(1))
        {
            var s=line.Trim();if(s.Length==0||s.StartsWith('*'))continue;
            var head=s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            var feature=head switch {
                ".control" or ".endc"=>"control_blocks", ".subckt" or ".ends" or ".include" or ".lib"=>"hierarchy",
                ".model"=>"model_cards_present", ".param" or ".func"=>"parameter_expressions",
                ".measure" or ".meas"=>"measurements", ".dc" or ".noise" or ".pz" or ".tf" or ".sens" or ".disto"=>"analysis_"+head[1..],
                ".load" or ".osdi" or ".codemodel"=>"external_code", _=>head[0] switch {
                    'x'=>"hierarchy",'m'=>"mosfet",'q'=>"bjt",'j'=>"jfet",'a'=>"xspice",'b'=>"behavioral_source",
                    't' or 'o' or 'u'=>"transmission_line",'k'=>"mutual_inductance",'s' or 'w'=>"switch",_=>null}};
            if(feature is not null)features.Add(feature);
            if(s.Contains('{')||s.Contains('}'))features.Add("parameter_expressions");
        }
        var analyses=Regex.Matches(text,@"(?im)^\s*\.(op|ac|tran|dc|noise|pz|tf|sens|disto)\b")
            .Select(m=>m.Groups[1].Value.ToLowerInvariant()).Distinct().Count();
        if(analyses>1)features.Add("multiple_declared_analyses");
        return features.ToArray();
    }
    internal static bool UnsafeReference(string text)=>Regex.IsMatch(text,
        @"(?im)^\s*\.(control|include|inc|lib|load|osdi|codemodel|exec|shell)\b");
    internal static int Remaining(IEnumerable<CorpusRow> rows)=>rows.Count(r=>r.Status!="compared_pass");
    internal static int ExitCode(CorpusReport report)=>
        report.IssueState is "publish_failed" or "created_with_publisher_error"?3:
        report.State=="harness_failed"?4:
        report.State is "cancelled" or "budget_exhausted"?2:
        report.Rows.Any(r=>r.Status is "budget_exhausted" or "cancelled")?2:
        report.State=="provenance_changed"||report.Rows.Any(r=>r.Status is "comparison_failed" or "simulator_failed" or "oracle_failed" or "timeout" or "input_or_harness_failed" or "provenance_changed")?1:0;
    internal static string Clip(string text)=>text.Length<=1024?text:text[..1024];
    internal static Point Reference(Point[] reference,double coordinate,string kind)
    {
        Tables.Validate(reference);
        if(kind!="ac")return Tables.At(reference,coordinate,kind=="transient");
        if(!double.IsFinite(coordinate)||coordinate<=0)throw new InvalidDataException("AC comparison needs a positive finite frequency");
        // ngspice print axes round frequency. Match a nearby recorded point,
        // never interpolate AC phasors or silently select a distant frequency.
        var nearest=reference.MinBy(p=>Math.Abs(p.Coordinate-coordinate))!;
        if(!double.IsFinite(coordinate)||Math.Abs(nearest.Coordinate-coordinate)>1e-12+5e-6*Math.Abs(coordinate))
            throw new InvalidDataException("Missing matching AC frequency within declared print-axis bound.");
        return nearest with {Coordinate=coordinate};
    }
    internal static Point[] Selected(Point[] points)
    {
        Tables.Validate(points);if(points.Length<=200)return points;
        return Enumerable.Range(0,200).Select(i=>points[(int)((long)i*(points.Length-1)/199)]).ToArray();
    }
    internal static string ObservedDeck(string text,string kind,string[] nodes)
    {
        if(UnsafeReference(text))throw new InvalidDataException("External resource/control execution is outside the read-only oracle profile.");
        if(nodes.Length==0 || nodes.Any(n=>!Regex.IsMatch(n,@"^[A-Za-z0-9_.$:-]+$")))
            throw new InvalidDataException("Node name outside explicit observation identifier profile.");
        // Remove whole observation records: a continuation must not attach to
        // the previous device. Comments/blanks do not start a new logical card.
        var lines=new List<string>();
        var discardContinuation=false;
        foreach(var line in text.Split('\n'))
        {
            var trimmed=line.TrimStart();
            if(trimmed.StartsWith('+')) {if(!discardContinuation)lines.Add(line);continue;}
            if(trimmed.Length==0||trimmed.StartsWith('*')) {lines.Add(line);continue;}
            discardContinuation=Regex.IsMatch(line,@"(?i)^\s*\.(end|print|plot|probe)\b");
            if(!discardContinuation)lines.Add(line);
        }
        var result=string.Join('\n',lines)+"\n";
        if(kind=="op"&&!Regex.IsMatch(text,@"(?im)^\s*\.op\s*$"))result+=".op\n";
        foreach(var n in nodes)result+=$".print {(kind=="transient"?"tran":kind)} v({n})\n";
        return result+".end\n";
    }
    internal static string Repository(string remote)
    {
        var match=Regex.Match(remote.Trim(),@"^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?$");
        return match.Success?match.Groups[1].Value:throw new InvalidDataException("Origin must identify an explicit GitHub repository.");
    }
}
