#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Relocate only identical source-line contexts. Never guess a new mutation
// when production logic changed; original identities and report hashes remain.
if(args is not [var inventoryPath,var archive,var output]||!File.Exists("Cargo.toml")||!Directory.Exists(archive))
    throw new ArgumentException("From checkout: inventory.json original-source-archive output-names.json.");
if(!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))throw new DirectoryNotFoundException("Output parent missing.");
if(new FileInfo(inventoryPath).Length>131072)throw new InvalidDataException("Inventory too large.");
var inventory=JsonNode.Parse(File.ReadAllBytes(inventoryPath))!.AsObject();
var names=new JsonArray();
foreach(var node in inventory["survivors"]!.AsArray()) {
    var item=node!.AsObject();var file=item["file"]!.GetValue<string>();
    if(!file.StartsWith("src/",StringComparison.Ordinal)||file.Contains("..",StringComparison.Ordinal)||file.Contains('\\'))throw new InvalidDataException("Unsafe source path.");
    var before=File.ReadAllLines(Path.Combine(archive,file));var current=File.ReadAllLines(file);
    var original=item["line"]!.GetValue<int>()-1;var column=item["column"]!.GetValue<int>();
    var start=Math.Max(0,original-2);var end=Math.Min(before.Length-1,original+2);
    var matches=new List<int>();
    for(var candidate=original-start;candidate<current.Length-(end-original);candidate++) {
        var equal=true;
        for(var offset=start-original;offset<=end-original;offset++)
            if(before[original+offset]!=current[candidate+offset]){equal=false;break;}
        if(equal)matches.Add(candidate+1);
    }
    if(matches.Count!=1)throw new InvalidDataException("Mutation context changed or is ambiguous: "+item["id"]);
    var oldName=item["original_name"]!.GetValue<string>();
    var prefix=$"{file}:{original+1}:{column}:";
    if(!oldName.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("Original span/name mismatch.");
    var name=$"{file}:{matches[0]}:{column}:"+oldName[prefix.Length..];
    names.Add(name);item["current_name"]=name;item["current_line"]=matches[0];
    item["current_source_sha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}
if(names.Count!=35)throw new InvalidDataException("Expected the preserved 35-survivor cohort.");
var options=new JsonSerializerOptions{WriteIndented=true,NewLine="\n"};
File.WriteAllText(output,names.ToJsonString(options));
File.WriteAllText(inventoryPath,inventory.ToJsonString(options));
Console.WriteLine($"Prepared {names.Count} exact names from unchanged source contexts. Native inventory must still match before execution.");
