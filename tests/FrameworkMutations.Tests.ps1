# Negative process-boundary checks must reject malformed work before starting tools.
Describe 'Framework campaign input bounds' {
    It 'rejects invalid budgets, shards and escaping source filters' {
        $runner=Join-Path $PSScriptRoot '../scripts/FrameworkMutations.cs'
        foreach($arguments in @(@('--minutes','NaN'),@('--minutes','0'),@('--minutes','1441'),@('--shard','-1/16'),@('--shard','16/16'),@('--shard','1/0'),@('--rust-file','src/../secret'),@('--rust-test','../secret'),@('--rust-test','missing-suite'),@('--rust-test','lib,lib'),@('--rust-test','all,lib'),@('--rust-test','lib,missing-suite'),@('--rust-test',('lib,'*9)),@('--tool','unknown'),@('--workers','0'),@('--workers','17'),@('--rust-filter',('x'*513)),@('--unknown'))) {
            & dotnet run --file $runner -- @arguments *> $null
            $LASTEXITCODE|Should Not Be 0
        }
    }
    It 'partitions actual Rust candidates into disjoint reproducible zero-based shards' {
        $root=Split-Path $PSScriptRoot -Parent
        $cargo=Join-Path $root $(if($IsWindows){'.tools/mutation/cargo-mutants.exe'}else{'.tools/mutation/cargo-mutants'})
        $all=@(& $cargo mutants --list --file src/quantity.rs --colors never)
        if($LASTEXITCODE -ne 0){throw 'Candidate inventory failed'}
        $a=@(& $cargo mutants --list --file src/quantity.rs --shard 0/2 --sharding round-robin --colors never)
        $b=@(& $cargo mutants --list --file src/quantity.rs --shard 1/2 --sharding round-robin --colors never)
        ($a.Count+$b.Count)|Should Be $all.Count
        @($a|Where-Object {$_ -in $b}).Count|Should Be 0
        @($all|Where-Object {$_ -notin ($a+$b)}).Count|Should Be 0
        @(& $cargo mutants --list --file src/quantity.rs --shard 0/2 --sharding round-robin --colors never) -join "`n" | Should Be ($a -join "`n")
    }
}
