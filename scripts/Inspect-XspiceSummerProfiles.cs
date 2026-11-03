#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulator.Syntax;
using Simulator.Frontend;

// Literal search only selects candidate roots. Every reported model/port is
// resolved through the existing resource policy and generated declaration tree.
// The previously measured full census contains 12 lexical summer instances;
// agreement with that count is reported, never assumed from this candidate scan.
if(args.Length!=4)throw new ArgumentException("Expected repository, corpus, frozen inventory and fresh output JSON");
var repository=Path.GetFullPath(args[0]);var corpus=Path.GetFullPath(args[1]);var inventory=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
foreach(var directory in new[]{repository,corpus,Path.GetDirectoryName(output)!})if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);
if(File.Exists(output))throw new IOException("Fresh output required");
using var data=JsonDocument.Parse(File.ReadAllText(inventory));
var rows=new List<object>();var candidates=0;var failed=0;var changed=0;var total=0;
foreach(var item in data.RootElement.GetProperty("rows").EnumerateArray())
{
 total++;var scope=item.GetProperty("scope").GetString();var relative=item.GetProperty("path").GetString()!;
 if(scope is not ("repository" or "ngspice"))throw new InvalidDataException("Unknown scope");
 var root=scope=="repository"?repository:corpus;var path=Path.GetFullPath(relative,root);
 if(!path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Escaping inventory path");
 var bytes=File.ReadAllBytes(path);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
 if(!hash.Equals(item.GetProperty("result").GetProperty("Sha256").GetString(),StringComparison.OrdinalIgnoreCase)){changed++;continue;}
 string text;try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){continue;}
 if(!text.Contains("summer",StringComparison.OrdinalIgnoreCase))continue;candidates++;
 var resolved=ResourceResolver.Resolve(path,new([repository,corpus]){ParsingLimits=new(4_194_304,2_000_000)});
 if(!resolved.Success){failed++;continue;}
 var parsed=DeckParser.Parse(resolved.Text!,new(4_194_304,2_000_000),collectDeclarations:true);if(parsed.Declarations is not {} catalog){failed++;continue;}
 foreach(var instance in catalog.XspiceInstances)
 {
  var lookup=catalog.Lookup(DeclarationKind.Model,instance.Model.Text,instance.ScopeId);
  if(lookup.Status!=NameLookupStatus.Found||lookup.Candidates.Single().ModelFamily!="summer")continue;
  var model=lookup.Candidates.Single();
  var preparation=XspiceSummerLowering.Prepare(catalog,instance,ParameterEvaluator.Evaluate(catalog));
  rows.Add(new{prepared=preparation.Success,diagnostics=preparation.Diagnostics.Select(d=>d.Code),scope,path=relative,root_sha256=hash,name=instance.Name,lexical_scope=instance.ScopeId,ports=instance.Ports.Select(p=>p.Original.Text),model=model.Name,parameters=model.Parameters.Select(p=>new{p.Name,value=p.Value.Text})});
 }
}
File.WriteAllText(output,JsonSerializer.Serialize(new{scope="Generated lexical summer inspection of literal-selected frozen roots; inactive/repeated sources included",total,candidates,resource_or_syntax_refused=failed,input_changed=changed,instances=rows.Count,agrees_with_prior_full_census=rows.Count==12,rows},new JsonSerializerOptions{WriteIndented=true}));
