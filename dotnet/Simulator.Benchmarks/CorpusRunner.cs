using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulator.Client;
namespace Simulator.Benchmarks;
internal static class CorpusRunner
{
    internal static async Task Run(CorpusReport report,string engine,string ngspice,int budgetSeconds,CancellationToken cancel,Action checkpoint)
    {
        var clock=Stopwatch.StartNew();
        async Task<ProcessResult> Child(string exe,string[] args,string? input,string cwd)
        {
            var left=budgetSeconds-clock.Elapsed.TotalSeconds;
            if(left<=0)throw new TimeoutException("Whole corpus budget exhausted.");
            return await ProcessRunner.Run(exe,args,input,cwd,TimeSpan.FromSeconds(Math.Min(30,left)),cancel);
        }
        for(var index=0;index<report.Rows.Count;index++)
        {
            var row=report.Rows[index];
            if(row.Status!="pending")continue;
            if(cancel.IsCancellationRequested||clock.Elapsed.TotalSeconds>=budgetSeconds)
            {foreach(var pending in report.Rows.Skip(index).Where(r=>r.Status=="pending")){pending.Status=cancel.IsCancellationRequested?"cancelled":"budget_exhausted";pending.Diagnostic="Not attempted before campaign boundary.";}break;}
            var folder=Path.Combine(report.RawDirectory,index.ToString("D5"));Directory.CreateDirectory(folder);
            try
            {
                var path=Path.GetFullPath(row.Path,report.CorpusRoot);var bytes=Runner.Input(path);
                if(row.Sha256!=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())throw new IOException("Deck changed since inventory.");
                var text=new UTF8Encoding(false,true).GetString(bytes);
                var imported=await Child(engine,["--import-ngspice"],text,folder);
                File.WriteAllText(Path.Combine(folder,"import.stdout.json"),imported.Output);File.WriteAllText(Path.Combine(folder,"import.stderr"),imported.Error);
                using var importedJson=JsonDocument.Parse(imported.Output);
                if(imported.ExitCode!=0)
                {
                    var error=importedJson.RootElement.GetProperty("error");
                    var code=error.GetProperty("code").GetString();
                    row.Status=code=="unsupported"?"unsupported":code=="resource_exhausted"?"resource_rejected":"import_rejected";
                    row.Diagnostic=CorpusPolicy.Clip(code+": "+error.GetProperty("message").GetString());
                    continue;
                }
                var request=JsonNode.Parse(imported.Output) as JsonObject??throw new InvalidDataException("Importer did not return a request.");
                var analysis=request["analysis"]?["kind"]?.GetValue<string>();
                var kind=analysis switch {"operating_point" or "operating_point_newton"=>"op","ac"=>"ac","transient_configured"=>"transient",_=>throw new InvalidDataException("Unknown imported analysis")};
                row.Analysis=analysis;row.RequestSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imported.Output))).ToLowerInvariant();
                var nodes=request["circuit"]?["nodes"]?.AsArray().Skip(1).Select(n=>n!.GetValue<string>()).ToArray()??throw new InvalidDataException("Missing nodes");
                var elements=request["circuit"]?["elements"]?.AsArray()??throw new InvalidDataException("Missing elements");
                var voltageSources=elements.Where(e=>e?["kind"]?.GetValue<string>()=="voltage")
                    .Select(e=>e!["id"]!.GetValue<string>()).ToArray();
                if(CorpusPolicy.UnsafeReference(text)){row.Status="unsupported_reference_profile";row.Diagnostic="External resources/control execution require a separate qualified oracle profile.";continue;}
                // A defined sentinel keeps mutations of the catch continuation
                // executable instead of triggering blanket compiler rollback.
                // Normal preparation failures leave the row before any use.
                var observed=string.Empty;
                try {observed=CorpusPolicy.ObservedDeck(text,kind,nodes,voltageSources);}
                catch(InvalidDataException e){row.Status="comparison_unavailable";row.Diagnostic=e.Message;continue;}
                var deck=Path.Combine(folder,"observed.cir");File.WriteAllText(deck,observed,new UTF8Encoding(false));
                row.ExecutedDeckSha256=Runner.Hash(deck);
                row.ObservationPolicy="Removed logical .print/.plot/.probe/.end records; appended named non-ground node voltages and independent voltage-source branch currents, p-to-n positive, and .end; added .op only for inferred default OP. Equations/analysis parameters otherwise retained.";
                var log=Path.Combine(folder,"ngspice.log");string? reference=null;string? referenceError=null;
                try
                {
                    var ng=await Child(ngspice,["-n","-b","-o",log,deck],null,folder);
                    File.WriteAllText(Path.Combine(folder,"ngspice.stdout"),ng.Output);File.WriteAllText(Path.Combine(folder,"ngspice.stderr"),ng.Error);
                    if(ng.ExitCode!=0)throw new IOException($"ngspice exit {ng.ExitCode}");
                    if(!File.Exists(log)||new FileInfo(log).Length>8_388_608)throw new InvalidDataException("Missing/oversized reference log");
                    reference=File.ReadAllText(log);
                }
                catch(Exception e)when(e is IOException or InvalidDataException or TimeoutException){referenceError=CorpusPolicy.Clip(e.Message);}
                foreach(var backend in new[]{"lu","faer"})
                {
                    try
                    {
                        request["backend"]=backend;var payload=JsonSerializer.Serialize<JsonNode>(request,BenchmarkJson.Default.JsonNode);
                        File.WriteAllText(Path.Combine(folder,backend+".request.json"),payload);
                        var result=await Child(engine,[],payload,folder);
                        File.WriteAllText(Path.Combine(folder,backend+".stdout.json"),result.Output);File.WriteAllText(Path.Combine(folder,backend+".stderr"),result.Error);
                        if(result.ExitCode!=0){row.Backends.Add(new(backend,"simulator_failed",CorpusPolicy.Clip(result.Output),[]));continue;}
                        using var doc=JsonDocument.Parse(result.Output);var v=doc.RootElement;
                        var manifest=new SimulationResponse(v.GetProperty("schema_version").GetString()!,v.GetProperty("status").GetString()!,v.GetProperty("complete").GetBoolean(),v.Clone()).ReadManifest();
                        var expected=backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu";
                        if(manifest.Backend.Id!=expected||manifest.Backend.Gpu||manifest.Backend.Placement!="cpu")throw new InvalidDataException("Wrong CPU provider");
                        if(reference is null){row.Backends.Add(new(backend,"executed_uncompared",referenceError??"No reference",[]));continue;}
                        var signals=new List<CorpusSignal>();
                        foreach(var signal in CorpusPolicy.ObservationSignals(nodes,voltageSources))
                        {
                            var observable=signal.Name;var column=CorpusPolicy.SignalColumn(manifest.Signals,signal);
                            var absolute=CorpusPolicy.AbsoluteBound(report,signal);
                            var refs=Tables.Parse(reference,kind,observable);
                            var actual=kind=="ac"?manifest.AcSamples.Select(p=>new Point(p.FrequencyHz,p.Values[column].Real,p.Values[column].Imag)).ToArray():
                                manifest.Samples.Select(p=>new Point(kind=="op"?0:p.TimeS,p.Values[column],0)).ToArray();
                            var selected=CorpusPolicy.Selected(actual);var failed=0;var max=-1d;Point? worst=null;Point? worstExpected=null;
                            foreach(var point in selected)
                            {
                                var target=CorpusPolicy.Reference(refs,point.Coordinate,kind);
                                var difference=System.Numerics.Complex.Abs(new(target.Real-point.Real,target.Imag-point.Imag));
                                if(!Tables.Within(target,point,absolute,report.RelativeTolerance))failed++;
                                if(!double.IsFinite(difference))throw new InvalidDataException("Nonfinite discrepancy");
                                if(difference>max){max=difference;worst=point;worstExpected=target;}
                            }
                            signals.Add(new(observable,signal.Unit,absolute,selected.Length,failed,max,worst!.Coordinate,worstExpected!.Real,worstExpected.Imag,worst.Real,worst.Imag));
                        }
                        var pass=signals.Count>0&&signals.All(s=>s.FailedPoints==0);
                        row.Backends.Add(new(backend,pass?"compared_pass":"comparison_failed",pass?"Selected named points agree":"Numerical discrepancy exceeds declared bound",signals));
                    }
                    catch(Exception e)when(e is IOException or InvalidDataException or JsonException or TimeoutException or OperationCanceledException)
                    {row.Backends.Add(new(backend,e is TimeoutException?"timeout":"comparison_failed",CorpusPolicy.Clip(e.GetType().Name+": "+e.Message),[]));}
                }
                row.Status=row.Backends.Count==2&&row.Backends.All(b=>b.Status=="compared_pass")?"compared_pass":
                    row.Backends.Any(b=>b.Status=="simulator_failed")?"simulator_failed":reference is null?"oracle_failed":"comparison_failed";
                row.Diagnostic=row.Status=="compared_pass"?"Both CPU providers agree at selected named voltage/current points":
                    CorpusPolicy.Clip(string.Join("; ",row.Backends.Where(b=>b.Status!="compared_pass").Select(b=>b.Backend+": "+b.Detail)));
                if(row.Status=="compared_pass" && row.FeatureHints.Contains("multiple_declared_analyses",StringComparer.Ordinal))
                {row.Status="partial_comparison";row.Diagnostic=$"Both CPU providers agree for selected {analysis}; additional declared analyses are not compared by this one-analysis importer profile.";}
                if(Runner.Hash(deck)!=row.ExecutedDeckSha256||Runner.Hash(path)!=row.Sha256)throw new IOException("Input changed during execution");
            }
            catch(Exception e)when(e is IOException or InvalidDataException or JsonException or TimeoutException or OperationCanceledException or DecoderFallbackException)
            {row.Status=e is TimeoutException?"timeout":e is OperationCanceledException?"cancelled":"input_or_harness_failed";row.Diagnostic=CorpusPolicy.Clip(e.GetType().Name+": "+e.Message);}
            finally{checkpoint();}
        }
        report.State=cancel.IsCancellationRequested?"cancelled":report.Rows.Any(r=>r.Status=="budget_exhausted")?"budget_exhausted":"completed_with_results";
        report.FinishedUtc=DateTimeOffset.UtcNow.ToString("O");checkpoint();
    }
}
