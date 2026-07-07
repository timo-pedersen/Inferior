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
| DataBus | ✓ Done | 8 buses: System, Instruments, InstrumentState, InstrumentRanges, Radar, RadarLost, Spectra, Target |
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
| **Yagi antenna greeble** | ✓ Done | 5 element types (I/X/O/S/H), 7 base types; straight mast (0.3–1.2 m) + tilted boom (0–25° from normal); O-element discs fully solid (side + cap faces); seeded brightness variation per antenna; connectable for cable system |
| **Station cables / conduits** | ✓ Done | Grid-routed bundles and conduits on module faces; junction boxes; fasteners; edge clamps; parabolic dish and mast antenna bases registered as connectable endpoints; octagonal module side-face cables working (degenerate cap faces guarded at cable call only); muted industrial colour palette |
| **Directional lighting** | ✓ Done | Applied to station meshes at generation time; pre-baked vertex colours; SunDirection pre-set in OnEnter before baking; stationRot included in world normal transform |
| **Animated glow lights** | ✓ Done | `StationLightInfo` with Rate/Phase/LightPattern; `ComputeGlowIntensity`; strobe, pulse, heartbeat patterns; aviation warning lights on tall structures |
| **Targeting system** | ✓ Done | 'C' key + click targeting; `TargetingSystem` class; HUD brackets; `ProjectToScreen` fixed for render-scale 1e-9 |
| **Planetary flight (`FlightMode`)** | ✓ Done (Brief E overhaul) | `FlightMode { Docked, SystemNewtonian, SystemSlipstream, AtmosphericNewtonian, AtmosphericSlipstream }`; auto-detect via nearest body altitude; force-based atmo physics (gravity, drag, lift); Flight Assist (V), Slipstream/mode toggle (G), X-Stop (X), Gear (scroll) |
| **SystemNewtonian flight model** | ✓ Done | Gear-ceiling Newtonian with thrust taper; 10-gear speed table from `FlightConstants`; X-Stop brakes to reference-body velocity; gear auto-selected on Slipstream exit |
| **Afterburner (SystemNewtonian)** | ✓ Done, unverified in-engine | `Z` (rising edge, SystemNewtonian only, no re-trigger while active) engages 2s of constant forward accel at `FlightConstants.AfterburnerAccelMultiplier` (5×) the current full-throttle accel (`Gear1AccelerationMs2` or `ship.FlightAcceleration`, whichever `TickNewtonianPhysics` is already using) — not tapered by gear ceiling, not WASD-steerable (`TickNewtonianPhysics` early-returns before the WASD/X-Stop block while active). `XStopToggle`/`SlipstreamToggle` input is gated (`&& !_afterburnerActive`) in `SpaceSimulation.TickPhysics`; the `H` hyperspace trigger is gated separately on the main thread (`SystemSpaceState.HandleKeyboard`) via a new `AfterburnerActive` field on `ShipSnapshot`, since hyperspace entry isn't routed through `PlayerInput`/the sim tick at all. Roll and mouse-look pitch/yaw stay responsive; additionally, while active, small random pitch/yaw jitter (`FlightConstants.AfterburnerShakeRadians`) is added directly onto the ship's real rotation each tick for a Brownian-ish shake — deliberately not a cosmetic view-only overlay (confirmed with Timo: render space is scaled ~1e-9, making a literal view-space translation's magnitude hard to reason about and impossible for me to verify visually in this environment, whereas an angular jitter is scale-invariant and also gives the intentional minor drift in actual travel direction). **Not yet visually confirmed** — same computer-use keyboard blocker as the docking-bay/station-spawn work; verification items 4–6 (exact 5× accel, exactly 2s, input inertness during burn, re-press does nothing, shake feel) need Timo's upcoming manual test pass. |
| **SystemSlipstream flight model** | ✓ Done | 10-harmonic log-scaled table (1 km/s – 30 Gm/s); smooth-step ramp between harmonics; clunk roll animation (Newtonian only); `ComputeProximityScale()` cubic speed dropoff from 100 km; planet dropout at 200 km alt; station dropout at 20 km with 400 m/s exit cap |
| **LKM station zones** | ✓ Done | 3 concentric zones (8 km / 2 km / 500 m) with per-zone gear cap; 6-second compliance window; violation flag stub; forces Slipstream exit on zone entry with 400 m/s velocity cap |
| **Flight HUD** | ✓ Done | Mode / gear / LKM / X-STOP indicator line; `Topics.Flight.*` DataBus topics; clunk camera-space roll (view-space multiplication, no planet-glue) |
| **Keplerian orbital mechanics** | ✓ Done | Full orbital elements (`e`, `i`, `Ω`, `ω`, `M₀`) on `OrbitalBody`; `ComputePosition` + `ComputeVelocity`; Newton solver for eccentric anomaly; moons/asteroids/stations keep circular rail |
| **PlanetData + PlanetFactory** | ✓ Done | `PlanetType`, `AtmosphereCompositionType` enums; `PlanetData` record with physical/atmosphere/surface data; `PlanetFactory` procedural generation; per-tick planet orientation update in sim |
| **Reference frame fix** | ✓ Done | On atmosphere entry, planet orbital velocity subtracted from `ship.Velocity` (→ planet-relative); position integration adds `_atmosphericPlanetVelocity` to keep galaxy position tracking. Restored on exit. `UpdateReferenceFrame` sends `DVec3.Zero` in atmosphere. |
| **PlanetaryCoordinateSensor** | ✓ Done | `Inferior.Gameplay/Sensors/`; publishes `PlanetCoord.*` topics each tick in atmosphere: Altitude, Latitude, Longitude, Heading, GroundSpeed, VerticalSpeed, Temperature. Topics added to `Inferior.Core/DataBus/Topics.cs`. |
| **Ground radar HUD panel** | ✓ Done | 8-row panel (ALT/VS/LAT/LON/HDG/GS/TEMP/PRES) in `DrawAtmosPanel()`; PRES shown in green when ≥ 0.1 bar (Slipstream threshold); subscribes/unsubscribes to `PlanetCoord.*` on state enter/exit. |
| **DriveInstrumentPanel** | ✓ Done | Right cockpit-rail wing; DRIVE header + mode label; Newtonian: GEAR/CEIL/FWD/REL rows (X-STOP overlay); Slipstream: HARM/SPEED rows; FUEL/PWR/HEAT stub bars; `Topics.Flight.*` DataBus driven. |
| **LedIndicator control** | ✓ Done | Round/square lamp; colour ranges; variable blink (BlinkClock global); exponential brightness easing (k=60, ~50 ms); stopping mode LED in HUD (amber, LabelAnchor.Bottom, subscribes to `Topics.Flight.XStopActive`) |
| **CockpitRail notch connectors** | ✓ Done | Full-rect minus top-right notch (A→B horizontal + B→C diagonal); LED centered in lower-outer area; hStep/dz computed from LED size (no named constants); STOP (amber, round, left) and WARN (green/yellow/red/blink-red, round, right) LEDs in connectors; `Topics.Ship.WarnLevel` added, stubbed at 0.0 |
| **Checkerboard planet sphere** | ✓ Done | Per-planet `VertexPositionColor` sphere (128×64 segments) built in `BuildPlanetSphere()`; 5°×5° cells with type-specific colour pairs (7 `PlanetType`s); pole caps; equator stripe; pre-baked directional lighting; rotates via `body.Orientation`. |
| **GeometryBuilder** | ✓ Done | `Inferior.Rendering/GeometryBuilder.cs`; `AddConvexFace` / `AddFace(outwardNormal)`; winding auto-corrected from centroid or explicit normal; `BuildDynamic` (VertexPositionNormalTexture, flat normals) and `BuildBaked` (VertexPositionColor). |
| **MeshRenderer** | ✓ Done | `Inferior.Rendering/MeshRenderer.cs`; `DrawBaked` (VertexPositionColorTexture, no lighting) and `DrawDynamic` (VertexPositionNormalTexture, BasicEffect star light); explicit `CullCounterClockwiseFace`. |
| **Container rendering (debug only)** | ✓ Done, known gap | Single shared chamfered-box mesh (2.5×2.5×6 m, 0.1 m chamfer) via `GeometryBuilder`; flat per-lock-grade colour at draw time, no texture or name markings; seeded angular velocity tumble; drawn through `MeshRenderer.DrawDynamic`. Never connected to the real `ShippingContainer`/`ShippingContainerFactory` domain model (`Inferior.Game/Containers/`) — investigation not started. |
| **Type-1 ship hull** | ✓ Done | 31-face hull + hex nacelles + pylons; dynamic lighting; third-person camera (F3); drawing owned by `ShipMeshRenderer` (`Inferior.Rendering`) |
| **`SystemSpaceState` file structure** | ✓ Done | Split into a `partial class` across `Inferior.Game/States/`: primary `SystemSpaceState.cs` (fields, ctor, `OnEnter`/`OnExit`/`OnResize`/`Update`/`Draw`/`HandleKeyboard`) plus `.Stations.cs`, `.CelestialBodies.cs` (now nearly empty — one Stations-owned texture helper left), `.Skybox.cs`, `.Ship.cs`, `.Targeting.cs`, `.Helpers.cs`, `.DebugContainers.cs`. `.Hyperspace.cs`/`.Hud.cs` were deleted once their contents fully moved to `FlatHyperspaceController`/`CockpitUI`. |
| **`FlatHyperspaceController`** | ✓ Done | `Inferior.Game/Hyperspace/`, alongside `HyperspacePlane`/`FlatHyperspaceConstants`/`IHyperspaceSheetRenderer`; owns flat-hyperspace flight (preamble alignment, travel, drop-out, overlay); `Camera3D`/`Star`/ship snapshot always passed per-call, never stored (camera can be reassigned via debug Home-key reset); `EnterSystem` world/skybox swap stays on `SystemSpaceState`, handed in as an `Action<Star, DVec3, Quaternion, FlightMode>` callback. |
| **`BusSubscription<T>`** | ✓ Done | `Inferior.Core/DataBus/`; `IDisposable` wrapper pairing one `Bus<T>` subscribe/unsubscribe, so subscriptions collect into a `List<IDisposable>` and tear down in one loop instead of 15 hand-paired named fields. 3 of the 15 (gravity direction X/Y/Z) stayed on `SystemSpaceState` since `UpdateReferenceFrame` needs them too. |
| **`CockpitUI`** | ✓ Done | `Inferior.Game/UI/`, alongside `DriveInstrumentPanel`/`RadarDisplay`/`LandingRadarPanel`/`DockingInstrument`/`CockpitRail`/`HudAlertDisplay`/`LedIndicator`/`SpectrumGraph`; owns the entire cockpit instrument/HUD tree (`UIManager`, panels, meters, dir-balls, radar displays), split into `.cs`/`.DirectionBalls.cs`/`.Targeting.cs`/`.Hud.cs`; takes borrowed deps plus `galaxyToEcliptic`/`onShieldToggle` callbacks rather than owning coordinate-transform/shield knowledge. `FeedRadarContacts`/`UpdatePadTargetPosition` stay on `SystemSpaceState`, calling `CockpitUI.NotifyRadarContact`/`NotifyRadarContactLost` where they need the cockpit direction ball. |
| **`SpritePrimitives`** | ✓ Done | `Inferior.UI`; `DrawText`/`DrawRect`/`DrawRectBorder` promoted out of per-state duplicates into one shared static helper; used by `CockpitUI` and `SystemSpaceState.Helpers.cs`. |
| **`CelestialBodyRenderer`** | ✓ Done, known gap | `Inferior.Rendering`; owns star/planet body+glow+atmosphere drawing, orbit rings, and the underlying sphere/glow/atmosphere GPU meshes. Its planet-sphere lighting bake needed `SceneLighting`, which moved from `Inferior.Game` down to `Inferior.Rendering` as part of this extraction (`Inferior.Rendering` can't reference `Inferior.Game`). `Dispose()` now frees per-planet sphere buffers on `OnExit` — previously leaked, accumulating for every system visited in a play session. **Known gap, not fixed:** mid-session `EnterSystem` (hyperspace dropout into a different system) doesn't rebuild planet spheres or station geometry; pre-existing, deliberately left open. |
| **`RingPrimitive`** | ✓ Done | `Inferior.Rendering`; small shared ring-mesh utility extracted from the old local ring-draw methods; used by both `CelestialBodyRenderer` (planet/moon orbit rings) and `SystemSpaceState.Stations.cs` (station orbit rings, which build their own compound world matrix first and need the plain draw overload, not a scale-only one). |
| **`SkyboxRenderer`** | ✓ Done | `Inferior.Rendering`; `Build` (static)/`Load`/`Draw`; `Load` runs from both `OnEnter` and `EnterSystem`, so — unlike `CelestialBodyRenderer` above — the skybox correctly rebuilds on a mid-session system change. Star-hover/click targeting logic and `_targetableStars` stay on `SystemSpaceState`; the hyperspace-mode draw guard moved to the `SystemSpaceState.Draw()` call site since `Inferior.Rendering` can't see `Inferior.Game.Hyperspace`. |
| **`ShipMeshRenderer`** | ✓ Done | `Inferior.Rendering`; owns the ship hull/nacelle/pylon meshes (via `Type1HullFactory`) and their drawing; shares the single `MeshRenderer` instance with debug test containers (borrowed via constructor, not owned — `SystemSpaceState` still disposes it). `Draw` takes the already-rolled view matrix as an explicit parameter to preserve the clunk-roll fix below. Camera-control/spawn-orientation math (`UpdateThirdPersonCamera`, `QuatLookAtWithUp`, `QuatLookAt`) stays on `SystemSpaceState`. |
| **Clunk-roll view-matrix bug** | ✓ Fixed | The gear-shift/harmonic "clunk" camera roll is applied once per frame onto the shared `BasicEffect.View`. Several call sites independently re-derived the raw, un-rolled `_camera.ViewMatrix` instead and so stayed visually fixed during a clunk: own-ship third-person mesh, debug test containers, targeting brackets (incl. containers), the locked hyperspace-target skybox ring, station dot markers, station nav-light/warning-strobe glow, planet atmosphere billboards, plus skybox star hover and "C"-key target selection. All now read the already-rolled matrix. `DrawStarGlow3D`'s behind-camera cull check was checked and confirmed not affected by roll. |
| **Station display-position separation (`SystemMapState`)** | ✓ Done, pending user visual confirmation | At high zoom a station's true orbital position could sit close enough to its parent's dot to overlap and become unclickable (unlike moons, whose real orbital radius is usually large enough that this wasn't visible). New `GetStationDisplayScreen(station)` nudges the station's own screen marker away from its parent along the true offset direction whenever the true screen distance is below `parentVisualRadius + stationDotRadius + 10px`; used by `DrawStations`, `DrawStationNames`, `HitTestStation`, and `HandleRightButton`'s station loop. `DrawOrbitRings`'s orbit ring is untouched — still drawn at the true orbital radius around the true parent position, per brief (only the dot/label/hit-test moves, not the ring). Parent-position resolution (handling the one level of grandparent indirection a moon needs) was already duplicated between `GetStationSystemPos` and `DrawOrbitRings` — factored into a shared `GetOrbitalBodyPos`/`GetStationParentPos`, no behavior change there. Also de-duplicated the star-radius formula (`StarVisualRadiusPx`, shared by `DrawStar` and the new separation calc) and the station-dot-radius switch (`StationDotRadius`, shared by `DrawStations` and the new separation calc). |
| **`docking-bay` station module (MVP)** | ✓ Done, unverified in-engine | First hollow station module — ships fly inside through a 40×24 m framed door on the -Z face of a 48×32×100 m module. `Inferior.Game/Station/DockingBayHull.cs` builds the whole hull itself (5 solid chamfered walls, door frame, 5 inward-facing interior walls, seeded wall thickness 20–50cm) since MeshFactory modules own their entire mesh. `BaseFaceCount` deliberately left at 0 so every `StationDecorator` face-iterating pass (windows/greebles/AO/etc.) no-ops for it — no decoration, per brief. Attached once via a pre-growth step in `StationGenerator.Run()` (before the organic frontier loop), with a reserved 50×35×150 m approach-corridor volume tracked in a new `_reservedVolumes` list (checked by `IntersectsAny` alongside `_placed`) so nothing can grow in front of the door. **Fixed a latent bug found while implementing this:** `CoreHubLarge` (40 m, `PortSize.Large` ports) was dead code — `Run()` always hardcoded the small `CoreHub` as root regardless of scale, and `CoreHubLarge` was excluded from organic growth via its `"core"` category. Now `CoreHubLarge` is the real root for `StationScale.Port`+ (large) stations, which is also what the docking bay's `Large`-tier ports need to attach to at all. `StationGenerator.PopulateLandingPads` no longer filters by `Category == "docking"` — any `IsDocking` port on any category registers as a real `Galaxy.LandingPad`, needed since this module's docking port is interior, not on category `"docking"`. `StationDecorator.GenerateEdgeTrimStrips`'s chamfer-bevel math was extracted into a shared `internal static AddChamferEdgeTrim` so the new hull could reuse the exact same 12-edge/8-corner bevel geometry as ordinary box modules. **Follow-up session** added a station stats readout: `StationGenerator.FindDockingBay(station)` runs just the growth loop (no `GraphicsDevice`, no mesh building — measured ~0.5ms/call, `Inferior.Game.Test/DockingBayLookupTests.cs`) and returns the matched `StationModuleDefinition`, cached per station in `SystemMapState.OnEnter`. `SystemMapState` now has `_hoveredStation`/`_selectedStation` (mirroring the body ones), hover/select rings on station icons, and a station info panel (name, size class, orbit, bay overall/door dimensions) shown on hover/click. Also fixed a gap from the first session: `StationModuleDefinition.DoorOpening` (`Vector2`) didn't actually exist — the original brief asked for the door size to be parameterized on the definition rather than hardcoded, but `DockingBayHull.Build` had it as a local `const`. Now the registry's `DockingBayDoorOpening` value feeds both the definition and the mesh factory. **Not yet visually confirmed in-engine, still**: build is clean and all xUnit tests pass (10 total), but a second attempt to fly to a Large station and screenshot both the interior and the new station panel again failed for tooling reasons — this time narrowed down to `tasklist` showing 4 simultaneous `Inferior.Game.exe` processes mid-session, likely spawned by repeated `open_application` calls; screenshots and keyboard focus were probably going to different instances. Next session: kill all `Inferior.Game.exe` first, launch the built exe directly (not `dotnet run`), call `open_application` at most once, then retry the flythrough (temporarily force `StationSize.Large` in `Station.RollSize`, then revert) to confirm: interior walls actually render from inside (real risk — `MeshRenderer` uses `CullCounterClockwiseFace` globally, interior winding only verified by code-reading/dot-product auto-correction), nothing ends up in front of the door, growth still extends from the bay's 5 non-door ports, and the station panel's reported bay/door dimensions match what's actually flown to (both the has-a-bay and no-bay cases). |

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
public enum FlightMode
{
    Docked,
    SystemNewtonian,          // Gear-ceiling force-based Newtonian
    SystemSlipstream,         // Harmonic warp-speed flight
    AtmosphericNewtonian,     // Force-based atmospheric (gravity, drag, lift)
    AtmosphericSlipstream,    // High-speed atmospheric mode
}
```

Planned future GameStates (not yet designed or implemented):

```csharp
enum GameState
{
    GalaxyMap, SystemMap,
    HyperspaceEntry, Hyperspace, HyperspaceExit,
    SystemSpace,   // all FlightMode variants run within this state
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

0. **User is confirming manually**: `docking-bay` module + station stats panel + station
   display-position separation, all in-engine. Fly to a Large station and confirm the hollow
   interior actually renders (culling risk), the door/frame look right, growth still extends
   from its 5 non-door ports, the system-map station panel's bay/door dimensions match, a
   station previously overlapping its parent at max zoom is now separated and clickable, and a
   station orbiting a moon still resolves its parent correctly. Two sessions in a row failed to
   verify any of this via computer-use for tooling reasons (see table above) — if picked back up
   by Claude later, kill all `Inferior.Game.exe` processes first and avoid repeated
   `open_application` calls.
1. **`StationSceneRenderer` extraction** — station mesh/glow/dot rendering out of
   `SystemSpaceState.Stations.cs` into `Inferior.Rendering`, same pattern as
   `CelestialBodyRenderer`/`SkyboxRenderer`/`ShipMeshRenderer`.
2. **`SpawnShip` vs. `ShipBuilder` convention** — `SpawnShip` still manually wires
   reactor/bus/shield/heatsink/coolant directly, bypassing the documented "`ShipBuilder`
   is the sole construction path for `Ship`" rule. Investigation in progress as of this
   doc update; no resolution decided yet.
3. **Debug test containers vs. `ShippingContainer`/`ShippingContainerFactory`** —
   decide whether/how to connect the flat-colour debug containers to the real domain
   model (see Open design decisions).
4. **Player-editable cockpit** — design pass (see Open design decisions).

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
Inferior.Core        — DVec3, Units, DataBus, CommandBus, BusSubscription<T>, GameClock, Noise, Topics
Inferior.Galaxy      — star/system generation, OrbitalBody, StarPhysics
Inferior.Gameplay    — Simulation, Physics/, SensorData/, Sensors/, PlayerInput
Inferior.Persistence — ShipRecord, repositories, log (pure IO, no live objects)
Inferior.Rendering   — Camera3D, MeshFactory, GeometryBuilder, MeshRenderer, Type1HullFactory,
                       SceneLighting, CelestialBodyRenderer, RingPrimitive, SkyboxRenderer,
                       ShipMeshRenderer
Inferior.UI          — UIManager, UIRenderer, Theme, all controls, SpritePrimitives
Inferior.Game        — entry point, game states, SpaceSimulation, TargetingSystem, ShipBuilder,
                       factories, StationGenerator, StationDecorator, StationModuleRegistry,
                       Hyperspace/ (FlatHyperspaceController + hyperspace sheet renderers),
                       UI/ (CockpitUI, DriveInstrumentPanel)
```

Dependency: `Core ← Galaxy ← Gameplay ← Rendering`, `Core ← Persistence`, and `Core ← UI`, all
converging in `Game` (which also depends on `Galaxy`/`Gameplay` directly).

> Corrected from the previous version of this doc: `PlayerInput` lives in `Inferior.Gameplay`
> (not `Core`), `TargetingSystem` lives in `Inferior.Game` (not `Gameplay`), and
> `Inferior.Persistence` only references `Inferior.Core` directly — it does not go through
> `Galaxy`/`Gameplay`. Verified against each project's `.csproj` while updating this doc.

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
| Antenna dish interior winding | Needs `AddFace(outwardNormal)` in `GeometryBuilder` when antenna geometry is revisited — concave interior faces point back toward stem, not away from mesh origin |
| Station weathering pass | Not yet designed |
| Station enclosed archetypes (Sphere, Pyramid, Prism, etc.) | Designed, not implemented |
| Planetary terrain rendering | Deferred — separate brief required |
| Landing radar instrument | Deferred — requires design doc with sketches |
| Atmospheric visual effects (clouds, haze, re-entry glow) | Not yet designed |
| Formal small-object rendering strategy (ships, containers, whatever comes after) | Deferred — waiting for a second real data point beyond ships, i.e. until the debug-container-vs-`ShippingContainer` question below is answered |
| Player-editable cockpit (runtime add/remove/edit of cockpit instruments) | Not designed — `CockpitUI`'s clean construction/lifecycle boundary was partly built in service of this, but the feature itself hasn't been designed |
| Debug test containers vs. real `ShippingContainer`/`ShippingContainerFactory` domain model | Not investigated — debug containers render flat/untextured with no name markings and were never connected to the real model |

---

## Document map

| File | Where | Purpose |
|---|---|---|
| `inferior-current-state.md` | docs-claude | This file — active state, conventions, next steps |
| `inferior-architecture-map-claude.md` | docs-claude | Flat one-line-per-file map of every project — "where do I look for X?" |
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
