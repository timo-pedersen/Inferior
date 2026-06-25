# Inferior — Current State

> Updated per design session. Tells Claude and Claude Code what is done,
> what is in progress, and what is next.

---

## What is implemented and working

| System | Status | Notes |
|---|---|---|
| Galaxy map | ✓ Done | 2048 stars, fixed seed, deterministic |
| System map | ✓ Done | Bodies, orbits |
| 3D flight state (`SystemSpaceState`) | ✓ Done | Newtonian, origin-shifting render |
| UI library (`Inferior.UI`) | ✓ Done | Button, Label, TextBox, Panel, Window, InstrumentMeter, SystemConsole, DirectionBall, EdgePanelHost, UIManager, Theme, InputState |
| DataBus | ✓ Done | 6 buses: System, Instruments, InstrumentState, InstrumentRanges, Radar, RadarLost |
| CommandBus | ✓ Done | Reverse direction; sim thread drains |
| Simulation loop | ✓ Done | 60Hz background thread; PlayerInput immutable snapshot |
| DVec3 + origin shifting | ✓ Done | Double-precision coordinates throughout |
| GravitySensor | ✓ Done | PassiveSensor with noise; publishes to Instruments |
| Noise library | ✓ Done | Simplex1, White, Pink, Periodic, Spike |
| GameClock | ✓ Done | SimTime, InGameDate |
| Environment query class | ✓ Done | Gravity, nearest star/body, field vectors |
| Persistence layer | ✓ Partial | Architecture designed; file implementations in progress |
| ShipSizeClass enum | ✓ Stubbed | Exists, not yet enforced |
| **Power system** | ✓ Done | PowerCore → PowerBus → Connector → Shield chain working; cold start sequence; PowerPriorityManager; instruments reporting to DataBus; CommandBus integration |
| **Station generation** | ✓ Done (ongoing refinement) | Full procedural generation pipeline; see `inferior-design-stations-claude.md` |
| **Directional lighting** | ✓ Done | Applied to station meshes at generation time; pre-baked vertex colours |
| **Animated glow lights** | ✓ Done | `StationLightInfo` with Rate/Phase/LightPattern; `ComputeGlowIntensity`; strobe, pulse, heartbeat patterns; aviation warning lights on tall structures |
| **Targeting system** | ✓ Done | 'C' key + click targeting; `TargetingSystem` class; HUD brackets; `ProjectToScreen` fixed for render-scale 1e-9 |
| **Planetary flight (`FlightMode`)** | ✓ Done | `FlightMode { Space, Atmosphere }` in sim; auto-detect via nearest body altitude; force-based physics in atmosphere (gravity, drag, lift); Flight Assist (V key) and Glide Mode (G key) sim-owned; `SystemSpaceState` stays active throughout |
| **Keplerian orbital mechanics** | ✓ Done | Full orbital elements (`e`, `i`, `Ω`, `ω`, `M₀`) on `OrbitalBody`; `ComputePosition` + `ComputeVelocity`; Newton solver for eccentric anomaly; moons/asteroids/stations keep circular rail |
| **PlanetData + PlanetFactory** | ✓ Done | `PlanetType`, `AtmosphereCompositionType` enums; `PlanetData` record with physical/atmosphere/surface data; `PlanetFactory` procedural generation; per-tick planet orientation update in sim |

---

## Game states

Implemented (3):

| State | Class | Purpose |
|---|---|---|
| `GalaxyMap` | `GalaxyMapState` | Top-level galaxy overview, star selection |
| `SystemMap` | `SystemMapState` | 2D orbital map of selected star system |
| `SystemSpace` | `SystemSpaceState` | In-system 3D flight, including atmospheric flight |

**Architectural note — FlightMode, not separate states:**
Atmospheric flight is a `FlightMode` enum within `SystemSpaceState`, not a separate
`GameState`. The sim thread and all ship state run continuously through the transition.
`FlightMode` controls which forces the sim applies and which render passes are active.

```csharp
public enum FlightMode { Space, Atmosphere }
```

Planned future GameStates (not yet designed or implemented):

```csharp
enum GameState
{
    GalaxyMap, SystemMap,
    HyperspaceEntry, Hyperspace, HyperspaceExit,
    SystemSpace,   // atmospheric flight is FlightMode.Atmosphere within this state
    Surface,       // on foot — future
    Docked
}
```

`PlanetApproach` and `Atmosphere` have been removed as separate GameStates.

Navigation flow (current): Galaxy map → (double-click star) → System map → (double-click body) → System flight, spawning near the selected body.

---

## What is in progress

### Power system — refinement phase

Core working: reactor, bus, shield startup sequence, instruments. Needs:
- More ship components wired in (engine power draw, gyro, artificial gravity)
- FlyabilityMonitor checks
- Heat system implementation
- Coolant loop

---

## What is next (priority order)

1. **Brief B** — atmospheric instruments (lat/lon, ground radar HUD panel, checkerboard surface mesh, temperature map); reads `OrbitalBody.Orientation` and `PlanetData` set by Brief A
2. **Planetary flight HUD** — altitude, ground speed, density overlay in SystemSpaceState when `FlightMode == Atmosphere`
3. **Sky rendering** — atmosphere colour gradient + haze at low altitude; pass through Atmosphere.fx in SystemSpaceState
4. **Station text/markings pass** — station name on hull, bay numbers
5. **Power system refinement** — heat, coolant, more components

---

## Key conventions

- **Rate properties** (`MaxPower`, `PowerConsumption`, reactor output): always **watts (W)**
- **Storage properties** (`MaxJ`, capacitors): always **joules (J)**
- **Thermal mass** (`HeatCapacity`): always **J/K** (joules per kelvin)
- Each tick: `energy (J) = power (W) × dt`
- No display scaling in simulation; `InstrumentMeter.ScaleFactor` handles unit conversion for gauges
- No MW or MJ in code — raw SI throughout
- Topic convention on DataBus: `ComponentName.ValueName`; multiple instances: `ComponentName_N.ValueName`
- `ShipRecord` must not appear outside `ShipBuilder`, `ShipExtensions`, and `ShipPersistenceService`
- `ShipBuilder` is the sole construction path for `Ship`

---

## Project structure

```
Inferior.Core        — DVec3, Units, DataBus, CommandBus, GameClock, Noise, PlayerInput, Topics
Inferior.Galaxy      — star/system generation, OrbitalBody, StarPhysics
Inferior.Gameplay    — Simulation, Physics/, SensorData/, Sensors/, TargetingSystem
Inferior.Persistence — ShipRecord, repositories, log (pure IO, no live objects)
Inferior.Rendering   — Camera3D, MeshFactory
Inferior.UI          — UIManager, UIRenderer, Theme, all controls
Inferior.Game        — entry point, game states, SpaceSimulation, ShipBuilder, factories,
                       StationGenerator, StationDecorator, StationModuleRegistry
```

Dependency: `Core ← Galaxy ← Gameplay ← Persistence` and `Core ← UI`, all converging in `Game`.

---

## Station generation — architecture summary

See `inferior-design-stations-claude.md` for full reference. Key facts:

- **Generation is deterministic** — same seed always produces same station
- **Pre-baked lighting** — directional light and AO applied at generation time to vertex colours; renderer draws static meshes
- **Screen-space glow** — `StationLightInfo` list on `StationModel`; `DrawStationGlows` uses `BlendState.Additive` SpriteBatch pass after 3D scene
- **AnimTag stubs** — warning strobes tagged for future animation; renderer does not yet use them
- **Decoration order matters** — occupancy tracking ensures no overlaps; passes run in fixed order; AO and lighting always last

---

## Open design decisions

| Decision | Status |
|---|---|
| Hyperspace mode geometry (flat/tunnel types, Voronoi, gravity shadows) | Not designed |
| Faction / reputation system | Not designed |
| Internal component penetration formula — `(1 − integrity)²` confirmed | Partially decided |
| Generator fuel: nuclear or consumable? | **Undecided** |
| Lore epoch / time scale for in-game date | **Undecided** |
| Hyperspace interference lock formula | Placeholder only |
| Shield coverage mapping — which hull faces a given shield covers | Pending |
| Weapons system | Not yet designed |
| Multiplayer architecture compatibility | Noted, deferred |
| Station text/markings pass — font atlas geometry pipeline | Not yet designed |
| Station module shape variety — non-box modules | Design noted, not implemented |
| Station weathering pass | Not yet designed |
| Station enclosed archetypes (Sphere, Pyramid, Prism, etc.) | Designed, not implemented |
| Planetary terrain rendering | Deferred — separate brief required |
| Landing radar instrument | Deferred — requires design doc with sketches |
| Atmospheric visual effects (clouds, haze, re-entry glow) | Not yet designed |

---

## Document map

| File | Where | Purpose |
|---|---|---|
| `inferior-current-state.md` | docs-claude | This file — active state, conventions, next steps |
| `inferior-design-claude.md` | docs-claude | Design decisions, philosophy, all major systems |
| `inferior-lore-claude.md` | docs-claude | Lore reference — bands, species, drive, materials |
| `inferior-components-claude.md` | docs-claude | Component specs, properties, units |
| `inferior-design-ship-claude.md` | docs-claude | Ship classes, roles, hull system |
| `inferior-design-stations-claude.md` | docs-claude | Station generation — architecture, modules, decoration |
| `inferior-design-planetary-claude.md` | docs-claude | Planetary flight — FlightMode, forces, glide, Flight Assist |
| `inferior-design.md` | docs | Full design doc with rationale |
| `inferior-lore.md` | docs | Full lore with narrative |
| `inferior-classes.md` | docs-archive | Class sketches — may be stale; repo is authoritative |
| `inferior-design-persistence.md` | docs-archive | Persistence design — implemented |
| `inferior-design-ui.md` | docs-archive | UI design — implemented |
