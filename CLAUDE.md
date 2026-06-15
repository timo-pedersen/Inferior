# CLAUDE.md

This file provides guidance to You, Claude Code (claude.ai/code) when working with code in
this repository.

Primary documentation is available in the "Docs-claude" folder.
This includes a file "inferior-current-state.md" which you are solely responsible for.
It lists current state of development, what area to focus on now, what is postponed, and
anything else you find useful to work efficiently.

Additional info is available in these two folders:
- Docs
- Docs-archive

"Docs-claude" documents are maintained largely by You.
They are usually created from docs in the Docs folder (not true for inferior-current-state.md),
but with less noise and dead weight.
Files in this folder are named after this pattern: "inferior-<something>-claude.md".
In the main docs folder there may be a similar named file "inferior-<something>.md". This implies
a relationship between these files.

"Docs" contains documents that I may maintain and update. They are too noisy for regular AI use
(e.g. may contain a full design discussion with reasons), but may occasionally be read by You,
when deemed necessary by me or explicitly stated in a task. You may request to use these docs.

"Docs-archive" contains documents that may be outdated or already implemented.
May be occasionally referred to when deemed necessary by me or explicitly stated in a task.
You may request to use these docs.

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

No test projects exist.

## Architecture

**Inferior** is a space exploration game built on MonoGame (.NET 10.0). Some of the projects:

- **Inferior.Core** — math primitives (`DVec3`, `DMath`, `Units`), `SeededRandom`,
  `GameStateMachine`, `DataBus` (typed message buses), `GameClock`, `Noise`,
  and `PlayerInput`.
- **Inferior.Galaxy** — procedural universe generation: `GalaxyGenerator` creates ~2048
  stars in spiral arms using `SeededRandom`, plus `StarSystem`, `OrbitalBody`, and
  `StarPhysics` for stellar mass/radius/pressure calculations.
- **Inferior.Gameplay** — simulation layer. `Simulation` runs the 60 Hz background
  thread. `Physics/` holds `CelestialBody` and `SimWorld`. `SensorData/` holds
  `Environment` (world query class), `GravityCalculations`, and `StarPhysics` stubs.
  `Sensors/` holds `PassiveSensor` and `GravitySensor`.
- **Inferior.Rendering** — 3D rendering utilities: `Camera3D` (quaternion free-look,
  origin-shift rendering) and `MeshFactory` (sphere, ring, quad mesh generation).
- **Inferior.UI** — self-contained UI framework: `UIManager` tracks focus/hover,
  `UIRenderer` draws controls, `Theme` controls visuals. Controls: `Label`, `Button`,
  `Panel`, `Window`, `InstrumentMeter`, `SystemConsole`, `DirectionBall`.
- **Inferior.Game** — the executable. `InferiorGame` owns the game loop and three
  game states. `SpaceSimulation` (extends `Simulation`) wires sensors to the live
  star system each tick.

### Dependency graph

```
Core  ←  Galaxy  ←  Gameplay  ←  Rendering
Core  ←─────────────────────────  UI
Core  ←  Galaxy  ←  Gameplay  ←  Game  (references everything)
```

### Game State Flow

Three states currently implemented (see `inferior-current-state.md` for full planned enum):

| State | Purpose |
|---|---|
| `GalaxyMapState` | Top-level galaxy overview, star selection |
| `SystemMapState` | 2D orbital map of a selected star system |
| `SystemSpaceState` | In-system 3D flight |

Navigation: Galaxy map → (double-click star) → System map → (double-click body) →
System flight, spawning near the selected body.

State transitions go through `GameStateMachine` in `Inferior.Core`. Each state is a
discrete class; the machine holds the active state and calls `Update`/`Draw` on it.

### Key Design Notes

- All physics/position math uses **double precision** (`DVec3`) — not MonoGame's
  single-precision `Vector3`. Cast to `Vector3` only at render time after subtracting
  the camera universe position.
- Galaxy generation is **fully deterministic** via `SeededRandom`; same seed → same
  universe.
- `Camera3D` uses a **quaternion orientation** — no pitch clamp, no gimbal lock,
  true 6DOF look.
- `DataBus` is the inter-system message bus. The simulation thread publishes freely;
  the main thread calls `DataBus.Drain()` once per `Update()` to dispatch handlers.
- `Directory.Build.props` enables nullable reference types and implicit usings globally.

---

## What NOT to do

- Do not simulate the full universe while the player is away — use seeded randomness
  instead. Only player-touched state is persistent (see world state model in
  `inferior-design-claude.md`).
- Do not use `float` for universe coordinates — precision errors compound badly at scale.
- Do not mix state logic across `GameState` boundaries.
- Do not hard-code fire rate or shield recharge — these emerge from capacitor charge state.
- Do not add MW or MJ unit conversions in code — raw SI (watts, joules) throughout.

## Developer console (planned, not from day one)

Essential for tuning. Commands will include:
`goto sol` / `timescale 10000` / `spawn ship pirate` / `planet earth`
