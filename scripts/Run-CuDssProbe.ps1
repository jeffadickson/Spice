#requires -Version 7.0
<#
.SYNOPSIS
Builds and runs a pinned NVIDIA FP64 sparse solve as a workstation smoke test.
.DESCRIPTION
Uses installed tooling and a fresh artifact directory. Exit 0 is a completed
requested operation, 1 is a failed check, and 2 is an unavailable prerequisite.
Preflight never downloads or builds the sample.
#>
[CmdletBinding()]
param(
    [string]$CudssConfig = $env:CUDSS_CONFIG_DIR,
    [string]$CudssRuntime = $env:CUDSS_RUNTIME_DIR,
    [string]$ArtifactRoot = $env:CODEX_ARTIFACT_ROOT,
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
$revision = '8b0573b11d183d9b3a36cd2955aa31c16cc61b95'
$hashes = [ordered]@{
    'CMakeLists.txt' = 'F6E7F4B7DCCC61A37D5015343AF66D21BF071F54C77B91BDF2E1644B8CAE8B04'
    'simple.cpp' = 'F8CDB725ECA4B9E19A4B59BC216DAB522AC53261227C959D7E712016A881C587'
}
$tools = @{}
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($tool in 'nvidia-smi', 'nvcc', 'cmake') {
    $command = Get-Command -Name $tool -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($command) { $tools[$tool] = $command.Source }
    else { $missing.Add($tool) }
}
if (!$CudssConfig -or !(Test-Path -LiteralPath $CudssConfig -PathType Container)) {
    $missing.Add('cuDSS CMake package directory')
}
if (!$CudssRuntime -or !(Test-Path -LiteralPath $CudssRuntime -PathType Container)) {
    $missing.Add('cuDSS runtime library directory')
}
if ($missing.Count -gt 0) {
    [ordered]@{ status = 'unavailable'; missing = $missing.ToArray(); sample_commit = $revision } |
        ConvertTo-Json
    exit 2
}

# Time includes process startup. This is smoke-test wall time, not a GPU timing.
function Invoke-ProbeCommand([string]$File, [string[]]$Arguments, [int]$TimeoutMs) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    if ($IsWindows) { $info.Environment['PATH'] = "$CudssRuntime;$env:PATH" }
    else { $info.Environment['LD_LIBRARY_PATH'] = "${CudssRuntime}:$env:LD_LIBRARY_PATH".TrimEnd(':') }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit($TimeoutMs)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        [ordered]@{
            file = $File
            arguments = $Arguments
            exit_code = $process.ExitCode
            timed_out = $timedOut
            elapsed_ms = $clock.Elapsed.TotalMilliseconds
            stdout = $stdout.GetAwaiter().GetResult()
            stderr = $stderr.GetAwaiter().GetResult()
        }
    } finally { $process.Dispose() }
}

$CudssConfig = (Resolve-Path -LiteralPath $CudssConfig).Path
$CudssRuntime = (Resolve-Path -LiteralPath $CudssRuntime).Path
$report = [ordered]@{
    status = 'preflight'
    sample_commit = $revision
    sample_sha256 = $hashes
    host_os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    cudss_config_dir = $CudssConfig
    cudss_runtime_dir = $CudssRuntime
    commands = [ordered]@{}
}
$report.commands.adapter = Invoke-ProbeCommand $tools['nvidia-smi'] @(
    '--query-gpu=name,driver_version,compute_cap', '--format=csv,noheader'
) 30000
if ($report.commands.adapter.timed_out -or $report.commands.adapter.exit_code -ne 0 -or
    [string]::IsNullOrWhiteSpace($report.commands.adapter.stdout)) {
    $report.status = 'unavailable'
    $report | ConvertTo-Json -Depth 8
    exit 2
}
if ($PreflightOnly) {
    $report | ConvertTo-Json -Depth 8
    exit 0
}

if (!$ArtifactRoot -and $IsWindows) { $ArtifactRoot = 'E:/Artifacts' }
if (!$ArtifactRoot -or !(Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
    throw 'Pass -ArtifactRoot with an existing writable directory.'
}
$parent = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$runName = 'Spice-cuDSS-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $parent $runName
New-Item -ItemType Directory -Path $run | Out-Null
$reportPath = Join-Path $run 'report.json'
$status = 1
try {
    # Retain installed package version text and runtime identity without copying
    # or redistributing vendor binaries.
    $report.cudss_version_files = @(Get-ChildItem -LiteralPath $CudssConfig -File |
        Where-Object Name -Match 'version\.cmake$' | ForEach-Object {
            @{ name = $_.Name; text = Get-Content -LiteralPath $_.FullName -Raw }
        })
    $report.cudss_runtime_sha256 = @(Get-ChildItem -LiteralPath $CudssRuntime -File |
        Where-Object Name -Match '^cudss[^.]*\.dll$|^libcudss\.so(\.|$)' |
        Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash)
    $source = Join-Path $run 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    $uri = "https://raw.githubusercontent.com/NVIDIA/CUDALibrarySamples/$revision/cuDSS/simple"
    foreach ($name in $hashes.Keys) {
        $destination = Join-Path $source $name
        Invoke-WebRequest -Uri "$uri/$name" -OutFile $destination -TimeoutSec 30
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hashes[$name]) {
            throw "Source hash differs for $name."
        }
    }

    $report.commands.nvcc = Invoke-ProbeCommand $tools['nvcc'] @('--version') 30000
    $report.commands.cmake = Invoke-ProbeCommand $tools['cmake'] @('--version') 30000
    $build = Join-Path $run 'build'
    $report.commands.configure = Invoke-ProbeCommand $tools['cmake'] @(
        '-S', $source, '-B', $build, "-Dcudss_DIR=$CudssConfig",
        '-DCMAKE_BUILD_TYPE=Release', "-DCMAKE_CUDA_COMPILER=$($tools['nvcc'])",
        '-DCMAKE_CUDA_ARCHITECTURES=native'
    ) 180000
    if ($report.commands.configure.exit_code -ne 0 -or $report.commands.configure.timed_out) {
        throw 'Sample configuration failed.'
    }
    $report.commands.build = Invoke-ProbeCommand $tools['cmake'] @(
        '--build', $build, '--config', 'Release', '--parallel', '2', '--verbose'
    ) 180000
    if ($report.commands.build.exit_code -ne 0 -or $report.commands.build.timed_out) {
        throw 'Sample build failed.'
    }
    $name = if ($IsWindows) { 'simple_example.exe' } else { 'simple_example' }
    $exe = @((Join-Path $build "Release/$name"), (Join-Path $build $name)) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$exe) { throw 'Built sample executable is unavailable.' }
    $report.executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $report.commands.solve = Invoke-ProbeCommand $exe @() 60000
    if ($report.commands.solve.exit_code -ne 0 -or $report.commands.solve.timed_out -or
        $report.commands.solve.stdout -notmatch '(?m)^Example PASSED\r?$') {
        throw 'The sample did not pass its numerical assertion.'
    }
    # Require every printed solution component too. The vendor's fabs-based
    # check alone can let a NaN through because unordered comparisons are false.
    foreach ($index in 0..4) {
        $expected = $index + 1
        if ($report.commands.solve.stdout -notmatch "(?m)^x\[$index\] = $expected\.0000 expected $expected\.0000\r?$") {
            throw "Sample solution component $index is missing or invalid."
        }
    }
    $report.status = 'passed_vendor_smoke'
    $status = 0
} catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output $reportPath
}
exit $status
