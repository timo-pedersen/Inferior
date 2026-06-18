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
| UI library (`Inferior.UI`) | ✓ Done | Button, Label, TextBox, Panel, Window, InstrumentMeter, SystemConsole, DirectionBall, EdgePanelHost, UIManager, Theme, InputState, **RadarDisplay** |
| DataBus | ✓ Done | 6 buses: System, Instruments, InstrumentState, InstrumentRanges, Radar, RadarLost |
| **System message priority** | ✓ Done | `SystemMessagePriority` enum (Info→Critical); `SystemMessage` record on System bus; `SystemConsole` coloured prefixes; `HudAlertDisplay` centre-screen overlay (Warning 4 s, ImportantWarning 6 s, Critical until keypress) |
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
| **Heat & coolant system** | ✓ Done | `ThermalNode.LastHeatInputW`; `ShipComponent.EffectiveEfficiency` + overtemp damage in Tick; throttle heat curve in `PowerReactor`; `ShieldComponent` ThermalNode added; `HyperspaceHeatSink` resets on saturation + publishes Critical alert; `CoolantSystem` edge-triggered level warnings (NB/Warning/ImportantWarning/Critical); `Topics.Ship.ThermalSignature` published per tick |
| **Station generation** | ✓ Done (ongoing refinement) | Full procedural generation pipeline; see `inferior-design-stations-claude.md` |
| **Directional lighting** | ✓ Done | Hull: real-time `VertexPositionNormalTexture` + `BasicEffect LightingEnabled=true` from actual star pos; Decoration: pre-baked vertex colours |
| **Animated glow lights** | ✓ Done | `StationLightInfo` with Rate/Phase/LightPattern; `ComputeGlowIntensity`; strobe, pulse, heartbeat patterns; aviation warning lights on tall structures |
| **Targeting system** | ✓ Done | 'C' key + click targeting; `TargetingSystem` class; HUD brackets; `ProjectToScreen` fixed for render-scale 1e-9 |
| **RadarDisplay** | ✓ Done | Oval disc (scanline fill, cos30° foreshortening); 5 linear range steps (500m–100km) with click-to-cycle; LOG mode (log distance mapping); 3 range rings; ELEV/TEXT/OOB/RINGS layer toggles; OOB bearing ring; contact markers by type (diamond/triangle/dash/hollow-circle); elevation bars; text labels; exclusion zone interface; bidirectional left speed bar (approach); unidirectional right speed bar (local frame); 5 LED indicators (PWR wired, others stubbed); cockpit DirectionBall wired with contact vectors |
| **Surface texture infrastructure** | ✓ Done | `VertexPositionColorTexture` throughout; UV projection in AddQuad/AddTriangle; `SurfaceTexture` enum; `StationTextureRegistry`; separate `GlassMesh` for windows |
| **Procedural station textures** | ✓ Done | `StationProfile` (economy/age/wealth); `TexturePalette` per economy type; 5-step 512×512 generation (noise → panels → seams → grime → scratches); cache by (surface, paletteHash); station name baked onto core module face; `BitmapFonts` 5×7 + `TexturePainter` |
| **Parabolic dishes** | ✓ Done | 3 size classes (9/11/13-sided); per-face small+medium in `GenerateDishes`; station-wide landmark large dish in `RunLargeDishPass` (22% of science/military stations); support arm, diagonal brace, feed mast+box, feed struts |
| **Window enhancements** | ✓ Done | Per-window weighted palette; rectangular/octagon/cupola frames (Lerp blend toward dark neutral); glass gradient (bottom 0.72× + blue nudge → top Lerp→White 0.18); cupola frame per triangle + 3 edge braces per panel; `AddQuadGradient`/`AddTriangleGradient` in StationModuleMesh |
| **Shipping containers** | ✓ Done (visual pass complete) | `ShippingContainer`, `ContainerContents`, `LockGrade`, `CommodityType` in `Inferior.Game.Containers`; `ShippingContainerFactory` with full chamfered mesh pipeline; `StationModuleMesh.ToArrays()` added; containers as station decoration via `GenerateContainers` in `StationDecorator` — placed on module faces using the same `FaceInfo`/`FaceOccupancy` system as tanks; full chamfer geometry (4 long + 8 short strips + 8 corner triangles, all gap-free); manufacturer label on ±Y faces via `ShippingContainerFactory.GenerateManufacturerName`; handedness fix for vertical/mirrored placements |

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

### Power system — refinement phase

Core working: reactor, bus, shield startup sequence, instruments, full thermal/coolant loop. Needs:
- More ship components wired in (engine power draw, gyro, artificial gravity)
- FlyabilityMonitor checks

---

## What is next (priority order)

1. **Station texture quality pass** — replace 5×7 font with larger / higher-res glyphs; add per-economy accent markings, panel rivets, warning stripes
3. **Station module shape variety** — octagonal/hexagonal module cross-sections; requires updating decoration passes to use general face list rather than BoxEdges lookup
4. **Power system refinement** — more components (engine, gyro), FlyabilityMonitor
5. **Ship hull implementation** — vertex-first mesh, panel auto-generation

### Container deferred work (do not implement until reviewed)
- Station decorator pass placing containers on docking/cargo modules
- Ship hardpoints and `ShippingModule` component
- `ShippingContainerStack` (magnetically bonded groups)
- Parent-relative transform
- Lock/unlock interaction
- Cargo simulation / `CommodityType` economy

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
| Station texture quality pass — larger glyphs, accent markings, rivets | Deferred (Session C) |
| Station module shape variety — non-box modules | Design noted, not implemented |
| Station weathering pass — per-module age overlay | Deferred |
| Station enclosed archetypes (Sphere, Pyramid, Prism, etc.) | Designed, not implemented |

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
