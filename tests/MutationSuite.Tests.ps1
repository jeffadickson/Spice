Describe 'Opt-in mutation aggregation inventory' {
    It 'includes every standalone Rust integration source once' {
        $repo=Split-Path $PSScriptRoot -Parent
        if(!(Test-Path -LiteralPath (Join-Path $repo 'tests') -PathType Container)){throw 'Tests directory absent'}
        $source=Get-Content -LiteralPath (Join-Path $repo 'tests/mutation_suite.rs') -Raw
        $actual=@([regex]::Matches($source,'#\[path = "([a-z0-9_]+\.rs)"\]')|ForEach-Object{$_.Groups[1].Value})
        $expected=@(Get-ChildItem -LiteralPath (Join-Path $repo 'tests') -Filter '*.rs' -File|Where-Object Name -ne 'mutation_suite.rs'|Select-Object -ExpandProperty Name)
        $actual.Count|Should -BeGreaterThan 0
        @($actual|Sort-Object -Unique).Count|Should -Be $actual.Count
        @(Compare-Object ($actual|Sort-Object) ($expected|Sort-Object)).Count|Should -Be 0
    }
    It 'requires an explicit feature and keeps normal profile defaults intact' {
        $repo=Split-Path $PSScriptRoot -Parent
        $manifest=Get-Content -LiteralPath (Join-Path $repo 'Cargo.toml') -Raw
        $manifest|Should -Match '(?s)\[\[test\]\].*?name = "mutation_suite".*?required-features = \["mutation-suite"\]'
        $manifest|Should -Match '(?s)\[profile.mutants\].*?inherits = "dev".*?debug = 0'
        $manifest|Should -Not -Match '\[profile\.(dev|test|release)\]'
    }
}

Describe 'Mutation worker command validation' {
    It 'rejects an unsupported profile before tool dispatch' {
        if(!$env:SPICE_FRAMEWORK_TEST_HELPER){Set-ItResult -Skipped -Because 'Set SPICE_FRAMEWORK_TEST_HELPER to the freshly compiled helper';return}
        Test-Path -LiteralPath $env:SPICE_FRAMEWORK_TEST_HELPER -PathType Leaf|Should -BeTrue
        $message=(& dotnet $env:SPICE_FRAMEWORK_TEST_HELPER --tool rust --rust-profile release 2>&1|Out-String)
        $LASTEXITCODE|Should -Not -Be 0
        $message|Should -Match 'Rust profile must be test or mutants'
    }
    It 'rejects zero test workers before tool dispatch' {
        if(!$env:SPICE_FRAMEWORK_TEST_HELPER){Set-ItResult -Skipped -Because 'Set SPICE_FRAMEWORK_TEST_HELPER to the freshly compiled helper';return}
        Test-Path -LiteralPath $env:SPICE_FRAMEWORK_TEST_HELPER -PathType Leaf|Should -BeTrue
        $message=(& dotnet $env:SPICE_FRAMEWORK_TEST_HELPER --tool rust --test-workers 0 2>&1|Out-String)
        $LASTEXITCODE|Should -Not -Be 0
        $message|Should -Match 'Test workers must be in 1..16'
    }
}