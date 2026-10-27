param([switch]$PublicationOnly)
# Developer contracts for setup/launcher/publication tooling; no system packages installed.
$ErrorActionPreference='Stop'
# These contracts use the repository's pinned Pester 3 syntax and discovery
# semantics. An unrelated newer module must not silently change their meaning.
Import-Module Pester -RequiredVersion 3.4.0 -Force
$result=if($PublicationOnly){
    Invoke-Pester -Script (Join-Path $PSScriptRoot '../tests/Remote.Tests.ps1') -TestName 'Remote qualification evidence publication' -PassThru
}else{
    Invoke-Pester -Script @((Join-Path $PSScriptRoot '../tests/Endurance.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/Remote.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/FrameworkMutations.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/EnduranceFramework.Tests.ps1')) -PassThru
}
if ($result.FailedCount -ne 0) { throw 'Remote tooling contracts failed.' }
