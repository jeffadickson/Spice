using System;
using System.Diagnostics;
using System.Text;

/// <summary>Bounded diagnostic transport that retains partial output on a hung owned child.</summary>
public static class IntelProcessProbe
{
    /// <summary>Runs one trusted executable, drains both pipes and reports timeout separately from a complete response.</summary>
    public static object Run(string executable,string argument,string input,int milliseconds)
    {
        return RunMany(executable,string.IsNullOrEmpty(argument)?Array.Empty<string>():new[]{argument},input,milliseconds);
    }
    /// <summary>Runs separately typed arguments without shell quoting or interpretation.</summary>
    public static object RunMany(string executable,string[] arguments,string input,int milliseconds)
    {
        var output=new StringBuilder();var errors=new StringBuilder();var gate=new object();bool clipped=false;
        using(var process=new Process())
        {
            process.StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in arguments)process.StartInfo.ArgumentList.Add(argument);
            process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate){if(output.Length+e.Data.Length<1048576)output.AppendLine(e.Data);else clipped=true;}};
            process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate){if(errors.Length+e.Data.Length<1048576)errors.AppendLine(e.Data);else clipped=true;}};
            var timer=Stopwatch.StartNew();process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            if(input!=null)process.StandardInput.Write(input);process.StandardInput.Close();
            bool exited=process.WaitForExit(milliseconds);
            if(exited)process.WaitForExit();
            else{process.Kill(true);process.WaitForExit(2000);}
            lock(gate)return new{process_id=process.Id,exited,exit_code=exited?(int?)process.ExitCode:null,elapsed_seconds=timer.Elapsed.TotalSeconds,stdout=output.ToString(),stderr=errors.ToString(),clipped};
        }
    }
}
