#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>Bounded process-wall timing of frozen original-deck qualification artifacts.</summary>
/// <remarks>Ours consumes prepared JSON; ngspice consumes SPICE text. The pinned RSpice
/// library may be a debug build. Scope/build differences are preserved, never a claimed speedup.</remarks>
public static class OriginalDeckTimings
{
    private sealed record Sample(bool Completed,double WallMs,int Exit,bool Timeout,string Error,JsonElement? Detail);
    /// <summary>One warmup and repeated cold child processes; validation remains outside measured execution.</summary>
    public static void Run(string summary,string corpus,string engine,string ngspice,string rspice,string output,int repetitions,bool onlyAgreed=false)
    {
        if(repetitions<1||repetitions>5)throw new ArgumentOutOfRangeException(nameof(repetitions));
        foreach(var file in new[]{summary,engine,ngspice,rspice})if(!File.Exists(file))throw new FileNotFoundException(file);
        if(!Directory.Exists(corpus)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException();
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Fresh output required");
        using var source=JsonDocument.Parse(File.ReadAllText(summary));var root=source.RootElement;
        if(root.GetProperty("state").GetString()!="completed_with_results"||!root.GetProperty("provenance_valid").GetBoolean()
            ||string.IsNullOrWhiteSpace(root.GetProperty("source_commit").GetString()))throw new InvalidDataException("Completed qualification provenance required");
        var cases=root.GetProperty("rows").EnumerateArray().ToArray();if(cases.Length>32)throw new InvalidDataException("Bounded cohort required");
        var selectedCount=cases.Count(row=>!onlyAgreed||row.GetProperty("native_agreed").ValueKind==JsonValueKind.True);
        if(selectedCount==0)throw new InvalidDataException("No agreeing cases to measure");
        Directory.CreateDirectory(output);var results=new List<object>();var qualification=Path.GetDirectoryName(summary)!;
        for(int i=0;i<cases.Length;i++)
        {
            var row=cases[i];if(onlyAgreed&&row.GetProperty("native_agreed").ValueKind!=JsonValueKind.True)continue;var relative=row.GetProperty("path").GetString()!;
            var original=Path.GetFullPath(relative,corpus);var prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(corpus))+Path.DirectorySeparatorChar;
            if(!original.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(original))throw new InvalidDataException("Original escapes corpus");
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))).ToLowerInvariant();
            if(hash!=row.GetProperty("input_sha256").GetString())throw new InvalidDataException("Original hash changed");
            var caseDir=Path.Combine(output,i.ToString("D4"));Directory.CreateDirectory(caseDir);
            var requests=Directory.GetFiles(Path.Combine(qualification,"cases",i.ToString("D4")),"*.request.json",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            var providers=new Dictionary<string,object>();
            foreach(var provider in new[]{"ours_prepared_native","ngspice_original","rspice_original"})
            {
                var samples=new List<Sample>();Sample? warmup=null;
                for(int repeat=-1;repeat<repetitions;repeat++)
                {
                    var runDir=Path.Combine(caseDir,provider+"-"+(repeat<0?"warmup":repeat.ToString()));Directory.CreateDirectory(runDir);
                    Sample sample;
                    if(provider=="ours_prepared_native")
                    {
                        if(!row.GetProperty("requested_executed").GetBoolean()||requests.Length==0)
                            sample=new(false,0,-1,false,"No completed frozen requests",null);
                        else
                        {
                            double wall=0;bool ok=true;int exit=0;bool timeout=false;string error="";
                            for(int n=0;n<requests.Length;n++)
                            {
                                var item=Execute(engine,Array.Empty<string>(),File.ReadAllText(requests[n]),runDir,n.ToString());wall+=item.WallMs;
                                using var document=ReadOutput(runDir,n.ToString());
                                ok&=item.Completed&&document?.RootElement.TryGetProperty("complete",out var complete)==true&&complete.GetBoolean();
                                if(!ok){exit=item.Exit;timeout=item.Timeout;error=item.Error;break;}
                            }
                            sample=new(ok,wall,exit,timeout,error,null);
                        }
                    }
                    else if(provider=="ngspice_original")
                    {
                        var args=new List<string>{"-n","-D","filetype=ascii","-b","-r",Path.Combine(runDir,"native.raw"),"-o",Path.Combine(runDir,"native.log")};
                        var dialect=row.GetProperty("operations")[0].GetProperty("native_dialect").GetString();
                        if(dialect!="default"&&!string.IsNullOrWhiteSpace(dialect)){args.Add("-D");args.Add("ngbehavior="+dialect);}
                        args.Add(original);sample=Execute(ngspice,args.ToArray(),null,runDir,"native");
                        sample=sample with{Completed=sample.Completed&&File.Exists(Path.Combine(runDir,"native.raw"))};
                    }
                    else
                    {
                        sample=Execute(rspice,new[]{original},null,runDir,"rspice");using var doc=ReadOutput(runDir,"rspice");
                        if(doc!=null)sample=sample with{Detail=doc.RootElement.Clone(),Completed=sample.Completed&&doc.RootElement.GetProperty("state").GetString()=="completed"&&doc.RootElement.GetProperty("operations").GetInt32()==row.GetProperty("operations").GetArrayLength()};
                        else sample=sample with{Completed=false};
                    }
                    if(repeat<0){warmup=sample;if(!sample.Completed)break;}
                    else samples.Add(sample);
                }
                var sorted=samples.Where(s=>s.Completed).Select(s=>s.WallMs).OrderBy(x=>x).ToArray();
                providers[provider]=new{warmup,samples,median_ms=sorted.Length==repetitions?(double?)Median(sorted):null};
            }
            results.Add(new{path=relative,input_sha256=hash,qualification_status=row.GetProperty("status").GetString(),request_processes=requests.Length,providers});
            Write();
        }
        void Write()=>File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{state=results.Count==selectedCount?"completed":"running",cohort_total=cases.Length,selected_count=selectedCount,only_agreed=onlyAgreed,qualification_sha256=Hash(summary),source_commit=root.GetProperty("source_commit").GetString(),repetitions,scope="Child process wall time. Ours excludes SPICE preparation; ngspice includes parser/RAW; RSpice includes parser with cached debug core. No reference invocation inside RSpice timing probe; comparison outside time. No automatic speedup claim.",engine_sha256=Hash(engine),ngspice_sha256=Hash(ngspice),rspice_probe_sha256=Hash(rspice),rows=results},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static double Median(double[] sorted)=>0.5*sorted[(sorted.Length-1)/2]+0.5*sorted[sorted.Length/2];
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    private static JsonDocument? ReadOutput(string directory,string name){try{return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,name+".stdout")));}catch(JsonException){return null;}}
    private static Sample Execute(string executable,string[] args,string? input,string directory,string name)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input!=null};
        foreach(var arg in args)info.ArgumentList.Add(arg);
        var clock=Stopwatch.StartNew();using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(input!=null){process.StandardInput.Write(input);process.StandardInput.Close();}
        var done=process.WaitForExit(30000);if(!done){process.Kill(true);process.WaitForExit();}
        var text=stdout.GetAwaiter().GetResult();var errors=stderr.GetAwaiter().GetResult();clock.Stop();
        File.WriteAllText(Path.Combine(directory,name+".stdout"),text);File.WriteAllText(Path.Combine(directory,name+".stderr"),errors);
        return new(done&&process.ExitCode==0,clock.Elapsed.TotalMilliseconds,process.ExitCode,!done,errors,null);
    }
}
