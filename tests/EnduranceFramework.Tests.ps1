# A tiny synthetic crate exercises the real supervisor without presenting its
# fixture counters as simulator, cargo-mutants or Stryker qualification evidence.
Describe 'Endurance generated-framework orchestration' {
    It 'archives framework categories then runs seeded batches in a clean source fixture' {
        $root=Split-Path $PSScriptRoot -Parent
        $fixture=Join-Path $root ('work/framework-supervisor-tests/'+[guid]::NewGuid().ToString('N'))
        foreach($folder in @('src','tests','examples','compat','dotnet','scripts')){New-Item -ItemType Directory -Force (Join-Path $fixture $folder)|Out-Null}
        foreach($file in @('rust-toolchain.toml','global.json','dotnet-tools.json','Makefile','scripts/Endurance.cs','scripts/Run-Endurance.ps1','scripts/HostTools.ps1')){Copy-Item (Join-Path $root $file) (Join-Path $fixture $file)}
        Set-Content (Join-Path $fixture 'Cargo.toml') "[package]`nname='supervisor-fixture'`nversion='0.1.0'`nedition='2021'`n[workspace]"
        Set-Content (Join-Path $fixture 'Cargo.lock') "version = 4`n[[package]]`nname = 'supervisor-fixture'`nversion = '0.1.0'"
        Set-Content (Join-Path $fixture 'src/lib.rs') '// Synthetic supervisor fixture; no simulator equations.'
        Set-Content (Join-Path $fixture 'tests/endurance.rs') '#[test] #[ignore] fn seeded_batch(){ println!("ENDURANCE cases=1024 seed={}",std::env::var("SPICE_CAMPAIGN_SEED").unwrap()); }'
        Set-Content (Join-Path $fixture '.gitignore') "/work/`n/target/"
        Set-Content (Join-Path $fixture 'scripts/Mutation.cs') 'System.IO.Directory.CreateDirectory("evidence");System.IO.File.WriteAllText("evidence/mutation.json","{\"killed\":0,\"total\":0}");'
        $stub=@'
#:property PublishAot=false
using System.Text.Json;
Directory.CreateDirectory("work");
var shard=args[Array.IndexOf(args,"--shard")+1];
object Tool(string version)=>new{version,status="completed",report_sha256=(string?)null,candidates=0,killed=0,survived=0,timed_out=0,invalid=0,not_covered=0,pending=0,other=0,ignored=0,unattempted=0};
var path=Path.GetFullPath("work/synthetic-framework.json");
File.WriteAllText(path,JsonSerializer.Serialize(new{status="completed",shard,cargo_mutants=Tool("27.1.0"),stryker=Tool("5.0.0")}));
Console.WriteLine("Framework summary: "+path);
'@
        Set-Content (Join-Path $fixture 'scripts/FrameworkMutations.cs') $stub
        & git init -b main $fixture *> $null
        & git -C $fixture add .
        & git -C $fixture -c user.name=Fixture -c user.email=fixture@localhost commit -m fixture *> $null
        Push-Location $fixture
        try{$output=& dotnet run --file scripts/Endurance.cs -- --minutes 0.75 --seed 42 2>&1|Out-String;$code=$LASTEXITCODE}finally{Pop-Location}
        if($code -ne 0){throw $output}
        $marker=[regex]::Match($output,'(?m)^Campaign summary: (.+)\r?$')
        $path=$marker.Groups[1].Value.Trim()
        $report=Get-Content -Raw $path|ConvertFrom-Json
        $report.status|Should Match '^budget_exhausted'
        $report.source_dirty|Should Be $false
        $report.mutation_status|Should Be 'passed'
        $report.framework_mutation_status|Should Be 'completed'
        $report.framework_shard|Should Be '10/16'
        @($report.framework_mutation_counts.PSObject.Properties).Count|Should Be 20
        $report.framework_tool_versions.stryker|Should Be '5.0.0'
        $report.cases|Should BeGreaterThan 0
        $native=Join-Path (Split-Path $path -Parent) 'framework-mutations.json'
        $report.framework_summary_sha256|Should Be (Get-FileHash $native).Hash.ToLowerInvariant()
    }
}
