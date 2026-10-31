using System.Globalization;
using System.Security;
using System.Text;
namespace Simulator.Benchmarks;
internal static class CorpusPresentation
{
    internal static string Markdown(CorpusReport report,bool full)
    {
        var b=new StringBuilder($"# ngspice corpus results: {report.RunId}\n\nState: **{report.State}**. This is an inventory/comparison campaign, not a full-SPICE pass.\n\n");
        b.AppendLine($"Source `{report.SourceCommit}`; upstream `{report.CorpusCommit}`; inventory `{report.Inventory}`.");
        b.AppendLine($"Mode `{report.InventoryMode}`; source dirty `{report.SourceDirty}`; corpus dirty `{report.CorpusDirty?.ToString()??"unavailable"}`. Unclassified extensionless assets: {report.UnclassifiedAssets.Count}.");
        if(report.Failure is not null)b.AppendLine($"Harness failure: `{Escape(report.Failure)}`");
        b.AppendLine($"\n{report.Rows.Count} decks; {report.Rows.Count(r=>r.Status=="compared_pass")} compared passes; {CorpusPolicy.Remaining(report.Rows)} remaining.\n");
        b.AppendLine("| Outcome | Decks |\n|---|---:|");
        foreach(var g in report.Rows.GroupBy(r=>r.Status).OrderBy(g=>g.Key))b.AppendLine($"| {g.Key} | {g.Count()} |");
        b.AppendLine("\n## Fix checklist by observed diagnostic\n");
        foreach(var g in report.Rows.Where(r=>r.Status!="compared_pass").GroupBy(r=>r.Status+": "+
            System.Text.RegularExpressions.Regex.Replace(r.Diagnostic,@"line \d+","line N")).OrderByDescending(g=>g.Count()).Take(40))
        {
            b.AppendLine($"- [ ] **{g.Count()}** decks — `{Escape(g.Key)}`");
            foreach(var row in g.Take(3))b.AppendLine($"  - `{Escape(row.Path)}`");
        }
        b.AppendLine("\n## Features present in blocked decks\n\nCo-occurrence hints, not isolated root-cause proof. Counts overlap.\n\n| Feature hint | Decks |\n|---|---:|");
        foreach(var g in report.Rows.Where(r=>r.Status!="compared_pass").SelectMany(r=>r.FeatureHints).GroupBy(s=>s).OrderByDescending(g=>g.Count()))
            b.AppendLine($"| {Escape(g.Key)} | {g.Count()} |");
        b.AppendLine("\n## Feature triage tasks\n\nFirst isolate a minimal reproducer; then implement a qualified contract or document deliberate exclusion. Hints can overlap.\n");
        foreach(var g in report.Rows.Where(r=>r.Status!="compared_pass").SelectMany(r=>r.FeatureHints).GroupBy(s=>s).OrderByDescending(g=>g.Count()))
            b.AppendLine($"- [ ] Triage `{Escape(g.Key)}` in {g.Count()} remaining decks; retain a reproducible case and verification evidence.");
        b.AppendLine($"\nComparisons: representable node voltages and supported V/L/E/H MNA branch currents (p-to-n positive), at most 200 coordinates each. Absolute {report.AbsoluteToleranceV:G} V or {report.AbsoluteToleranceA:G} A + relative {report.RelativeTolerance:G}; AC axis bound 1e-12 + 5e-6*f Hz. Transient uses declared interpolation and four-ULP endpoint alignment, no extrapolation. Other-device currents/global error remain unqualified.\n");
        if(full)
        {
            b.AppendLine("## Every discovered deck\n\n| Path | Status | Diagnostic |\n|---|---|---|");
            foreach(var row in report.Rows)b.AppendLine($"| `{Escape(row.Path)}` | {row.Status} | `{Escape(row.Diagnostic)}` |");
        }
        b.AppendLine("\nFull JSON/Markdown, raw logs and SVG burndown are retained locally by the runner. The burndown counts every nonpassing outcome as remaining; changed inventories use separate series. No unsupported/unfinished result is called passing.");
        return b.ToString();
    }
    private static string Escape(string s)=>s.Replace("`","'",StringComparison.Ordinal).Replace("|","\\|",StringComparison.Ordinal).Replace("\r"," ",StringComparison.Ordinal).Replace("\n"," ",StringComparison.Ordinal);
    internal static string Chart(CorpusHistory[] history,string inventory)
    {
        var rows=history.Where(h=>h.Inventory==inventory).TakeLast(100).ToArray();
        var max=Math.Max(1,rows.Select(r=>r.Total).DefaultIfEmpty(1).Max());
        var b=new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"400\" viewBox=\"0 0 1000 400\"><rect width=\"1000\" height=\"400\" fill=\"#fff\"/><g font-family=\"sans-serif\" fill=\"#182534\"><text x=\"60\" y=\"30\" font-size=\"21\">ngspice corpus burndown — remaining decks</text><text x=\"60\" y=\"55\" font-size=\"13\">Unsupported, failed and unfinished remain work. Same byte-fingerprinted inventory only.</text>");
        for(var i=0;i<=4;i++){var y=90+i*60;b.Append(CultureInfo.InvariantCulture,$"<line x1=\"60\" y1=\"{y}\" x2=\"940\" y2=\"{y}\" stroke=\"#dde3ea\"/><text x=\"8\" y=\"{y+4}\" font-size=\"12\">{max*(4-i)/4}</text>");}
        var points=new List<string>();
        for(var i=0;i<rows.Length;i++)
        {
            var x=60+880d*i/Math.Max(1,rows.Length-1);var remaining=rows[i].Total-rows[i].Passed;var y=330-240d*remaining/max;
            points.Add(FormattableString.Invariant($"{x:F2},{y:F2}"));
            b.Append(CultureInfo.InvariantCulture,$"<circle cx=\"{x:F2}\" cy=\"{y:F2}\" r=\"4\" fill=\"#be3e35\"><title>{SecurityElement.Escape(rows[i].RunId)}: Remaining {remaining}/{rows[i].Total}</title></circle>");
        }
        b.Append($"<polyline points=\"{string.Join(' ',points)}\" fill=\"none\" stroke=\"#be3e35\" stroke-width=\"2\"/><text x=\"60\" y=\"365\" font-size=\"13\">{(rows.Length<=1?"First run baseline; no trend inferred.":$"{rows.Length} observed runs; no synthetic progress points.")}</text></g></svg>");
        return b.ToString();
    }
}
