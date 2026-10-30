param(
    [Parameter(Mandatory)][string]$CorpusRoot,
    [Parameter(Mandatory)][string]$NgspicePath,
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [ValidateRange(1,21600)][int]$BudgetSeconds=1800,
    [string]$Output='evidence/ngspice-corpus',
    [ValidateSet('tracked','files')][string]$InventoryMode='tracked',
    [switch]$Offline,
    [switch]$SkipEngineBuild,
    [switch]$Managed
)
# Native builds are incremental. Publishing is the authorized default: one
# results issue per run. Offline mode records that no issue was requested.
$ErrorActionPreference='Stop'
foreach($directory in @($Root,$CorpusRoot)) {if(-not(Test-Path -LiteralPath $directory -PathType Container)){throw "Directory missing: $directory"}}
if(-not(Test-Path -LiteralPath $NgspicePath -PathType Leaf)){throw 'ngspice executable missing'}
$source=(Resolve-Path -LiteralPath $Root).Path
$corpus=(Resolve-Path -LiteralPath $CorpusRoot).Path
$reference=(Resolve-Path -LiteralPath $NgspicePath).Path
$previous=Get-Location
try {
    Set-Location -LiteralPath $source
    [Environment]::CurrentDirectory=$source
    $work=Join-Path $source 'work'
    if(-not(Test-Path -LiteralPath $work -PathType Container)){New-Item -ItemType Directory -Path $work|Out-Null}
    $launchLock=[IO.FileStream]::new((Join-Path $work 'corpus-launch.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    . (Join-Path $PSScriptRoot 'HostTools.ps1')
    Import-SpiceTools -Root $source
    if(-not $SkipEngineBuild){cargo build --locked --release;if($LASTEXITCODE){throw 'Release build failed'}}
    $engine=Join-Path $source ('target/release/circuit-simulator'+$(if($IsWindows){'.exe'}else{''}))
    if(-not(Test-Path -LiteralPath $engine -PathType Leaf)){throw 'Engine missing'}
    $arguments=@('--root',$source,'--engine',$engine,'--ngspice',$reference,'--corpus-root',$corpus,
        '--output',$Output,'--inventory-mode',$InventoryMode,'--budget-seconds',[string]$BudgetSeconds,'--publish-issue',$(if($Offline){'false'}else{'true'}))
    if($Managed){dotnet run --project dotnet/Simulator.Benchmarks -c Release -- @arguments}
    else {
        if($IsWindows){Import-SpiceMsvc}
        elseif($IsLinux){
            $hostFile=Get-Item -LiteralPath (Get-Command dotnet).Source;$target=$hostFile.ResolveLinkTarget($true)
            if($null -ne $target){$hostFile=$target};$env:DOTNET_ROOT=$hostFile.Directory.FullName
        } else {throw 'Native profile supports Windows/Linux'}
        foreach($relative in @('build','build/corpus-helper')) {
            $path=Join-Path $source $relative;if(-not(Test-Path -LiteralPath $path -PathType Container)){New-Item -ItemType Directory -Path $path|Out-Null}
        }
        $rid=$(if($IsWindows){'win-'}else{'linux-'})+[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
        $outputPath=Join-Path $source "build/corpus-helper/$rid"
        if(-not(Test-Path -LiteralPath $outputPath -PathType Container)){New-Item -ItemType Directory -Path $outputPath|Out-Null}
        dotnet publish dotnet/Simulator.Benchmarks -c Release -r $rid -p:PublishAot=true --self-contained true -o $outputPath
        if($LASTEXITCODE){throw 'Native helper publish failed'}
        & (Join-Path $outputPath ('Simulator.Benchmarks'+$(if($IsWindows){'.exe'}else{''}))) @arguments
    }
    # 0 = completed report, possibly unsupported gaps; 1 = execution/comparison
    # failures, 2 = unfinished budget/cancellation, 3 = publisher failure, 4 = harness.
    $result=$LASTEXITCODE
    if($result){Write-Warning "Corpus exit $result; inspect the saved matrix and issue state. Unsupported is never an accuracy pass."}
    exit $result
} finally {if($null -ne $launchLock){$launchLock.Dispose()};Set-Location -LiteralPath $previous.Path;[Environment]::CurrentDirectory=$previous.Path}
