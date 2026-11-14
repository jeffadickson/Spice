param([Parameter(Mandatory)][string]$OutputDirectory)
# Compile a standard-library-only diagnostic with this checkout's pinned Rust.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
foreach($directory in @($root,(Split-Path -Parent ([IO.Path]::GetFullPath($OutputDirectory))))){
 if(!(Test-Path -LiteralPath $directory -PathType Container)){throw "Missing $directory"}
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh diagnostic output required'}
$artifactRoot=if($env:CODEX_ARTIFACT_ROOT){$env:CODEX_ARTIFACT_ROOT}else{'E:/Artifacts'}
if($IsWindows){
 if(!(Test-Path -LiteralPath $artifactRoot -PathType Container)){throw 'Artifact root missing'}
 $prefix=[IO.Path]::GetFullPath($artifactRoot).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
 if(![IO.Path]::GetFullPath($OutputDirectory).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Use the E: artifact root'}
}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$out=(Resolve-Path -LiteralPath $OutputDirectory).Path
$old=Get-Location;$temp=$env:TEMP;$tmp=$env:TMP
try{
 Set-Location -LiteralPath $root;$env:TEMP=$out;$env:TMP=$out
 . (Join-Path $PSScriptRoot HostTools.ps1);Import-SpiceTools -Root $root;Import-SpiceMsvc
 $version=(& rustc --version).Trim();if($LASTEXITCODE){throw 'Compiler unavailable'}
 $pinned=(Select-String -LiteralPath (Join-Path $root rust-toolchain.toml) -Pattern '^channel = "([^"]+)"').Matches.Groups[1].Value
 if(!$pinned -or $version -notmatch ('^rustc '+[regex]::Escape($pinned)+'\s')){throw 'Diagnostic compiler does not match the checkout pin'}
 $source=Join-Path $root experiments/file-flush.rs
 $binary=Join-Path $out $(if($IsWindows){'file-flush.exe'}else{'file-flush'})
 & rustc --edition=2024 -C opt-level=1 $source -o $binary
 if($LASTEXITCODE){throw 'Diagnostic compilation failed'}
 $result=& $binary $out;if($LASTEXITCODE){throw 'File flush diagnostic failed'}
 @{compiler=$version;platform=[Runtime.InteropServices.RuntimeInformation]::OSDescription;result=($result|ConvertFrom-Json);source_sha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();binary_sha256=(Get-FileHash -LiteralPath $binary).Hash.ToLowerInvariant();limits='Four finite I/O observations on the recorded platform; not power-loss durability or every future Rust version.'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $out result.json)
 Get-Content -LiteralPath (Join-Path $out result.json)
}finally{Set-Location -LiteralPath $old.Path;$env:TEMP=$temp;$env:TMP=$tmp}
