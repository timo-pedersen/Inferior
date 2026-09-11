# Inferior — Test Execution Profiles

## Default policy

The repository has two test tiers:

- **Fast** — the default edit/commit loop. `dotnet test Inferior.slnx` excludes tests
  carrying `[Trait("Category", "Slow")]` through `TestProfiles/Fast.runsettings`.
- **Slow** — full procedural generation, multi-seed sweeps, repeated determinism runs,
  and broad preparation/residency integration. These remain normal tests, but require
  the explicit `RunSlowTests` opt-in.

The initial split classifies 208 production-scale or rendered-output test cases as
Slow and leaves 772 tests in the default solution tier (650 Game, 35 UI, 6 Gameplay
and 81 ObjectDesigner). The last known complete pre-split baseline is 980 tests. Treat
these counts as a useful audit baseline rather than a permanent contract as tests are
added or reclassified.

On the initial audited workspace, the fast Game project completed in about 6 seconds
and the whole fast solution command (including incremental build) in about 12 seconds.
The previous complete solution run took roughly 11 minutes. Timings are machine- and
workload-dependent; the tier boundary, not these exact numbers, is authoritative.

Commands:

```powershell
# Fast default suite
dotnet test Inferior.slnx

# Complete suite, including Slow
dotnet test Inferior.slnx -p:RunSlowTests=true

# Slow tier only
dotnet test Inferior.slnx -p:RunSlowTests=true --filter "Category=Slow"

# One focused slow test (the explicit property is required)
dotnet test Inferior.Game.Test/Inferior.Game.Test.csproj `
  -p:RunSlowTests=true `
  --filter "FullyQualifiedName~ExactTestName"
```

## Classification rule

Use the Slow trait when a test normally takes more than about one second or performs
work whose cost is predictably integration-scale, especially:

- complete ordinary/Bolon megastation generation;
- repeated generation for determinism or compatibility signatures;
- multi-station or multi-seed generation sweeps;
- full station CPU preparation/residency;
- stress-sized production mesh construction.
- rendered pixel/smoke tests whose real graphics setup and readback exceed the threshold.

Classify by the work performed, not a single observed duration. Shared `Lazy<T>` test
fixtures can make whichever test initializes them look slow while later tests appear
cheap. When a class is fundamentally a production-fixture integration suite, applying
the trait at class level is appropriate. Keep pure planner, geometry-helper, and small
fixture tests in the fast tier where practical.

The filter excludes tests; it does not permanently skip or disable them. Do not use
`Fact(Skip=...)` for this purpose.

## Agent verification policy

For routine implementation and commit requests:

1. Build the affected configuration.
2. Run focused tests for the changed subsystem. Include `-p:RunSlowTests=true` when a
   focused test is categorized Slow.
3. Run the fast default suite when proportionate.
4. Do not run the complete Slow tier solely because a commit was requested.

Run the complete suite for shared generator/topology changes, station preparation or
residency changes, deliberate milestone verification, or when Timo explicitly asks.
Always report which tier actually ran; “tests pass” must not imply the Slow tier ran if
it was filtered out.

## Maintaining the split

When a test grows past roughly one second because it begins using production generation,
mark it Slow in the same change. If a previously slow test is refactored into a genuinely
small fixture, remove the trait so it rejoins the default suite. Prefer reducing repeated
production generation through immutable shared fixtures, but never share mutable results
between tests merely to improve timing.
