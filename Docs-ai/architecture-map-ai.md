# Inferior — Architecture Map

> A flat table of contents to the codebase: one line per file, what it is, nothing more.
> Distinct from `!current-state.md`, which tracks *why* things changed and what's
> in progress — this doc only answers "where do I look for X?" Regenerate wholesale when
> it drifts noticeably from `git ls-files`; don't hand-patch it per brief.

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

- `HullDefinition.cs` — immutable hull-class template: slots, mass, cockpit offset, size, aerodynamics.
- `HullDefinitionLibrary.cs` — static registry of hull definitions, looked up by stable `HullTypeId`.
- `HullSlot.cs` — single component-install location with category restriction and max-class soft cap.
- `SlotCategory.cs` — enum restricting component types per slot (Reactor, Engine, Shield, Sensor, etc.).

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

- `Ship.cs` — the unique physical object in the universe: position, velocity, orientation, components, config.
- `SizeClass.cs` — hull class ratings: Small, Medium, Large, Capital.

**Root**

- `FlatHyperspaceConstants.cs` — tunable 2D hyperspace travel: speed, disturbance field, dropout, alignment.
- `FlightConstants.cs` — tunable Newtonian/slipstream physics: gear speeds, thrust taper, station zones, X-stop.
- `FlightMode.cs` — enum: Docked, SystemNewtonian, SystemSlipstream, AtmosphericNewtonian, AtmosphericSlipstream, EnteringFlatHyperspace, FlatHyperspace.
- `HyperspacePlane.cs` — 2D hyperspace plane defined by ship up-vector + position; projects stars onto the plane.
- `PlayerInput.cs` — immutable input snapshot (thrust, rotation, toggles), read once per sim tick.
- `Simulation.cs` — 60 Hz background sim loop: clock, environment, physics, power, damage, radar, publish.

---

## Inferior.Rendering

3D rendering utilities and the per-subsystem GPU-mesh owners extracted this session. Depends on Core, Gameplay.

- `Camera3D.cs` — quaternion free-look camera, origin-shift rendering, `RenderScale` constant.
- `CelestialBodyRenderer.cs` — star/planet body+glow+atmosphere drawing, orbit rings, planet-sphere GPU meshes.
- `GeometryBuilder.cs` — face/winding helpers (`AddConvexFace`/`AddFace`), `BuildDynamic` (VertexPositionNormalColorTexture, White baked, ship hull/nacelle/pylon), `BuildBaked` (VertexPositionColor, currently no callers).
- `MeshFactory.cs` — sphere/ring mesh generation.
- `MeshRenderer.cs` — draws over the shared `LitSurface.fx` effect (Content/Effects/LitSurface.fx): `DrawDynamicLit` (DynamicLit technique — ships, containers, station hull) / `DrawBakedColorLit` (BakedColorLit technique — station decoration; vertex alpha is the self-illumination floor S).
- `RingPrimitive.cs` — shared ring-mesh scratch buffer + draw, used by celestial-body and station orbit rings.
- `SceneLighting.cs` — scene-level directional light parameters (SunDirection/Ambient/SunColour) shared by all 3D passes.
- `ShipMeshRenderer.cs` — owns and draws the ship hull/nacelle/pylon meshes (built via `Type1HullFactory`).
- `SkyboxRenderer.cs` — starfield background: `Build` (static)/`Load`/`Draw`.
- `Type1HullFactory.cs` — builds the Type-1 ship hull/nacelle/pylon meshes.

---

## Inferior.UI

Self-contained UI framework. Depends on Core only.

**Root**

- `Alignment.cs` — horizontal/vertical text alignment enums.
- `BlinkClock.cs` — global timer driving synchronized LED blink phases across all `LedIndicator`s.
- `Control.cs` — base class for all UI controls: hierarchy, layout, input routing, theme support.
- `FontHelper.cs` — safe font wrappers, sanitizing text before measuring/drawing to prevent crashes.
- `InputState.cs` — immutable per-frame input snapshot (mouse, keyboard, typed chars) with edge detection.
- `SpritePrimitives.cs` — shared static `DrawText`/`DrawRect`/`DrawRectBorder` helpers.
- `TextFilters.cs` — profanity filter, case-preserving.
- `Theme.cs` — visual style config (colours, fonts, geometry); `InferiorDark`/`Light` presets.
- `UIManager.cs` — root UI system: top-level controls, focus, hover, input routing, draw order.
- `UIRenderer.cs` — centralized drawing backend: primitives, buttons, windows, textboxes, clipping.

**Controls/**

- `AnalogueNeedle.cs` — 180° sweep gauge, self-subscribes to DataBus via `Topic`.
- `Button.cs` — clickable button, Space/Enter activation, `Clicked` event.
- `EdgePanelHost.cs` — sliding edge-mounted panel with tab strip (left/right/top/bottom).
- `InstrumentMeter.cs` — horizontal bar meter, animated smoothing, self-subscribes via `Topic`.
- `Label.cs` — non-interactive text label, left/centre/right alignment.
- `LedIndicator.cs` — standalone LED lamp: colour ranges, blinking, easing brightness transitions.
- `Panel.cs` — simple container with optional background/border and content padding.
- `SystemConsole.cs` — scrolling message log, priority-coloured, word-wrap/clip line-break modes.
- `TextBox.cs` — full text input: selection, multiline, clipboard, scrolling, swear filtering.
- `ToggleButton.cs` — two-state button with pending/confirmed indicator for async feedback.
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

## Inferior.Persistence

Pure IO, no live objects. Depends on Core only.

**Data/**

- `CockpitLayoutRecord.cs` — serializable cockpit-layout snapshot (positions, config).
- `ConsumablesRecord.cs` — serializable reactor fuel/coolant/fuel-rod quantities.
- `HullElementStateRecord.cs` — serializable hull-panel integrity/identity by face.
- `InstalledComponentRecord.cs` — serializable mounted-component record (damage, power, settings).
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

- `InferiorGame.cs` — MonoGame game class: owns the state machine, window mode, simulation lifecycle.
- `Program.cs` — entry point, instantiates and runs `InferiorGame`.
- `SpaceSimulation.cs` — sim-thread physics loop for the player ship (extends `Simulation`); publishes `ShipSnapshot` to DataBus each tick. Owns canonical station relocation: `RequestStationRelocation(persistenceId, standOffMeters)` — resolves live station position, applies stand-off, matches reference-frame velocity, faces the station. Used by new-game start, system-map arrival, and debug station cycle.
- `TargetingSystem.cs` — maintains radar contacts, nav target, and hyperspace target for the player.

**States/** — game states + payloads

- `SystemSpaceState.cs` — primary file: fields, ctor, `OnEnter`/`OnExit`/`OnResize`/`Update`/`Draw`/`HandleKeyboard`. In-system 3D flight (all `FlightMode` variants).
- `SystemSpaceState.CelestialBodies.cs` — nearly empty; one Stations-owned texture helper left (`CreateNavGlowTexture`).
- `SystemSpaceState.DebugContainers.cs` — debug radar-test container spawn/draw (flat colour, no texture — see current-state doc).
- `SystemSpaceState.Helpers.cs` — coordinate math, reference-frame tracking, proximity speed scale, near-clip, `EnterSystem`; starter-station relocation plan (`Far Station`, 500 m stand-off) and `SystemMapStationArrivalStandOffMeters` (2 km).
- `SystemSpaceState.Ship.cs` — spawn/input mapping, third-person camera math, cockpit-layout capture.
- `SystemSpaceState.Skybox.cs` — star hover/click hyperspace-target selection (rendering itself lives in `SkyboxRenderer`).
- `SystemSpaceState.Stations.cs` — station mesh/glow/dot drawing (next extraction candidate — see current-state doc).
- `SystemSpaceState.Targeting.cs` — `FeedRadarContacts`/`UpdatePadTargetPosition` (world state → targeting system).
- `CockpitLayout.cs` — snapshot of open cockpit panels + active tabs, persisted across state transitions.
- `GalaxyMapPayload.cs` — return-to-flight payload for `GalaxyMapState` (star, time, spawn pos/orient).
- `GalaxyMapState.cs` — top-level galaxy map: camera, star selection, search, jump targeting.
- `SystemMapPayload.cs` — transition payload from `SystemSpaceState` to `SystemMapState`.
- `SystemMapState.cs` — 2D orbital map of a star system: zoom/pan, time compression, nav selection.
- `SystemSpacePayload.cs` — entry payload for `SystemSpaceState` (spawn location, nav targets).

**Input/**

- `StationCycleController.cs` — debug station-cycle (Ctrl+F12 rising edge): orders system stations, issues relocation requests by `PersistenceId` via the canonical relocation path.
- `StationCyclePlatformInput.cs` — platform chord detection helper for the cycle control.

**Hyperspace/**

- `FlatHyperspaceController.cs` — owns flat-hyperspace flight: preamble alignment, travel, drop-out, overlay.
- `GridHyperspaceSheetRenderer.cs` — draws the two hyperspace corridor-wall sheets.
- `IHyperspaceSheetRenderer.cs` — sheet-renderer interface + `PlaneBasis` struct.

**UI/**

- `CockpitUI.cs` — fields, constructor (full instrument/panel/rail construction), `Dispose`, lifecycle, subscriptions.
- `CockpitUI.DirectionBalls.cs` — direction-ball updates, radar-contact notify.
- `CockpitUI.Hud.cs` — 2D HUD drawing (`DrawHud`, atmo panel, crosshair, UI-tree/alert draw).
- `CockpitUI.Targeting.cs` — targeting HUD drawing, radar/landing-radar/targeting-dirball updates.
- `DriveInstrumentPanel.cs` — right cockpit-rail wing: drive mode, gear/harmonic, speed readouts.

**ShipBuilder/**

- `ShipBuilder.cs` — fluent builder for constructing `Ship` from `ShipRecord` (mostly stubbed — see current-state doc).
- `ShipExtensions.cs` — mapping between the `Ship` domain object and `ShipRecord` persistence.
- `ShipPersistenceService.cs` — async load/save bridge between `Ship` and `IShipRepository`.

**Containers/**

- `CommodityType.cs` — enum of cargo types (Food, Electronics, Fuel, Weapons, Contraband).
- `ShippingContainer.cs` — real container domain model: colour, wear, lock, location, pre-built textured mesh.
- `ShippingContainerFactory.cs` — deterministic container mesh builder with wear + text overlay (not yet wired to the debug containers in `SystemSpaceState.DebugContainers.cs`).

**Station/** — procedural station generation

- `BitmapFonts.cs` — 5×7 pixel bitmap font glyphs (A–Z, 0–9, space, hyphen) with lit-pixel queries.
- `PlacedModule.cs` — a placed station module: transform, decoration meshes, lights, ports.
- `StationArchetypes.cs` — port-scoring/category-biasing growth strategies (cluster, linear-spine, hub-spoke).
- `StationCableGenerator.cs` — routes cable bundles between greeble connectors on module faces.
- `StationDecorator.cs` — adds per-module decoration (windows, hatches, antennas, dishes, lights, pipes).
- `StationGenerator.cs` — builds stations by port-to-port module attachment, collision detection, landing pads.
- `StationModuleDefinition.cs` — hull definition for a module: bounding box, category, ports, mesh factory, weight.
- `StationModuleMesh.cs` — CPU-side mesh accumulator for quads/triangles in local module space.
- `StationModuleRegistry.cs` — registry of all module types (hab, cargo, docking, science, connector).
- `StationPort.cs` — attachment point on a module: size, category filters, terminal/docking flags.
- `StationProfile.cs` — generated station attributes: economy, age, wealth, population.
- `StationTextureRegistry.cs` — procedural panel texture generation + caching by palette.
- `StationYagiAntenna.cs` — Yagi antenna element builder: randomized geometry and placement.
- `SurfaceTexture.cs` — enum of surface types (CleanPanel, TechPanel, Glass, etc.).
- `TexturePainter.cs` — CPU pixel-buffer text drawing using `BitmapFonts`.
- `TexturePalette.cs` — per-economy colour scheme (base/accent/grime, panel noise/contrast).

---

## Inferior.Game.Test

- `ShipRecordContainmentTests.cs` — xUnit test enforcing that `ShipRecord` only appears in `ShipBuilder`/`ShipExtensions`/`ShipPersistenceService`.
