# Small observation boundary shared with deterministic process-exit race tests.
# No memory thresholds or admission decisions belong in this function.
function Get-MutationWorkerObservation {
 param([Parameter(Mandatory)]$Process)
 try {
  if($Process.HasExited){return $null}
  $Process.Refresh()
  return [pscustomobject]@{pid=$Process.Id;started_utc=$Process.StartTime.ToUniversalTime().ToString('o');private_bytes=$Process.PrivateMemorySize64;name=$Process.ProcessName}
 } catch [System.InvalidOperationException] {return $null}
   catch [System.ComponentModel.Win32Exception] {return $null}
}
