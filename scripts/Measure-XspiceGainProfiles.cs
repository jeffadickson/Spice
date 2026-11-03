#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Syntax;
using Simulator.Frontend;

// This census reads frozen expanded sources and counts lexical A-card families.
// It neither traverses active hierarchy nor claims any complete circuit solves.
if(args.Length!=4)throw new ArgumentException("Expected repository, ngspice corpus, frozen inventory JSON and fresh JSON output file");
var repository=Path.GetFullPath(args[0]);var corpus=Path.GetFullPath(args[1]);var inventory=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
if(!Directory.Exists(repository)||!Directory.Exists(corpus)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException();
if(File.Exists(output))throw new IOException("Fresh output required");
using var input=JsonDocument.Parse(File.ReadAllText(inventory));
var rows=new List<object>();var families=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var profiles=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var accepted=0;var files=0;var gainRoots=0;var refused=0;var changed=0;
var behavioralRows=new List<object>();var behavioralProfiles=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var behavioralRoots=0;var preparedBehavioralRoots=0;
var limits=new ParserLimits(4_194_304,2_000_000);
foreach(var item in input.RootElement.GetProperty("rows").EnumerateArray())
{
 var scope=item.GetProperty("scope").GetString();var relative=item.GetProperty("path").GetString()!;
 if(scope is not ("repository" or "ngspice"))throw new InvalidDataException("Unknown inventory scope");
 var selectedRoot=scope=="repository"?repository:corpus;var path=Path.GetFullPath(relative,selectedRoot);
 if(!path.StartsWith(Path.TrimEndingDirectorySeparator(selectedRoot)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Path escapes selected root");
 files++;
 if(!File.Exists(path)||!Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))).Equals(item.GetProperty("result").GetProperty("Sha256").GetString(),StringComparison.OrdinalIgnoreCase)){changed++;continue;}
 var resolved=ResourceResolver.Resolve(path,new([repository,corpus]){ParsingLimits=limits});
 if(!resolved.Success){refused++;continue;}
 var syntax=DeckParser.Parse(resolved.Text!,limits,collectDeclarations:true);if(syntax.Status!="accepted_structure")continue;accepted++;
 var catalog=syntax.Declarations!;var hasGain=false;
 foreach(var instance in catalog.XspiceInstances)
 {
  var lookup=catalog.Lookup(DeclarationKind.Model,instance.Model.Text,instance.ScopeId);
  var family=lookup.Status==NameLookupStatus.Found?lookup.Candidates.Single().ModelFamily??"unknown":"unbound";
  families[family]=families.GetValueOrDefault(family)+1;
  if(!family.Equals("gain",StringComparison.OrdinalIgnoreCase))continue;hasGain=true;
  var prepared=XspiceGainLowering.Prepare(catalog,instance,ParameterEvaluator.Evaluate(catalog,instance.ScopeId));
  var profile=prepared.Success?"prepared_lexical_defaults":prepared.Diagnostics[0].Code;profiles[profile]=profiles.GetValueOrDefault(profile)+1;
  rows.Add(new{scope,path=relative,root_sha256=resolved.Sources[0].Sha256,name=instance.Name,lexical_scope=instance.ScopeId,profile,model=instance.Model.Text,gain=prepared.Gain?.Gain,offset=prepared.Gain?.Offset});
 }
 if(hasGain)gainRoots++;
 var hasBehavior=false;var preparedBehavior=false;
 foreach(var source in catalog.BehavioralSources)
 {
  hasBehavior=true;
  var result=BehavioralSourceLowering.Prepare(catalog,source,ParameterEvaluator.Evaluate(catalog,source.ScopeId));
  var profile=result.Success?"prepared_affine_lexical_defaults":result.Diagnostics[0].Code;
  behavioralProfiles[profile]=behavioralProfiles.GetValueOrDefault(profile)+1;
  preparedBehavior|=result.Success;
  behavioralRows.Add(new{scope,path=relative,root_sha256=resolved.Sources[0].Sha256,name=source.Name,lexical_scope=source.ScopeId,profile,quantity=source.Quantity.ToString(),terms=result.Source?.Terms.Length,offset=result.Source?.Offset});
 }
 if(hasBehavior)behavioralRoots++;
 if(preparedBehavior)preparedBehavioralRoots++;
}
File.WriteAllText(output,JsonSerializer.Serialize(new{scope="lexical generated A-card census over resource-expanded frozen inventory; no hierarchy activation, instance override qualification or full-circuit simulation",files,accepted,resource_refused=refused,input_changed=changed,inventory_sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(inventory))),gain_roots=gainRoots,families,profiles,rows,behavioral_roots=behavioralRoots,prepared_behavioral_roots=preparedBehavioralRoots,behavioral_profiles=behavioralProfiles,behavioral_rows=behavioralRows},new JsonSerializerOptions{WriteIndented=true}));
