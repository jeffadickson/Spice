param([Parameter(Mandatory)][string]$Analyzer,
      [Parameter(Mandatory)][string]$ArtifactDirectory,
      [Parameter(Mandatory)][string]$FrontendSource)

BeforeAll {
    if (!(Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) { throw 'Artifact parent missing.' }
    if (!(Test-Path -LiteralPath $Analyzer -PathType Leaf)) { throw 'Build Rank-RequestedCorpus.cs first.' }
    if (!(Test-Path -LiteralPath $FrontendSource -PathType Leaf)) { throw 'Frontend source missing.' }
    function New-AuditManifest {
        $dotnet=Split-Path -Parent (Split-Path -Parent $FrontendSource)
        $root=Split-Path -Parent $dotnet
        $manifest=@{}
        foreach ($project in @('Simulator.Syntax','Simulator.Client','Simulator.Frontend')) {
            $directory=Join-Path $dotnet $project
            if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw 'Dependency source missing.' }
            $files=@(Get-ChildItem -LiteralPath $directory -File | Where-Object Extension -in @('.cs','.csproj','.json'))
            if ($project -eq 'Simulator.Syntax') { $files+=Get-ChildItem -LiteralPath (Join-Path $directory 'Grammar') -Filter '*.g4' -File }
            foreach ($file in $files) {
                $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
                $manifest["/frozen/$relative"]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        return $manifest
    }
}

Describe 'Offline corpus ranking evidence admission' {
    It 'Canonicalizes SPICE directive spelling without predicting executable circuits' {
        $case=Join-Path $ArtifactDirectory 'directive-case'
        if (Test-Path -LiteralPath $case) { throw 'Fresh fixture directory required.' }
        New-Item -ItemType Directory -Path $case | Out-Null
        $rows=@(); $index=0
        foreach ($spelling in @('.option', '.OPTIONS')) {
            $folder=Join-Path $case ('cases/{0:D4}' -f $index)
            if (!(Test-Path -LiteralPath $case -PathType Container)) { throw 'Fixture parent missing.' }
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            $expanded=Join-Path $folder 'expanded.cir'
            Set-Content -LiteralPath $expanded -Value "fixture`nV1 n 0 1`nR1 n 0 1k`n$spelling reltol=1e-3`n.end"
            $hash=(Get-FileHash -LiteralPath $expanded -Algorithm SHA256).Hash.ToLowerInvariant()
            $rows+=@{id="root$index";input_sha256=$hash;expected_sha256=('a'*64);status='requested_preparation_refused';diagnostics=@('unsupported_physical_directive');expanded_syntax_status='accepted_structure';expanded_sha256=$hash}
            $index++
        }
        # Synthetic completed metadata exercises reporting glue only; it is not
        # genuine native qualification and is isolated from the real census.
        $report=@{state='completed_with_results';provenance_valid=$true;inventory_sha256='fixture';source_commit='fixture';rows=$rows;source_hashes=@{}}
        $report.source_hashes=New-AuditManifest
        $summary=Join-Path $case 'summary.json'; $output=Join-Path $case 'ranking.json'
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summary
        & $Analyzer $case $summary $output | Out-Null
        $LASTEXITCODE | Should -Be 0
        $ranking=Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $directives=@($ranking.structural_observations | Where-Object key -like 'directive_policy/*')
        $directives.Count | Should -Be 1
        $directives[0].key | Should -Be 'directive_policy/unqualified_directive/.options'
        $directives[0].distinct_inputs | Should -Be 2
        $ranking.primary_diagnostics[0].unique_contents | Should -Be 1
        $ranking.transitions.Count | Should -Be 0
    }

    It 'Refuses <Mode> without publishing a ranking' -ForEach @(
        @{Mode='running'; Message='Completed, provenance-valid'},
        @{Mode='invalid-provenance'; Message='Completed, provenance-valid'},
        @{Mode='baseline-running'; Message='Baseline must have matching'},
        @{Mode='inventory-mismatch'; Message='Baseline must have matching'},
        @{Mode='missing-manifest'; Message='Frozen frontend source manifest missing'},
        @{Mode='partial-manifest'; Message='Frozen frontend source manifest incomplete'},
        @{Mode='root-set-mismatch'; Message='Baseline root identity set differs'},
        @{Mode='source-mismatch'; Message='Audit frontend source differs'},
        @{Mode='expanded-missing'; Message='Expanded input changed'},
        @{Mode='existing-output'; Message='Fresh output required'}
    ) {
        $case=Join-Path $ArtifactDirectory $Mode
        if (Test-Path -LiteralPath $case) { throw 'Fresh fixture directory required.' }
        New-Item -ItemType Directory -Path $case | Out-Null
        $baseline=@{state='completed_with_results';provenance_valid=$true;inventory_sha256='inventory';rows=@()}
        $report=@{state='completed_with_results';provenance_valid=$true;inventory_sha256='inventory';source_commit='fixture';rows=@();source_hashes=@{}}
        switch ($Mode) {
            'running' { $report.state='running' }
            'invalid-provenance' { $report.provenance_valid=$false }
            'baseline-running' { $baseline.state='running' }
            'inventory-mismatch' { $baseline.inventory_sha256='different' }
            'source-mismatch' { $report.source_hashes['/frozen/dotnet/Simulator.Frontend/CircuitReadiness.cs']='incorrect' }
            'partial-manifest' { $report.source_hashes['/frozen/dotnet/Simulator.Frontend/CircuitReadiness.cs']=(Get-FileHash -LiteralPath $FrontendSource -Algorithm SHA256).Hash.ToLowerInvariant() }
            'root-set-mismatch' { $baseline.rows=@(@{id='omitted-root'}); $report.source_hashes=New-AuditManifest }
            'expanded-missing' {
                $report.source_hashes=New-AuditManifest
                $row=@{id='root';input_sha256='content';expected_sha256='content';status='requested_preparation_refused';diagnostics=@('gap');expanded_syntax_status='accepted_structure';expanded_sha256='missing'}
                $report.rows=@($row); $baseline.rows=@($row)
            }
        }
        $summary=Join-Path $case 'summary.json'; $old=Join-Path $case 'baseline.json'; $output=Join-Path $case 'ranking.json'
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summary
        $baseline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $old
        if ($Mode -eq 'existing-output') { Set-Content -LiteralPath $output -Value 'preserve me' }
        # Arguments are passed directly; report contents never become commands.
        $errorFile=Join-Path $case 'stderr.log'
        & $Analyzer $case $old $output 2> $errorFile | Out-Null
        $LASTEXITCODE | Should -Not -Be 0
        (Get-Content -LiteralPath $errorFile -Raw) | Should -Match ([regex]::Escape($Message))
        if ($Mode -eq 'existing-output') { (Get-Content -LiteralPath $output -Raw).Trim() | Should -Be 'preserve me' }
        else { Test-Path -LiteralPath $output | Should -BeFalse }
    }
}
