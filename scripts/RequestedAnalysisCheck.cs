#:property PublishAot=false
#:project ../dotnet/Simulator.Frontend.Tests/Simulator.Frontend.Tests.csproj
using System.Reflection;
using FsCheck;
using Microsoft.FSharp.Core;
using Simulator.Frontend.Tests;
// Qualification-only runner: calls the existing xUnit assertions and seeded FsCheck properties
// directly, so the executed inventory is independent of VSTest's observed missing methods.
if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE"))||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")))throw new ArgumentException("Set existing engine, ngspice and requested artifact paths.");
var passed=new List<string>();
foreach(var type in new[]{typeof(RequestedAnalysisTests),typeof(RequestedAnalysisNativeTests)}){
 var instance=Activator.CreateInstance(type)!;
 foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){
  var attributes=method.GetCustomAttributesData();
  if(attributes.Any(a=>a.AttributeType==typeof(FsCheck.Xunit.PropertyAttribute))){
   var seed=method.Name.StartsWith("Arbitrary",StringComparison.Ordinal)?2359UL:2357UL;
   var configuration=Config.QuickThrowOnFailure.WithMaxTest(100).WithReplay(FSharpOption<Replay>.Some(new(new Rnd(seed,20261103),FSharpOption<int>.None)));
   Check.Method(configuration,method,FSharpOption<object>.Some(instance));passed.Add(method.Name+" [FsCheck100 seed="+seed+",20261103]");continue;
  }
  var data=attributes.Where(a=>a.AttributeType==typeof(Xunit.InlineDataAttribute)).ToArray();
  var rows=data.Length==0?[Array.Empty<object?>()]:data.Select(a=>((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).Select(v=>v.Value).ToArray()).ToArray();
  foreach(var row in rows){
   try{var result=method.Invoke(instance,row);if(result is Task task)await task;}
   catch(TargetInvocationException e) when(e.InnerException!=null){throw new InvalidOperationException(method.Name,e.InnerException);}
   passed.Add(method.Name+"("+string.Join(',',row.Select(v=>v?.ToString()))+")");
  }
 }
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="passed",cases=passed.Count,checks=passed,limitations="Qualification runner uses xUnit assertions directly; production engine/native oracle remain independent processes. Managed runtime, not NativeAOT."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
