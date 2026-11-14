#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$MeasurementDirectory)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $MeasurementDirectory -PathType Container)){throw 'Missing measurements'}
$manifest=Get-Content -LiteralPath (Join-Path $MeasurementDirectory 'summary.json') -Raw|ConvertFrom-Json
$results=@()
foreach($case in ($manifest.rows.case|Sort-Object -Unique)){
 $rows=@($manifest.rows|Where-Object {$_.case -eq $case -and !$_.warmup})
 $cpuRow=$rows|Where-Object {$_.backend -eq 'lu' -and $_.success}|Select-Object -First 1
 foreach($candidate in @($rows|Where-Object {$_.backend -ne 'lu'})){
  $state='unavailable_execution';$detail='A complete CPU and candidate response are required';$maximum=0.;$points=0
  if($cpuRow -and $candidate.success){
   $cpuTrace=Get-Content -LiteralPath (Join-Path $MeasurementDirectory $cpuRow.artifact) -Raw|ConvertFrom-Json
   $otherTrace=Get-Content -LiteralPath (Join-Path $MeasurementDirectory $candidate.artifact) -Raw|ConvertFrom-Json
   $a=$cpuTrace.stdout|ConvertFrom-Json;$b=$otherTrace.stdout|ConvertFrom-Json
   $state='agreed';$detail='All signals on matching grids; voltage 1e-6 V, current 1e-9 A, relative 1e-4'
   try {
    if($manifest.runtime_provenance_valid -ne $true -or [string]::IsNullOrWhiteSpace($cpuRow.request_sha256) -or $cpuRow.request_sha256 -ne $candidate.request_sha256){throw 'Runtime provenance or same-input hash gap'}
    if(!$a.complete -or !$b.complete -or $a.status -ne 'succeeded' -or $b.status -ne 'succeeded' -or $a.case_id -ne $b.case_id){throw 'Incomplete or different case'}
    $expected=@{faer='faer_sparse_lu';intel='intel_opencl_viennacl_gmres_experimental'}[$candidate.backend]
    if($a.backend.id -ne 'nalgebra_dense_lu_reference' -or $b.backend.id -ne $expected -or ($candidate.backend -eq 'intel' -and (!$b.backend.gpu -or $b.backend.placement -ne 'gpu'))){throw 'Provider identity/placement gap'}
    if(!@($a.signals).Count){throw 'No observables'}
    if(($a.signals|ConvertTo-Json -Compress) -ne ($b.signals|ConvertTo-Json -Compress)){throw 'Signal identity/unit gap'}
    $ac=@($a.ac_samples).Count -gt 0
    if(($ac -and @($a.samples).Count) -or (@($b.ac_samples).Count -and @($b.samples).Count)){throw 'Mixed real/AC response shape'}
    if($ac -ne (@($b.ac_samples).Count -gt 0)){throw 'Analysis shape gap'}
    $left=if($ac){@($a.ac_samples)}else{@($a.samples)};$right=if($ac){@($b.ac_samples)}else{@($b.samples)}
    if(!$left.Count -or $left.Count -ne $right.Count){throw 'Different sample grids; no interpolation or omitted points permitted'}
    for($i=0;$i -lt $left.Count;$i++){
     $axisA=if($ac){$left[$i].frequency_hz}else{$left[$i].time_s};$axisB=if($ac){$right[$i].frequency_hz}else{$right[$i].time_s}
     if($null -eq $axisA -or $null -eq $axisB -or ![double]::IsFinite($axisA) -or ![double]::IsFinite($axisB) -or [Math]::Abs($axisA-$axisB) -gt $(if($ac){1e-9}else{1e-14})){throw 'Different or unknown sample grids'}
     if($left[$i].values.Count -ne $a.signals.Count -or $right[$i].values.Count -ne $a.signals.Count){throw 'Missing signal values'}
     for($j=0;$j -lt $a.signals.Count;$j++){
      $absolute=switch($a.signals[$j].unit){'V'{1e-6};'A'{1e-9};default{throw 'Unqualified signal unit'}}
      $u=$left[$i].values[$j];$v=$right[$i].values[$j]
      if($ac){$components=@(@($u.real,$v.real),@($u.imag,$v.imag))}else{$components=@(,@($u,$v))}
      foreach($pair in $components){
       if($null -eq $pair[0] -or $null -eq $pair[1] -or ![double]::IsFinite($pair[0]) -or ![double]::IsFinite($pair[1])){throw 'Unknown/nonfinite value'}
       $error=[Math]::Abs($pair[0]-$pair[1]);$bound=$absolute+1e-4*[Math]::Max([Math]::Abs($pair[0]),[Math]::Abs($pair[1]))
       $maximum=[Math]::Max($maximum,$error/$bound);if($error -gt $bound){$state='numerical_disagreement'}
      }
     };$points++
    }
   }catch{$state='unavailable_evidence';$detail=$_.Exception.Message}
  }
  $results+=@{case=$case;backend=$candidate.backend;iteration=$candidate.iteration;state=$state;points=$points;maximum_bound_ratio=$maximum;detail=$detail}
 }
}
@{scope='CPU LU versus complete replay responses; no missing-value substitution, interpolation, or unequal-grid pass';results=$results}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $MeasurementDirectory 'cpu-agreement.json')
