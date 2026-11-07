BeforeAll {
    $launcher = Join-Path $PSScriptRoot '../Run-ParallelProbe.ps1'
}

Describe 'Parallel probe launcher' {
    It 'refuses a missing artifact root without creating it' {
        $missing = Join-Path $TestDrive 'missing'
        { & $launcher -Probe Cpu -TestOnly -ArtifactRoot $missing } |
            Should -Throw '*existing artifact root*'
        Test-Path -LiteralPath $missing | Should -BeFalse
    }

    It 'does not run a CPU benchmark in test mode' {
        Mock cargo { $global:LASTEXITCODE = 0 }
        & $launcher -Probe Cpu -TestOnly -ArtifactRoot $TestDrive
        Should -Invoke cargo -Times 1 -Exactly -ParameterFilter { $args[0] -eq 'test' }
        Should -Invoke cargo -Times 0 -Exactly -ParameterFilter { $args[0] -eq 'run' }
    }

    It 'does not run a CPU benchmark after a failed test' {
        Mock cargo { $global:LASTEXITCODE = 7 }
        { & $launcher -Probe Cpu -ArtifactRoot $TestDrive } | Should -Throw '*7*'
        Should -Invoke cargo -Times 0 -Exactly -ParameterFilter { $args[0] -eq 'run' }
    }

    It 'runs the Intel self-test without opening the GPU and restores the environment' {
        Mock dotnet {
            if ($args[0] -eq 'build') {
                $build = $args[-1]
                if (!(Test-Path -LiteralPath $build -PathType Container)) {
                    New-Item -ItemType Directory -Path $build | Out-Null
                }
                $bin = Join-Path $build 'bin/release'
                New-Item -ItemType Directory -Path $bin | Out-Null
                New-Item -ItemType File -Path (Join-Path $bin 'IntelSpmv.dll') | Out-Null
            }
            $global:LASTEXITCODE = 0
        }
        $prior = $env:DOTNET_TieredCompilation
        & $launcher -Probe Intel -TestOnly -ArtifactRoot $TestDrive
        Should -Invoke dotnet -Times 2 -Exactly
        Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter { $args[-1] -eq '--self-test' }
        $env:DOTNET_TieredCompilation | Should -Be $prior
    }

    It 'reuses the build cache, isolates reports and refuses an active cache lock' {
        Mock cargo { $global:LASTEXITCODE = 0 }
        $root = Join-Path $TestDrive 'cache-contract'
        New-Item -ItemType Directory -Path $root | Out-Null
        & $launcher -Probe Cpu -TestOnly -ArtifactRoot $root
        & $launcher -Probe Cpu -TestOnly -ArtifactRoot $root
        $cache = @(Get-ChildItem -LiteralPath $root -Directory -Filter 'Spice-parallel-build-Cpu-*')
        $cache.Count | Should -Be 1
        @(Get-ChildItem -LiteralPath $root -Directory -Filter 'Spice-parallel-Cpu-*').Count | Should -Be 2
        $lock = [IO.File]::Open((Join-Path $cache[0].FullName 'probe.lock'), 'Open', 'ReadWrite', 'None')
        try {
            { & $launcher -Probe Cpu -TestOnly -ArtifactRoot $root } | Should -Throw
        } finally {
            $lock.Dispose()
        }
        Should -Invoke cargo -Times 2 -Exactly
    }
}
