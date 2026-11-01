using System.Collections.Immutable;
using System.Text.Json;

namespace Simulator.Syntax;

/// <summary>Occurrences of one syntactic feature in a deck, with its first physical location.</summary>
public sealed record FeatureTally(string Category,string Name,int Count,SourceRange First);
/// <summary>A reviewed, bounded implementation assessment. Absence means unassessed, never supported.</summary>
public sealed record CapabilitySupport(string Category,string Name,string Status,string Evidence,string Limitation);
/// <summary>Corpus reach of a construct. Counts overlap across features and do not predict runnable circuits.</summary>
public sealed record CapabilityReach(string Category,string Name,long Occurrences,int Decks,string Status,string Evidence,string Limitation);
/// <summary>Unavailable inventory evidence for a required input.</summary>
public sealed record InventoryOutstanding(string Key,string Status);
/// <summary>Inventory completion is distinct from numerical capability qualification.</summary>
public sealed record CapabilitySnapshot(string State,int InventoryInputs,int RequiredDecks,int AcceptedDecks,int ExcludedDecks,
    ImmutableArray<CapabilityReach> Features,ImmutableArray<InventoryOutstanding> Outstanding);

/// <summary>Pure aggregation of bounded worker evidence and explicit capability assessments.</summary>
public static class CapabilityInventory
{
    /// <summary>Coalesces occurrences without counting container statements or repeated features as extra decks.</summary>
    public static FeatureTally[] Tally(IEnumerable<SyntaxFeature> features)=>features
        .GroupBy(f=>(f.Category,f.Name)).OrderBy(g=>g.Key.Category,StringComparer.Ordinal).ThenBy(g=>g.Key.Name,StringComparer.Ordinal)
        .Select(g=>new FeatureTally(g.Key.Category,g.Key.Name,g.Count(),g.First().Range)).ToArray();

    /// <summary>Checks report identity, adjudications and explicit replay before aggregating accepted observations.</summary>
    /// <remarks>Malformed or inconsistent evidence throws. Valid but unavailable input outcomes remain outstanding.</remarks>
    public static CapabilitySnapshot FromReports(string primaryJson,string replayJson,string catalogJson,IReadOnlyList<CapabilitySupport> support)
    {
        using var primaryDocument=JsonDocument.Parse(primaryJson);
        using var replayDocument=JsonDocument.Parse(replayJson);
        using var catalogDocument=JsonDocument.Parse(catalogJson);
        var primary=primaryDocument.RootElement;var replay=replayDocument.RootElement;
        var rows=Rows(primary);var repeats=Rows(replay);
        if(primary.GetProperty("limits").GetProperty("legacy_bytes").GetBoolean() || !replay.GetProperty("limits").GetProperty("legacy_bytes").GetBoolean())
            throw new InvalidDataException("Expected strict primary and explicit legacy replay");
        foreach(var identity in new[]{"source_sha256","binary_sha256"})
        {
            foreach(var document in new[]{primary,replay})
                if(!document.GetProperty(identity).EnumerateObject().Any() || document.GetProperty(identity).EnumerateObject().Any(p=>string.IsNullOrWhiteSpace(p.Value.GetString())))
                    throw new InvalidDataException("Missing provenance: "+identity);
            if(!Identity(primary.GetProperty(identity)).SequenceEqual(Identity(replay.GetProperty(identity))))
                throw new InvalidDataException("Replay provenance differs from primary: "+identity);
        }
        foreach(var (key,row) in repeats)
            if(!rows.TryGetValue(key,out var original)||Hash(row)!=Hash(original))throw new InvalidDataException("Replay input identity differs: "+key);
        var adjudications=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var entry in catalogDocument.RootElement.GetProperty("entries").EnumerateArray())
            if(!adjudications.TryAdd(Key(entry),entry))throw new InvalidDataException("Duplicate adjudication");
        var registry=new Dictionary<(string,string),CapabilitySupport>();
        foreach(var entry in support)
        {
            if(entry.Status is not ("partial" or "unsupported" or "unassessed") || string.IsNullOrWhiteSpace(entry.Evidence) || string.IsNullOrWhiteSpace(entry.Limitation))
                throw new InvalidDataException("Capability assessment needs conservative status, evidence and limits");
            if(!registry.TryAdd((entry.Category,entry.Name),entry))throw new InvalidDataException("Duplicate capability assessment");
        }
        var totals=new Dictionary<(string Category,string Name),(long Occurrences,int Decks)>();
        var outstanding=ImmutableArray.CreateBuilder<InventoryOutstanding>();
        var accepted=0;var excluded=0;
        foreach(var (key,original) in rows)
        {
            var row=original;var status=Status(row);
            if(adjudications.TryGetValue(key,out var entry))
            {
                if(Hash(row)!=entry.GetProperty("input_sha256").GetString() || status!=entry.GetProperty("expected_primary_status").GetString())
                    throw new InvalidDataException("Adjudicated input hash/outcome changed: "+key);
                var disposition=entry.GetProperty("disposition").GetString();
                if(disposition is "invalid_syntax_fixture" or "non_deck_document"){excluded++;continue;}
                if(disposition!="legacy_encoded_deck")throw new InvalidDataException("Unknown adjudication disposition");
                if(!repeats.TryGetValue(key,out row)){outstanding.Add(new(key,"missing_legacy_replay"));continue;}
                status=Status(row);
            }
            if(status!="accepted_structure"){outstanding.Add(new(key,status));continue;}
            var result=row.GetProperty("result");
            if(!result.TryGetProperty("Features",out var features)||features.ValueKind!=JsonValueKind.Array)
            {outstanding.Add(new(key,"missing_features"));continue;}
            var seen=new HashSet<(string,string)>();
            foreach(var feature in features.EnumerateArray())
            {
                var tally=feature.Deserialize<FeatureTally>()??throw new InvalidDataException("Null feature tally");
                if(tally.Count<1||string.IsNullOrWhiteSpace(tally.Category)||string.IsNullOrWhiteSpace(tally.Name)||!seen.Add((tally.Category,tally.Name)))
                    throw new InvalidDataException("Invalid or duplicated feature tally: "+key);
                var featureKey=(tally.Category,tally.Name);totals.TryGetValue(featureKey,out var count);
                totals[featureKey]=(checked(count.Occurrences+tally.Count),checked(count.Decks+1));
            }
            accepted++;
        }
        if(adjudications.Keys.Any(key=>!rows.ContainsKey(key)))throw new InvalidDataException("Adjudicated input missing from inventory");
        var reach=totals.Select(pair=>
        {
            registry.TryGetValue(pair.Key,out var assessment);
            return new CapabilityReach(pair.Key.Category,pair.Key.Name,pair.Value.Occurrences,pair.Value.Decks,
                assessment?.Status??"unassessed",assessment?.Evidence??"No reviewed assessment",
                assessment?.Limitation??"Observation only; semantic and numerical support not established");
        }).OrderByDescending(f=>f.Decks).ThenBy(f=>f.Category,StringComparer.Ordinal).ThenBy(f=>f.Name,StringComparer.Ordinal).ToImmutableArray();
        return new(outstanding.Count==0?"complete_inventory":"incomplete_inventory",rows.Count,rows.Count-excluded,accepted,excluded,reach,outstanding.ToImmutable());
    }

    private static Dictionary<string,JsonElement> Rows(JsonElement report)
    {
        if(report.GetProperty("state").GetString()!="completed_with_results")throw new InvalidDataException("Campaign is unfinished or its provenance changed");
        var rows=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var row in report.GetProperty("rows").EnumerateArray())if(!rows.TryAdd(Key(row),row))throw new InvalidDataException("Duplicate input identity");
        if(rows.Count!=report.GetProperty("inputs").GetInt32()||rows.Count!=report.GetProperty("completed").GetInt32())throw new InvalidDataException("Incomplete campaign rows");
        return rows;
    }
    private static string Key(JsonElement row)=>row.GetProperty("scope").GetString()+"/"+row.GetProperty("path").GetString();
    private static string? Hash(JsonElement row)=>row.GetProperty("result").GetProperty("Sha256").GetString();
    private static string Status(JsonElement row)=>row.GetProperty("result").GetProperty("Status").GetString()??throw new InvalidDataException("Missing outcome");
    private static IEnumerable<string> Identity(JsonElement dictionary)=>dictionary.EnumerateObject()
        .OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>p.Name+"="+p.Value.GetString());
}
