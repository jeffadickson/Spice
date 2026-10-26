param([switch]$Install)
# Pinned developer tools, isolated from product/runtime dependencies. GitHub release
# asset SHA-256 values were recorded from the upstream v27.1.0 release metadata.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version='27.1.0'
$platform=if($IsWindows){'x86_64-pc-windows-msvc'}elseif($IsLinux){'x86_64-unknown-linux-gnu'}else{throw 'Mutation bootstrap supports Windows/Linux x64.'}
if([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64'){throw 'Mutation bootstrap requires x64.'}
$extension=if($IsWindows){'zip'}else{'tar.gz'}
$digest=if($IsWindows){'2a2f00e47d4b458262a41501b0820aa26015fd35779903d2c8b30b2993f36791'}else{'dfe6dc37d0342c891d2829b5a695aa57c2d0edecef7e7d0399a30cc6e206411e'}
$folder=Join-Path $root '.tools/mutation'
$executable=Join-Path $folder $(if($IsWindows){'cargo-mutants.exe'}else{'cargo-mutants'})
if($Install){
    New-Item -ItemType Directory -Force $folder|Out-Null
    if(-not(Test-Path $executable)){
        $asset="cargo-mutants-$platform.$extension"
        $archive=Join-Path $folder $asset
        Invoke-WebRequest "https://github.com/sourcefrog/cargo-mutants/releases/download/v$version/$asset" -OutFile $archive
        if((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest){throw 'cargo-mutants archive SHA-256 mismatch.'}
        if($IsWindows){Expand-Archive -LiteralPath $archive -DestinationPath $folder -Force}
        else{& tar -xzf $archive -C $folder;if($LASTEXITCODE -ne 0){throw 'cargo-mutants extraction failed.'}}
    }
    Push-Location $root
    try{dotnet tool restore;if($LASTEXITCODE -ne 0){throw 'Pinned Stryker restore failed.'}}finally{Pop-Location}
}
if(-not(Test-Path $executable)){throw 'Run scripts/Setup-MutationTools.ps1 -Install first.'}
$actual=& $executable mutants --version
if($LASTEXITCODE -ne 0 -or $actual -notmatch ('\b'+[regex]::Escape($version)+'\b')){throw 'cargo-mutants version does not match pin.'}
Push-Location $root
try{$stryker=& dotnet tool list --local;if($LASTEXITCODE -ne 0 -or ($stryker -join ' ') -notmatch '\b5\.0\.0\b'){throw 'Stryker version does not match pin.'}}finally{Pop-Location}
$env:SPICE_CARGO_MUTANTS=$executable
Write-Host 'Pinned cargo-mutants 27.1.0 and Stryker.NET 5.0.0 verified.'

