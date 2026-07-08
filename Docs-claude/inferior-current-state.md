# Inferior — Current State

> Updated per design session. Tells Claude and Claude Code what is done,
> what is in progress, and what is next.

---

## What is implemented and working

| System | Status | Notes |
|---|---|---|
| Galaxy map | ✓ Done | 20480 stars, fixed seed, deterministic |
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
| **`docking-bay` station module (MVP)** | ✓ Done, unverified in-engine | First hollow station module — ships fly inside through a 40×24 m framed door on the -Z face of a 48×32×100 m module. `Inferior.Game/Station/DockingBayHull.cs` builds the whole hull itself (5 solid chamfered walls, door frame, 5 inward-facing interior walls, seeded wall thickness 20–50cm) since MeshFactory modules own their entire mesh. `BaseFaceCount` deliberately left at 0 so every `StationDecorator` face-iterating pass (windows/greebles/AO/etc.) no-ops for it — no decoration, per brief. Attached once via a pre-growth step in `StationGenerator.Run()` (before the organic frontier loop), with a reserved 50×35×150 m approach-corridor volume tracked in a new `_reservedVolumes` list (checked by `IntersectsAny` alongside `_placed`) so nothing can grow in front of the door. **Fixed a latent bug found while implementing this:** `CoreHubLarge` (40 m, `PortSize.Large` ports) was dead code — `Run()` always hardcoded the small `CoreHub` as root regardless of scale, and `CoreHubLarge` was excluded from organic growth via its `"core"` category. Now `CoreHubLarge` is the real root for `StationScale.Port`+ (large) stations, which is also what the docking bay's `Large`-tier ports need to attach to at all. `StationGenerator.PopulateLandingPads` no longer filters by `Category == "docking"` — any `IsDocking` port on any category registers as a real `Galaxy.LandingPad`, needed since this module's docking port is interior, not on category `"docking"`. `StationDecorator.GenerateEdgeTrimStrips`'s chamfer-bevel math was extracted into a shared `internal static AddChamferEdgeTrim` so the new hull could reuse the exact same 12-edge/8-corner bevel geometry as ordinary box modules. **Follow-up session** added a station stats readout: `StationGenerator.FindDockingBay(station)` runs just the growth loop (no `GraphicsDevice`, no mesh building — measured ~0.5ms/call, `Inferior.Game.Test/DockingBayLookupTests.cs`) and returns the matched `StationModuleDefinition`, cached per station in `SystemMapState.OnEnter`. `SystemMapState` now has `_hoveredStation`/`_selectedStation` (mirroring the body ones), hover/select rings on station icons, and a station info panel (name, size class, orbit, bay overall/door dimensions) shown on hover/click. Also fixed a gap from the first session: `StationModuleDefinition.DoorOpening` (`Vector2`) didn't actually exist — the original brief asked for the door size to be parameterized on the definition rather than hardcoded, but `DockingBayHull.Build` had it as a local `const`. Now the registry's `DockingBayDoorOpening` value feeds both the definition and the mesh factory. **Visually confirmed stable** in Timo's manual test pass (station panel, display-position separation, afterburner all reported working well). One real bug found and fixed from that pass: the door frame only had its outer-facing surface, not the inner one the other 5 walls correctly have — added via the same `AddWoundQuad`-style pattern (see `DockingBayHull.cs`).
**Pad-driven sizing rework** (this session): the fixed 48×32×100m envelope and single interior pad are gone. `DockingBayLayout.Compute(stationSeed, stationScale)` (new file, public for test access) generates a seeded pad mix (medium 36×36m serving Small/Medium ships, large 36×72m serving Large ships; mostly-medium with occasional large, 4–10 pad-equivalents at Port scale today, 12–28 at Megastation scale for whenever that tier becomes reachable), packs it into a grid (round-robin large-then-medium across `clamp(ceil(sqrt(slots)),2,6)` columns), and derives cavity/envelope dimensions from the grid footprint + spacing/clearance margins. Door size now varies with the pad mix: 40m wide always (Medium and Large ships share the same 36m max width per the ship-size reference), but only 24m tall if the bay actually serves Large pads, 16m tall for medium-only bays — both derived as ship-max-height + 4m clearance, a convention that reproduces the original MVP's fixed 40×24m exactly when solved backwards. The door hole itself is now a chamfered rectangle (octagon by construction) rather than a sharp-cornered one — chamfer depth is 5–25% of door height, seeded once per station so multiple future bays would share one proportional look. `StationModuleRegistry.DockingBay` (the old static singleton) is gone, replaced by `CreateDockingBay(stationSeed, stationScale)` — envelope must be known before the module's own per-attachment seed exists (needed for the AABB check), so it's derived from the station's seed directly rather than `StationGenerator`'s mutable `_rng` stream. A **nominal** (non-seeded) wall thickness is used only for that early envelope computation; the actual per-module seeded wall thickness (20–50cm, unchanged) still drives the real mesh, ~15cm of slack between the two that's invisible at this scale. New `DockingBayLayoutTests.cs` covers the layout math directly (door variety, chamfer bounds, grid-never-clips, scale-gate); `DockingBayLookupTests.cs` still passes unchanged (~0.5ms/call, 500/500 success).
**Exterior decoration re-enabled** (this session): the MVP deliberately skipped all `StationDecorator` passes for this module by leaving `mesh.BaseFaceCount` at its default 0 (the sole exclusion mechanism — no category check exists in `StationDecorator` itself). Now set to exactly the 5 solid exterior walls right after they're built, before the door frame/chamfer/interior walls are added, so `ComputeFaces`/`ApplyAmbientOcclusion` pick up only those 5 for standard treatment (windows, hatches, vents, greebles, panel seams, edge trim, AO) — same as every other box module. Door-specific additions live in a new `PlaceDockingBayDoorDecoration` (`StationDecorator.cs`), gated on `Category == "docking-bay"`: 4 pulsing guidance lights near the opening (calls the existing flush-mount `AddLight` directly, since the door is a framed opening rather than a base face `PlaceBayGuidanceLights`'s generic face-lookup could find) and a "CAUTION - BAY" placard on the frame above the opening, reusing `ShippingContainerFactory.AddTextGeometry` (bumped to `internal`) — the same per-pixel bitmap-font technique already used on containers, no new rendering capability. **Interior ambient lighting boost** (this session): `StationModuleMesh.ApplyLighting` bakes one directional-sun-plus-flat-ambient factor per face, using the station-wide `SceneLighting.Ambient` (0.09, "space is dark") — the bay's interior walls mostly face away from the sun and the sun can only reach a limited area through the door, so under that model most of the interior would bake near-black. Deliberately not solved as real interior-light-fixture baking (placed sources, falloff) — that's tied to the bigger "shadow rendering / HLSL" milestone already noted as future work; this is a disposable flat approximation instead. New generic fields on `StationModuleMesh` (`AmbientOverrideFaceStart/FaceCount/Value`, mirroring `BaseFaceCount`'s existing pattern) let a MeshFactory module flag a face range wanting a different ambient floor; `DockingBayHull` sets these to its 5 interior-wall faces (only those — not the door frame's inner rim) at ambient 0.75. `StationGenerator.BakeLighting` calls a new `BoostAmbientForFaceRange` right after the normal `ApplyLighting` pass, using only the mesh's existing public API (`LocalFaceNormal`/`MultiplyFaceColor`) — no changes to the shared lighting method itself, so this needs no unwinding when real interior lighting eventually replaces it. A visible brightness seam at the door threshold (dim frame rim next to brightly-boosted interior) is expected and considered fine at this stage, not a bug.
**Bugs found in Timo's first look** (this session, fixed): (1) door frame wall thickness (0.20-0.50m) read as no thickness at all at this module's scale — spec revised to 0.5-1.5m (`DockingBayHull.WallThicknessForSeed`), nominal envelope thickness in `StationModuleRegistry` updated to match (1.0m midpoint). (2) Door corner chamfer had a real geometry bug, not just a look-and-feel issue: `AddDoorCorner`'s quad spanned the full corner square (including the area past the chamfer diagonal, which should be open door hole), and the separately-added triangle then overlapped part of that same quad — this was the reported "square sticking out into the opening" plus the flicker (coincident/overlapping faces). Fixed with a correct 2-quad decomposition (upper block + a diagonal-edged wedge) that exactly tiles the pentagon with no overlap; the triangle path (and its now-unused `AddWoundTriangle` helper) is gone. (3) Guidance lights were positioned within the door's own opening bounds — copied from `PlaceBayGuidanceLights`' docking-arm convention, where that's correct because the arm's "face" is a solid pad; here the door is an actual hole, so the same offset put the lights floating in open air. Moved onto the frame's flat strips instead (beyond the door's half-height by a margin), now placed both above and below the opening; the hazard signage was nudged up to clear the new top light row.
**Interior lighting — door-proximity gradient + orientation cue + corner noise** (this session, replaces the flat 0.75 ambient boost above): that flat value made the interior visible but unreadable — one uniform brightness gave no sense of depth, no up/down cue, no corner detail. `StationGenerator.BoostAmbientForFaceRange` now computes a per-face (flat-shaded, not per-vertex) brightness from three additive terms on top of a 0.45 base floor: **door proximity** (linear falloff from the door plane to the back wall, derived generically from the flagged faces' own Z spread rather than a hardcoded door position — weight 0.35, brighter near the door); **overhead cue** (faces whose normal points down — the ceiling, in this hull's convention — read brighter than the floor; weight 0.25, an orientation cue rather than a real light source); **corner noise** (seeded per-face jitter, same per-face-constant technique as `ShippingContainerFactory.ApplyWear`, weighted full strength on the back wall and 0.3× on the other 4 faces per the "can't see corners" complaint; weight 0.15). Result clamped to `[SceneLighting.Ambient, 1.0]`. The now-unused flat `AmbientOverrideValue` field was removed from `StationModuleMesh` rather than left dead. `PlacedModule.Seed` threads through `BakeLighting` to give the noise term a deterministic, disposable RNG stream independent of any geometry-affecting draw. Weights were picked to be reasonable, not derived — brief explicitly calls this an eye-tuned aesthetic pass; still no visual test pass has happened in this environment (see below), so the three weight constants (`InteriorBaseFloor`/`DoorProximityWeight`/`OverheadCueWeight`/`CornerNoiseWeight` in `StationGenerator.cs`) are the first place to adjust once Timo can see it.
**Door throat (this session):** Timo confirmed the corner-chamfer fix reads correctly in-engine, but reported the door wall still had no visible thickness even after the 0.5-1.5m spec bump. Root cause: `AddDoorFrame`'s outer (-Z) and inner (+Z) layers use the exact same door-hole octagon, so nothing filled the gap between them — looking through the opening (or standing in it) showed straight through with no surface revealing depth, so it read as zero thickness regardless of `t`. Fixed with a new `AddDoorThroat` (8 quads tracing the same octagon perimeter as the frame — 4 flat sides + 4 chamfer diagonals — each a straight prism panel from `zOuter` to `zInner`), walling in the opening so the tunnel is actually visible. **Confirmed working** by Timo — throat fill and overall interior brightness both read correctly now.
**Interior gradient fix (this session):** Timo reported the interior was now nicely lit but the door-proximity gradient wasn't visible at all. Root cause: the 4 interior side walls were each a single quad spanning the *entire* depth from door to back wall — since `BoostAmbientForFaceRange`'s brightness is one flat value per face, a wall that's only one face can only ever show one brightness, no matter what the formula computes. The gradient had nowhere to actually render. Fixed by subdividing each side wall into `Clamp(Round(depth / 20m), 3, 8)` segments along Z (back wall stays a single quad — it's already at the far end, nothing to subdivide along); `BoostAmbientForFaceRange` needed no changes, since it already derives its Z range generically from whatever faces are flagged. Face count for the interior went from a fixed 5 to roughly 13-33 depending on bay depth.
**Door guidance lights — throat mount + missing glow (this session):** Timo confirmed the gradient fix looks good, then asked for two more things on the door lights. (1) Move them onto the door throat (the recessed prism wall between the frame's outer and inner faces, see `DockingBayHull.AddDoorThroat`) so they're visible from both inside and outside — a light flush with the frame's outward face only reads face-on from outside; recessed into the throat and facing inward (perpendicular to the door normal), it reads looking down the tunnel from either direction. Repositioned in `PlaceDockingBayDoorDecoration`: still 4 lights above + 4 below, now mounted at the throat's approximate mid-depth (`StationModuleRegistry.NominalWallThickness`, the same non-seeded approximation already used for the envelope), facing inward (top row faces down, bottom row faces up) instead of outward along the door normal. (2) The lights weren't actually shining — real bug, not a look-and-feel note: the pulsing housing/lens geometry existed, but unlike every other light in `StationDecorator` (`PlaceNavigationLights`, `PlaceWarningStrobes`), this one never added a `StationLightInfo` to `mod.GlowLights`, so the billboard glow sprite (`ComputeGlowIntensity` / `SystemSpaceState.Stations.cs`) never drew for it. Now registers each light as `GlowType.AmbientMarker`/`LightPattern.SlowPulse` (its doc comment literally says "dock guidance"), synced to the same 2-second pulse period and per-light phase as the mesh's own `AnimTag`.
**Door guidance lights — brighter + bigger (this session):** Timo liked the throat mount and asked for the lights brighter and their sprites ~50% bigger. `BaseIntensity` bumped 0.75 → 1.0 (peak sine intensity now saturates the sprite's alpha instead of topping out at 191/255). Sprite size is keyed off `GlowType` in a shared table (`SystemSpaceState.Stations.cs`'s `baseSize` switch) that these lights previously shared with every other `AmbientMarker` on a station — bumping that value directly would have enlarged unrelated ambient markers station-wide, so added a new `GlowType.DockGuidance` (400 → 600, i.e. the requested 1.5x) used only by these lights, leaving `AmbientMarker` itself untouched.
**Not yet visually confirmed** — same computer-use keyboard blocker as everything else; needs Timo's next test pass to confirm the door lights now glow, at the right brightness/size, and read correctly from both sides of the throat, door/shell sizes vary sensibly with pad mix, decoration density matches other modules, and that the interior lighting gradient/ceiling-floor/corner-noise all still look right together with the relocated lights. |

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
