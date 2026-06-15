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
| **Targeting system** | ⚠ Brief written | 'C' key + mouse click targeting; `TargetingSystem` class; HUD brackets; implementation status with Code unknown — verify |

---

## Game states

Implemented (3):

| State | Class | Purpose |
|---|---|---|
| `GalaxyMap` | `GalaxyMapState` | Top-level galaxy overview, star selection |
| `SystemMap` | `SystemMapState` | 2D orbital map of selected star system |
| `SystemSpace` | `SystemSpaceState` | In-system 3D flight |

Planned, not yet implemented:

```csharp
enum GameState
{
    GalaxyMap, SystemMap,
    HyperspaceEntry, Hyperspace, HyperspaceExit,
    SystemSpace,
    PlanetApproach, Atmosphere, Surface,
    Docked
}
```

Navigation flow (current): Galaxy map → (double-click star) → System map → (double-click body) → System flight, spawning near the selected body.

---

## What is in progress

### Station generation — Session 7 interrupted

Session 7 was cut short by usage limit. **First action in next Code session: ask Code for a status report on what was completed.** Session 7 scope was:

| Item | Expected status |
|---|---|
| Ambient reduction (0.18 → 0.09) | Likely done |
| Panel seam color subtlety (factor 0.48 → 0.72) | Likely done |
| Panel seam Z-offset (0.012 → 0.028) | Likely done |
| AO restricted to base mesh faces only (`BaseFaceCount`) | Unknown |
| `AddOrientedBox` correct per-face normals | Unknown |
| `StationLightInfo` updated with Rate/Phase/Pattern | Unknown |
| Blinking glow animation in `DrawStationGlows` | Unknown |
| Aviation warning lights on antenna/chimney tips | Unknown |
| Ambient marker lights on modules | Unknown |
| New vent types (Louvered, ScreenMesh) | Unknown |

Session 7 brief is in docs-claude outputs: `inferior-session7-lighting-refinement.md`.

### Power system — refinement phase

Core working: reactor, bus, shield startup sequence, instruments. Needs:
- More ship components wired in (engine power draw, gyro, artificial gravity)
- FlyabilityMonitor checks
- Heat system implementation
- Coolant loop

---

## What is next (priority order)

1. **Complete Session 7** — get status from Code, finish remaining items
2. **Station text/markings pass** — station name on hull, bay numbers (Session 8 when ready)
3. **Station module shape variety** — octagonal/hexagonal module cross-sections; requires updating decoration passes to use general face list rather than BoxEdges lookup
4. **Power system refinement** — heat, coolant, more components
5. **Ship hull implementation** — vertex-first mesh, panel auto-generation

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
| Targeting system implementation status | Verify with Code |

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
| `inferior-design.md` | docs | Full design doc with rationale |
| `inferior-lore.md` | docs | Full lore with narrative |
| `inferior-classes.md` | docs-archive | Class sketches — may be stale; repo is authoritative |
| `inferior-design-persistence.md` | docs-archive | Persistence design — implemented |
| `inferior-design-ui.md` | docs-archive | UI design — implemented |
