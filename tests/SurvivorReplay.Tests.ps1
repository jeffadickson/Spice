# Synthetic source contexts test the helper's relocation/atomic rejection only.
Describe 'Bounded survivor inventory relocation' {
    It 'accepts a small cohort without a historical fixed count' {
        $repo=Split-Path $PSScriptRoot -Parent
        if(-not(Test-Path -LiteralPath (Join-Path $repo 'work') -PathType Container)){throw 'Work missing'}
        $fixture=Join-Path $repo ('work/replay-contract-'+[guid]::NewGuid().ToString('N'))
        if(Test-Path -LiteralPath $fixture -PathType Container){throw 'Fixture already exists'}
        New-Item -ItemType Directory -Path $fixture|Out-Null
        foreach($name in @('src','archive','archive/src')){
            $folder=Join-Path $fixture $name
            if(-not(Test-Path -LiteralPath (Split-Path $folder -Parent) -PathType Container)){throw 'Fixture parent missing'}
            if(-not(Test-Path -LiteralPath $folder -PathType Container)){New-Item -ItemType Directory -Path $folder|Out-Null}
        }
        Set-Content -LiteralPath (Join-Path $fixture 'Cargo.toml') '[workspace]'
        $source="fn fixture() {`n let x=1;`n x + 1`n}`n// end"
        Set-Content -LiteralPath (Join-Path $fixture 'src/input.rs') $source
        Set-Content -LiteralPath (Join-Path $fixture 'archive/src/input.rs') $source
        $item=@{id='fixture-1';file='src/input.rs';line=3;column=1;original_name='src/input.rs:3:1: replace + with - in fixture'}
        @{survivors=@($item)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $fixture 'inventory.json')
        Push-Location -LiteralPath $fixture
        try {
            & dotnet run --file (Join-Path $repo 'scripts/Prepare-SurvivorReplay.cs') -- inventory.json archive names.json
            $LASTEXITCODE|Should Be 0
            @(Get-Content -LiteralPath names.json -Raw|ConvertFrom-Json).Count|Should Be 1
            # Duplicate contexts must fail without modifying inventory or publishing names.
            Remove-Item -LiteralPath names.json
            $before=(Get-FileHash -LiteralPath inventory.json -Algorithm SHA256).Hash
            Set-Content -LiteralPath src/input.rs ($source+"`n"+$source)
            & dotnet run --file (Join-Path $repo 'scripts/Prepare-SurvivorReplay.cs') -- inventory.json archive names.json *> $null
            $LASTEXITCODE|Should Not Be 0
            (Test-Path -LiteralPath names.json -PathType Leaf)|Should Be $false
            (Get-FileHash -LiteralPath inventory.json -Algorithm SHA256).Hash|Should Be $before
            foreach($count in @(0,65)) {
                @{survivors=@(for($i=0;$i -lt $count;$i++){$item})}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath inventory.json
                & dotnet run --file (Join-Path $repo 'scripts/Prepare-SurvivorReplay.cs') -- inventory.json archive names.json *> $null
                $LASTEXITCODE|Should Not Be 0
            }
        } finally {Pop-Location}
    }
}
