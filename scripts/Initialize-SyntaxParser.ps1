# Acquires the pinned build-time generator, verifying bytes before use. Java 11+
# and .NET 10 are prerequisites; runtime parsing does not require Java.
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Missing repository'}
Get-Command java -ErrorAction Stop | Out-Null
$tools=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $repository '.tools'}
if(!(Test-Path -LiteralPath (Split-Path $tools -Parent) -PathType Container)){throw 'Generator output parent missing'}
if(!(Test-Path -LiteralPath $tools -PathType Container)){New-Item -ItemType Directory -Path $tools | Out-Null}
$folder=Join-Path $tools 'antlr'
if(!(Test-Path -LiteralPath $folder -PathType Container)){New-Item -ItemType Directory -Path $folder | Out-Null}
$jar=Join-Path $folder 'antlr-4.13.1-complete.jar'
$expected='bc13a9c57a8dd7d5196888211e5ede657cb64a3ce968608697e4f668251a8487'
if(!(Test-Path -LiteralPath $jar -PathType Leaf)){
    Invoke-WebRequest 'https://www.antlr.org/download/antlr-4.13.1-complete.jar' -OutFile $jar
}
if((Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'ANTLR generator checksum mismatch'}
Write-Output "Verified ANTLR 4.13.1: $jar"
