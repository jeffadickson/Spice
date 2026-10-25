<!-- Authoritative requirements: ngspice-compatibility.sdoc -->

# ngspice compatibility corpus

The local ngspice checkout is an **external, read-only** compatibility corpus. Run:

```powershell
dotnet run --file scripts/Inventory-Ngspice.cs -- C:\dev\ngspice-ngspice
```

The initial local snapshot at revision `e8285746c` contains 118 recognized circuit fixtures under `tests/`. The inventory emits each relative path, SHA-256 and first-token feature markers, classifying it as `candidate_basic` or `future_feature`. It does not run or modify ngspice.

`candidate_basic` only means the lexical marker set is within the importer’s small declared subset. It is not an execution pass. A fixture becomes `supported` only after a named oracle and numerical comparison are added. Fixtures with XSPICE, compact models, transmission lines, parser/control language, subcircuits, or unsupported device families remain `future_feature` until their semantics and validation contracts exist.
