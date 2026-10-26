param([switch]$PublicationOnly)
# Developer contracts for setup/launcher/publication tooling; no system packages installed.
$ErrorActionPreference='Stop'
Import-Module Pester
$result=if($PublicationOnly){
    Invoke-Pester -Script (Join-Path $PSScriptRoot '../tests/Remote.Tests.ps1') -TestName 'Remote qualification evidence publication' -PassThru
}else{
    Invoke-Pester -Script @((Join-Path $PSScriptRoot '../tests/Endurance.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/Remote.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/FrameworkMutations.Tests.ps1'),(Join-Path $PSScriptRoot '../tests/EnduranceFramework.Tests.ps1')) -PassThru
}
if ($result.FailedCount -ne 0) { throw 'Remote tooling contracts failed.' }
