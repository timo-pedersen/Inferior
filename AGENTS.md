# AGENTS.md — Inferior

## Required context

Before substantial work, read:

1. `Docs-ai/!invariants.md`
2. `Docs-ai/!current-state.md`
3. `Docs-ai/architecture-map-ai.md`
4. The active subsystem `*-ai.md` reference relevant to the task

Use `Docs/` only when rationale is needed or a current reference points there. Treat
`Docs-archive/` as historical context only.

## Current-state ownership

`Docs-ai/!current-state.md` is maintained primarily by Claude Code.

Read it as active project context. Report stale, contradictory, or missing information
when discovered. Do not modify it unless Timo explicitly asks you to update it.

## Authority

For current implementation:

1. Repository code
2. Architecture map
3. Current-state document
4. Active subsystem references

The architecture map is authoritative for code location and structure. Current-state is 
authoritative for development status, known regressions, and active transition state.

For intended design:

1. Timo's current instructions / active brief
2. `!invariants.md`
3. Authoritative active subsystem reference
4. General compact design reference
5. Full design/lore docs for rationale

Never merge contradictory claims into a compromise. State the conflict and use the
higher-authority source. If code conflicts with newer authoritative design, report a
design/code mismatch.

## Build and tests

```powershell
dotnet build Inferior.slnx
dotnet run --project Inferior.Game
dotnet build Inferior.slnx -c Release
```

MonoGame content builds automatically from `Inferior.Game/Content/Content.mgcb`.
Tests are in `Inferior.Game.Test` (xUnit).

## Working method

- Inspect relevant code before changing it.
- Preserve working behaviour outside the requested scope.
- Prefer the smallest coherent solution.
- Do not create duplicate implementations.
- Do not build generic infrastructure before a concrete use case requires it.
- Do not silently fix unrelated issues unless they block the task.
- Verify exact identifiers and APIs against the repository.

For simulation/threading/reference-frame work, map ownership and data flow before editing.

For 3D geometry, define local axes, points vs directions, normals/winding, transform order,
ports, collision/clearance volumes, and testable invariants before generating vertices.

## Core rules

- Simulation thread is the intended owner of mutable live universe state.
- Rendering/UI should consume snapshots or presentation data; input/commands flow back.
- Do not introduce a second world authority on the main thread.
- Use double precision (`DVec3`) for universe-space math; cast only after origin shifting.
- Use raw SI internally; kelvin internally for temperature.
- Do not use runtime-dependent hashes for persistent procedural seeds.
- Derive semantic child seeds so unrelated random subsystems do not perturb one another.
- Regenerate deterministic procedural baseline; persist meaningful deltas.
- Keep serialization-format types out of long-term domain models.
- Render depth tier and LOD/detail are separate concerns.
- Thermal topology is established; detailed thermal equations remain provisional.
- Preserve Inferior's dark, industrial, asymmetrical, detail-rich visual identity unless the task
  explicitly redesigns it.

## Verification

Distinguish:

- implemented;
- builds;
- tests pass;
- inspected/reasoned about;
- visually confirmed by Timo in-engine.

Never claim visual verification from code inspection alone.
