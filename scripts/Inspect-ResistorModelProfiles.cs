#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulator.Syntax;
using Simulator.Frontend;

// All frozen roots are inspected. Every reported model/instance is
// resolved through the existing resource policy and generated declaration tree.
if(args.Length is not (4 or 5))throw new ArgumentException("Expected repository, corpus, frozen inventory, fresh output JSON and optional prior full census for focused model-root revalidation");
var repository=Path.GetFullPath(args[0]);var corpus=Path.GetFullPath(args[1]);var inventory=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
foreach(var directory in new[]{repository,corpus,Path.GetDirectoryName(output)!})if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);
if(File.Exists(output))throw new IOException("Fresh output required");
using var data=JsonDocument.Parse(File.ReadAllText(inventory));
// A prior full census can select roots for rechecking changed numerical
// admission. Every frozen input hash is still checked; syntax-universe changes
// require a fresh full scan rather than this explicitly labelled selection.
HashSet<string>? selected=null;
if(args.Length==5)
{
 using var prior=JsonDocument.Parse(File.ReadAllText(args[4]));
 selected=prior.RootElement.GetProperty("rows").EnumerateArray().Select(row=>row.GetProperty("scope").GetString()+"|"+row.GetProperty("path").GetString()).ToHashSet(StringComparer.Ordinal);
}
var rows=new List<object>();var candidates=0;var failed=0;var changed=0;var total=0;var encodingRefused=0;
foreach(var item in data.RootElement.GetProperty("rows").EnumerateArray())
{
 total++;var scope=item.GetProperty("scope").GetString();var relative=item.GetProperty("path").GetString()!;
 if(scope is not ("repository" or "ngspice"))throw new InvalidDataException("Unknown scope");
 var root=scope=="repository"?repository:corpus;var path=Path.GetFullPath(relative,root);
 if(!path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Escaping inventory path");
 var bytes=File.ReadAllBytes(path);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
 if(!hash.Equals(item.GetProperty("result").GetProperty("Sha256").GetString(),StringComparison.OrdinalIgnoreCase)){changed++;continue;}
 string text;try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){encodingRefused++;continue;}
 if(selected!=null&&!selected.Contains(scope+"|"+relative))continue;candidates++;
 var resolved=ResourceResolver.Resolve(path,new([repository,corpus]){ParsingLimits=new(4_194_304,2_000_000)});
 if(!resolved.Success){failed++;continue;}
 var parsed=DeckParser.Parse(resolved.Text!,new(4_194_304,2_000_000),collectDeclarations:true);if(parsed.Declarations is not {} catalog){failed++;continue;}
 var environments=new Dictionary<int,ParameterEvaluation>();
 foreach(var primitive in catalog.Primitives.Where(p=>p.Family=="R"&&p.ResistorModel!=null))
 {
  var syntax=primitive.ResistorModel!;var lookup=catalog.Lookup(DeclarationKind.Model,syntax.Reference.Text,primitive.ScopeId);
  if(lookup.Status!=NameLookupStatus.Found||lookup.Candidates.Single().ModelFamily!="r")continue;
  var model=lookup.Candidates.Single();
  if(!environments.TryGetValue(primitive.ScopeId,out var environment)){environment=ParameterEvaluator.Evaluate(catalog,primitive.ScopeId);environments.Add(primitive.ScopeId,environment);}
  var preparation=ResistorModelLowering.Prepare(catalog,primitive,environment);
  rows.Add(new{scope,path=relative,root_sha256=hash,name=primitive.Name,lexical_scope=primitive.ScopeId,model=model.Name,geometry=syntax.Parameters.Select(p=>new{p.Name,value=p.Value.Text}),model_parameters=model.Parameters.Select(p=>new{p.Name,value=p.Value.Text}),prepared=preparation.Success,profile=preparation.Resistor?.Profile.ToString(),ohms=preparation.Resistor?.Ohms,diagnostics=preparation.Diagnostics.Select(d=>d.Code)});
 }
}
File.WriteAllText(output,JsonSerializer.Serialize(new{scope="Generated lexical R-model inspection of all frozen roots; inactive/repeated sources and default lexical environments included",selection_mode=selected==null?"all frozen roots":"prior resolved R-model roots only; all frozen hashes checked",prior_census=args.Length==5?Path.GetFullPath(args[4]):null,total,candidates,encoding_refused=encodingRefused,resource_or_syntax_refused=failed,input_changed=changed,instances=rows.Count,rows},new JsonSerializerOptions{WriteIndented=true}));
