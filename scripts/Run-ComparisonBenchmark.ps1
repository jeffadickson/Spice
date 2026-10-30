param(
    [Parameter(Mandatory)][string]$NgspicePath,
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$EnginePath='',
    [ValidateRange(0,5)][int]$Warmups=1,
    [ValidateRange(1,20)][int]$Iterations=3,
    [ValidateRange(1,3600)][int]$BudgetSeconds=300,
    [string]$Report='',
    [switch]$SkipEngineBuild,
    [switch]$Managed
)
# Build incrementally; native compilation and warmups are outside measured child
# intervals. Global SDK/PATH configuration is never persisted by this script.
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $Root -PathType Container)){throw 'Checkout missing'}
$benchmarkRoot=(Resolve-Path -LiteralPath $Root).Path
if(-not(Test-Path -LiteralPath $NgspicePath -PathType Leaf)){throw 'ngspice executable missing'}
$reference=(Resolve-Path -LiteralPath $NgspicePath).Path
$previous=Get-Location
try {
    Set-Location -LiteralPath $benchmarkRoot
    [Environment]::CurrentDirectory=$benchmarkRoot
    . (Join-Path $PSScriptRoot 'HostTools.ps1')
    Import-SpiceTools -Root $benchmarkRoot
    if(-not $IsWindows){
        $hostFile=Get-Item -LiteralPath (Get-Command dotnet).Source
        $linked=$hostFile.ResolveLinkTarget($true)
        if($null -ne $linked){$hostFile=$linked}
        $env:DOTNET_ROOT=$hostFile.Directory.FullName
    }
    if(-not $SkipEngineBuild){
        cargo build --release --locked
        if($LASTEXITCODE){throw 'Release engine build failed'}
    }
    if(-not $EnginePath){$EnginePath=Join-Path $benchmarkRoot ('target/release/circuit-simulator'+$(if($IsWindows){'.exe'}else{''}))}
    if(-not(Test-Path -LiteralPath $EnginePath -PathType Leaf)){throw 'Engine missing'}
    $engine=(Resolve-Path -LiteralPath $EnginePath).Path
    $arguments=@('--root',$benchmarkRoot,'--engine',$engine,'--ngspice',$reference,
        '--warmups',[string]$Warmups,'--iterations',[string]$Iterations,'--budget-seconds',[string]$BudgetSeconds)
    if($Report){$arguments+=@('--report',$Report)}
    if($Managed){
        dotnet run --project dotnet/Simulator.Benchmarks -c Release -- @arguments
    } else {
        if($IsWindows){Import-SpiceMsvc}
        if(-not $IsWindows -and -not $IsLinux){throw 'Native helper profile supports Windows/Linux'}
        if(-not(Test-Path -LiteralPath build -PathType Container)){New-Item -ItemType Directory -Path build|Out-Null}
        $parent=Join-Path $benchmarkRoot 'build/benchmark-helper'
        if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null}
        $rid=$(if($IsWindows){'win-'}else{'linux-'})+[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
        $output=Join-Path $parent $rid
        if(-not(Test-Path -LiteralPath $output -PathType Container)){New-Item -ItemType Directory -Path $output|Out-Null}
        dotnet publish dotnet/Simulator.Benchmarks -c Release -r $rid -p:PublishAot=true --self-contained true -o $output
        if($LASTEXITCODE){throw 'Native helper publish failed; use -Managed explicitly if this host cannot compile Native AOT'}
        $executable=Join-Path $output ('Simulator.Benchmarks'+$(if($IsWindows){'.exe'}else{''}))
        & $executable @arguments
    }
    if($LASTEXITCODE){throw 'Comparison failed; inspect the new report and its raw artifact directory'}
} finally {
    Set-Location -LiteralPath $previous.Path
    [Environment]::CurrentDirectory=$previous.Path
}
