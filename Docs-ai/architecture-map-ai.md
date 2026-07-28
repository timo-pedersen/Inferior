# Inferior — Architecture Map

> A flat table of contents to the codebase: one line per file, what it is, nothing more.
> Distinct from `!current-state.md`, which tracks *why* things changed and what's
> in progress — this doc only answers "where do I look for X?" Regenerate wholesale when
> it drifts noticeably from `git ls-files`; don't hand-patch it per brief.

---

## Assets

Loose source assets, separate from MonoGame compiled Content.

**Ships/**

- `beren.ship.json` — versioned authoring source for the Beren hull; loaded by both the game and `Inferior.ObjectDesigner`.

---

## Inferior.Core

Foundation layer — no dependencies on any other Inferior project.

**DataBus/**

- `Bus.cs` — generic thread-safe pub/sub channel (`Bus<T>`): `Publish` enqueues off-thread, `Drain` dispatches on main thread.
- `BusSubscription.cs` — `IDisposable` wrapper pairing one `Bus<T>` subscribe/unsubscribe into a single disposable.
- `CommandBus.cs` — reverse-direction string-command channel (UI → sim thread), drained on the sim thread.
- `ComponentCommand.cs` — `readonly record struct` payload for `CommandBus` (`Topic` + optional `double Value`).
- `DataBus.cs` — static hub declaring the 8 named `Bus<T>` instances (System, Instruments, InstrumentState, InstrumentRanges, Radar, RadarLost, Spectra, Target) and `Drain()`.
- `RadarContact.cs` — `RadarContact` record + `ContactType` enum for radar/targeting data.
- `RangeValue.cs` — `readonly record struct` (Low, High) — an instrument's operating envelope.
- `SystemMessage.cs` — `SystemMessage` record + `SystemMessagePriority` enum for the System bus / console / HUD alerts.
- `Topics.cs` — static string-constant topic names for all DataBus channels.

**Math/**

- `DMath.cs` — double-precision math helpers (e.g. orbital angle).
- `DVec3.cs` — double-precision 3D vector; used for all universe-space coordinates.
- `Units.cs` — physical constants and unit-formatting helpers (AU, G, `FormatSpeed`, etc.).

**Random/**

- `SeededRandom.cs` — deterministic seeded PRNG used throughout procedural generation.

**Simulation/**

- `GameClock.cs` — sim-time clock (SimTime, in-game date), advanced once per tick.
- `Noise.cs` — noise generator library (Simplex1, White, Pink, Periodic, Spike).

**Time/**

- `GameDate.cs` — immutable absolute-day date value with ordering and day arithmetic.
- `GameCalendar.cs` — proleptic-Gregorian civil conversion, validation, arithmetic, and weekday operations.
- `GalacticEraTimeline.cs` — Galactic Era overlay, fixed initial game date, strict canonical formatting/parsing, and Era-boundary validation.
- `GameDateJsonConverter.cs` — numeric JSON persistence for `GameDate.AbsoluteDay`.

**Root**

- `GameState.cs` — `GameStateId` enum, `StateTransition`, abstract `GameState` base class, `GameStateMachine`.
- `PowerPriority.cs` — enum (Critical/High/Normal/Low) consumed by `PowerPriorityManager`.

---

## Inferior.Galaxy

Procedural universe generation. Depends on Core only.

- `GalaxyGenerator.cs` — procedurally generates the fixed galaxy (`StarCount` = 20,480; spiral-arm + core distribution). *Note: doc comment in this file and other docs say "2048 stars" — stale, the actual constant is 20,480. Worth a one-line fix wherever that's repeated.*
- `LandingPad.cs` — single pad on a station exterior: size, occupancy, local position/orientation.
- `OrbitalBody.cs` — orbital mechanics host: Keplerian elements, atmosphere, rotation, child bodies.
- `PlanetData.cs` — planet record: type, temperature, atmosphere composition, wind.
- `PlanetFactory.cs` — generates Keplerian elements + `PlanetData` from orbital position and star class.
- `PlanetType.cs` — enums for planet types and atmospheric composition categories.
- `Star.cs` — star data: spectral class, mass, radius, luminosity, lighting/map styling.
- `StarMap.cs` — read-only query layer over the star array (radius search, nearest-N, name lookup).
- `StarPhysics.cs` — derives stellar radius from mass/class; approximates core pressure via virial theorem.
- `StarSystem.cs` — full system generator + position calculator for planets/moons/asteroids/stations.
- `StarterSystemSelector.cs` — single canonical starter star/station selection (nearest G/K to galactic origin with ≥3 stations; largest-size starter station), replacing two duplicated implementations and a by-name lookup.
- `Station.cs` — space station data: orbit, size, services, landing pads, orientation, generation factory.

---

## Inferior.Gameplay

Simulation domain model. Depends on Core, Galaxy.

**Components/**

- `ArtificialGravityComponent.cs` — artificial gravity/inertial dampening, critical-priority battery backup.
- `ComponentSensor.cs` — single named instrumentation point publishing readings + ranges to DataBus.
- `ComponentStatus.cs` — enum: PowerOff, PowerOn, Initializing, Running.
- `ConnectorComponent.cs` — power cable between bus and consumer with wire-gauge throughput limits.
- `ConverterComponent.cs` — power-band transformer routing one power type to another via efficiency factor.
- `CoolantSystem.cs` — thermal transport medium carrying heat from components to a heat sink, with leakage.
- `EngineComponent.cs` — main propulsion: throttleable thrust, downthrust, optional gyro power generation.
- `ExhaustSystem.cs` — accumulates degenerate matter from engine operation, reduces efficiency when blocked.
- `ExternalLightsComponent.cs` — running/landing lights; battery-backed, player-controlled.
- `FlyabilityMonitor.cs` — registration-based configuration checks, posts warnings to the system bus.
- `GyroComponent.cs` — optional rotation-authority booster using Red Alpha power from paired engines.
- `HyperspaceHeatSink.cs` — central thermal mass + dissipation to hyperspace; saturation triggers EM burst.
- `InternalLightsComponent.cs` — cabin lighting; always active, can drop to emergency minimum-draw mode.
- `LifeSupportComponent.cs` — cabin pressure/temperature/oxygen; long battery duration.
- `ShieldComponent.cs` — directed-energy shield absorbing damage from a capacitor; charges off the power bus.
- `ShipComponent.cs` — abstract base for all installed components (power, startup, lifecycle).
- `ThermalNode.cs` — per-component thermal state; temperature = stored heat / capacity, with failure thresholds.
  - `Power/PowerBus.cs` — passive energy buffer between reactor and consumers; throughput + capacity limits.
  - `Power/PowerCapacitor.cs` — reusable joule-based energy storage (buses, components, reactor output).
  - `Power/PowerPriorityManager.cs` — distributes bus power to registered consumers in priority order.
  - `Power/PowerReactor.cs` — primary power generator: throttleable output, heat production, output capacitor.
  - `Power/PowerTypeEnum.cs` — power bands: Standard, RedAlpha, ShieldAlpha, ShieldTheta, ArtificialGravity.

**Hull/**

- `HullDefinition.cs` — immutable hull-class template: component slots, physical cockpit mounts, mass, size, aerodynamics, and optional designed-single-engine propulsion efficiencies.
- `HullDefinitionLibrary.cs` — static registry of hull definitions, looked up by stable `HullTypeId`.
- `AriesHullDefinitionFactory.cs` — Aries hull metadata, semantic geometry, component slots, and its physical C2 cockpit mount.
- `CosmoHullDefinitionFactory.cs` — compact no-cargo Cosmo sport hull: C1 top cockpit, dorsal single Needle H2 mount, tapered octagonal semantic geometry.
- `AsteriskHullDefinitionFactory.cs` — compact one-container Asterisk hull, front cargo-door assembly, starboard C2 cockpit mount, and single port H2 engine mount.
- `BerenHullDefinitionFactory.cs` — thin loader-backed adapter for `Assets/Ships/beren.ship.json`.
- `AntegaHullDefinitionFactory.cs` — 99 m, 120-container civilian hauler with segmented forward hatch, dorsal aft C5 bridge mount, and four external Atlas H10 engine mounts.
- `HullSlot.cs` — single component-install location with category restriction and max-class soft cap.
- `SlotCategory.cs` — enum restricting component types per slot (Reactor, Engine, Shield, Sensor, etc.).

**Hull/Authoring/**

- `HullAuthoringDtos.cs` — schema-versioned loose JSON DTOs for ship/hull authoring assets, including semantic geometry, cargo, mounts, and slots.
- `ShipAuthoringConverter.cs` — converts between authoring DTOs and runtime `HullDefinition` / semantic hull records.
- `ShipAuthoringJson.cs` — deterministic JSON options, asset path probing, load/save, and authoring validation diagnostics.

**Engines/**

- `EngineInstallationGenerator.cs` — installs one engine instance on one authored mount, including handed geometry and physical interface validation; default construction loops this operation for any mount count.
- `EnginePairGenerator.cs` — mirrored-pair validation/generation retained for workflows that specifically require a paired installation.
- `EngineDefinitionLibrary.cs` — stable registry for manufactured engine variants, currently Mule H2, Needle H2, and Atlas Civilian Drive H10.
- `AtlasEngineDefinitionFactory.cs` — 58.4 m large civilian H10 drive geometry, design intent, lights, and aft exhaust metadata.
- `EngineMount.cs` — live hull-owned engine socket and its installed engine instance.
- `EngineDefinition.cs` — immutable engine-family data including validated SI mass, maximum forward thrust, directional fractions, rotational torque, harmony count/endpoints, and quadratic harmony resolution.
- `EngineInstance.cs` — unique installed engine condition and simulation-owned selected harmony state.

**Cockpit/**

- `CockpitDefinitions.cs` — cockpit mount/module definitions plus mount-class, facing, and installation-rotation enums.
- `CockpitDefinitionLibrary.cs` — immutable cockpit-module registry containing the Aries roof canopy, Cosmo C1 sport cockpit, Asterisk starboard command blister, Beren underslung command pod, and Antega C5 civilian bridge.
- `CockpitCommandTopics.cs` — command-bus topic constants for canopy and internal cockpit lights.
- `InstalledCockpit.cs` — simulation-owned installation/runtime state and mount → installation → module camera-pose resolution.
- `CockpitVisualGeometry.cs` — immutable module-local cockpit mesh parts and material roles owned by cockpit definitions.
- `AriesCivilianCockpitGeometryFactory.cs` — C2 mounting body, housing, canopy, frame, dark backing, and light geometry for the Aries civilian cockpit.
- `CosmoC1CockpitGeometryFactory.cs` — compact C1 top sport cockpit geometry: low canopy, housing, frame, backing, and independent light geometry.
- `AsteriskStarboardCockpitGeometryFactory.cs` — compact C2 side-blister housing, forward/outward glass, frame, backing, and independent light geometry.
- `BerenUnderslungCockpitGeometryFactory.cs` — full downward-mounted C2 command pod with collar, housing, faceted canopy, frame, backing, and independent light geometry.
- `AntegaCivilianBridgeGeometryFactory.cs` — broad keyed C5 bridge plug, armoured base and housing, framed forward/side glazing, backing, and restrained light geometry.
- `CockpitPresentationSnapshot.cs` — immutable installed-cockpit root pose and light state published for rendering.

**Physics/**

- `CelestialBody.cs` — runtime star/planet/moon representation: position, mass, rotation state.
- `SimWorld.cs` — container of all live massive/orbital bodies; nearest-star/body queries for sensors.

**SensorData/**

- `EMP.cs` — thread-safe EMP detonation tracking with inverse-square falloff decay over time.
- `Environment.cs` — static query interface for world state; decouples sensors from world representation.
- `GravityCalculations.cs` — pure functions computing net gravitational acceleration from nearby bodies.

**Sensors/**

- `AtmosphericPressureSensor.cs` — passive atmospheric pressure reader; silent in vacuum.
- `ExternalPressureSensor.cs` — passive hull external-pressure measurement with white/pink noise.
- `ExternalTemperatureSensor.cs` — passive skin temperature near stellar photosphere or deep space.
- `GravitySensor.cs` — passive gravity field magnitude + direction, selective noise on strength.
- `LandingPadData.cs` — immutable snapshot of a targeted landing pad (position, normal, size, bay ID).
- `LandingSupportSystem.cs` — computes landing approach geometry, publishes to HUD, damage-based noise.
- `MagneticFieldSensor.cs` — passive magnetic field strength/direction; significant near neutron stars.
- `PassiveSensor.cs` — reusable sensor template applying white/pink noise and external noise sources.
- `PlanetaryCoordinateSensor.cs` — computes/publishes altitude, lat/lon, heading, speeds, temp, pressure.
- `RadiationSensor.cs` — passive total ionizing flux from stellar sources at ship position.
- `SolarSpectrumSensor.cs` — active scan-on-command sensor, publishes normalized spectral bins after a delay.

**Ship/**

- `Ship.cs` — the unique physical object in the universe: position, velocity, normalized orientation, simulation-owned ship-local angular velocity, components, and configuration.
- `ShipPropulsion.cs` — per-engine harmony/directional resolution, shared translational-envelope allocation, installation-oriented force aggregation, hull efficiency, lowest-engine speed ceiling, applied acceleration, snapshots, and diagnostic hover estimates.
- `ShipRotation.cs` — validated box-derived pitch/yaw/roll inertias, torque/inertia angular acceleration, assisted target-rate resolution, and bounded angular-velocity stepping.
- `ShipPresentationBounds.cs` — gameplay-owned composite ship-local bounds from authored hull vertices plus transformed installed engine and cockpit geometry; cached by configuration revision for simulation inertia and presentation framing.
- `SizeClass.cs` — hull class ratings: Small, Medium, Large, Capital.

**Root**

- `FlatHyperspaceConstants.cs` — tunable 2D hyperspace travel: speed, disturbance field, dropout, alignment.
- `FlightConstants.cs` — shared Newtonian/slipstream physics constants: thrust taper, reverse ceiling ratio, station zones, X-stop, and assisted-rate limits; Newtonian harmony endpoints are engine-owned.
- `FlightMode.cs` — enum: Docked, SystemNewtonian, SystemSlipstream, AtmosphericNewtonian, AtmosphericSlipstream, EnteringFlatHyperspace, FlatHyperspace.
- `HyperspacePlane.cs` — 2D hyperspace plane defined by ship up-vector + position; projects stars onto the plane.
- `PlayerInput.cs` — immutable input snapshot (translation, lift-channel selection, normalized assisted rotation commands, toggles), read once per sim tick.
- `Simulation.cs` — 60 Hz background sim loop: clock, environment, physics, power, damage, radar, publish.

---

## Inferior.Rendering

3D rendering utilities and the per-subsystem GPU-mesh owners extracted this session. Depends on Core, Gameplay.

- `Camera3D.cs` — quaternion free-look camera, origin-shift rendering, `RenderScale` constant.
- `CelestialBodyRenderer.cs` — star/planet body+glow+atmosphere drawing, orbit rings, planet-sphere GPU meshes.
- `GeometryBuilder.cs` — face/winding helpers (`AddConvexFace`/`AddFace`), `BuildDynamic` (VertexPositionNormalColorTexture, White baked, ship hull/nacelle/pylon), `BuildBaked` (VertexPositionColor, currently no callers).
- `MeshFactory.cs` — sphere/ring mesh generation.
- `MeshRenderer.cs` — draws over the shared `LitSurface.fx` effect (Content/Effects/LitSurface.fx): `DrawDynamicLit` / `DrawBakedColorLit`, plus station-only shadowed variants for Phase B (`DynamicLitShadowed`, `BakedColorLitShadowed`). DynamicLit callers share explicit specular/shininess, material-map, bump-strength and render-space eye-position binding; the default eye remains `Vector3.Zero` for origin-shifted `Camera3D` passes.
- `RingPrimitive.cs` — shared ring-mesh scratch buffer + draw, used by celestial-body and station orbit rings.
- `SceneLighting.cs` — scene-level directional light parameters (SunDirection/Ambient/SunColour) shared by all 3D passes.
- `ShipMeshRenderer.cs` — owns and draws ship hulls plus installed engine/cockpit child modules through the same DynamicLit material/effect path. Cockpit rendering consumes the simulation-published root pose and definition-owned geometry. Object Designer can pass an in-memory hull override, local render scale and preview eye position, then invalidate the semantic mesh cache after edits.
- `CockpitMeshBuilder.cs` / `CockpitGpuMesh.cs` — validate and upload definition-owned cockpit triangles into material-separated GPU parts.
- `SkyboxRenderer.cs` — starfield background: `Build` (static)/`Load`/`Draw`.
- `Type1HullFactory.cs` — builds the Type-1 ship hull/nacelle/pylon meshes.

---

## Inferior.UI

Self-contained UI framework. Depends on Core only.

**Root**

- `Alignment.cs` — horizontal/vertical text alignment enums.
- `BlinkClock.cs` — global timer driving synchronized LED blink phases across all `LedIndicator`s.
- `Control.cs` — base class for all UI controls: hierarchy, layout, overflow-aware clipping/hit testing, input routing, theme support.
- `FontHelper.cs` — safe font wrappers, sanitizing text before measuring/drawing to prevent crashes.
- `InputState.cs` — immutable per-frame input snapshot (mouse, keyboard, typed chars) with edge detection.
- `OverflowMode.cs` — overflow behavior enum for visible vs clipped control contents.
- `SpritePrimitives.cs` — shared static `DrawText`/`DrawRect`/`DrawRectBorder` helpers.
- `TextFilters.cs` — profanity filter, case-preserving.
- `Thickness.cs` — immutable four-sided layout spacing value.
- `Theme.cs` — visual style config (colours, fonts, geometry); `InferiorDark`/`Light` presets.
- `UIManager.cs` — root UI system: top-level controls, overlay controls/popups, focus, hover, input routing, draw order.
- `UIRenderer.cs` — centralized drawing backend: primitives, buttons, windows, textboxes, clipping, and custom-content SpriteBatch suspension/restoration.
- `UiCustomDrawContext.cs` — graphics-device and clip payload passed to custom UI surface render callbacks.

**Controls/**

- `AnalogueNeedle.cs` — 180° sweep gauge, self-subscribes to DataBus via `Topic`.
- `Button.cs` — clickable button, Space/Enter activation, `Clicked` event.
- `ChoiceGroup.cs` — authoritative mutually-exclusive selection group backed by toggle buttons.
- `CollapsiblePanel.cs` — titled panel that can hide/show arranged child content.
- `EdgePanelHost.cs` — sliding edge-mounted panel with tab strip (left/right/top/bottom).
- `GridPanel.cs` — fixed/auto/star row-column layout container.
- `InstrumentMeter.cs` — horizontal bar meter, animated smoothing, self-subscribes via `Topic`.
- `Label.cs` — non-interactive text label, left/centre/right alignment.
- `LedIndicator.cs` — standalone LED lamp: colour ranges, blinking, easing brightness transitions.
- `MenuControls.cs` — simple menu bar, menu buttons, popup menus, and menu items.
- `Panel.cs` — simple container with optional background/border and content padding.
- `ScrollPanel.cs` — clipped vertical scrolling container.
- `StackPanel.cs` — horizontal/vertical stack layout container.
- `SystemConsole.cs` — scrolling message log, priority-coloured, word-wrap/clip line-break modes.
- `TextBlock.cs` — wrapped multiline read-only text control.
- `TextBox.cs` — full text input: selection, multiline, clipboard, scrolling, swear filtering.
- `ToggleButton.cs` — two-state button with pending/confirmed indicator for async feedback; includes `ExclusiveButtonGroup`.
- `Window.cs` — draggable titled window with close button.

**Controls.Cockpit/**

- `CockpitRail.cs` — bottom-anchored sliding panel: tab strip, side wings, connector LEDs.
- `DirectionBall.cs` — 3D direction-vector sphere; filled/hollow dots for front/rear hemispheres.
- `DockingInstrument.cs` — landing aid encoding lateral offset/height/heading/pitch via circle deformation.
- `HudAlertDisplay.cs` — centre-screen alert overlay for system messages, priority-based auto-dismissal.
- `LandingRadarPanel.cs` — top-down approach radar, relative ship position vs. landing pad.
- `RadarDisplay.cs` — tactical radar: range scales, log mode, exclusion zones, LED status.
- `SpectrumGraph.cs` — smoothed filled-area graph for solar/stellar spectrum (Catmull-Rom interpolation).

---

## Inferior.ObjectDesigner

Standalone MonoGame engineering tool for versioned loose object/ship authoring. Depends on Core, Gameplay, Rendering, UI. Copies the game-built Content output and the loose Beren JSON asset into its output directory.

- `Program.cs` — entry point, runs `ObjectDesignerGame`.
- `DesignerSurfaceControl.cs` — UI-owned 2D/3D editor surface bounds and clipping container.
- `ObjectDesignerGame.cs` — Beren editor shell: menu/toolbar, draw-order-separated 3D render-target preview, active orthographic projection, mutually-exclusive projection/constraint choices, multi-select vertex pick/drag/marquee/pan/zoom, numeric coordinate edits, save/reload, diagnostics/status panels, and reuse of `ShipMeshRenderer`.
- `Content/Content.mgcb` — reference content manifest retained for source clarity; the project currently copies built game content instead of invoking a second content build.

**Editing/**

- `ProjectionKind.cs` — top/side/front orthographic projection enum.
- `OrthographicProjection.cs` — screen/model projection math, projection axes, and axis labels.
- `EditCommands.cs` — `IEditCommand`, single/multi-vertex move commands, and undo/redo clean-state tracking.
- `VertexDragOperation.cs` — immutable drag-start snapshot for grouped vertex translation: selected stable IDs, original positions, active vertex, constraint mode/reference data, mouse start, and fixed active-face plane data for Face mode.
- `ObjectDesignerSession.cs` — loaded document/session owner: multi-selection, active vertex, active face, last-valid preview hull, rebuild, validation, save/reload, incident-face lookup/cycling, overlay data, and stable-ID vertex mutation.

## Inferior.UI.Test

- `BasicControlInteractionTests.cs` — xUnit coverage for command buttons, toggle buttons, topmost text-box hit regions, disabled controls, and clipped input.
- `ClippingHitTestingTests.cs` — xUnit coverage for single/nested clipping, empty intersections, drawing/input clip agreement, and `OverflowMode.Visible` hit policy.
- `InstrumentedCompositionTests.cs` — fake render-context ordering tests for custom-content suspension/resume, following sibling draw calls, nested clip balance, custom failure cleanup, empty custom clips, and overlay ordering.
- `LayoutControlTests.cs` — xUnit coverage for stack/grid arrangement, Object Designer-like region allocation, resize minima, collapsed panel input behavior, and exclusive choice groups.
- `PanelTraversalTests.cs` — xUnit coverage for visible/hidden/empty sibling traversal and nested depth-first draw order.
- `ZOrderHitTestingTests.cs` — xUnit coverage for overlapping sibling/root z-order, hidden/disabled top controls, and deterministic hit order.
- `InstrumentedCompositionTests.cs` — fake render-context ordering tests for custom-content suspension/resume, following sibling draw calls, nested clip balance, and overlay ordering.

## Inferior.ObjectDesigner.Test

- `DesignerSurfaceControlTests.cs` / `ObjectDesignerCompositionFixtureTests.cs` / `ObjectDesignerEditingTests.cs` — xUnit coverage for designer surface layout/clipping invariants, Object Designer-like composition, command history, multi-selection, save blocking, stable IDs, and projection math.

## Inferior.Gameplay.Test

- `BerenAuthoringJsonTests.cs` — xUnit coverage for Beren authoring JSON load/rejection/round-trip, structured diagnostics, and semantic triangulation.

---

## Inferior.Persistence

Pure IO, no live objects. Depends on Core only.

**Data/**

- `CockpitLayoutRecord.cs` — serializable cockpit-layout snapshot (positions, config).
- `ConsumablesRecord.cs` — serializable reactor fuel/coolant/fuel-rod quantities.
- `HullElementStateRecord.cs` — serializable hull-panel integrity/identity by face.
- `InstalledComponentRecord.cs` — serializable mounted-component record (damage, power, settings).
- `InstalledCockpitRecord.cs` — serializable cockpit installation, rotation, and light state.
- `LogEntryRecord.cs` — serializable timestamped ship-log entry with type and hash chain.
- `ShipRecord.cs` — top-level serializable record bundling all persistent ship state.

**Root**

- `IShipLogRepository.cs` — interface: append/retrieve/validate/delete ship logs.
- `IShipRepository.cs` — interface: load/save/delete/list ship records.
- `LocalFileShipLogRepository.cs` — NDJSON log storage, 32KB pagination, SHA-256 hash chaining + validation.
- `LocalFileShipRepository.cs` — JSON file storage for ship records under the career directory.
- `ShipRecordMigrator.cs` — schema-version validation stub for future `ShipRecord` migrations.
- `ShipSummary.cs` — lightweight record exposing ship ID/hull type/name for list views.

---

## Inferior.Game

Entry point; references everything. Depends on Core, Galaxy, Gameplay, Persistence, Rendering, UI.

**Root**

- `InferiorGame.cs` — MonoGame game class: owns the state machine, window mode, simulation lifecycle; global Ctrl+C rising-edge screenshot trigger (captured at end of `Draw()` via `Platform.HostServices`).
- `Program.cs` — entry point, instantiates and runs `InferiorGame`.
- `SpaceSimulation.cs` — sim-thread physics loop for the player ship (extends `Simulation`); owns shared pilot harmony changes, applies per-engine allocated force/current-mass translation and harmony-scaled torque/box-inertia assisted rotation, and publishes immutable propulsion/rotation diagnostics. Owns canonical station relocation and player-hull cycling; cycling preserves angular velocity while explicit pose/velocity-reset relocations clear it.
- `Ships/PlayerShipCycleCatalog.cs` — stable Aries -> Cosmo -> Asterisk -> Beren -> Antega -> Aries order used by the simulation-owned cockpit control.
- `TargetingSystem.cs` — maintains radar contacts, nav target, and hyperspace target for the player.

**States/** — game states + payloads

- `SystemSpaceState.cs` — primary file: fields, ctor, `OnEnter`/`OnExit`/`OnResize`/`Update`/`Draw`/`HandleKeyboard`. In-system 3D flight (all `FlightMode` variants).
- `SystemSpaceState.CalibrationCube.cs` — fixed-position 10m lighting test-card cube near the starter station: six axis-coded face albedos + labels, rails orientation, `DrawDynamicLit`.
- `SystemSpaceState.CelestialBodies.cs` — nearly empty; one Stations-owned texture helper left (`CreateNavGlowTexture`).
- `SystemSpaceState.Containers.cs` — station-placed shipping containers: real `ShippingContainerFactory` geometry, standard rendering path, rails kinematics (`SpawnContainers`/`PlacedContainer`/`DrawContainers`).
- `SystemSpaceState.Helpers.cs` — coordinate math, reference-frame tracking, proximity speed scale, near-clip, `EnterSystem`; starter-station relocation plan (`StarterSystemSelector`-selected station, 500 m stand-off), `SystemMapStationArrivalStandOffMeters` (2 km), and the shared `RailsOrientation` helper (containers + calibration cube).
- `SystemSpaceState.Ship.cs` — spawn/input mapping, bounds-centred third-person camera math, cockpit-layout capture.
- `ChaseCameraState.cs` — persistent chase/orbital direction, radius, and roll; enforces a generic minimum framing radius from snapshot-published composite ship bounds.
- `SystemSpaceState.Shadows.cs` — station shadow-map owner: 2048² StationMap render target, CullNone caster pass, fitted station-local light camera, F6/F7/F8/F9 diagnostics. Phase C: separate `_decoCasterMeshes` per module (one extra draw, hull caster unchanged) composed from `StationModuleMesh.DecorClassRanges` filtered by the current `CasterStage`; Ctrl+F6 cycles `CasterStage { HullOnly, PlusC1, PlusC2, PlusC3, AllClasses }`, defaulting to whatever `StationDecorator.DecorCastingPolicy` already has landed. `FitStationShadowLight` fits from `_shadowCasterHullBounds` ∪ `_shadowCasterDecoBounds` (module-local AABBs precomputed once at caster-build time from real caster vertex data via `StationModuleMesh.ComputeFaceRangeBounds`/`ComputeIndexRangeBounds`), not `Definition.BoundingBox`. `TryGetMeshFactoryHullFaceRange` (internal, pure, no GraphicsDevice) is the single decision for which face range a MeshFactory module's hull caster uses — generalized off any category special-case after a bug where non-docking-bay MeshFactory modules (octagonal blocks) got no hull caster while their decoration still cast (floating shadows); a post-composition safety net warns via SystemMessage if any module ends up with no hull caster at all.
- `SystemSpaceState.Skybox.cs` — star hover/click hyperspace-target selection (rendering itself lives in `SkyboxRenderer`).
- `SystemSpaceState.Stations.cs` — station mesh/glow/dot drawing (next extraction candidate — see current-state doc).
- `SystemSpaceState.Targeting.cs` — `FeedRadarContacts`/`UpdatePadTargetPosition` (world state → targeting system).
- `CockpitLayout.cs` — snapshot of open cockpit panels + active tabs, persisted across state transitions.
- `GalaxyMapPayload.cs` — return-to-flight payload for `GalaxyMapState` (star, time, spawn pos/orient).
- `GalaxyMapState.cs` — top-level galaxy map: camera, star selection, search, jump targeting; first-entry default system comes from `StarterSystemSelector.SelectStar`.
- `SystemMapPayload.cs` — transition payload from `SystemSpaceState` to `SystemMapState`.
- `SystemMapState.cs` — 2D orbital map of a star system: zoom/pan, time compression, nav selection.
- `SystemSpacePayload.cs` — entry payload for `SystemSpaceState` (spawn location, nav targets).

**Input/**

- `StationCycleController.cs` — debug station-cycle (Ctrl+F12 rising edge): orders system stations, issues relocation requests by `PersistenceId` via the canonical relocation path.
- `StationCyclePlatformInput.cs` — platform chord detection helper for the cycle control.

**Platform/**

- `HostServices.cs` — host-system (OS) concerns kept out of game/simulation code; `SaveScreenshot(GraphicsDevice)` reads the backbuffer on the render path, backgrounds the PNG encode/file write to `Screenshots/`.

**Hyperspace/**

- `FlatHyperspaceController.cs` — owns flat-hyperspace flight: preamble alignment, travel, drop-out, overlay.
- `GridHyperspaceSheetRenderer.cs` — draws the two hyperspace corridor-wall sheets.
- `IHyperspaceSheetRenderer.cs` — sheet-renderer interface + `PlaneBasis` struct.

**UI/**

- `CockpitUI.cs` — fields, constructor (full instrument/panel/rail construction), `Dispose`, lifecycle, subscriptions, and the CTRL-panel `NEXT SHIP` command button.
- `CockpitUI.DirectionBalls.cs` — direction-ball updates, radar-contact notify.
- `CockpitUI.Hud.cs` — 2D HUD drawing (`DrawHud`, atmo panel, projected ship-forward reticle, UI-tree/alert draw).
- `ShipForwardReticleProjector.cs` — pure camera-originated ship-forward ray projection with viewport-edge clamping; independent of velocity and ship-centre convergence.
- `CockpitUI.Targeting.cs` — targeting HUD drawing, radar/landing-radar/targeting-dirball updates.
- `DriveInstrumentPanel.cs` — right cockpit-rail wing: drive mode, Newtonian engine harmony or slipstream harmonic, and speed readouts.

**ShipBuilder/**

- `ShipBuilder.cs` — fluent builder for constructing `Ship` from `ShipRecord`; resolves hull-authored cockpit and engine defaults, installing each configured engine mount independently (other component/persistence mapping remains mostly stubbed).
- `ShipExtensions.cs` — mapping between the `Ship` domain object and `ShipRecord` persistence.
- `ShipPersistenceService.cs` — async load/save bridge between `Ship` and `IShipRepository`.

**Containers/**

- `CommodityType.cs` — enum of cargo types (Food, Electronics, Fuel, Weapons, Contraband).
- `ShippingContainer.cs` — real container domain model: colour, wear, lock, location, pre-built textured mesh.
- `ShippingContainerFactory.cs` — deterministic container mesh builder with wear + text overlay; used both by standalone/debug-spawn containers and station-placed ones (`SystemSpaceState.Containers.cs`, `StationDecorator.PlaceContainer`).

**Station/** — procedural station generation

- `BitmapFonts.cs` — 5×7 pixel bitmap font glyphs (A–Z, 0–9, space, hyphen, plus) with lit-pixel queries.
- `PlacedModule.cs` — a placed station module: transform, decoration meshes, lights, ports.
- `StationArchetypes.cs` — port-scoring/category-biasing growth strategies (cluster, linear-spine, hub-spoke).
- `StationCableGenerator.cs` — routes cable bundles between greeble connectors on module faces.
- `StationDecorator.cs` — adds per-module decoration (windows, hatches, antennas, dishes, lights, pipes). Tags `mesh.CurrentDecorClass` before each pass call (Phase C); `DecorCastingPolicy` is the static `DecorClass → bool` casting-policy table (with `C1Classes`.. `C4Classes` rollout groupings), the executable form of `Docs/station-lighting-pipeline-spec.md`'s documented casting policy.
- `StationGenerator.cs` — builds stations by port-to-port module attachment, collision detection, landing pads.
- `StationModuleDefinition.cs` — hull definition for a module: bounding box, category, ports, mesh factory, weight.
- `StationModuleMesh.cs` — CPU-side mesh accumulator for quads/triangles in local module space; can build a face range for docking-bay hull-only shadow casting. Phase C: `DecorClass` enum + `CurrentDecorClass`/`DecorClassRanges`; every index-appending call records its range via `RecordDecorClassRange`; `BuildIndexRanges` composes a remapped (vb, ib, triCount) from an arbitrary set of index ranges (used to build the per-module deco shadow caster from casting-enabled classes); `ComputeFaceRangeBounds`/`ComputeIndexRangeBounds` return module-local AABBs (no GPU buffers) over the same face/index-range selections, used by `FitStationShadowLight`'s C3 fit extension.
- `StationModuleRegistry.cs` — registry of all module types (hab, cargo, docking, science, connector).
- `StationPort.cs` — attachment point on a module: size, category filters, terminal/docking flags.
- `StationProfile.cs` — generated station attributes: economy, age, wealth, population.
- `StationTextureRegistry.cs` — procedural panel texture generation + caching by palette.
- `StationYagiAntenna.cs` — Yagi antenna element builder: randomized geometry and placement.
- `SurfaceTexture.cs` — enum of surface types (CleanPanel, TechPanel, Glass, etc.).
- `TexturePainter.cs` — CPU pixel-buffer text drawing using `BitmapFonts`.
- `TexturePalette.cs` — per-economy colour scheme (base/accent/grime, panel noise/contrast).

**Station/Megastations/** — occupancy-generated megastation prototype path

- `ConnectivityValidation.cs` — GraphicsDevice-free validation of occupied-volume connected components and sealed empty cavities.
- `BoundaryMeshValidation.cs` — CPU-side boundary mesh validation for finite vertices, degenerates, duplicate triangles, and manifold edge incidence.
- `BoundaryTopology.cs` — exact-grid boundary face/edge/vertex topology classification and conservative chamfer eligibility.
- `BoundaryTopologySignature.cs` — canonical semantic SHA-256 signature for boundary topology and chamfer eligibility.
- `CornerRegionGenerator.cs` — plans and applies eight shared corner-region masses around the structural core.
- `EdgeRegionGenerator.cs` — plans and applies twelve shared edge-region profiles plus face-region support shoulders.
- `ExteriorSpace.cs` — flood-fills externally accessible empty cells and tests exposed structural faces.
- `MegastationMassingSignature.cs` — canonical SHA-256 signatures for accepted megastation occupancy/massing regression fixtures.
- `MegastationPrototypeGenerator.cs` — CPU/GPU entry point for megastation generation, diagnostics, and single-module station-model wrapping.
- `MegastationPrototypeMeshBuilder.cs` — consumes regularised occupancy boundary topology, validates sharp/final meshes, and emits the current render mesh with debug colour modes.
- `MegastationPrototypeSettings.cs` — generation settings, development-selection source, and generator/seed compatibility version declarations.
- `MegastationSeed.cs` — stable semantic FNV-style seed derivation for megastation subsystems.
- `RegionPlans.cs` — stable region identities and edge/corner plan records.
- `SliceGrid.cs` — deterministic non-uniform rectilinear grid, core ranges, exterior layers, and cell coordinate helpers.
- `StructuralOccupancy.cs` — compact per-cell occupancy flags, owner metadata, and stable region ids.
- `StructuralVolumeGenerator.cs` — fills the current cuboid structural core occupancy baseline.
- `SurfacePatch.cs` — exposed-face patch records and patch-local coordinate discovery.
- `UrbanGrowth.cs` — monotonic face-interior district/depth-map growth for each major surface patch.
- `UrbanStyle.cs` — station-wide style tendencies and deterministic per-face settings modifiers.

---

## Inferior.Game.Test

- `MegastationPrototypeTests.cs` — xUnit coverage for the occupancy-generated megastation prototype: slice grid, exterior flood fill, face/edge/corner ownership, connectivity, massing signatures, version/seed compatibility, and mesh sanity.
- `ShipRecordContainmentTests.cs` — xUnit test enforcing that `ShipRecord` only appears in `ShipBuilder`/`ShipExtensions`/`ShipPersistenceService`.
