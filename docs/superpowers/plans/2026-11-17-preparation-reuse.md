# Preparation reuse plan

User approved eliminating the measured preparation bottleneck without further design pauses. Preserve original inputs, all requested analyses, numerical tolerances and source provenance. Existing ANTLR/runtime and immutable syntax records suffice; no replacement parser or dependency is justified.

1. Measure full deck recognition with a standard counter and regression-test duplicate root recognition through resource resolution + parsing. Preserve baseline evidence.
2. Retain a request-local recognized root only for exact expanded-text equality. Add ResourceResolution.Parse with default-limit/metadata checks and safe fallback. No filesystem/global cache; altered record text, features, different bounds, expanded includes and trimmed tails cannot reuse incompatible results.
3. Route normal requested-deck, corpus and benchmark consumers through the same method. Keep independent original syntax evidence in corpus qualification separate.
4. Run managed regressions, malformed/limit/provenance/property checks and measured coverage. Requalify both original SANDLER decks against ngspice and time full pipeline and native/RSpice/ngspice boundaries serially. Native code unchanged; do not miscount these as new decks.
5. Parse StrictDoc, review, record measured limits and commit/push the existing development branch. Artifacts E:, guarded workloads, no CI/main changes.