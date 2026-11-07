# Grammar implementation map

The [HTML map](grammar-implementation.html) and
[machine-readable inventory](../evidence/grammar-implementation.json) enumerate
the actual generated ANTLR parser productions, then join reviewed implementation,
requirement and verification associations. Extraction, preparation and execution
have independent statuses. Associations include structural infrastructure,
bounded implementations, explicit refusals and unexecuted observations.

The second table lists concrete control headings observed in the historical
924-input syntax inventory. It exposes missing commands hidden behind the
generic control production. Its overlapping occurrence counts describe that
historical inventory; current execution counts come from a separate frozen
928-path requested-analysis campaign. Model parameters/levels, lexer modes and
port contracts also need their existing detailed assessments.

`let`, `define` and `unlet` now have generated metadata. A bounded suffix after the
last analysis/run can be retained as `postprocessing_not_executed`, preserving
physical ranges. This lets individual ordinary analyses run while making missing
vector/function evaluation visible. Prefix/interleaved declarations, root-vector
or axis replacement and nonlocal deletion refuse atomically. Display/listing and
literal xbrushwidth1..64 are reporting omissions. Loops, alter, setplot, shell,
arbitrary writes and substitutions remain blockers.

## Original-deck checklist

The [named cohort checklist](../evidence/grammar-cohort-checklist.md) joins actual
production names to blockers observed on original decks and records independent
numerical evidence by input hash. A diagnostic derivative is tracked separately.

## Regenerate

From the checkout root in PowerShell7 with the pinned .NET SDK, Java and ANTLR
generator installed. Linux users should set CODEX_ARTIFACT_ROOT to an existing
writable native artifact directory first.

```powershell
$repo = (Get-Location).Path
if (-not (Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
if (-not (Test-Path -LiteralPath (Join-Path $repo 'Cargo.toml') -PathType Leaf)) { throw 'Run from the checkout root' }
$root = $env:CODEX_ARTIFACT_ROOT
if (-not $root -and $IsWindows) { $root = 'E:/Artifacts' }
if (-not $root -or -not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Set an existing artifact root' }
$jar = Join-Path $repo '.tools/antlr/antlr-4.13.1-complete.jar'
if (-not (Test-Path -LiteralPath $jar -PathType Leaf)) { throw 'Initialize the pinned syntax generator first' }
$run = Join-Path $root ('Spice-grammar-map-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
if (Test-Path -LiteralPath $run) { throw 'Fresh output required' }
New-Item -ItemType Directory -Path $run | Out-Null
dotnet run --file scripts/GrammarImplementationMap.cs --artifacts-path "$run/build" "-p:GrammarOutput=$run/grammar" "-p:AntlrJar=$jar" -- $repo (Join-Path $repo 'compat/ngspice/grammar-implementation.json') "$run/report"
if ($LASTEXITCODE) { throw 'Grammar map generation failed' }
```

Edit `compat/ngspice/grammar-implementation.json` when behavior changes. New
productions appear as unassessed automatically. Stale or duplicate production and
command identities, missing requirement IDs and missing/escaping source paths
prevent publication. `GrammarImplementationTests` checks every registered control
heading against actual generated metadata. A changed association needs evidence;
the tool does not rerun referenced tests or infer numerical correctness.

Raw source hashes, coverage and mutation evidence remain under the task's E:
artifact root. The older capability table is explicitly historical; its old
"ANTLR elaboration absent" labels have been reconciled in the current catalog.
