# Pester integration tests use synthetic reports and a local bare Git remote;
# they exercise publication without claiming those fixtures are simulator runs.
Describe 'Remote qualification evidence publication' {
    $publisher=if ($env:SPICE_REMOTE_PUBLISHER) { $env:SPICE_REMOTE_PUBLISHER } else { Join-Path $PSScriptRoot '../scripts/RemoteQualification.cs' }
    function New-RemoteFixture {
        $base=Join-Path (Split-Path $PSScriptRoot -Parent) ('work/remote-tests/'+[guid]::NewGuid().ToString('N'))
        $source=Join-Path $base 'source';$bare=Join-Path $base 'origin.git';$clone=Join-Path $base 'clone'
        New-Item -ItemType Directory -Force (Join-Path $source 'tests') | Out-Null
        Set-Content (Join-Path $source 'Cargo.toml') '[workspace]'
        Set-Content (Join-Path $source 'Cargo.lock') 'version = 4'
        Set-Content (Join-Path $source 'tests/endurance.rs') '// synthetic publication fixture'
        Set-Content (Join-Path $source '.gitignore') "/work/"
        & git init -b main $source *> $null
        & git -C $source add .
        & git -C $source -c user.name=Fixture -c user.email=fixture@localhost commit -m fixture *> $null
        & git init --bare $bare *> $null
        & git -C $source remote add origin $bare
        & git -C $source push -u origin main *> $null
        & git clone --branch main $bare $clone *> $null
        $sha=(& git -C $clone rev-parse HEAD).Trim()
        $hashes=@{};foreach($file in @('Cargo.toml','Cargo.lock','tests/endurance.rs')){$hashes[$file]=(Get-FileHash (Join-Path $clone $file) -Algorithm SHA256).Hash.ToLowerInvariant()}
        $run=Join-Path $clone 'work/import';New-Item -ItemType Directory -Force $run|Out-Null
        $report=[ordered]@{status='budget_exhausted';source_commit=$sha;source_dirty=$false;started_utc='2026-10-26T00:00:00Z';finished_utc='2026-10-26T10:00:00Z';budget_seconds=36000;elapsed_seconds=36000;cases=1024;batches=1;initial_seed='1';mutation_status='skipped_explicitly';source_hashes=$hashes}
        $report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'summary.json')
        return @{Clone=$clone;Bare=$bare;Sha=$sha;Run=$run;Report=$report}
    }
    function Invoke-Publisher($Fixture,[string[]]$Extra=@()) {
        Push-Location $Fixture.Clone
        try {$output=& dotnet run --file $publisher -- --minutes 600 --label integration --import-run $Fixture.Run @Extra 2>&1|Out-String;$code=$LASTEXITCODE}
        finally {Pop-Location}
        return @{Code=$code;Output=$output}
    }
    It 'publishes only small explicit evidence files to an identifiable branch' {
        $fixture=New-RemoteFixture
        $result=Invoke-Publisher $fixture
        if($result.Code -ne 0){throw $result.Output}
        $branch=(& git -C $fixture.Bare for-each-ref '--format=%(refname:short)' refs/heads/remote-runs/).Trim()
        $expectedPlatform=if($IsWindows){'windows'}else{'linux'}
        $branch | Should Match ("^remote-runs/"+$expectedPlatform+"-x64-integration/")
        (& git -C $fixture.Bare rev-parse refs/heads/main).Trim() | Should Be $fixture.Sha
        $files=@(& git -C $fixture.Bare diff --name-only main $branch)
        $files.Count | Should Be 2
        foreach($file in $files){$file|Should Match '^evidence/remote-runs/.+/(manifest|checksums)\.json$'}
        $manifestPath=($files|Where-Object {$_ -like '*/manifest.json'})[0]
        # Preserve scalar string indexing semantics in both Windows PowerShell/Pwsh.
        $manifestPath=@($files|Where-Object {$_ -like '*/manifest.json'})[0]
        $payload=& git -C $fixture.Bare show ('{0}:{1}' -f $branch,$manifestPath)
        $manifest=($payload -join [Environment]::NewLine)|ConvertFrom-Json
        $manifest.mode|Should Be 'endurance_only_import'
        $manifest.requested_minutes|Should Be 600
        $manifest.coverage.status|Should Be 'not_measured'
        ($payload -join '')|Should Not Match '"snapshot"|"build_workspace"'
    }
    It 'publishes a failed run as failure evidence with a nonzero exit' {
        $fixture=New-RemoteFixture;$fixture.Report.status='failed';$fixture.Report.cases=0
        $fixture.Report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $fixture.Run 'summary.json')
        $result=Invoke-Publisher $fixture
        $result.Code|Should Be 1
        @(& git -C $fixture.Bare for-each-ref '--format=%(refname:short)' refs/heads/remote-runs/).Count|Should Be 1
    }
    It 'rejects stale tested source and malformed hashes' {
        foreach($kind in @('stale','path','hash','wronghash')) {
            $fixture=New-RemoteFixture
            switch($kind){
                stale {$fixture.Report.source_commit='0000000000000000000000000000000000000000'}
                path {$fixture.Report.source_hashes['../secret']='a'*64}
                hash {$fixture.Report.source_hashes['Cargo.toml']='bad'}
                wronghash {$fixture.Report.source_hashes['Cargo.toml']='0'*64}
            }
            $fixture.Report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $fixture.Run 'summary.json')
            (Invoke-Publisher $fixture).Code|Should Not Be 0
            @(& git -C $fixture.Bare for-each-ref '--format=%(refname:short)' refs/heads/remote-runs/).Count|Should Be 0
        }
    }
    It 'rejects unfinished reports and dirty source before publishing' {
        foreach($kind in @('running','dirty')) {
            $fixture=New-RemoteFixture
            if($kind -eq 'running'){$fixture.Report.status='running';$fixture.Report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $fixture.Run 'summary.json')}
            else {Set-Content (Join-Path $fixture.Clone 'untracked.txt') 'preserve me'}
            (Invoke-Publisher $fixture).Code|Should Not Be 0
        }
    }
}

Describe 'Host configuration contracts' {
    It 'updates the case-sensitive PATH variable without changing user profiles' {
        $root=Join-Path (Split-Path $PSScriptRoot -Parent) ('work/host-path-tests/'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force (Join-Path $root '.tools/rust/cargo/bin')|Out-Null
        $before=@{};foreach($name in @('PATH','CARGO_HOME','RUSTUP_HOME','DOTNET_ROOT')){$before[$name]=[Environment]::GetEnvironmentVariable($name)}
        try {
            . (Join-Path $PSScriptRoot '../scripts/HostTools.ps1')
            Import-SpiceTools -Root $root
            $env:PATH.StartsWith((Join-Path $root '.tools/rust/cargo/bin'),[StringComparison]::Ordinal)|Should Be $true
        } finally {foreach($name in $before.Keys){[Environment]::SetEnvironmentVariable($name,$before[$name])}}
    }
    It 'verifies the prepared host without acquiring installers' {
        & (Join-Path $PSScriptRoot '../scripts/Setup-Host.ps1') -SkipNativeTools
        $report=Get-Content -Raw (Join-Path $PSScriptRoot '../work/setup/host.json')|ConvertFrom-Json
        $report.status|Should Be 'ready'
        @($report.acquisitions).Count|Should Be 0
    }
}
