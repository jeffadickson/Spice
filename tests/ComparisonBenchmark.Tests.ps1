Describe 'Comparison benchmark launch bounds' {
    It 'rejects an invalid repetition count before building or launching' {
        $script=Join-Path $PSScriptRoot '../scripts/Run-ComparisonBenchmark.ps1'
        if(-not(Test-Path -LiteralPath $script -PathType Leaf)){throw 'Benchmark wrapper missing'}
        $output=& pwsh -NoProfile -NonInteractive -File $script -NgspicePath missing -Iterations 0 2>&1|Out-String
        $LASTEXITCODE|Should Not Be 0
    }
    It 'rejects a missing checkout before building or launching' {
        $script=Join-Path $PSScriptRoot '../scripts/Run-ComparisonBenchmark.ps1'
        if(-not(Test-Path -LiteralPath $script -PathType Leaf)){throw 'Benchmark wrapper missing'}
        $output=& pwsh -NoProfile -NonInteractive -File $script -NgspicePath missing -Root (Join-Path $TestDrive missing) 2>&1|Out-String
        $LASTEXITCODE|Should Not Be 0
        $output|Should Match 'Checkout missing'
    }
}
