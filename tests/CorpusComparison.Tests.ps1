Describe 'Corpus comparison launch boundaries' {
    It 'rejects an invalid budget before compiling or publishing' {
        $script=Join-Path $PSScriptRoot '../scripts/Run-CorpusComparison.ps1'
        if(-not(Test-Path -LiteralPath $script -PathType Leaf)){throw 'Wrapper missing'}
        $output=& pwsh -NoProfile -NonInteractive -File $script -CorpusRoot missing -NgspicePath missing -BudgetSeconds 0 2>&1|Out-String
        $LASTEXITCODE|Should Not Be 0
    }
    It 'rejects a missing corpus before compiling or publishing' {
        $script=Join-Path $PSScriptRoot '../scripts/Run-CorpusComparison.ps1'
        if(-not(Test-Path -LiteralPath $script -PathType Leaf)){throw 'Wrapper missing'}
        $output=& pwsh -NoProfile -NonInteractive -File $script -CorpusRoot (Join-Path $TestDrive missing) -NgspicePath missing 2>&1|Out-String
        $LASTEXITCODE|Should Not Be 0
        $output|Should Match 'Directory missing'
    }
}
