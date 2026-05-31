# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
# Build entire solution
dotnet build Inferior.slnx

# Run the game
dotnet run --project Inferior.Game

# Build in release mode
dotnet build Inferior.slnx -c Release
```

MonoGame content (fonts, textures) is compiled via `MonoGame.Content.Builder.Task` automatically on build. Content source is in `Inferior.Game/Content/Content.mgcb`.

No test projects exist.

## Architecture

**Inferior** is a space exploration game built on MonoGame (.NET 10.0). Five projects:

- **Inferior.Core** — math primitives (`DVec3`, `DMath`, `Units`), `SeededRandom`, and the `GameStateMachine` that drives state transitions.
- **Inferior.Galaxy** — procedural universe generation: `GalaxyGenerator` creates ~2048 stars in spiral arms using `SeededRandom`, plus `StarSystem` and `OrbitalBody` for system contents.
- **Inferior.Game** — the executable. `InferiorGame` (MonoGame `Game` subclass) owns the game loop, `Camera3D`, `MeshFactory`, and three game states.
- **Inferior.UI** — self-contained UI framework: `UIManager` tracks focus/hover, `UIRenderer` draws controls, `Theme` controls visuals. Controls: `Label`, `Button`, `Panel`, `Window`.
- **Inferior.Rendering** — placeholder library, currently empty.

### Key decisions / what NOT to do
See "Project Design Reference" section below.

### Game State Flow

Three states live in `Inferior.Game/States/`:

| State | Purpose |
|---|---|
| `GalaxyMapState` | Top-level galaxy overview, star selection |
| `SystemMapState` | 2D orbital map of a selected star system |
| `SystemSpaceState` | In-system 3D flight |

State transitions go through `GameStateMachine` in `Inferior.Core`. Each state is a discrete class; the machine holds the active state and calls `Update`/`Draw` on it.

### Key Design Notes

- All math uses **double precision** (`DVec3`, `DMath`) — not MonoGame's single-precision `Vector3`.
- Galaxy generation is **fully deterministic** via `SeededRandom`; same seed → same universe.
- `Directory.Build.props` enables nullable reference types and implicit usings globally across all projects.


# Inferior — Project Design Reference

## What it is
A solo-developed 3D space exploration game inspired by Elite. Emphasis on scale,
atmosphere, and a living-feeling universe. Built as a learning project for C# and
Claude Code, but needs to be ready for release eventually. 

## Tech stack
- C# / .NET (latest LTS)
- MonoGame (3D, BasicEffect — no custom shaders initially)
- Visual Studio

## Aesthetic
Low-poly flat-shaded 3D, Elite-inspired. No texture budget required.
Lighting is the atmosphere — this is a deliberate creative and practical choice.

Lighting targets:
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
    GalaxyMap, HyperspaceEntry, Hyperspace, HyperspaceExit,
    SystemSpace, PlanetApproach, Atmosphere, Surface, Docked
}
```

## Coordinate system and rendering
- DVec3 everywhere for physics/position (double precision — non-negotiable)
- Rendering: subtract ship position before casting to float (always safe)
- Zoom via MetersPerPixel — no coordinate system switch at different scales
- Orbital mechanics in the ecliptic plane (Y=0), slight inclinations for visual interest

## Physics and time compression
- Newtonian flight model, double precision
- Time compression multiplier on physics timestep (not real time)
- Fixed substep count scales with warp level for stability
- Auto-drop time compression near bodies (proximity threshold)
- Combat always resets to 1x

## Galaxy and world generation
- Fully procedural, seeded — deterministic from galaxy seed
- Star types, positions, spiral arms, Z distribution
- Systems generated on demand from star seed
- Planets, moons, orbits all seeded

## World state / persistence
- Two tiers:
  - **Procedural baseline** — generated from seed, never stored
  - **Exception list** — only what the player has changed (destroyed bases,
    crashed ships, etc.) stored as delta on top of procedural
- Destroyed bases: permanent
- Small debris (crashed ships etc.): decay timer, removed after days/week of game time
- Randomness-as-simulation: events seeded on (systemID + timeWindow) so
  the universe feels consistent without full simulation

## Developer console (from day one)
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

