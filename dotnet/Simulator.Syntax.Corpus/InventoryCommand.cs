using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Simulator.Syntax;

// Presentation and filesystem ownership stay in the corpus executable, outside
// pure syntax/aggregation. Report text is data and never a shell instruction.
internal static class InventoryCommand
{
    internal static void Run(string primaryPath,string legacyPath,string catalogPath,string registryPath,string outputStem)
    {
        if(!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(outputStem))))throw new DirectoryNotFoundException("Output parent must exist");
        foreach(var path in new[]{primaryPath,legacyPath,catalogPath,registryPath})
            if(new FileInfo(path).Length>128*1024*1024)throw new InvalidDataException("Report exceeds 128 MiB");
        var support=JsonSerializer.Deserialize<CapabilitySupport[]>(File.ReadAllText(registryPath))??throw new InvalidDataException("Missing registry");
        var primaryText=File.ReadAllText(primaryPath);
        var snapshot=CapabilityInventory.FromReports(primaryText,File.ReadAllText(legacyPath),File.ReadAllText(catalogPath),support);
        using var primary=JsonDocument.Parse(primaryText);
        var identity=new Dictionary<string,JsonElement>();
        foreach(var field in new[]{"source_commit","source_dirty","source_sha256","binary_sha256","inventory_sha256","corpus_commit","corpus_dirty","started_utc","elapsed_seconds","platform","limits"})
            identity[field]=primary.RootElement.GetProperty(field);
        var report=new{schema_version=1,generated_utc=DateTimeOffset.UtcNow,identity,
            reports_sha256=new{primary=Hash(primaryPath),legacy=Hash(legacyPath),adjudications=Hash(catalogPath),support=Hash(registryPath)},snapshot,
            limitations=new[]{"Feature presence is not semantic validity, model binding, execution support, or numerical qualification.",
                "Declared records inside subcircuits and unused models are included; includes are not loaded and control/quoted languages remain opaque.",
                "Deck counts overlap. Priorities measure observed reach, not the number of circuits a change will unlock.",
                "Family assessments are conservative legacy-importer subsets; ANTLR-to-core elaboration is not implemented.",
                "This chart establishes a baseline; no historical simulation progress is inferred from parser acceptance."}};
        File.WriteAllText(outputStem+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        var md=new StringBuilder("# Corpus capability inventory\n\n");
        md.AppendLine($"State: **{snapshot.State}**. Accepted observations: **{snapshot.AcceptedDecks}/{snapshot.RequiredDecks}** required decks; {snapshot.ExcludedDecks} explicitly adjudicated exclusions from {snapshot.InventoryInputs} inputs.");
        md.AppendLine("\nPresence is not simulation support. Counts overlap; declarations may be unused. Includes are not expanded, LEVEL expressions are not evaluated, and command headings/quoted text remain opaque. No circuits are claimed newly runnable.\n");
        Table("Device and model families",snapshot.Features.Where(f=>f.Category is "device" or "model"));
        Table("Most widespread planning gaps",snapshot.Features.Where(f=>f.Status!="partial"&&f.Category is "structure" or "external_resource" or "model" or "device" or "directive" or "control_command").Take(30));
        Table("Model LEVEL profiles (literal spelling; unspecified is not an inferred default)",snapshot.Features.Where(f=>f.Category=="model_level").Take(30));
        Table("Source forms",snapshot.Features.Where(f=>f.Category=="source_form"));
        md.AppendLine("\n## Next work\n\n1. Build source-located semantic binding and parameter/subcircuit elaboration; keep unavailable external resources explicit.\n2. Minimize representative model/LEVEL/parameter profiles from the JSON inventory before selecting numerical implementations.\n3. Connect already-supported numerical subsets through the typed boundary, then compare actual results against ngspice.\n\nThe JSON contains every observed feature, counts, support evidence and exact campaign hashes. The SVG is a baseline of assessed feature kinds, not a simulation-pass burndown. Prior numerical corpus results remain a separate series.\n");
        foreach(var missing in snapshot.Outstanding)md.AppendLine($"- Outstanding: {Safe(missing.Key)} — {Safe(missing.Status)}");
        File.WriteAllText(outputStem+".md",md.ToString());
        WriteChart(outputStem+".svg",snapshot);
        Console.WriteLine($"{snapshot.State}: {snapshot.AcceptedDecks}/{snapshot.RequiredDecks} required inputs; {snapshot.Features.Length} distinct feature kinds. Reports: {outputStem}.*");
        if(snapshot.State!="complete_inventory")throw new InvalidDataException("Inventory remains incomplete; inspect outstanding evidence");

        void Table(string title,IEnumerable<CapabilityReach> rows)
        {
            md.AppendLine("\n## "+title+"\n\n| Construct | Decks | Occurrences | Assessment | Limits |\n|---|---:|---:|---|---|");
            foreach(var row in rows)md.AppendLine($"| {Safe(row.Category+":"+row.Name)} | {row.Decks} | {row.Occurrences} | {row.Status} | {Safe(row.Limitation)} |");
        }
    }

    private static string Safe(string value)=>value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("|","\\|").Replace('`','\'').Replace('\r',' ').Replace('\n',' ');
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void WriteChart(string path,CapabilitySnapshot snapshot)
    {
        XNamespace ns="http://www.w3.org/2000/svg";
        XElement Text(int y,string text)=>new(ns+"text",new XAttribute("x",36),new XAttribute("y",y),new XAttribute("font-family","sans-serif"),new XAttribute("font-size",17),text);
        var unassessed=snapshot.Features.Count(f=>f.Status=="unassessed");
        var root=new XElement(ns+"svg",new XAttribute("viewBox","0 0 960 320"),new XAttribute("role","img"),
            new XElement(ns+"title","Capability assessment baseline; no simulated-pass claims"),
            new XElement(ns+"rect",new XAttribute("width",960),new XAttribute("height",320),new XAttribute("fill","#f5f4ed")),
            Text(42,"Corpus capability burndown — baseline only"),Text(82,$"Observed feature kinds: {snapshot.Features.Length}; unassessed: {unassessed}"),
            Text(119,$"Partial: {snapshot.Features.Count(f=>f.Status=="partial")}; unsupported: {snapshot.Features.Count(f=>f.Status=="unsupported")}"),
            Text(156,$"Inventory evidence: {snapshot.AcceptedDecks}/{snapshot.RequiredDecks} required decks; outstanding: {snapshot.Outstanding.Length}"),
            new XElement(ns+"line",new XAttribute("x1",42),new XAttribute("x2",900),new XAttribute("y1",210),new XAttribute("y2",210),new XAttribute("stroke","#56756c")),
            new XElement(ns+"circle",new XAttribute("cx",80),new XAttribute("cy",210),new XAttribute("r",8),new XAttribute("fill","#12675d")),
            Text(250,"First measurement. Future comparable snapshots can establish change."),
            Text(285,"Feature counts overlap. Parser acceptance does not close simulation gaps."));
        new XDocument(root).Save(path);
    }
}
