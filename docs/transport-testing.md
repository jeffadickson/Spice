# Managed transport qualification

The production client retains caller-selected cancellation and existing 1 MiB
request / 32 MiB output bounds. It now checks cancellation in read/completion
loops even for synchronously completed operations. Product latency and arbitrary
process containment are not guaranteed by these finite tests.

Engine and fixture calls in managed tests/conformance use BoundedTestClient:
five-second linked cancellation, seven-second independent outer wait, and
Task.Run invocation so a synchronous faulty loop cannot block installation of
the assertion deadline. The helper is outside the production client assembly.
Typed requests retain the public wire DTO graph. Stryker's additional timeout is
15 seconds, allowing test failure observation; native timeouts remain separate.

Fixture ownership begins at descendant launch. Both synthetic pre-acknowledgement
failure and a real conflicting parent marker are tested. Startup exceptions reap
the owned descendant before rethrowing. Cancellation tests hold acknowledged
process handles and independently clean up after assertions. Direct parent exit
is required; descendant exit gets a separate two-second settling limit.

OS permission denial, unkillable processes, arbitrary executable trees and
interactive Windows console behavior remain unqualified. Conditional BOM
survivors, compiler-invalid and uncovered candidates retain separate dispositions.

Use the checked-in host setup, then run the full managed test project and
FrameworkMutations.cs with --tool dotnet --minutes 25 --workers 1. Raw native
reports/logs remain under work/framework-mutations. Exact case identities,
source hashes and scoped coverage are recorded in evidence/five-area-transport.json.
