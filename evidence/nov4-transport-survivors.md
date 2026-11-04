# November 4 transport survivor triage

The overnight Stryker 5.0.0 report on de5d84e had five survivors in Client.cs.

| Mutation | Disposition |
| --- | --- |
| CreateNoWindow set to false | Open. The existing probe is insensitive when the test host has no attached console. |
| Cancellation check removed from pending task loop | Open. Existing end-to-end cancellation tests do not isolate cancellation during inline task completion. |
| Process cleanup removed | Direct source replay failed both cancellation cases on the live-child assertion. The discrepancy with the Stryker classification remains open. |
| Cancellation check removed from bounded stream read | Open. Existing asynchronous pipe cancellation does not isolate a successfully completed read racing cancellation. |
| UTF-8 encoder preamble flag enabled | Equivalent at the decode-only GetString call. Added a fixture regression for a real wire BOM. |

The managed suite passed 85 tests with the retained corpus engine. Coverlet measured
90.67% line and 57.06% branch coverage for Simulator.Client. The custom artifact
layout required staging the built fixture beside the test assembly. StrictDoc
parsed 51 documents with 409 unique IDs and 381 resolved parent links. Cleanup
replay and raw coverage are under E:/Artifacts/Spice-nov4-transport.

The follow-up campaign uses a short NTFS source path with bulk outputs on E:.
This tests the long-path hypothesis for the prior baseline LNK1104.
