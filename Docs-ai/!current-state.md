# Inferior — Current State

> Updated per design session. Tells AI what is done, what is in progress, and what is next.
>
> Megastation development (branch `mega-stations`) merged into `master` on 2026-09-11 — see the
> merge note near the end of this file for conflict-resolution details. The accepted production baseline is the
> sharp manifold A/B/C0/C2 structural mesh plus Z1 semantic zoning, Z2a habitation windows, Z2b
> glow lighting, G1 attached secondary structures, G2a native infrastructure, mega-greeble,
> Fabric Structures, the M1 shared system material library, single-station visual residency,
> compacted ordinary texture uploads, SC1/SC2/SC3 planar Service Channels, the H1/H1a/H1b
> hollow flight interior and constructed entrance throat, accepted H1c-A/B/C/D static artificial
> bay lighting and visibility, the visually accepted L1-A through L2a multi-site landing-district,
> operational-floor, frontage, floodlight, human-scale and fabricated-access baseline, H1e approach guidance beams, H1f
> Standard/Grand entrances, crown-derived entrance precinct clearance, the Bolon B0–B2b
> molecular-vessel/pressure-shell baseline, B3a pentagonal utilities and the visually accepted
> B4a/B4a.1 ambassador bay with H1e approach beams and rear visitor port. Collision remains
> deferred. The megastation shadow map remains 8192². Timo visually accepted
> the complete Fabric + M1 baseline, including close-range material appearance, on 2026-08-26.
> Major authoritative bay/interior structure now shares the exact finalized structural mesh with
> the existing stellar-shadow caster path; Timo directly confirmed internal wall geometry and Mega
> Shelf trusses casting central-star shadows across the far bay wall on 2026-09-10, while the dramatic
> sunlight strip entering through the mouth remains intact.
> L3c/L3d-A/B/C wall composition is now a visually accepted baseline: inhabited galleries and
> facility cut-outs coexist with synchronized major wall/ceiling truss fields, clustered exposed
> cable networks, and coherent pipe/vent/hatch/ladder installations while preserving operational
> reservations and deliberately quiet wall regions. Timo accepted the completed utility layer on
> 2026-09-11.
> L4a-L4g Mega Shelves are now an accepted visual baseline: deterministic cantilever, corner,
> full-span and multi-level Bookcase structures normally carry complete landing sites, use the shared
> structural-truss vocabulary, carry accepted shelf-owned safety/work lighting, and support compact
> wall-integrated landing frontage with authoritative wall-to-edge reservations. The separate
> H1h distance-blueing experiment proved that bounded interior colour separation is viable; it
> remains an explicitly halted tuning experiment rather than a finalized atmospheric effect.
> Mega-greeble parabolic antennas, flat SurfaceArrays, RadialSolarWings, and native
> G2/mega-greeble shadow participation remain accepted and locked. The abandoned
> `E:\Git\GitInferior-megastation-prototype-c`
> investigation remains untouched and unmerged.

---

## What is implemented and working

| System | Status | Notes |
|---|---|---|
| Galaxy map | ✓ Done | 20480 stars, fixed seed, deterministic |
| System map | ✓ Done | Bodies, orbits |
| 3D flight state (`SystemSpaceState`) | ✓ Done | Newtonian, origin-shifting render |
| UI library (`Inferior.UI`) | ✓ Done | Button, Label, TextBox, TextBlock, Panel, Window, GridPanel, StackPanel, CollapsiblePanel, ScrollPanel, menu/popup controls, InstrumentMeter, SystemConsole, DirectionBall, EdgePanelHost, UIManager, Theme, InputState |
| DataBus | ✓ Done | 11 channels: SystemMessages, ScalarTelemetry, VectorTelemetry, SpectrumTelemetry, TelemetryInfo, DeviceInfo, DeviceState, ShipSystemsTopology, Radar, RadarLost, Target |
| CommandBus | ✓ Done | Reverse direction; sim thread drains |
| Simulation loop | ✓ Done | 60Hz background thread; PlayerInput immutable snapshot |
| DVec3 + origin shifting | ✓ Done | Double-precision coordinates throughout |
| GravitySensor | ✓ Done | PassiveSensor with noise; publishes to `DataBus.ScalarTelemetry`/`VectorTelemetry` |
| Noise library | ✓ Done | Simplex1, White, Pink, Periodic, Spike |
| GameClock | ✓ SimTime done; calendar integration pending | `SimTime` advances on the simulation thread. The existing `DateTime InGameDate` remains a legacy placeholder and is not the new calendar authority. |
| Galactic civil calendar | ✓ Done | `Inferior.Core.Time`: immutable numeric `GameDate` (`AbsoluteDay`), continuous proleptic-Gregorian civil conversion/arithmetic, Galactic Era overlay (`BE`, `E1`, `O1`, `E2`, `O2`, `E3`), strict full-date parsing/formatting, and direct numeric JSON persistence. Fixed setting anchor: `6864-07-19` / `E3.326-07-19`. Manufacturer integration, time of day, and simulation advancement are deliberately not implemented. |
| Environment query class | ✓ Done | Gravity, nearest star/body, field vectors |
| Persistence layer | ✓ Partial | Architecture designed; file implementations in progress |
| ShipSizeClass enum | ✓ Stubbed | Exists, not yet enforced |
| **Power system** | ✓ Done | PowerCore → PowerBus → Connector → Shield chain working; cold start sequence; PowerPriorityManager; instruments reporting to DataBus; CommandBus integration |
| **Station generation** | ✓ Done (ongoing refinement) | Full procedural generation pipeline; see `stations-ai.md` |
| **Megastation structural massing** | ✓ Done, visually accepted by Timo. | Deterministic non-uniform `SliceGrid`/`StructuralOccupancy` cuboid structural-volume growth across all six faces plus shared edges/corners, a topology-regularisation pass that adds material only, and exact-grid boundary/chamfer diagnostics — chamfers were visually rejected (sparse coverage, tapering to sharp vertices), so production uses the clean sharp manifold mesh. **Do not reopen chamfers without an explicit new brief.** Versioned (`GeneratorVersion=5`, topology/boundary/chamfer algorithm versions tracked separately) so raw-massing signatures stay comparable across changes. |
| **Single-station visual residency + upload streaming** | ✓ Done, confirmed in-engine by Timo. | `SystemSpaceState` keeps lightweight per-station identity/orbit data for every station but owns zero-or-one completed `StationVisualPackage` at a time — 200 km load / 250 km unload hysteresis, persistent-identity tie-breaking, explicit-arrival supersession. `StationGenerator.PrepareCpu` runs geometry/texture-pixel/upload-plan preparation on a worker (`StationPreparationTask<T>`); `StationVisualUploadScheduler` uploads one GPU resource at a time under a 2 ms/frame cooperative budget. Repeated `Texture2D.SetData` calls, not megastation mesh size, were the dominant historical hitch — ordinary texture variants are compacted to the selected unique set before upload. |
| **Station decoration: zoning, density, hull-brightness fixes** | ✓ Done, part of the accepted production baseline. | Large/mega box modules subdivide multi-zone faces (`StationDecorator.Zones.cs`) into typed regions (Windows/Machinery/TankFarm/ServiceCore/Structural/Storage/Signage/CommsArray/PipeCorridor) with guaranteed minimums, per-zone exposure checks against real neighbour footprints, area-scaled content density, and structured (non-overlapping) tank-row/container-yard/antenna-array arrangement — ordinary small modules are provably untouched (byte-identical geometry hashes) throughout. A separate hull-vs-decoration rendering split (`MeshFactory` modules own a genuinely separate hull mesh, `PlacedModule.HullMesh`) fixed mega-module hulls rendering uniformly dark (AO/baked-shading was wrongly applied to load-bearing hull faces) and let them pick up real dynamic lighting/specular. Window size is capped independent of face size so windows on a mega face read as human-scale rows rather than scaling up with the module. `Ctrl+F4` shows a zone-type debug overlay. |
| **Megastation secondary structures, native infrastructure, mega-greeble** | ✓ Done, visually accepted by Timo. **Mega-greeble forms/placement/shadow participation are locked — do not alter without an explicit new brief.** | G1 attaches deterministic module-as-greeble buildings to selected semantic/topological surfaces. G2a adds native machinery-housing/ventilation/tank installations from the same planar-region substrate. The mega-greeble layer adds two solar forms (flat `SurfaceArray`, outward-projecting `RadialSolarWing`) and two supported/surface-mounted parabolic-dish forms. All three layers batch into one visible mesh plus one selective caster mesh each, own no textures, and participate in the station shadow map through the same casting-policy mechanism ordinary decoration uses. |
| **Megastation Fabric Structures + M1 shared material library** | ✓ Done, visually accepted by Timo (incl. close inspection) on 2026-08-26. This is the material-tuning baseline. | Fabric places `UtilityHall`/`SteppedBlock`/`Warehouse`/`TechnicalTower`/`MachineryBlock`/`ServiceCompound` archetypes in coherent rows/clusters/fields between the structural hull and G1/G2/mega-greeble, batched into one visible mesh plus a simplified caster, no owned textures. M1 (`SystemSpaceState`-owned, system-lifetime) generates four shared 512² albedo/material pairs (`DullStructuralMetal`/`PaintedCoatedMetal`/`HeavyIndustrialPlate`/`CleanTechnicalAlloy`) from stable system identity, used by structural hull and Fabric via deterministic station/role/archetype assignment — station packages borrow references and never own/dispose these textures. |
| **Megastation Service Channels (SC1-SC3)** | ✓ Done, visually accepted by Timo. | Non-subtractive planar presentation architecture generated after Fabric: recessed dark floor with raised lips (SC1) promoted into deterministic region-local rectilinear networks — trunks, branches, turns, junctions, covered utility-node variants (SC2/SC2a) — with G2/Fabric made channel-aware of channel topology (SC3). Material-grouped batched geometry, zero owned textures. `Shift+F5` shows the debug route/node overlay. |
| **Megastation flight interior, entrances, artificial lighting** | ✓ Done, visually accepted by Timo through 2026-09-10 (H1g stellar-shadow participation). This is the current interior/entrance/lighting baseline. | Every megastation carves one deterministic protected main flight cavity + exterior throat before regularisation (H1). A continuous thick-walled rectangular tube (H1b) with a separate monumental crown (H1f: Standard/Grand variants) replaces the raw excavation; H1e adds four fixed kilometre-scale cyan/amber approach-guidance beams on the crown. H1c-A/B/C/D bake static per-vertex artificial lighting (twelve cold-white sources + weak bounce term), generation-time occlusion against real structural geometry, and colour-matched landing-pad spill — no runtime point lights, only baked `COLOR1` incident RGB added alongside ambient/shadowed stellar diffuse. H1g made the finalized structural mesh itself the stellar-shadow caster, so bay walls/major interior structure participate in the existing 8192² station shadow map through capability alone (no second map/draw path). **Collision remains deferred** — nothing here physically blocks spacecraft yet. H1h (interior distance-based blue colour separation) is a halted, bounded proof of concept, not a finalized atmospheric effect — do not resume tuning it without a new brief. |
| **Megastation landing infrastructure (L1-L4)** | ✓ Done, visually accepted by Timo through 2026-09-11. | Ordinary megastations deterministically compose 1-3 Landing Sites (Small/Medium/Large, Compact-Service/Mixed-Operations/Cargo-Heavy character) on the cavity floor, each with its own apron, service frontage, containers, floodlights, fabricated access and baked-light receivers (L1-L2a). L3a-L3d layer sparse habitation windows, inhabited galleries/facilities, wall/ceiling structural trusses, cable networks, and secondary pipe/vent/hatch/ladder installations onto the bay walls, respecting facility/landing/approach reservations (L3a.1 density follow-up remains explicitly deferred). L4a-L4g add a macro-structural shelf system (Cantilever/Corner/FullSpan/Bookcase) that also carries Landing Sites on its top surfaces, plus shelf-owned safety floods/obstacle beacons. All layers are deterministic, batched, M1-material-backed, and resource-neutral. Landing mechanics, wall/ceiling-site landing, and collision remain deferred. |
| **Bolon megastations (B0-B4a.1)** | ✓ Done, visually accepted by Timo through B4a/B4a.1 on 2026-08-31. This is the accepted Bolon baseline. | Deterministic low-degree molecular graph of 6-12 joined C60 pressure vessels (B1) with vessel-wide surface-history material regions (B2), real hexagonal optical openings with ruby/violet/rare-green illumination (B2a/B2b), and pentagonal reinforcement utilities (B3a). B4a/B4a.1 add one deterministic flyable ambassador bay on a reserved hex face with H1e-style approach beams and a rear visitor-port stub. **Collision remains deferred.** Further foreground Bolon greeble needs a new explicit brief. |
| **Planar world-text geometry** | ✓ Done, visually accepted with L1d/L1d.1 on 2026-09-04. | `PlanarTextGeometry` is the sole bitmap-font-to-mesh authority for world/station labels (containers, tanks, docking-bay signage, calibration cube, landing pads, KEEP CLEAR/LOADING AREA) — derives a proper-handed glyph-up axis from a caller-supplied normal/reading direction, so labels never render through a reflective transform. Replaced two independently-drifting `AddTextGeometry` implementations. |
| **StationDecorator mechanical split** | ✓ Done, geometry-identity verified. | `StationDecorator.cs` (3199 lines) split into 11 `partial` files by section; pure move, no behaviour change — verified via SHA-256 mesh-hash A/B across 20 seeded stations (528 modules), all identical. |
| **Sun rendering: glare, colour, disc size** | ✓ Done, visually confirmed by Timo — the multi-round disc/glare z-fighting and off-centre "iris" artifact saga is closed ("Its gone, finally :) Good work!"). | Star glow is 5 additive concentric billboards (strengthened from the original 4, tuned as a stack); `SceneLighting.SunColourForStar` derives sun colour from the active star's spectral class at constant target luminance (was a hardcoded constant, never varied); disc radius derives from `Star.RadiusMeters` (was a fixed visual floor) with a per-pixel-minimum floor for very small/distant stars. |
| **Station brightness tuning** | ✓ Done, visually confirmed by Timo ("Looking good") — the station-brightness thread (diagnostic → sun fixes → tuning panel → baked values) is closed. | Root cause of "stations read subdued": decoration (most of a station's visible area) renders at roughly half the brightness of a fully-sunlit bare hull face at the same texture, since decoration multiplies texture × AO-tinted vertex colour while the hull's vertex colour is flat white. `StationBrightnessTuning` (`Inferior.Rendering`) owns a live-tunable decoration-brightness multiplier, variant value floor, compression strength, and saturation falloff, baked to non-neutral tuned defaults (`DefaultVariantValueFloor=0.2`, `DefaultVariantCompressionStrength=1`, `DefaultSaturationFalloff=1`) after Timo's in-engine tuning pass. `StationGenerator.RegenerateTextures` re-derives a station's texture variants live from the current tuning values (Ctrl+F8 panel) without re-running growth/decoration. |
| **Station panel textures + material** | ✓ Done, visually confirmed by Timo. | Per-station-owned texture variants (not a galaxy-wide shared cache) rolled per `(SurfaceTexture, Economy)` pair actually used, seeded from `station.PersistenceId`; each variant gets its own hue/brightness/saturation-jittered palette (`OffsetPaletteForVariant`) so modules on the same station read as genuinely different, not just noise-shifted. Includes a derivative-based bump/specular pass, strength locked at `Whisper` (0.3). |
| **Yagi antenna greeble** | ✓ Done | 5 element types (I/X/O/S/H), 7 base types; straight mast (0.3–1.2 m) + tilted boom (0–25° from normal); O-element discs fully solid (side + cap faces); seeded brightness variation per antenna; connectable for cable system |
| **Station cables / conduits** | ✓ Done | Grid-routed bundles and conduits on module faces; junction boxes; fasteners; edge clamps; parabolic dish and mast antenna bases registered as connectable endpoints; octagonal module side-face cables working (degenerate cap faces guarded at cable call only); muted industrial colour palette |
| **Directional lighting** | ✓ Done, visually confirmed by Timo (2026-07-17) | Phase A lighting-pipeline swap landed (`Docs/station-lighting-pipeline-spec.md`) — see the "Station lighting / shadows" row below for the full write-up. Real-time, per-frame in `LitSurface.fx`; nothing baked into vertex colour except albedo × AO (+ self-illumination floor S in alpha). |
| **Specular highlight (Brief S1)** | Done, visually confirmed by Timo — `Tight` preset (strength 0.5, shininess 96) set as the default | Single-source Blinn-Halfway specular on `LitSurface.fx`'s `DynamicLit`/`DynamicLitShadowed` techniques only — ships, containers, calibration cube, **and station hulls** (box modules + MeshFactory hulls like docking-bay all render through `DynamicLit` too; only station *decoration*, `BakedColorLit*`, stays untouched — confirmed with Timo as in-scope rather than special-cased out, matching `!invariants.md` §13's "no special cases keyed on identity"). Per-pixel `H = normalize(L+V)`, `spec = pow(saturate(dot(N,H)), Shininess)`, gated by `saturate(dot(N,L))` and (on the shadowed technique) the same `StationShadowTerm` the diffuse term uses — a shadowed fragment gets no glint. New `RenderPos` VS interpolant (render-space, i.e. camera-relative, pre-View) carries the surface position `SpecularHighlight` needs for `V`. `EyePositionWorld` is a real shader parameter but is *not* threaded from any caller — `MeshRenderer.SetSpecularParameters` sets it to `Vector3.Zero` internally, since every `World` matrix in this codebase already places geometry relative to the same camera whose `View` looks from `Vector3.Zero` (`Camera3D.ToRenderSpace`/`ViewMatrix`); confirmed by reading `Camera3D.cs` before assuming it, not guessed. `SpecularStrength`/`SpecularShininess` threaded as new required params through `MeshRenderer.DrawDynamicLit`/`DrawDynamicLitRange`/`DrawDynamicLitShadowed` and up through every caller: `SystemSpaceState.Stations.cs` (hull pass, both branches), `.Containers.cs`, `.CalibrationCube.cs`, and `ShipMeshRenderer.Draw` (+ its 4 internal draw helpers) — `Inferior.ObjectDesigner`'s hull-preview tool also calls `ShipMeshRenderer.Draw` and now passes `0f` (Off) explicitly, since it has no preset UI of its own. New `SystemSpaceState.Specular.cs` partial: `SpecularPreset { Off, Subtle, Default, Strong, Tight }`, starts (default) at the `Tight` preset (strength 0.5, shininess 96) — Off's `strength=0` still zeroes the term exactly (provably byte-identical to pre-S1 output, confirmed by Timo), so the preset list itself doubles as the on/off toggle. **`K`** cycles presets (grepped free — every letter except B/I/J/K/O/P/U/Y and every F-key 1-12 already bound at least once; a fresh key rather than another F6/F7 modifier, since this is an unrelated debug family), publishing the active preset + strength/shininess via `SystemMessage`. **Two judgment calls made explicit, not silently decided:** (1) specular is *not* gated by `EclipseFactor` — S1's formula only gates on sun-facing and shadow, so an eclipsed sun would still show a glint today; `EclipseFactor` is always 1.0 until Phase E anyway, revisit together. (2) emissive cockpit parts (canopy/internal glow) get no visible specular contribution as a side effect of `SunColour` already being passed as `Color.Black` for those draws — not a special-cased skip, falls out of the existing emissive gating. **Visually confirmed by Timo**: specular-off byte-identical, glint correctly tracks the camera (not the sun) orbiting a container/ship, no glint on shadowed faces, `Tight` read as nicest by far on the calibration cube/container/ship hull ("looks nicest by far") over Off/Subtle/Default/Strong. Non-goals held: no station-decoration specular, no bump/normal perturbation, no environment/Fresnel/metalness/second light, no albedo tint, no diffuse/ambient/AO/shadow/eclipse-term changes. |
| **Animated glow lights** | ✓ Done | `StationLightInfo` with Rate/Phase/LightPattern; `ComputeGlowIntensity`; strobe, pulse, heartbeat patterns; aviation warning lights on tall structures |
| **Targeting system** | ✓ Done | 'C' key + click targeting; `TargetingSystem` class; HUD brackets; `ProjectToScreen` fixed for render-scale 1e-9 |
| **Planetary flight (`FlightMode`)** | ✓ Done (Brief E overhaul) | `FlightMode { Docked, SystemNewtonian, SystemSlipstream, AtmosphericNewtonian, AtmosphericSlipstream }`; auto-detect via nearest body altitude; force-based atmo physics (gravity, drag, lift); Flight Assist (V), Slipstream/mode toggle (G), X-Stop (X), engine harmony (scroll) |
| **SystemNewtonian flight model** | ✓ Implemented at `7a8ece9`; in-engine harmony acceptance pending | Installed-engine harmony owns both thrust output and speed ceiling; the old global 10-gear speed table is removed. Approach-to-ceiling taper still scales force, X-Stop brakes to reference-body velocity, and Slipstream exit auto-selects the lowest harmony containing exit speed. Mixed-engine ship ceiling is the lowest operational engine ceiling. |
| **Engine harmonies / shared translation envelope** | ✓ Implemented at `7a8ece9`; tests pass, in-engine acceptance pending | Each `EngineInstance` owns selected harmony `1..count`; shared scroll commands remain simulation-owned. Mule/Needle/Atlas provisionally use 8/16/10 steps, 0.10 minimum thrust, and 50–25,600 m/s endpoints. The shared quadratic curve scales every directional maximum and rotational torque positively. Longitudinal/lateral/vertical commands share one normalized envelope evaluated per installed engine; reverse/lateral/lift are definition-owned forward-thrust fractions. `R/F` use lateral-strength vertical authority, while `Space` selects stronger positive lift without stacking. Asterisk efficiencies remain hull-owned. F2 shows harmony, curve, multiplier, ceiling, directional maxima, allocation, applied force/acceleration, and diagnostic hover estimates. Fuel, gravity/hover assist, rotation-envelope competition, heat, and power allocation remain deferred. |
| **Assisted rotational physics** | ✓ Implemented, tests pass, in-engine acceptance pending | `Ship.AngularVelocityLocalRadPerSec` maps X/Y/Z to pitch/yaw/roll. Cached gameplay-owned configured bounds include hull plus installed engine/cockpit geometry. Three cuboid scalar inertias produce axis acceleration from effective installed torque; normalized input requests the retained 1.4 up-pitch / 1.0 down-pitch / 1.0 yaw / 1.5 roll rad/s target limits. Harmony-scaled available torque accelerates and brakes angular velocity without passive damping or overshoot; orientation integrates a normalized local axis-angle quaternion. Maximum authored torque remains Mule 0.60 MN m, Needle 1.05 MN m, Atlas 90 MN m. F2 diagnostics publish/display bounds, inertia, torque, acceleration, angular velocity, target, and assist ON. Flight-assist-OFF torque control, full inertia tensor, component mass positions, cargo, and centre-of-mass pivot remain deferred. |
| **Afterburner (SystemNewtonian)** | ✓ Done, unverified in-engine | `Z` (rising edge, SystemNewtonian only, no re-trigger while active) engages 2s of constant forward acceleration at `FlightConstants.AfterburnerAccelMultiplier` (5×) the configured ship's current harmony-scaled installed-engine forward force divided by current mass — not tapered by the harmony speed ceiling, not WASD-steerable (`TickNewtonianPhysics` early-returns before the WASD/X-Stop block while active). `XStopToggle`/`SlipstreamToggle` input is gated (`&& !_afterburnerActive`) in `SpaceSimulation.TickPhysics`; the `H` hyperspace trigger is gated separately on the main thread (`SystemSpaceState.HandleKeyboard`) via `ShipSnapshot.AfterburnerActive`. Mouse pitch/roll and A/D yaw stay responsive; while active, small random pitch/yaw jitter perturbs the assisted target and remains torque-limited. |
| **SystemSlipstream flight model** | ✓ Done | 10-harmonic log-scaled table (1 km/s – 30 Gm/s); smooth-step ramp between harmonics; clunk roll animation (Newtonian only); `ComputeProximityScale()` cubic speed dropoff from 100 km; planet dropout at 200 km alt; station dropout at 20 km with an engine-harmony-derived Newtonian exit cap. |
| **LKM station zones** | ✓ Done, previous behavior visually confirmed by Timo; harmony-derived limits pending acceptance | 3 concentric zones (8 km / 2 km / 500 m) with per-zone maximum harmony index; 6-second compliance window; violation flag stub; forces Slipstream exit on zone entry with a ceiling resolved from installed engines at the permitted harmony. Station proximity used for LKM/slipstream is computed on the simulation thread from canonical `Ship.Position`, `GameClock.SimTime`, and installed `StarSystem`. `ShipSnapshot` uses a post-physics proximity sample for published LKM zone/max harmony while pre-physics LKM state remains the enforcement/compliance input. |
| **Simulation world-state ownership cleanup** | ✓ Done (targeted phases) | Camera position no longer affects physical reference-frame selection; reference-frame source/velocity are simulation-owned and published via `ShipSnapshot`; `Ship.Position` and `GameClock.SimTime` no longer round-trip through Main in `WorldSnapshot`; `Star`/`StarSystem` are installed explicitly on system transition instead of resent every frame; debug-camera F11 return snaps camera to ship instead of teleporting ship. Mid-session `EnterSystem(...)` now recomputes the main-side ecliptic transform after generating the new system, fixing station render/LKM mismatch confirmed by Timo. Remaining teleport debt: `TeleportShip` still means zero absolute velocity and applies after `UpdateEnvironment`, producing one stale-environment tick. |
| **Flight HUD** | ✓ Done | Mode / engine harmony / LKM / X-STOP indicator line; `Topics.Flight.*` DataBus topics; clunk camera-space roll (view-space multiplication, no planet-glue) |
| **Keplerian orbital mechanics** | ✓ Done | Full orbital elements (`e`, `i`, `Ω`, `ω`, `M₀`) on `OrbitalBody`; `ComputePosition` + `ComputeVelocity`; Newton solver for eccentric anomaly; moons/asteroids/stations keep circular rail |
| **PlanetData + PlanetFactory** | ✓ Done | `PlanetType`, `AtmosphereCompositionType` enums; `PlanetData` record with physical/atmosphere/surface data; `PlanetFactory` procedural generation; per-tick planet orientation update in sim |
| **Reference frame fix** | ✓ Done | On atmosphere entry, planet orbital velocity is subtracted from `ship.Velocity` (→ planet-relative); position integration adds `_atmosphericPlanetVelocity` to keep galaxy position tracking. Restored on exit. Physical reference-frame source/velocity selection now runs in `SpaceSimulation` from sim-owned ship position, sim time, installed system context, and sensor world data; atmosphere mode reports zero reference velocity because ship velocity is already planet-relative. |
| **PlanetaryCoordinateSensor** | ✓ Done | `Inferior.Gameplay/Sensors/`; publishes `PlanetCoord.*` topics each tick in atmosphere: Altitude, Latitude, Longitude, Heading, GroundSpeed, VerticalSpeed, Temperature. Topics added to `Inferior.Core/DataBus/Topics.cs`. |
| **Ground radar HUD panel** | ✓ Done | 8-row panel (ALT/VS/LAT/LON/HDG/GS/TEMP/PRES) in `DrawAtmosPanel()`; PRES shown in green when ≥ 0.1 bar (Slipstream threshold); subscribes/unsubscribes to `PlanetCoord.*` on state enter/exit. |
| **DriveInstrumentPanel** | ✓ Done | Right cockpit-rail wing; DRIVE header + mode label; Newtonian: HARM/CEIL/FWD/REL rows (X-STOP overlay); Slipstream: HARM/SPEED rows; FUEL/PWR/HEAT stub bars; `Topics.Flight.*` DataBus driven. |
| **LedIndicator control** | ✓ Done | Round/square lamp; colour ranges; variable blink (BlinkClock global); exponential brightness easing (k=60, ~50 ms); stopping mode LED in HUD (amber, LabelAnchor.Bottom, subscribes to `Topics.Flight.XStopActive`) |
| **CockpitRail notch connectors** | ✓ Done | Full-rect minus top-right notch (A→B horizontal + B→C diagonal); LED centered in lower-outer area; hStep/dz computed from LED size (no named constants); STOP (amber, round, left) and WARN (green/yellow/red/blink-red, round, right) LEDs in connectors; `Topics.Ship.WarnLevel` added, stubbed at 0.0 |
| **Checkerboard planet sphere** | ✓ Done | Per-planet `VertexPositionColor` sphere (128×64 segments) built in `BuildPlanetSphere()`; 5°×5° cells with type-specific colour pairs (7 `PlanetType`s); pole caps; equator stripe; pre-baked directional lighting; rotates via `body.Orientation`. |
| **GeometryBuilder** | ✓ Done | `Inferior.Rendering/GeometryBuilder.cs`; `AddConvexFace` / `AddFace(outwardNormal)`; winding auto-corrected from centroid or explicit normal; `BuildDynamic` (VertexPositionNormalColorTexture since lighting-pipeline Phase A, White baked, flat normals) and `BuildBaked` (VertexPositionColor, currently no callers). |
| **MeshRenderer** | ✓ Done | `Inferior.Rendering/MeshRenderer.cs`; draws over the shared `LitSurface.fx` effect (lighting-pipeline Phase A) — `DrawDynamicLit` (DynamicLit technique) and `DrawBakedColorLit` (BakedColorLit technique); explicit `CullCounterClockwiseFace`. |
| **Container rendering** | ✓ Done | Promoted from a debug helper to an ordinary world object (Brief-StarterAndTestProps Task 2): real `ShippingContainerFactory`-generated geometry, standard `MeshRenderer.DrawDynamicLit` rendering path, real world-object bookkeeping. `SystemSpaceState.Containers.cs`: `SpawnContainers`/`PlacedContainer`/`_containers`/`DrawContainers` (all renamed off `Test`/`Debug`). Placement seed derives from `SeededRandom.Derive(station.PersistenceId).Derive("containers")` (was `station.Name.GetHashCode()` — process-randomized in .NET, forbidden by `!invariants.md` §6); per-container tumble derives from that container's own `(station, local index)` identity (was a global spawn-order counter). Kinematics on rails: position is a fixed station-relative offset, orientation is `RailsOrientation` — a pure function of sim time (`SystemSpaceState.Helpers.cs`), evaluated at draw time, no per-frame mutation. |
| **Calibration cube** | ✓ Done, visually confirmed by Timo | `SystemSpaceState.CalibrationCube.cs` (Brief-StarterAndTestProps Task 3) — 10 m lighting test card near the starter station: six flat axis-coded face albedos (+X red / -X dark red / +Y green / -Y dark green / +Z blue / -Z dark blue) with white "+X"/"-X"/... labels (`ShippingContainerFactory.AddTextGeometry`; added a `+` glyph to `BitmapFonts`). Anchored **station-relative, translation only** (live station position + fixed galaxy-space offset each frame, no station rotation applied), captured once at a `RelocationSequence`-gated moment, 100 m in front of the starter spawn pose; orientation on rails via the same `RailsOrientation` helper as containers, ~0.05 rad/s spin. Same `DrawDynamicLit` rendering path, no special-casing. **Three placement bugs found and fixed before first sighting:** (1) capture from the first non-null snapshot read pre-relocation ship state — fixed with sim-published `ShipSnapshot.RelocationSequence` (also hardened `_waitingForStationRelocationSnapshot`'s identical latent race); (2) absolute universe position fell tens of km behind the orbiting station within seconds — fixed by station-relative anchoring; (3) station position at capture was evaluated at the previous frame's `_gameTimeSeconds` while ship position came from the current snapshot — during heavy startup frames this diverged by hundreds of ms (~tens of km at orbital speed) — fixed by evaluating at `snap.SimTime` (position and time from the same snapshot), with a sanity warning if the offset exceeds station radius + 2 km, and the capture diagnostic published as a HUD `SystemMessage` (console-only logging is invisible in a windowed game). |
| **Starter system/station selection** | ✓ Done | `Inferior.Galaxy.StarterSystemSelector` (Brief-StarterAndTestProps Task 1) replaces two independently-duplicated nearest-G/K-star implementations (`InferiorGame.FindStartStar`, `GalaxyMapState.FindStartingSystem`) and the by-name (`"Far Station"`) starter-station lookup, which broke the moment the seed or galaxy changed. `SelectStar`: nearest G/K star to galactic origin among the 200 nearest candidates whose generated system has ≥3 stations, falling back to the plain nearest G/K star with a logged diagnostic if none qualify. `SelectStarterStation`: largest `StationSize` within that system, tie-broken by ordinal `PersistenceId`. Tests: `StarterSystemSelectorTests.cs` (determinism, station-count floor, size/tie-break ordering), `StarterStationRelocationTests.cs` updated off the name dependency. |
| **Screenshot capture** | ✓ Done, confirmed working by Timo (incl. clipboard paste) | `Inferior.Game/Platform/HostServices.cs` (Brief-StarterAndTestProps Task 4) — the home for host-system/OS-specific concerns going forward. `SaveScreenshot(GraphicsDevice)`: `GetBackBufferData` synchronously on the render path, PNG encode + file write backgrounded via `Task.Run`; saves to `Screenshots/` next to the executable as `yyyyMMdd_HHmmss_fff.png`, logs the path to console; also places the image on the Windows clipboard (CF_DIB via P/Invoke — BGRA bottom-up conversion, handle ownership transferred on successful `SetClipboardData`) so screenshots can be pasted directly into chat for AI analysis. Trigger: Ctrl+C rising edge, detected globally in `InferiorGame.Update` (same chord-detection shape as `StationCycleController`'s Ctrl+F12), captured at the end of `Draw()`. **Flagged, not silently assumed:** the brief claimed no existing Ctrl+C binding conflict — false, `Inferior.UI/Controls/TextBox.cs` already binds Ctrl+C to `CopySelection()` while a TextBox has focus. Implemented as specified anyway (global, focus-independent); both firing together is harmless but is a real collision, not hypothetical. Workaround for OS-level screenshotting misbehaving while the game runs (diagnosis of that bug out of scope). |
| **Type-1 ship hull** | ✓ Done | 31-face hull + hex nacelles + pylons; dynamic lighting; third-person camera (F3); drawing owned by `ShipMeshRenderer` (`Inferior.Rendering`) |
| **Physical cockpit mounts / installed cockpit** | ✓ Done; visually confirmed by Timo (2026-07-19), except revised light controls | `Docs-ai/ship-cockpits.md` is the active authority and supersedes the old hull camera-offset design. `Inferior.Gameplay/Cockpit/` defines mount/module data, registry, installation rotation, runtime light state, presentation snapshot, and module-owned Aries geometry/material groups. Aries has one hull-owned C2 top mount (`type-1.cockpit.top.01`); `ShipBuilder` installs `aries-civilian-canopy-cockpit` by default. The simulation-owned ship publishes both cockpit root and module camera poses. `ShipMeshRenderer` renders the installed cockpit in chase/orbital view from the root pose; the old hull-authored cockpit-like surfaces are ordinary armour panels, so there is one visual owner. First-person own-ship geometry remains hidden. The HUD reticle now projects ship-local −Z from the cockpit camera origin through the active view/projection, making the accepted authored 3° inward camera yaw visible as the intended reticle offset; behind-camera and viewport-edge behavior are covered. `InstalledCockpitRecord` persists mount, definition, rotation, and light state; older Aries records with no cockpit data receive the hull default. Canopy/internal light Set/Toggle commands drain through `CommandBus` on the simulation thread and drive cockpit material state. Debug controls were revised after Ctrl+F1 produced no visible result for Timo: `F1` toggles canopy lights (`Ctrl+F1` remains an alias), `Shift+F1` toggles internal lights, and the HUD reports explicit on/off state. The revised controls/light appearance still need an in-engine confirmation. Build is clean; 245 tests pass, including cockpit transforms/persistence/commands/presentation geometry, reticle projection, and debug input edges. |
| **Authored flyable hulls / player ship cycle** | ✓ Done through Cosmo/Antega; previous maximum-output translation visually confirmed, Cosmo build-tested but not visually accepted; harmony/lift acceptance pending | Aries (`type-1`), Cosmo (`cosmo`), Asterisk (`asterisk`), Beren (`beren`), and Antega (`antega`) are registered semantic hulls using the same generic composite renderer and stable cockpit `NEXT SHIP` cycle. Cosmo is the new ~8 m no-cargo sport small-class hull: 12 t hull, centred C1 top cockpit, tapered three-ring octagonal body with flatter underside, and a single Needle H2 above/behind the cockpit on a 1.5 m dorsal strut (temporary use of Needle until a top-mount engine exists). Installed Mule, Needle, and Atlas instances author mass, harmony, directional fractions, thrust, and torque. `Ship.Mass` includes engine dry mass and the transitional 1,200 kg reactor contribution. Current max-forward engine thrust is Mule 780 kN, Needle 939 kN, Atlas 17.926 MN; speed ceilings remain unchanged at 50-25,600 m/s. At maximum harmony, forward/lateral/lift acceleration is Aries 20.0/10.0/15.0 m/s², Cosmo 47.42/23.71/35.57, Asterisk 37.5/18.75/28.125, Beren 20.0/10.0/15.0, and empty Antega 20.0/5.0/10.0 (1.020 g diagnostic hover maximum). Asterisk and Cosmo own 0.75 forward, 0.75 maneuvering, and 0.60 rotation efficiencies. Ship cycling preserves angular velocity and applies the selected shared harmony within the replacement engines' ranges. Cargo mass and centre-of-mass work remain deferred. Stable harmony/shared-thrust checkpoint: `7a8ece9`; 5x thrust retune verified with `ShipPropulsionTests` 31/31 and `dotnet build Inferior.slnx` clean; Cosmo verified with focused hull/cycle/cockpit tests and solution build, pending in-engine visual/handling acceptance. |
| **Object Designer / Beren JSON authoring** | Architecturally proven; visually functioning; usability not accepted | `Inferior.ObjectDesigner` is a standalone MonoGame + `Inferior.UI` executable for Beren authoring. `Assets/Ships/beren.ship.json` is the source asset for Beren; `BerenHullDefinitionFactory` is a thin loader-backed adapter using `Inferior.Gameplay/Hull/Authoring` DTO/load/validate/convert code. The designer opens Beren by default, uses the shared semantic hull/engine/cockpit rendering path, and now has a UI-owned side-by-side shell: top menu/toolbar, left 2D editor, right 3D render-target preview, collapsible properties/diagnostics, and status bar. The rendering architecture is proven: the 3D preview is prepared into a pane-sized render target before the backbuffer clear/UI pass, then sampled by the perspective surface through ordinary SpriteBatch UI composition; the 2D editor remains a clipped custom primitive surface; overlays draw last. The custom-content helper restores render targets, viewport, scissor, rasterizer, blend, depth/stencil and sampler state. A UI hardening pass added structural and rendered-output tests for panel traversal, layout, clipping, z-order, overlap hit testing, overlays, custom rendering, render-target restoration, basic controls, and Object Designer-like composition. Menus live on `UIManager`'s overlay layer, projection/constraint buttons use authoritative `ChoiceGroup<T>` values (`Plane` replaces the ambiguous second `View`), disabled controls are not hit-test targets, and `OverflowMode.Visible` children can be hit outside parent bounds when no clipping ancestor blocks them. Editing can load Beren from JSON, select semantic vertices, marquee-select, edit numeric active-vertex coordinates, apply view/X/Y/Z/active-face-plane constraints, show incident-face diagnostics, pan/zoom/fit the 2D view, orbit/pan/light/zoom the 3D view, update the 3D preview after valid geometry edits, maintain command history/dirty state, save, and reload. Invalid in-memory geometry is expected authoring state: structured diagnostics are retained, save returns blocked instead of throwing through the UI, and the preview keeps the last renderable hull. The tool is still rough and unsuitable for serious hull production: active-face-plane constraint behavior is unclear or incorrect; selected-button border state is visually misleading despite the LED/group state; dragging a multi-vertex selection currently moves only one vertex; substantial editing-workflow refinement remains; full functionality review is pending Timo's later workout. Test ownership was split into `Inferior.ObjectDesigner.Test`, `Inferior.UI.Test`, and `Inferior.Gameplay.Test`; full Debug tests and Release build pass. |
| **`SystemSpaceState` file structure** | ✓ Done | Split into a `partial class` across `Inferior.Game/States/`: primary `SystemSpaceState.cs` (fields, ctor, `OnEnter`/`OnExit`/`OnResize`/`Update`/`Draw`/`HandleKeyboard`) plus `.Stations.cs`, `.CelestialBodies.cs` (now nearly empty — one Stations-owned texture helper left), `.Skybox.cs`, `.Ship.cs`, `.Targeting.cs`, `.Helpers.cs`, `.Containers.cs`, `.CalibrationCube.cs`, and `.Shadows.cs`. `.Hyperspace.cs`/`.Hud.cs` were deleted once their contents fully moved to `FlatHyperspaceController`/`CockpitUI`. |
| **`FlatHyperspaceController`** | ✓ Done | `Inferior.Game/Hyperspace/`, alongside `HyperspacePlane`/`FlatHyperspaceConstants`/`IHyperspaceSheetRenderer`; owns flat-hyperspace flight (preamble alignment, travel, drop-out, overlay); `Camera3D`/`Star`/ship snapshot always passed per-call, never stored (camera can be reassigned via debug Home-key reset); `EnterSystem` world/skybox swap stays on `SystemSpaceState`, handed in as an `Action<Star, DVec3, Quaternion, FlightMode>` callback. |
| **`BusSubscription<T>`** | ✓ Done | `Inferior.Core/DataBus/`; `IDisposable` wrapper pairing one `Bus<T>` subscribe/unsubscribe, so subscriptions collect into a `List<IDisposable>` and tear down in one loop instead of 15 hand-paired named fields. Gravity direction X/Y/Z subscriptions stay on `SystemSpaceState` for cockpit direction balls; `UpdateReferenceFrame` no longer reads them from Main. |
| **`CockpitUI`** | ✓ Done | `Inferior.Game/UI/`, alongside `DriveInstrumentPanel`/`RadarDisplay`/`LandingRadarPanel`/`DockingInstrument`/`CockpitRail`/`HudAlertDisplay`/`LedIndicator`/`SpectrumGraph`; owns the entire cockpit instrument/HUD tree (`UIManager`, panels, meters, dir-balls, radar displays), split into `.cs`/`.DirectionBalls.cs`/`.Targeting.cs`/`.Hud.cs`; takes borrowed dependencies plus `galaxyToEcliptic`, simulation shield-request, and simulation ship-cycle callbacks rather than owning coordinate-transform, shield, or live-ship authority. The CTRL panel contains the accepted `NEXT SHIP` button. `FeedRadarContacts`/`UpdatePadTargetPosition` stay on `SystemSpaceState`, calling `CockpitUI.NotifyRadarContact`/`NotifyRadarContactLost` where they need the cockpit direction ball. |
| **`SpritePrimitives`** | ✓ Done | `Inferior.UI`; `DrawText`/`DrawRect`/`DrawRectBorder` promoted out of per-state duplicates into one shared static helper; used by `CockpitUI` and `SystemSpaceState.Helpers.cs`. |
| **`CelestialBodyRenderer`** | ✓ Done, known planet gap | `Inferior.Rendering`; owns star/planet body+glow+atmosphere drawing, orbit rings, and the underlying sphere/glow/atmosphere GPU meshes. Its planet-sphere lighting bake needed `SceneLighting`, which moved from `Inferior.Game` down to `Inferior.Rendering` as part of this extraction (`Inferior.Rendering` can't reference `Inferior.Game`). `Dispose()` now frees per-planet sphere buffers on `OnExit` — previously leaked, accumulating for every system visited in a play session. **Known gap:** mid-session `EnterSystem` still does not rebuild planet spheres. Station visuals no longer share that gap: system change invalidates the resident station package and rebuilds the lightweight station visual catalogue. |
| **`RingPrimitive`** | ✓ Done | `Inferior.Rendering`; small shared ring-mesh utility extracted from the old local ring-draw methods; used by both `CelestialBodyRenderer` (planet/moon orbit rings) and `SystemSpaceState.Stations.cs` (station orbit rings, which build their own compound world matrix first and need the plain draw overload, not a scale-only one). |
| **`SkyboxRenderer`** | ✓ Done | `Inferior.Rendering`; `Build` (static)/`Load`/`Draw`; `Load` runs from both `OnEnter` and `EnterSystem`, so — unlike `CelestialBodyRenderer` above — the skybox correctly rebuilds on a mid-session system change. Star-hover/click targeting logic and `_targetableStars` stay on `SystemSpaceState`; the hyperspace-mode draw guard moved to the `SystemSpaceState.Draw()` call site since `Inferior.Rendering` can't see `Inferior.Game.Hyperspace`. |
| **`ShipMeshRenderer`** | ✓ Done | `Inferior.Rendering`; owns the ship hull/nacelle/pylon meshes (via `Type1HullFactory`) and their drawing; shares the single `MeshRenderer` instance with debug test containers (borrowed via constructor, not owned — `SystemSpaceState` still disposes it). `Draw` takes the already-rolled view matrix as an explicit parameter to preserve the clunk-roll fix below. Camera-control/spawn-orientation math (`UpdateThirdPersonCamera`, `QuatLookAtWithUp`, `QuatLookAt`) stays on `SystemSpaceState`. |
| **Clunk-roll view-matrix bug** | ✓ Fixed | The gear-shift/harmonic "clunk" camera roll is applied once per frame onto the shared `BasicEffect.View`. Several call sites independently re-derived the raw, un-rolled `_camera.ViewMatrix` instead and so stayed visually fixed during a clunk: own-ship third-person mesh, debug test containers, targeting brackets (incl. containers), the locked hyperspace-target skybox ring, station dot markers, station nav-light/warning-strobe glow, planet atmosphere billboards, plus skybox star hover and "C"-key target selection. All now read the already-rolled matrix. `DrawStarGlow3D`'s behind-camera cull check was checked and confirmed not affected by roll. |
| **Station display-position separation (`SystemMapState`)** | ✓ Done, pending user visual confirmation | At high zoom a station's true orbital position could sit close enough to its parent's dot to overlap and become unclickable (unlike moons, whose real orbital radius is usually large enough that this wasn't visible). New `GetStationDisplayScreen(station)` nudges the station's own screen marker away from its parent along the true offset direction whenever the true screen distance is below `parentVisualRadius + stationDotRadius + 10px`; used by `DrawStations`, `DrawStationNames`, `HitTestStation`, and `HandleRightButton`'s station loop. `DrawOrbitRings`'s orbit ring is untouched — still drawn at the true orbital radius around the true parent position, per brief (only the dot/label/hit-test moves, not the ring). Parent-position resolution (handling the one level of grandparent indirection a moon needs) was already duplicated between `GetStationSystemPos` and `DrawOrbitRings` — factored into a shared `GetOrbitalBodyPos`/`GetStationParentPos`, no behavior change there. Also de-duplicated the star-radius formula (`StarVisualRadiusPx`, shared by `DrawStar` and the new separation calc) and the station-dot-radius switch (`StationDotRadius`, shared by `DrawStations` and the new separation calc). |
| **`docking-bay` station module** | ✓ Done, visually confirmed by Timo (station panel, display-position separation, and afterburner all reported working well in the same manual test pass). | First hollow station module — ships fly inside through a framed door on the -Z face of a box hull `DockingBayHull.cs` builds itself (chamfered walls, door frame, throat, interior walls), attached via a pre-growth step in `StationGenerator.Run()` with a reserved approach-corridor volume so nothing else grows in front of the door. Pad-driven sizing (`DockingBayLayout.Compute`) derives envelope/door dimensions from a seeded pad mix rather than a fixed size; the interior gets a door-proximity/overhead/corner-noise ambient-lighting gradient since the sun rarely reaches inside directly. `SystemMapState` shows a station info panel (name, size class, orbit, bay/door dimensions) on hover/click. |
| **Station navigation QoL** | ✓ Done | New games start near the starter station selected by `StarterSystemSelector` (see row above; 500 m surface stand-off). Station-relative relocation is simulation-owned (`SpaceSimulation.RequestStationRelocation`), addressed by station `PersistenceId`; applies a surface stand-off, matches destination reference-frame velocity, and faces the ship toward the station. System-map arrival (2 km stand-off, `SystemMapStationArrivalStandOffMeters`) and the debug station-cycle control (Ctrl+F12 rising edge, `StationCycleController`) use the same canonical relocation path — one operation, not parallel implementations. |
| **Flight controls QoL** | ✓ Done | Harmony selection may be changed during slipstream acceleration; X-Stop may be selected during afterburner (selection does not cancel the burn; damping becomes effective after afterburner thrust completes). Tests: `SlipstreamHarmonyRetargetingTests`, `XStopAfterburnerTests`, `StationCycleControllerTests`, `StarterStationRelocationTests`. |
| **Station lighting / shadows** | Phases A/B/C ✓ Done, visually confirmed by Timo (2026-07-17). **Phase D (FocusMap two-tier) abandoned** — hit an unresolved phantom-shadow bug across 5 measurement rounds, frozen on `feature/lighting-pipeline-phase-a` (tip `5fec418`), not merged, not revived. | One `SurfaceFormat.Single` StationMap per frame for the nearest/resident station (`Inferior.Game/Content/Effects/ShadowCaster.fx` + `SystemSpaceState.Shadows.cs`), `CullNone`, light camera fitted from real caster geometry bounds (`_shadowCasterHullBounds` ∪ `_shadowCasterDecoBounds`). Resolution is per-station-size-class (`StationShadowMegaBreakpointMetres`=1500m): `StationShadowMapSizeStandard`=8192², `StationShadowMapSizeMega`=8192² — the original 16384² megastation target caused a repeatable 1.7-4.2 km yaw/roll stutter band; reduced to 8192² and visually confirmed fixed by Timo, both classes now seen in-engine. Manual PCF soft edges (`ShadowKernelRadius`, Shift+F6 to cycle Off/3x3/5x5) default to 5x5, picked over 3x3/Off at both a typical and a mega station. Casters = module hull (box modules via `BuildHullMesh`; any `MeshFactory` module via its own separate hull mesh, general condition not a docking-bay special case) plus decoration gated by `StationDecorator.DecorCastingPolicy` — Pipes/SurfacePipes/PipeBrackets, Tanks/Containers/Greebles/Chimneys/VentGrilles/SolarPanels, Dishes/Antennas, and Windows/Hatches cast; PanelSeams/EdgeTrim/Cables/Lights/Glass/LandingPadMarkings don't (each with a WHY comment in the table itself) — plus dedicated `MegastationCasterClasses` (Infrastructure/MegaGreeble/Fabric/ServiceChannel/Interior "Major" classes) covering the native megastation presentation layers. A post-composition safety net warns via `SystemMessage` if any module ends up with no hull caster at all. Diagnostics: F6 delta view, F7 binary view, F8 overlay, F9 freeze, Ctrl+F6 caster-stage cycle. **Known in-spec artifact:** free-floating objects (ship, rails containers, calibration cube) don't cast or receive station shadows yet — deferred, no brief written. |

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
    SystemNewtonian,          // Engine-harmony-ceiling force-based Newtonian
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

0. **Next megastation stage requires an explicit brief.** The accepted production baseline is
   summarized in the header above (structural massing through L3c/L3d wall composition and
   L4a-L4g Mega Shelves) and in the compacted table rows below. H1h distance blueing remains a
   halted proof of concept, not an authorized next tuning stage. Further Bolon foreground
   layers, docking, collision, and true radiosity remain deferred — none is authorized by this
   status alone. Preserve structural geometry, Z1/Z2 zoning, G1, locked mega-greeble, M1
   ownership, streaming, native shadows, the protected H1 flight volume, accepted Bolon
   molecular structure/surface history/aperture construction, and the 8192² shadow baseline.
1. **Free-object shadow participation** remains deferred: ship, rails containers, and the
   calibration cube do not yet cast into or receive from the station shadow map. The abandoned
   FocusMap/two-tier Phase D line remains frozen and must not be revived.
2. **Cockpit light control in-engine verification** — the cockpit geometry, eye point,
   authored 3° inward yaw, chase presentation, and projected ship-forward reticle were
   visually accepted by Timo on 2026-07-19. Ctrl+F1 produced no visible result, so the
   debug route now uses `F1` for canopy lights (`Ctrl+F1` remains an alias) and `Shift+F1`
   for internal lights, with explicit on/off HUD feedback. Confirm that both light material
   groups visibly change in chase view. First-person own-ship geometry remains intentionally
   hidden.
3. **`StationSceneRenderer` extraction** — station mesh/glow/dot rendering out of
   `SystemSpaceState.Stations.cs` into `Inferior.Rendering`, same pattern as
   `CelestialBodyRenderer`/`SkyboxRenderer`/`ShipMeshRenderer`.
4. **`SpawnShip` vs. `ShipBuilder` convention** — `SpawnShip` still manually wires
   reactor/bus/shield/heatsink/coolant directly, bypassing the documented "`ShipBuilder`
   is the sole construction path for `Ship`" rule. Investigation in progress as of this
   doc update; no resolution decided yet.
5. **Player-editable cockpit** — design pass (see Open design decisions). This means
   runtime editing of cockpit instruments, not the physical cockpit mount/module system
   implemented above.

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
Inferior.Core        — DVec3, Units, DataBus, CommandBus, BusSubscription<T>, GameClock, GameDate/calendar, Noise, Topics
Inferior.Galaxy      — star/system generation, OrbitalBody, StarPhysics, StarterSystemSelector
Inferior.Gameplay    — Simulation, Physics/, SensorData/, Sensors/, PlayerInput
Inferior.Persistence — ShipRecord, repositories, log (pure IO, no live objects)
Inferior.Rendering   — Camera3D, MeshFactory, GeometryBuilder, MeshRenderer, Type1HullFactory,
                       SceneLighting, StationBrightnessTuning, CelestialBodyRenderer, RingPrimitive,
                       SkyboxRenderer, ShipMeshRenderer
Inferior.UI          — UIManager, UIRenderer, Theme, layout/menu/text controls, SpritePrimitives
Inferior.ObjectDesigner — standalone MonoGame Beren authoring tool; uses shared Gameplay
                       Hull/Authoring JSON load/validate/convert path
Inferior.UI.Test / Inferior.ObjectDesigner.Test / Inferior.Gameplay.Test
                      — subsystem-owned xUnit coverage split out from Game.Test where applicable
Inferior.Game        — entry point, game states, SpaceSimulation, TargetingSystem, ShipBuilder,
                       factories, StationGenerator, StationDecorator, StationModuleRegistry,
                       Station/Megastations/ (structural massing, zoning, Fabric, Service Channels,
                       H1/L-series interior/landing, Bolon — see architecture-map-ai.md for the
                       file-level breakdown),
                       Hyperspace/ (FlatHyperspaceController + hyperspace sheet renderers),
                       UI/ (CockpitUI, DriveInstrumentPanel), Platform/ (HostServices)
TestProfiles/        — Fast.runsettings (default, excludes [Trait("Category","Slow")]) and
                       All.runsettings (`-p:RunSlowTests=true`); see testing-ai.md
```

Dependency: `Core ← Galaxy ← Gameplay ← Rendering`, `Core ← Persistence`, and `Core ← UI`, all
converging in `Game` (which also depends on `Galaxy`/`Gameplay` directly).

> Corrected from the previous version of this doc: `PlayerInput` lives in `Inferior.Gameplay`
> (not `Core`), `TargetingSystem` lives in `Inferior.Game` (not `Gameplay`), and
> `Inferior.Persistence` only references `Inferior.Core` directly — it does not go through
> `Galaxy`/`Gameplay`. Verified against each project's `.csproj` while updating this doc.

---

## Station generation — architecture summary

See `stations-ai.md` for full reference. Key facts:

- **Generation is deterministic** — same seed always produces same station
- **AO baked, directional light real-time** — AO (and self-illumination floor S, in vertex alpha) applied at generation time to vertex colours; the sun term is computed every frame in `LitSurface.fx` from the real world normal, so a rotating station is lit correctly (lighting-pipeline Phase A — see `Docs/station-lighting-pipeline-spec.md`).
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
| In-game calendar advancement / time scale | Initial date fixed at `6864-07-19` (`E3.326-07-19`); simulation advancement and time scale remain **undecided** |
| Hyperspace interference lock formula | Placeholder only |
| Shield coverage mapping — which hull faces a given shield covers | Pending |
| Weapons system | Not yet designed |
| Multiplayer architecture compatibility | Noted, deferred |
| Station text/markings pass | Planar bitmap-font mesh geometry is centralized and accepted; font-atlas/decal typography remains not designed. |
| Station module shape variety — non-box modules | Partially done for ordinary stations (`docking-bay`, `hab-block-octagonal`/`science-block-octagonal` via `MeshFactory`); megastations use an entirely separate non-box structural-volume system (see the implemented table above). Further ordinary-module archetypes remain design-only (see the enclosed-archetypes row below). |
| Antenna dish interior winding | Needs `AddFace(outwardNormal)` in `GeometryBuilder` when antenna geometry is revisited — concave interior faces point back toward stem, not away from mesh origin |
| Station weathering pass | Not yet designed |
| Station enclosed archetypes (Sphere, Pyramid, Prism, etc.) | Designed, not implemented |
| Planetary terrain rendering | Deferred — separate brief required |
| Landing radar instrument | Deferred — requires design doc with sketches |
| Atmospheric visual effects (clouds, haze, re-entry glow) | Not yet designed |
| Formal small-object rendering strategy (ships, containers, whatever comes after) | Deferred — ships and containers (now a real, promoted world object — see "Container rendering" above) both exist as data points; no formal cross-object strategy written up yet |
| Player-editable cockpit (runtime add/remove/edit of cockpit instruments) | Not designed — `CockpitUI`'s clean construction/lifecycle boundary was partly built in service of this, but the feature itself hasn't been designed |

---

## `mega-stations` → `master` merge (2026-09-11)

Merged (47 commits on `mega-stations` vs. 36 on `master` since their common ancestor). This
file itself had its post-merge cleanup/consolidation pass the same day: ~20 giant brief-by-brief
megastation/sun-brightness rows were collapsed into current-state summaries (session-by-session
history is in git log / `Docs-archive` if ever needed), the duplicate "What is in progress"
megastation narrative was removed, several stale/contradictory facts were fixed (the DataBus
channel list below, a docking-bay row whose status badge contradicted its own prose, a shadow
row still claiming no mega-class station existed), and the Document map/Project structure
sections were brought back in sync with the repo.

Five files had real textual conflicts, all resolved in favour of preserving both sides'
independent work rather than picking one wholesale:

- **`SystemSpaceState.cs` / `.Shadows.cs`** — master's eager "build every station's GPU geometry
  in `OnEnter`" path (unchanged since the branches diverged) was superseded by mega-stations'
  lazy single-station visual residency system (`StationVisualPackage`, streamed by distance) —
  took mega-stations' side; the old `BuildStationShadowCasterMeshes` is gone, replaced by the
  residency system's own per-package caster building.
- **`StationDecorator.Containers.cs`** — master's dedup (`BuildContainerMesh`, Brief Z5 Fix 1) and
  mega-stations' mirrored-container-text fix are the same fix at two different points in the
  pipeline (`MergeTransformed`'s existing handedness auto-correction makes them numerically
  identical) — kept master's deduplicated call site.
- **`StationDecorator.Tanks.cs`** — combined both: mega-stations' primitive extraction
  (`StationIndustrialPrimitives.EmitTankCore`) and duplicate-text-geometry fix
  (`PlanarTextGeometry`) plus master's Brief Z4 Fix 3 variable tessellation (`sides` parameter,
  `TankSidesForRadius`) — added `sides` to `EmitTankCore` so both survive.
- **`StationGenerator.cs`** — both sides added new, non-overlapping methods at the same insertion
  point; kept both. Master's `RegenerateTextures` (the station-brightness tuning panel's live
  regen action) was rewritten against mega-stations' new CPU/GPU-split pipeline
  (`PrepareTextures`/`CompactSelectedTextures`) since the old single-shot `AssignTextures` it
  called no longer exists.

Beyond the marked conflicts, two **silent (non-conflicting) breaks** needed follow-up fixes —
git merged the surrounding text cleanly but the combined result didn't compile or didn't match:

- `Inferior.Core/DataBus/DataBus.cs`: master independently renamed the `System` bus to
  `SystemMessages` (and added an unrelated telemetry-channel redesign); mega-stations never
  touched this file, so ~18 call sites across 5 `Inferior.Game` files using the old `DataBus.System`
  name needed updating. Three of those files (`StationBrightnessTuning.cs`, `Stations.cs`'
  zone-debug overlay, `ZoneDebug.cs`'s Nova Anchorage dump) also referenced `_stationGeometry`/
  `_stationPanelTextures` — per-system dictionaries covering every station, removed by the
  residency system's move to one-resident-station-at-a-time — adapted to read
  `ResidentStationVisual` instead.
- `Inferior.Game.Test/SystemMaterialLibraryTests.OrdinaryStationTextureFixtureRemainsByteIdentical`:
  a mega-stations-only regression fixture pinned against the pre-Brief-B5 neutral
  `StationBrightnessTuning` defaults. Master's Brief B5 (visually confirmed by Timo, see the
  station-brightness-tuning entry above) baked tuned, non-neutral defaults
  (`DefaultVariantValueFloor=0.2`, `DefaultVariantCompressionStrength=1`,
  `DefaultSaturationFalloff=1`), so ordinary-station texture bytes legitimately changed —
  recomputed the fixture hash rather than reverting the defaults.

Build clean (0 warnings), all 851 tests pass (709 + 81 + 38 + 23 across the four test projects)
post-merge. Not yet visually confirmed by Timo in-engine — the merge is a mechanical/textual
integration, not a substitute for an in-engine look, especially given the DataBus/residency
adaptations above touched debug-only panels (station brightness tuning, zone-type debug
overlay) that don't have test coverage of their own.

---

## Document map

| File | Where | Purpose |
|---|---|---|
| `!current-state.md` | Docs-ai | This file — active state, conventions, next steps |
| `architecture-map-ai.md` | Docs-ai | Flat one-line-per-file map of every project — "where do I look for X?" |
| `design-ai.md` | Docs-ai | Design decisions, philosophy, all major systems |
| `lore-ai.md` | Docs-ai | Lore reference — bands, species, drive, materials |
| `components-ai.md` | Docs-ai | Component specs, properties, units |
| `ship-ai.md` | Docs-ai | Ship classes, roles, hull system |
| `stations-ai.md` | Docs-ai | Station generation — architecture, modules, decoration |
| `station-lighting-pipeline-spec.md` | Docs | Agreed lighting/shadow pipeline design (v2, replaces failed experiment) |
| `Shadow_fail_retrospective.md` / `Shadow_fail_design_spec.md` | Docs-archive | Failed shadow experiment — historical only |
| `inferior-design.md` | Docs | Full design doc with rationale |
| `inferior-lore.md` | Docs | Full lore with narrative |
| `inferior-classes.md` | Docs-archive | Class sketches — may be stale; repo is authoritative |
| `inferior-design-persistence.md` | Docs-archive | Persistence design — implemented |
| `inferior-design-ui.md` | Docs-archive | UI design — implemented |
| `testing-ai.md` | Docs-ai | Fast/Slow test-tier policy and how to run each |
| `docking-ai.md` | Docs-ai | Docking system design — pad types, instrument, navigation, landing detection |
| `flat-hyperspace-ai.md` | Docs-ai | Flat hyperspace travel layer design |
| `object-designer.md` | Docs-ai | Beren JSON authoring / Object Designer tool reference |
| `ship-cockpits.md` | Docs-ai | Cockpits and cockpit mounts — authoritative |
| `ship-engines.md` | Docs-ai | Engine system design — installed engines as physical components |
| `ship-engines-thrust.md` | Docs-ai | Ship mass, engine harmonies, shared thrust/rotation, landing capability |
| `ship-mass-and-propulsion.md` | Docs-ai | Active reference for configured ship mass and installed-engine propulsion |
| `ship-sizes-and-mass-ai.md` | Docs-ai | Ship size classes and length ranges — authoritative |
| `ship-visual-system-design-spec.md` | Docs-ai | Ship visual architecture draft — semantic hulls, armour, attachment points |
| `Instrumentation-analysis.md` | Docs-ai | Sensors/modules → buses → instruments architecture, partly superseded by the telemetry-channel refactor it now notes inline |
| `megastation-service-channels.md` | Docs-ai | Service Channels (SC1-SC3) design notes |
| `bolon-b4a-review.md` | Docs-ai | Historical Bolon B4a checkpoint test-count record |
