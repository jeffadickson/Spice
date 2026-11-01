using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class CapabilityInventoryTests
{
    private static readonly FeatureTally[] Tallies=[new("device","r",2,new(0,2,1,1))];
    private static string Report(object[] rows,bool legacy=false,string identity="same")=>JsonSerializer.Serialize(new{
        state="completed_with_results",inputs=rows.Length,completed=rows.Length,
        source_sha256=new{source=identity},binary_sha256=new{binary="same"},limits=new{legacy_bytes=legacy},rows});
    private static object Row(string path,string status="accepted_structure",string hash="abc",FeatureTally[]? features=null)=>
        new{scope="ngspice",path,result=new{Status=status,Sha256=hash,Features=features??Tallies}};
    private const string EmptyCatalog="{\"entries\":[]}";

    [Fact]
    public void Counts_occurrences_and_distinct_decks_without_implying_simulation_passes()
    {
        var result=CapabilityInventory.FromReports(Report([Row("a"),Row("b")]),Report([],true),EmptyCatalog,[]);
        var feature=Assert.Single(result.Features);
        Assert.Equal(4,feature.Occurrences);Assert.Equal(2,feature.Decks);
        Assert.Equal("unassessed",feature.Status);Assert.Equal(2,result.AcceptedDecks);
        Assert.Equal("complete_inventory",result.State);
    }

    [Fact]
    public void Legacy_replay_requires_same_hash_and_source_identity()
    {
        var primary=Report([Row("a","encoding_rejected")]);
        var catalog="{\"entries\":[{\"scope\":\"ngspice\",\"path\":\"a\",\"input_sha256\":\"abc\",\"expected_primary_status\":\"encoding_rejected\",\"disposition\":\"legacy_encoded_deck\"}]}";
        Assert.Equal(1,CapabilityInventory.FromReports(primary,Report([Row("a")],true),catalog,[]).AcceptedDecks);
        Refuses(()=>CapabilityInventory.FromReports(primary,Report([Row("a",hash:"changed")],true),catalog,[]),"Replay input identity differs");
        Refuses(()=>CapabilityInventory.FromReports(primary,Report([Row("a")],true,"changed"),catalog,[]),"Replay provenance differs");
    }

    [Fact]
    public void Missing_replay_and_budget_results_remain_outstanding()
    {
        var result=CapabilityInventory.FromReports(Report([Row("a","budget_unattempted")]),Report([],true),EmptyCatalog,[]);
        Assert.Equal("incomplete_inventory",result.State);Assert.Equal(0,result.AcceptedDecks);
        Assert.Equal("budget_unattempted",Assert.Single(result.Outstanding).Status);
    }

    [Fact]
    public void Exclusions_are_hash_and_outcome_pinned_not_a_path_allowlist()
    {
        var catalog="{\"entries\":[{\"scope\":\"ngspice\",\"path\":\"a\",\"input_sha256\":\"abc\",\"expected_primary_status\":\"syntax_rejected\",\"disposition\":\"invalid_syntax_fixture\"}]}";
        var result=CapabilityInventory.FromReports(Report([Row("a","syntax_rejected")]),Report([],true),catalog,[]);
        Assert.Equal(1,result.ExcludedDecks);Assert.Equal(0,result.RequiredDecks);
        Refuses(()=>CapabilityInventory.FromReports(Report([Row("a","accepted_structure")]),Report([],true),catalog,[]),"Adjudicated input hash/outcome changed");
    }

    [Fact]
    public void Duplicate_rows_and_missing_observations_are_not_complete_evidence()
    {
        Assert.Throws<InvalidDataException>(()=>CapabilityInventory.FromReports(Report([Row("a"),Row("a")]),Report([],true),EmptyCatalog,[]));
        var row=new{scope="ngspice",path="a",result=new{Status="accepted_structure",Sha256="abc",Features=(FeatureTally[]?)null}};
        var result=CapabilityInventory.FromReports(Report([row]),Report([],true),EmptyCatalog,[]);
        Assert.Equal("missing_features",Assert.Single(result.Outstanding).Status);
    }

    [Fact]
    public void Registry_attaches_scoped_evidence_and_rejects_duplicate_or_unsupported_status()
    {
        CapabilitySupport support=new("device","r","partial","src/netlist.rs","Constant linear subset only");
        var result=CapabilityInventory.FromReports(Report([Row("a")]),Report([],true),EmptyCatalog,[support]);
        Assert.Equal("partial",Assert.Single(result.Features).Status);
        Refuses(()=>CapabilityInventory.FromReports(Report([Row("a")]),Report([],true),EmptyCatalog,[support,support]),"Duplicate capability assessment");
        Refuses(()=>CapabilityInventory.FromReports(Report([Row("a")]),Report([],true),EmptyCatalog,[support with{Status="fully_supported"}]),"Capability assessment needs");
    }

    [Fact]
    public void Empty_matching_provenance_is_unavailable_not_identity_evidence()
    {
        var primary=Report([Row("a")]).Replace("\"source\":\"same\"","");
        var legacy=Report([],true).Replace("\"source\":\"same\"","");
        Refuses(()=>CapabilityInventory.FromReports(primary,legacy,EmptyCatalog,[]),"Missing provenance");
    }

    [Theory]
    [InlineData("unfinished")]
    [InlineData("row_count")]
    [InlineData("zero_count")]
    [InlineData("duplicate_feature")]
    [InlineData("empty_binary")]
    public void Corrupt_reports_are_refused(string corruption)
    {
        var primary=JsonNode.Parse(Report([Row("a")]))!;
        var legacy=JsonNode.Parse(Report([],true))!;
        switch(corruption)
        {
            case "unfinished":primary["state"]="running";break;
            case "row_count":primary["inputs"]=2;break;
            case "zero_count":primary["rows"]![0]!["result"]!["Features"]![0]!["Count"]=0;break;
            case "duplicate_feature":
                var features=primary["rows"]![0]!["result"]!["Features"]!.AsArray();features.Add(features[0]!.DeepClone());break;
            case "empty_binary":primary["binary_sha256"]!["binary"]=" ";legacy["binary_sha256"]!["binary"]=" ";break;
        }
        var message=corruption switch{"unfinished"=>"Campaign is unfinished","row_count"=>"Incomplete campaign rows","empty_binary"=>"Missing provenance",_=>"Invalid or duplicated feature tally"};
        Refuses(()=>CapabilityInventory.FromReports(primary.ToJsonString(),legacy.ToJsonString(),EmptyCatalog,[]),message);
    }

    [Fact]
    public void Missing_legacy_replay_remains_a_required_outstanding_input()
    {
        var catalog="{\"entries\":[{\"scope\":\"ngspice\",\"path\":\"a\",\"input_sha256\":\"abc\",\"expected_primary_status\":\"encoding_rejected\",\"disposition\":\"legacy_encoded_deck\"}]}";
        var result=CapabilityInventory.FromReports(Report([Row("a","encoding_rejected")]),Report([],true),catalog,[]);
        Assert.Equal(1,result.RequiredDecks);Assert.Equal(0,result.AcceptedDecks);
        Assert.Equal("missing_legacy_replay",Assert.Single(result.Outstanding).Status);
    }

    [Fact]
    public void Tally_groups_occurrences_sorts_keys_and_retains_first_location()
    {
        SourceRange first=new(6,2,2,1);
        var result=CapabilityInventory.Tally([new("device","v",first),new("device","r",new(9,2,3,1)),
            new("device","v",new(15,2,4,1)),new("analysis","tran",new(20,2,5,1))]);
        Assert.Equal(new[]{"analysis:tran","device:r","device:v"},result.Select(f=>f.Category+":"+f.Name));
        Assert.Equal(2,result[2].Count);Assert.Equal(first,result[2].First);
        Assert.Empty(CapabilityInventory.Tally([]));
    }

    [Fact]
    public void Assessments_and_priority_sorting_retain_their_evidence_and_limits()
    {
        FeatureTally[] features=[new("model","b",1,new(0,1,1,1)),new("device","z",1,new(0,1,1,1)),new("device","a",1,new(0,1,1,1)),new("device","r",1,new(0,1,1,1))];
        CapabilitySupport support=new("model","b","unsupported","Pinned source evidence","Specific model limitation");
        var result=CapabilityInventory.FromReports(Report([Row("a",features:features),Row("b")]),Report([],true),EmptyCatalog,[support]);
        Assert.Equal(new[]{"device:r","device:a","device:z","model:b"},result.Features.Select(f=>f.Category+":"+f.Name));
        Assert.Equal("Pinned source evidence",result.Features[^1].Evidence);Assert.Equal("Specific model limitation",result.Features[^1].Limitation);
        Assert.Equal("No reviewed assessment",result.Features[0].Evidence);
        Assert.Equal("Observation only; semantic and numerical support not established",result.Features[0].Limitation);
    }

    [Theory]
    [InlineData("primary_profile","Expected strict primary")]
    [InlineData("legacy_profile","Expected strict primary")]
    [InlineData("name","Invalid or duplicated feature tally")]
    [InlineData("category","Invalid or duplicated feature tally")]
    [InlineData("completed_count","Incomplete campaign rows")]
    [InlineData("duplicate_row","Duplicate input identity")]
    public void Guard_failures_retain_actionable_diagnostics(string fault,string diagnostic)
    {
        var primary=JsonNode.Parse(Report([Row("a")]))!;var legacy=JsonNode.Parse(Report([],true))!;
        switch(fault)
        {
            case "primary_profile":primary["limits"]!["legacy_bytes"]=true;break;
            case "legacy_profile":legacy["limits"]!["legacy_bytes"]=false;break;
            case "name":primary["rows"]![0]!["result"]!["Features"]![0]!["Name"]=" ";break;
            case "category":primary["rows"]![0]!["result"]!["Features"]![0]!["Category"]=" ";break;
            case "completed_count":primary["completed"]=0;break;
            case "duplicate_row":var rows=primary["rows"]!.AsArray();rows.Add(rows[0]!.DeepClone());break;
        }
        var error=Assert.Throws<InvalidDataException>(()=>CapabilityInventory.FromReports(primary.ToJsonString(),legacy.ToJsonString(),EmptyCatalog,[]));
        Assert.Contains(diagnostic,error.Message);
    }

    [Fact]
    public void A_single_empty_identity_among_valid_identities_is_still_unavailable()
    {
        var primary=JsonNode.Parse(Report([Row("a")]))!;var legacy=JsonNode.Parse(Report([],true))!;
        foreach(var report in new[]{primary,legacy})report["source_sha256"]!["another"]=" ";
        Refuses(()=>CapabilityInventory.FromReports(primary.ToJsonString(),legacy.ToJsonString(),EmptyCatalog,[]),"Missing provenance");
    }

    [Theory]
    [InlineData("duplicate_catalog","Duplicate adjudication")]
    [InlineData("unknown_disposition","Unknown adjudication disposition")]
    [InlineData("missing_catalog_input","Adjudicated input missing from inventory")]
    [InlineData("null_tally","Null feature tally")]
    [InlineData("null_status","Missing outcome")]
    [InlineData("empty_evidence","Capability assessment needs")]
    [InlineData("empty_limitation","Capability assessment needs")]
    public void Malformed_metadata_keeps_specific_failure_diagnostics(string fault,string diagnostic)
    {
        var primary=JsonNode.Parse(Report([Row("a")]))!;var catalog=JsonNode.Parse(EmptyCatalog)!;
        var entry=JsonNode.Parse("{\"scope\":\"ngspice\",\"path\":\"a\",\"input_sha256\":\"abc\",\"expected_primary_status\":\"accepted_structure\",\"disposition\":\"mystery\"}")!;
        CapabilitySupport[] support=[];
        switch(fault)
        {
            case "duplicate_catalog":catalog["entries"]!.AsArray().Add(entry);catalog["entries"]!.AsArray().Add(entry.DeepClone());break;
            case "unknown_disposition":catalog["entries"]!.AsArray().Add(entry);break;
            case "missing_catalog_input":entry["path"]="absent";catalog["entries"]!.AsArray().Add(entry);break;
            case "null_tally":primary["rows"]![0]!["result"]!["Features"]![0]=null;break;
            case "null_status":primary["rows"]![0]!["result"]!["Status"]=null;break;
            case "empty_evidence":support=[new("device","r","partial","","Restricted subset")];break;
            case "empty_limitation":support=[new("device","r","partial","Pinned evidence","")];break;
        }
        Refuses(()=>CapabilityInventory.FromReports(primary.ToJsonString(),Report([],true),catalog.ToJsonString(),support),diagnostic);
    }

    private static void Refuses(Action action,string diagnostic)=>Assert.Contains(diagnostic,Assert.Throws<InvalidDataException>(action).Message);

    [Theory]
    [InlineData("partial")]
    [InlineData("unsupported")]
    [InlineData("unassessed")]
    public void Explicit_assessment_statuses_are_preserved(string status)
    {
        var result=CapabilityInventory.FromReports(Report([Row("a")]),Report([],true),EmptyCatalog,
            [new("device","r",status,"Pinned source evidence","Bounded assessment")]);
        Assert.Equal(status,Assert.Single(result.Features).Status);
    }
}
