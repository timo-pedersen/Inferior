The system has solid foundations, builds cleanly, and is partially playable—but the documented “sensors/modules → buses → instruments” architecture is only partly connected.

Debug and Release builds succeed with zero warnings. All 649 tests pass. No files were changed.

## Current runtime system

### SensorData

Implemented providers:

- `Environment`: gravity, atmosphere queries, nearest bodies, stellar temperature, solar spectrum, heat and UV calculations.
- `GravityCalculations`: pure multi-body gravity calculation.
- `EMP`: detonation tracking and decay.

Important gaps:

- Magnetic field and radiation currently always return zero in [SimWorld.cs](E:/Git/GitInferior-instrumentation/Inferior.Gameplay/Physics/SimWorld.cs:47).
- `Environment.ExternalPressure` is a constant zero; the newer atmospheric-pressure calculation is separate.
- Solar heat and UV are implemented but nothing consumes them.
- EMP is not connected to heat-sink saturation or any sensor noise source.
- Sensor state is global to `SpaceSimulation`, not installed per ship or component slot.

### Sensors

| Sensor | Runtime status | Publication |
|---|---|---|
| Gravity | Installed and ticked | Every simulation tick |
| Atmospheric pressure | Installed and ticked | 4 Hz, silent in vacuum |
| Solar spectrum | Installed, command-triggered | Result after a two-second scan |
| Planetary coordinates | Installed | Every atmospheric physics tick |
| Landing support | Installed | Every tick while targeting a pad |
| External temperature | Class exists, not instantiated | None |
| External pressure | Class exists, not instantiated; source is stub zero | None |
| Radiation | Class exists, not instantiated; source is stub zero | None |
| Magnetic field | Class exists, not instantiated; source is stub zero | None |

The five runtime sensors are hard-coded fields in [SpaceSimulation.cs](E:/Git/GitInferior-instrumentation/Inferior.Game/SpaceSimulation.cs:353).

One visible defect: the `ATM SCAN` button sends `AtmosphericSensor.Scan`, but `AtmosphericPressureSensor` has no command subscription. The sensor is passive and already publishes at 4 Hz, so the button currently does nothing.

### Modules/components

The default ship installs seven `ShipComponent`s in [ShipBuilder.cs](E:/Git/GitInferior-instrumentation/Inferior.Game/ShipBuilder/ShipBuilder.cs:114):

- Reactor
- Main power bus
- Power priority manager
- Shield
- Shield connector
- Hyperspace heat sink
- Coolant system

These publish useful reactor, bus, connector, shield, coolant, and heat-sink telemetry.

Classes that exist but are not installed:

- `EngineComponent`
- `GyroComponent`
- `ArtificialGravityComponent`
- `ConverterComponent`
- `LifeSupportComponent`
- `FlyabilityMonitor`
- `ExhaustSystem`
- Internal lights
- External lights

There are also two parallel engine concepts. Real flight uses installed `EngineInstance`s, while the power-aware, heat-producing, command-bus-aware `EngineComponent` is unused. Consequently the engines that propel the current ships consume no simulated power and produce no component heat.

The cockpit similarly has physical runtime state and command-bus light control, but it is not a `ShipComponent` and publishes no instrument/status confirmation.

## Bus status

| Bus | Current use |
|---|---|
| `System` | Heavily used; feeds console and HUD alerts |
| `Instruments` | Heavily used by power, flight, and active sensors |
| `InstrumentRanges` | Component sensors publish ranges; nothing subscribes |
| `InstrumentState` | Declared but unused |
| `Spectra` | Working for solar scans |
| `Radar` / `RadarLost` | Subscriptions exist, but current radar does not publish through them |
| `Target` | Main-thread targeting publishes; nothing subscribes |

The bus is a simple unbounded queue with exact-topic subscriptions and no retention. That means:

- New subscribers receive nothing until a publisher posts again.
- State, events, and telemetry all use the same retention model.
- Slow consumers can allow queues to grow without a bound.
- On-demand results such as a spectrum are lost to instruments created after the scan.

Subscription ownership also needs work. `ComponentSensor`, reactor, engine, solar-spectrum sensor, `DockingInstrument`, and `DriveInstrumentPanel` retain subscriptions without disposing them. Ship cycling and repeated entry into `SystemSpaceState` can therefore leave old handlers registered. `UIManager.Dispose()` disposes graphics resources, not control-owned subscriptions.

## HUD paths that bypass the buses

Yes, several do.

Definite bypasses:

- Radar contacts are constructed on the main/render thread and pushed directly into `TargetingSystem` and cockpit controls in [SystemSpaceState.Targeting.cs](E:/Git/GitInferior-instrumentation/Inferior.Game/States/SystemSpaceState.Targeting.cs:34). `Simulation.TickRadar()` has no implementation.
- The shield button calls `SpaceSimulation.RequestSetShieldPower`, using an atomic request field rather than `CommandBus`, in [SpaceSimulation.cs](E:/Git/GitInferior-instrumentation/Inferior.Game/SpaceSimulation.cs:184).
- Docking target geometry is initially computed on the main thread, published directly to `Instruments`, and separately copied into `LandingPadData` for the simulation.
- The main HUD reads speed, flight mode, harmony, propulsion, rotation, and reference velocity directly from `ShipSnapshot` in [CockpitUI.Hud.cs](E:/Git/GitInferior-instrumentation/Inferior.Game/UI/CockpitUI.Hud.cs:31), even though several of these values are also published on `DataBus`.
- Radar orientation, local-frame speed, nav targets, landing radar, and target brackets are updated directly from snapshots and `TargetingSystem`.

Not every direct path is undesirable. Camera projection, reticle placement, and converting a known world direction into screen coordinates are presentation work and should remain snapshot-driven. The problematic cases are physical measurements and module commands that bypass an existing sensor/bus contract.

## Thermal and power concerns

The happy-path reactor → bus → connector → shield chain works, but several pieces remain provisional:

- `PowerBus.MaxPowerPerConnection` and `MaxConnections` are not enforced.
- `PowerPriorityManager` has no command-bus priority editing or telemetry despite the documentation.
- Components are initially marked powered and notified that power is available during installation, independent of an actual working connection.
- Shield is controlled outside the command bus.
- Engine, gyro, and artificial-gravity power consumption are not connected.

There is also a thermal conservation bug: `CoolantSystem` removes heat from every registered node, then caps only the amount passed into the heat sink. Any excess above `HeatSink.TransferRate` disappears in [CoolantSystem.cs](E:/Git/GitInferior-instrumentation/Inferior.Gameplay/Components/CoolantSystem.cs:69). This conflicts with the project’s explicit energy-conservation invariant.

Heat-sink saturation resets stored heat and posts a warning, but does not trigger `SensorData.EMP`.

## UI system status

The generic framework itself is substantial and healthy:

- Retained control tree
- Focus, hover, input routing, z-order
- Clipping and overflow
- Grid, stack, scroll, and collapsible layouts
- Menus/popups
- Text editing
- Themes
- Custom draw composition
- 41 passing UI tests, including rendered-output and clipping tests

It is not yet independently extractable:

- `Inferior.UI` references `Inferior.Core`.
- Generic-looking controls such as `InstrumentMeter`, `AnalogueNeedle`, `LedIndicator`, and `SpectrumGraph` subscribe directly to global Inferior buses.
- `SystemConsole`, `HudAlertDisplay`, and `RadarDisplay` use Inferior-specific domain types.
- The documentation describes an `IUIRenderer`; the implementation has a concrete sealed `UIRenderer`.
- Several cockpit controls hard-code topics and colours.
- Cockpit composition uses many fixed pixel sizes, so responsiveness and high-DPI behaviour are limited.
- There is no general control attach/detach/disposal lifecycle.

This is code- and test-verified, not visually verified in-engine.

## Recommended project split

Keep `Inferior.UI` as the truly generic MonoGame UI library:

- Core control tree, manager, renderer, input, layout, theme
- Buttons, labels, text, panels, windows, menus
- Value-driven `InstrumentMeter`, `AnalogueNeedle`, `LedIndicator`
- Value-driven `SpectrumGraph` and `DirectionBall`
- No `Inferior.Core` reference and no global bus subscriptions

Add an `Inferior.Instruments` project depending on `Inferior.UI`, `Inferior.Core`, and where necessary `Inferior.Gameplay`:

- Bus binding and subscription ownership
- `SystemConsole`/alerts
- Radar and landing controls
- Docking instrument
- Cockpit rail styling
- Drive panel
- Inferior topics, units, colours, and message types

`CockpitUI` can remain in `Inferior.Game` as the composition root because it currently depends on `SpaceSimulation`, `TargetingSystem`, camera state, and game-state layout.

My recommended first implementation slice is: fix subscription ownership and the dead atmospheric command, add bus/thermal characterization tests, then perform the no-visual-change UI project split. After that, radar and module command routing can be moved onto their intended buses without mixing architectural repair with visual redesign.
