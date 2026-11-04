#:property PublishAot=false
#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simulator.Syntax;

// Generated parser identities, not a grammar/SPICE text scanner, own the rows.
// The assessment is reviewed metadata; referenced tests are not rerun by this tool.
if(args.Length!=3)throw new ArgumentException("Expected repository, assessment JSON, fresh report directory.");
var root=Path.GetFullPath(args[0]);var assessmentPath=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
foreach(var path in new[]{root,Path.GetDirectoryName(output)!})if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);
if(!File.Exists(assessmentPath)||new FileInfo(assessmentPath).Length>1_048_576||Directory.Exists(output)||File.Exists(output))throw new IOException("Bounded assessment and fresh output required.");
var options=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower,allowIntegerValues:false));
var assessments=JsonSerializer.Deserialize<GrammarRuleAssessment[]>(File.ReadAllText(assessmentPath),options)??throw new InvalidDataException("Missing assessments.");
var rows=GrammarImplementationInventory.Build(assessments);
// A generic controlUnknown production can hide many concrete gaps. Join the
// historical observations without reusing stale support labels or projecting
// a count of executable circuits from overlapping occurrence counts.
var observationsPath=Path.Combine(root,"evidence/capability-inventory.json");
using var observed=JsonDocument.Parse(File.ReadAllText(observationsPath));
var snapshot=observed.RootElement.GetProperty("snapshot");
var headings=rows.SelectMany(r=>r.Commands.Select(command=>(command,row:r))).ToDictionary(p=>p.command,p=>p.row,StringComparer.OrdinalIgnoreCase);
var controls=snapshot.GetProperty("Features").EnumerateArray().Where(f=>f.GetProperty("Category").GetString()=="control_command")
    .Select(f=>{
        var name=f.GetProperty("Name").GetString()!;headings.TryGetValue(name,out var association);
        return new ControlReachRow(name,f.GetProperty("Decks").GetInt32(),f.GetProperty("Occurrences").GetInt64(),association?.Rule,
            association?.Execution??GrammarStageStatus.Unassessed,association?.Limitation??"No concrete command association; generic command syntax is not execution support.");
    }).OrderByDescending(c=>c.Decks).ThenBy(c=>c.Command,StringComparer.Ordinal).ToArray();
var ids=Directory.GetFiles(Path.Combine(root,"docs"),"*.sdoc").SelectMany(File.ReadLines).Where(line=>line.StartsWith("UID: ",StringComparison.Ordinal)).Select(line=>line[5..].Trim()).ToHashSet(StringComparer.Ordinal);
var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
foreach(var assessment in assessments)
{
    foreach(var id in assessment.Requirements)if(!ids.Contains(id))throw new InvalidDataException("Missing requirement: "+id);
    foreach(var path in assessment.Implementation.Concat(assessment.Evidence))
    {
        if(Path.IsPathRooted(path)||path.Split('/','\\').Contains("..",StringComparer.Ordinal))throw new InvalidDataException("Repository-relative association required.");
        var full=Path.GetFullPath(path,root);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))throw new InvalidDataException("Missing association: "+path);
        hashes[path.Replace('\\','/')]=Hash(full);
    }
}
foreach(var path in new[]{"dotnet/Simulator.Syntax/Grammar/SpiceParser.g4","dotnet/Simulator.Syntax/Grammar/SpiceLexer.g4","dotnet/Simulator.Syntax/GrammarImplementationInventory.cs","scripts/GrammarImplementationMap.cs"})hashes[path]=Hash(Path.Combine(root,path));
hashes["evidence/capability-inventory.json"]=Hash(observationsPath);
hashes["compat/ngspice/capability-support.json"]=Hash(Path.Combine(root,"compat/ngspice/capability-support.json"));
var commit=await Git("rev-parse","HEAD");var dirty=!string.IsNullOrEmpty(await Git("status","--porcelain"));
Directory.CreateDirectory(output);
var report=new{state="generated_inventory",source_commit=commit,source_dirty=dirty,assessment_sha256=Hash(assessmentPath),syntax_assembly_sha256=Hash(typeof(DeckParser).Assembly.Location),source_hashes=hashes,
    generated_rules=rows.Length,assessed_rules=rows.Count(r=>r.Assessed),unassessed_rules=rows.Count(r=>!r.Assessed),rules=rows,
    historical_inventory_inputs=snapshot.GetProperty("InventoryInputs").GetInt32(),observed_control_headings=controls,
    limitations=new[]{"Reviewed source/evidence associations, not a numerical or full-control-program conformance proof.","Parser productions only; lexer modes, wildcard command families and model parameter profiles require separate inventories.","Unexecuted postprocessing remains not_executed, even when independent analyses can run.","Navigation links follow main; exact tested/reviewed bytes are identified by hashes. StrictDoc grammar parsing and tests are separate gates."}};
File.WriteAllText(Path.Combine(output,"grammar-implementation.json"),JsonSerializer.Serialize(report,options));
string Escape(string text)=>HtmlEncoder.Default.Encode(text);
string Links(IEnumerable<string> paths)=>string.Join("<br>",paths.Select(p=>"<a href=\"https://github.com/jeffadickson/Spice/blob/main/"+Escape(p)+"\">"+Escape(p)+"</a>"));
var html=new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Spice grammar implementation map</title><style>body{font:15px system-ui;margin:2rem;color:#e5eaf5;background:#101725}h1{font-size:2rem}p{max-width:90ch;line-height:1.6}a{color:#8cc8ff}table{border-collapse:collapse;width:100%;font-size:13px}td,th{text-align:left;vertical-align:top;padding:.8rem;border-bottom:1px solid #344258}th{background:#202d42;position:sticky;top:0}code{color:#bce3ff}.gap{color:#ffca87}.bounded{color:#a8dfb0}input{margin-right:.5rem}footer{margin-top:2rem;color:#afbdd0}</style><h1>Spice grammar → implementation</h1>");
html.Append("<p>").Append(rows.Length).Append(" generated productions; ").Append(rows.Count(r=>r.Assessed)).Append(" reviewed associations; ").Append(rows.Count(r=>!r.Assessed)).Append(" unassessed. Syntax acceptance, semantic preparation and numerical execution are separate stages.</p><p>Source <code>").Append(Escape(commit)).Append("</code>; dirty source: ").Append(dirty).Append(". A bounded entry names a subset; it does not establish full SPICE compatibility. Linked verification is evidence to inspect, not a test run performed by this report.</p><label><input id=\"gaps\" type=\"checkbox\">Show unassessed, unsupported or unexecuted rules only</label><table><thead><tr><th>Generated rule</th><th>Extraction</th><th>Preparation</th><th>Execution</th><th>Implementation / requirement</th><th>Verification / limitation</th></tr></thead><tbody>");
foreach(var row in rows.OrderBy(r=>r.Assessed).ThenBy(r=>r.Rule,StringComparer.Ordinal))
{
    var gap=!row.Assessed||row.Execution is GrammarStageStatus.Unsupported or GrammarStageStatus.NotExecuted or GrammarStageStatus.Unassessed;
    html.Append("<tr data-gap=\"").Append(gap?"yes":"no").Append("\"><td><code>").Append(Escape(row.Rule)).Append("</code>").Append(row.Assessed?"":"<br>unassessed").Append("</td><td>").Append(row.Extraction).Append("</td><td>").Append(row.Preparation).Append("</td><td class=\"").Append(gap?"gap":"bounded").Append("\">").Append(row.Execution).Append("</td><td>").Append(Links(row.Implementation)).Append("<br>").Append(Escape(string.Join(", ",row.Requirements))).Append("</td><td>").Append(Links(row.Evidence)).Append("<p>").Append(Escape(row.Limitation)).Append("</p></td></tr>");
}
html.Append("</tbody></table><h2>Concrete control headings observed in the corpus</h2><p>Historical syntax inventory: ").Append(snapshot.GetProperty("InventoryInputs").GetInt32()).Append(" inputs. Counts overlap and do not predict newly executable circuits. Current associations below replace historical support labels; missing headings remain unassessed even though generic command syntax accepts their text.</p><table><thead><tr><th>Heading</th><th>Observed decks</th><th>Production</th><th>Execution</th><th>Limit</th></tr></thead><tbody>");
foreach(var command in controls)
    html.Append("<tr data-gap=\"").Append(command.Execution==GrammarStageStatus.Bounded?"no":"yes").Append("\"><td><code>").Append(Escape(command.Command)).Append("</code></td><td>").Append(command.Decks).Append("</td><td>").Append(Escape(command.GrammarRule??"unassessed")).Append("</td><td>").Append(command.Execution).Append("</td><td>").Append(Escape(command.Limitation)).Append("</td></tr>");
html.Append("</tbody></table><footer>Generated from the actual ANTLR C# parser and reviewed metadata. Missing mappings remain visible. No measured coverage percentage or universal support is inferred from this table.</footer><script>document.getElementById('gaps').addEventListener('change',function(){document.querySelectorAll('tbody tr').forEach(r=>r.hidden=this.checked&&r.dataset.gap!=='yes')})</script></html>");
File.WriteAllText(Path.Combine(output,"grammar-implementation.html"),html.ToString());
Console.WriteLine($"Generated {rows.Length} rules: {rows.Count(r=>r.Assessed)} assessed, {rows.Count(r=>!r.Assessed)} unassessed. {output}");

static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
async Task<string> Git(params string[] arguments)
{
    var start=new ProcessStartInfo("git"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in arguments)start.ArgumentList.Add(arg);
    using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try{await process.WaitForExitAsync(limit.Token);}catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
    var text=await stdout;if(process.ExitCode!=0)throw new IOException(await stderr);return text.Trim();
}

sealed record ControlReachRow(string Command,int Decks,long Occurrences,string? GrammarRule,GrammarStageStatus Execution,string Limitation);
