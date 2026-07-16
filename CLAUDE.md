# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

## Required context

Primary AI-facing documentation lives in `Docs-ai/`.

Before substantial work, read in this order:

1. `Docs-ai/!invariants.md`
2. `Docs-ai/!current-state.md`
3. `Docs-ai/architecture-map-ai.md`
4. The active `*-ai.md` subsystem reference relevant to the task

You are the implicit owner of `Docs-ai/!current-state.md`. Update it whenever useful
and include changes in commits.

Maintain it as needed:
- update current development state,
- remove completed work once it no longer helps active development,
- record known gaps and deferred work,
- keep it concise enough to remain useful as working memory.

Tell Timo whenever you change it so the updated version can be uploaded to Claude Projects.

`Docs-ai/` documents are compact AI references. They are often derived from a related
file in `Docs/`, but may also be native AI-maintained documents. The naming pattern is
usually `<something>-ai.md`.

`Docs/` contains fuller design material, often with rationale and discussion. Do not load it
by default. Read it when:

- Timo explicitly asks;
- an active compact reference points there;
- rationale is needed to resolve a design question.

`Docs-archive/` is historical context only. It may contain superseded or already implemented
designs. Do not use it as current authority unless explicitly instructed.

## Document authority

For what the code currently does:

1. Repository code
2. `architecture-map-ai.md`
3. `!current-state.md`
4. Active subsystem references
5. Full historical design documents

The architecture map is authoritative for code location and structure. Current-state is 
authoritative for development status, known regressions, and active transition state.

For intended design:

1. Timo's current instructions and the current development brief
2. `!invariants.md`
3. The authoritative active subsystem reference
4. `design-ai.md`
5. Full design/lore documents for rationale
6. Archived documents only as historical context

Never average, merge, or creatively reconcile conflicting claims. State the conflict and use
the higher-authority source. If code conflicts with newer authoritative design, report a
design/code mismatch rather than rewriting the design to match old code.

Exact code identifiers, file locations, and APIs must be verified against the repository before
use.

## Build & Run

```powershell
# Build entire solution
dotnet build Inferior.slnx

# Run the game
dotnet run --project Inferior.Game

# Build in release mode
dotnet build Inferior.slnx -c Release
```

MonoGame content (fonts, textures) is compiled via `MonoGame.Content.Builder.Task`
automatically on build. Content source is in `Inferior.Game/Content/Content.mgcb`.

Test project: `Inferior.Game.Test` (xUnit).

## Architecture

Inferior is a space exploration game built on MonoGame (.NET 10). The architecture map is the
current source for file-level structure; do not duplicate its details here. Broadly:

- `Inferior.Core` — foundational math, units, deterministic random, game state, buses, clock.
- `Inferior.Galaxy` — deterministic procedural galaxy and star-system data.
- `Inferior.Gameplay` — simulation domain, ship, physics, components, sensors.
- `Inferior.Rendering` — 3D rendering helpers and mesh/render owners.
- `Inferior.Persistence` — persistence records and IO; no live gameplay objects.
- `Inferior.UI` — self-contained retained-mode UI framework.
- `Inferior.Game` — executable composition layer, game states, station generation, UI wiring.
- `Inferior.Game.Test` — xUnit tests.

Dependency direction is intentionally layered:

```text
Core  ←  Galaxy  ←  Gameplay  ←  Persistence
Core  ←  Galaxy  ←  Gameplay  ←  Rendering
Core  ←─────────────────────────  UI
Core  ←  Galaxy  ←  Gameplay  ←  Game  (references everything)
```

Do not rely on the architecture summary above for exact current file names or implemented
features; use the architecture map and code.

## How to work

- Inspect relevant code before proposing or implementing changes.
- Preserve working behaviour outside the requested scope.
- Prefer the smallest coherent solution.
- Do not create duplicate implementations of the same concept.
- Do not build generic infrastructure before a concrete use case requires it.
- Do not silently fix unrelated issues; report them separately unless they block the task.
- Separate design discussion from implementation. When design is unsettled, do not make a
  permanent architecture choice silently.

For simulation, threading, or reference-frame work, first map ownership and data flow:

- who owns the value;
- who writes it;
- who reads it;
- how it crosses thread boundaries;
- whether a frame change is a coordinate transform or a physical effect.

For 3D geometry, reason before generating vertices:

1. Define local coordinate axes.
2. Separate points from directions.
3. Define outward normals and winding.
4. Define parent/child transform order.
5. Define attachment port position and orientation independently from mesh construction.
6. Define collision and clearance volumes.
7. Identify degenerate cases.
8. Add mathematical invariants/tests where practical.

Useful geometry invariants include:

- no NaN or infinity vertices;
- no unintended near-zero-area triangles;
- expected outward winding;
- connected port world positions coincide within tolerance;
- connected port normals oppose within tolerance.

## Core project rules

Detailed rules live in `!invariants.md`. Important highlights:

- The simulation thread is the intended owner of mutable live universe state.
- Rendering/UI should consume snapshots or presentation data; input/commands flow back.
- Do not introduce competing world authority on the main thread.
- All universe position/physics math uses double precision where required (`DVec3`); cast to
  `Vector3` only after origin shifting for rendering.
- Use raw SI internally. Temperature is kelvin internally; Celsius is presentation only.
- Persistent procedural generation must not use `System.HashCode`, `HashCode.Combine`, runtime
  object hashes, or other process-dependent hashing.
- Derive deterministic child seeds semantically so changes in one random subsystem do not
  reshuffle unrelated output.
- Deterministic procedural baselines are normally regenerated; persist meaningful deltas.
- Live gameplay objects do not know persistence DTOs or serialization formats.
- Render depth tier and geometric detail/LOD are separate systems.
- The thermal system's gameplay topology is established, but detailed equations remain
  provisional; do not fossilize current formulas into new architecture.
- The dark, industrial, asymmetrical, detail-rich station aesthetic is intentional.
- Do not hard-code fire rate or shield recharge where those should emerge from capacitor state.

## Branch awareness — station shadows

Current stable work must not assume station shadows exist. Master contains no shadow code.

The failed station lighting/shadow experiment is quarantined on
`wip/station-lighting-shadows` (stable recovery branch: `recovery/no-station-lighting-shadows`).
Treat it as read-only history — do not copy experimental shadow code back into stable
branches.

The agreed replacement design is `Docs/station-lighting-pipeline-spec.md` (fresh, phased
implementation). Shadow/lighting work happens only through explicitly scoped tasks based
on that spec. Historical context: `Docs-archive/Shadow_fail_retrospective.md`.

## Current-state maintenance

`!current-state.md` should answer: **where is development now?**

Keep it useful as working memory, not as permanent history.

- Remove stable, finished implementation narratives once they no longer help current work.
- Keep current work, known regressions, design gaps, deferred issues, and near-term next steps.
- Move long completed-session history elsewhere rather than letting the file grow indefinitely.
- Distinguish implementation status clearly, for example:
  - stable/verified;
  - working but design incomplete;
  - broken/regressed;
  - experimental;
  - designed but not implemented;
  - open design.

Do not mark something visually confirmed unless Timo has actually seen it in-engine.

## Verification language

Do not blur these together:

- implemented;
- builds;
- automated tests pass;
- inspected/reasoned about;
- visually confirmed by Timo in-engine.

Never claim visual verification from code inspection alone. A mathematically plausible result is
not necessarily visually correct.

## What NOT to do

- Do not simulate the full universe while the player is away; use deterministic generation and
  persistent deltas where appropriate.
- Do not use `float` for universe coordinates.
- Do not mix mutable state logic across game-state or thread-ownership boundaries.
- Do not add MW/MJ scaling to simulation code; display code handles presentation units.
- Do not treat archived or superseded documents as active design.
- Do not silently make a provisional design permanent because the current code happens to use it.

## Developer console (planned)

Useful tuning commands may eventually include:

`goto sol` / `timescale 10000` / `spawn ship pirate` / `planet earth`
