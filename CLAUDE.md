# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in 
this repository.

Additional info is available in the Docs folder, architecture, classes, 
and background lore etc.

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

**Inferior** is a space exploration game built on MonoGame (.NET 10.0). Six projects:

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

Three states live in `Inferior.Game/States/`:

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


# Inferior — Project Design Reference

## What it is
A solo-developed 3D space exploration game inspired by Elite. Emphasis on scale,
atmosphere, and a living-feeling universe. Built partly as a learning project 
for C# and Claude Code, but needs to be ready for release eventually. Performance
and polish are important, but not at the cost of maintainability or sanity.

## Tech stack
- C# / .NET (latest LTS)
- MonoGame (3D, BasicEffect — no custom shaders initially, but will come)
- Visual Studio 2026

## Aesthetic
Low-poly flat-shaded 3D, Elite-inspired. No texture budget required.
Lighting is the atmosphere — this is a deliberate creative and practical choice.

Lighting targets (initial design. Good to know: these will be improved on in the 
future, so we don't lock ourselves in):
- One directional light per system (the star), colour/intensity from star type
- Low ambient (5–10%) — space is dark
- Specular highlight on ship hull
- Planet terminator line (self-shadowing sphere)
- Ship shadow on nearby surfaces when landing

## Game state machine
Each state owns its update loop, renderer, and input handling.
Transitions are explicit — never bleed logic across states.

```csharp
enum GameState
{
    GalaxyMap, SystemMap, HyperspaceEntry, Hyperspace, HyperspaceExit,
    SystemSpace, PlanetApproach, Atmosphere, Surface, Docked
}
```

States subject to change.

## Coordinate system and rendering
- DVec3 everywhere for physics/position (double precision — non-negotiable)
- Rendering: subtract ship/camera universe position before casting to float (always safe)
- Zoom via MetersPerPixel — no coordinate system switch at different scales
- Orbital mechanics in the ecliptic plane (Y=0), slight inclinations for visual interest

## Physics and time compression
- Newtonian flight model, double precision
- Time compression exists in the 2D system map for watching orbits, but is not
  used during 3D flight (SystemSpaceState)
- Fixed substep count scales with warp level for stability

## Galaxy and world generation
- Fully procedural, seeded — deterministic from galaxy seed
- Star types, positions, spiral arms, Z distribution
- Systems generated on demand from star seed
- Planets, moons, asteroids, orbits all seeded

## World state / persistence
- Two tiers:
  - **Procedural baseline** — generated from seed, never stored
  - **Exception list 1** — Overrides to randomness, eg for designed systems or moons etc.
  - **Exception list 2** — only what the player has changed (destroyed bases,
    crashed ships, etc.) stored as delta on top of procedural
- Destroyed bases: permanent
- Small debris (crashed ships etc.): decay timer, removed after days/week of game time
- Randomness-as-simulation: events seeded on (systemID + timeWindow) so
  the universe feels consistent without full simulation

## Developer console (eventually, not from day one)
Essential for tuning. Commands like:
  goto sol / timescale 10000 / spawn ship pirate / planet earth

## Key decisions / what NOT to do
- Do not simulate the full universe while the player is away — use seeded
  randomness instead (only player-touched state is persistent)
- Do not use float for universe coordinates — precision errors compound badly
- Do not mix state logic across GameState boundaries
- Do not add textures/shaders early — commit to the flat-shaded aesthetic
- No coordinate system switch at zoom levels — MetersPerPixel handles all scales

This is a living document — update it as design decisions are made or revised. 
The reasoning behind decisions matters as much as the decisions themselves.
