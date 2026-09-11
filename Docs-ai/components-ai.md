# Inferior — Ship Components and Sensors

> Compressed reference for AI. Full version in Docs/inferior-components-and-sensors.md.
> All components report to message buses and listen to the command bus.

---

## Shared component properties (all components)

| Property | Type | Notes |
|---|---|---|
| Status | enum | `Stopped → PowerOn → Initializing → Started` |
| StartupTimer | double | Seconds from power-on to `Started`; 0 = instant |
| InputCapacitor | double (J) | Local energy buffer; absorbs brief supply interruptions silently |
| PowerConsumption | double (W) | Nominal peak draw; used for FlyabilityMonitor overspec checks |
| Efficiency | double | 0.0–1.0 |
| Damage | double | 0.0 = pristine, 1.0 = destroyed |
| HeatCapacity | double (J/K) | Local thermal mass |

## Ship-owned engineering topology

- `Ship.SystemsTopology` is authoritative for installed-device occupancy, empty hull slots,
  fixed devices, stable power-bus ports, and directed functional power connections.
- Hull slots exist in the topology whether occupied or empty. Optional slots therefore
  appear only on hulls that define them; for example, a hull without a gyro slot does not
  acquire one from the UI.
- Replaceability is a node capability and applies generally to components and sensors. It is
  not a closed type allowlist. The ship computer is the current explicit non-replaceable
  component.
- Power buses, connectors, converters, artificial gravity, sensors, and the flight recorder
  are ordinary topology components when installed. Some sensors require a power connection;
  others do not.
- The ship publishes an immutable retained topology snapshot for presentation. Live power,
  capacitor, heat, coolant, heat-sink, and device-state values remain separate retained
  telemetry/state topics.
- The engineering UI subscribes only while open. Power on/off requests are its one current
  control action: requests use `CommandBus`, and returned `DeviceState` confirms the result.
- Heat transport lines are not modeled. Show component heat and shared coolant/heat-sink
  values without inventing a thermal network.
- Cockpit/control panels are deliberately deferred to a separate panel-management view.

---

## Power core

- `MaxPowerGeneration` — peak output (**watts**)
- Consumable: Reactor fuel

## Power bus

Wire-gauge throughput limits (watts) are distinct from the internal `PowerCapacitor` buffer (joules). A bus can have a large buffer but a narrow wire gauge.

- `MaxPower` — total throughput ceiling (**watts**)
- `MaxPowerPerConnection` — per-connector ceiling (**watts**)
- `MaxConnections` — maximum attached consumers (count)

## Connectors

- `FromBus`, `ToComponent`
- `MaxPower` — throughput ceiling (**watts**)

## Converters

- `FromBus`, `ToComponent`
- `MaxPower` — throughput ceiling (**watts**)
- `PowerOutType`

## Engine(s)

Consumes power from the main bus. Provides thrust forward/backward and downward (for landing/pitch). Provides torque for pitch/yaw/roll. Certain engines can produce **Alpha Red** when paired with a second engine, which drives a gyro for extra torque.

- `MaxPower` — peak draw from main bus (**watts**)
- `MaxDownThrust` — maximum downward thrust (**newtons**; larger = usable on high-gravity planets)
- `MaxThrust` — max forward/reverse thrust; derived from MaxPower and fuel; read-only (**newtons**)
- `AlphaRedPower` — Alpha Red output when paired (**watts**)
- Consumable: Metal rods

## Artificial Gravity

Provides artificial gravity and inertial dampening. Affects flight and hyperspace. Requires converter from main bus (H-sw sub-band). **No off switch** — always-on in practice. Its `InputCapacitor` absorbs brief interruptions silently. Sustained bus failure will eventually drop gravity (minor consequence, no damage mechanic). Registered at `Critical` priority with `PowerPriorityManager`.

- `Power` — steady draw (**watts**)

## Gyro

Optional. Requires Alpha Red from engines.

- `CarbonCrystalType` — graphite crystal, carbon fiber, or diamond
- `CarbonCrystalQualityGrade` — A, B, or C
- `AddedTorqueFactor` — calculated from ship mass, crystal type, and quality; **double**; formula TBD

## Shields

Two per ship normally (top + bottom umbrella), equippable separately with different stats. Has an internal **shield capacitor** that drives damage deflection. Startup requires the capacitor to reach full charge — then starts like a fluorescent light (low ongoing power). Hitting a shield drains the capacitor; ground contact drains it almost instantly; atmospheric flight drains it faster than vacuum.

Connected via converter.

- `EnergyType` — V-Alpha or V-Theta sub-band (Theta is superior)
- `MaxPower` — peak draw from converter (**watts**); set at manufacture for a given Radius; scales with ShieldArea
- `InternalShieldCapacitor` — energy stored in shield buffer (**joules**)
- `Radius` — shield radius (**metres**); the player-facing stat
- `ShieldArea` — calculated: `π × Radius²` (**m²**); power consumption scales with this
- `Deflection` — fraction of incoming damage absorbed; calculated from MaxPower and ShieldArea (**0.0–1.0**)

**Shield startup sequence:**
1. Generator started
2. Shield capacitor charges from generator → system bus messages at intervals
3. Capacitor reaches full charge → shield fires up (small sonic "boom", visual effect)
4. Capacitor drains momentarily on startup
5. Shield begins building charge → system bus messages at intervals

---

## Battery-backed components (no bus power, no heat management)

These run on an infinite internal battery — outside the power simulation.

### FlyabilityMonitor
Reports to system bus. Runs periodic checks. Built-in checks: individual overspec, aggregate overspec, bus underrated. All advisory — ship can still launch with warnings.

### Life support
Internal pressure, temperature, oxygen. Always on, lasts weeks or months. Reports to system bus.

### Power Priority Manager
Controls power distribution from bus to components. Internal priority list. Reports to bus(es). Listens to CommandBus.

### Ship illumination (internal + external)
Always available when installed.

### Doors
Always working. Not currently planned.

### Ship cockpit and control panels
Always available. On/off switch for ship (turns off reactor).

### HyperspaceHeatSink

Central thermal mass and heat disposal. Dumps heat into a separate hyperspace plane rather than radiating into realspace (which would produce a large detectable thermal signature).

Heat flow:
1. Components heat up locally in their thermal masses
2. Coolant transports heat → HyperspaceHeatSink (rate per component capped by `HeatFlowPerComponent`; efficiency scales with coolant level)
3. Sink dissipates to hyperspace proportionally: `dissipation = HeatDissipation × (StoredHeatJ / CapacityJ)` — Newton's Law of Cooling. Natural equilibrium at `fill = inflow / HeatDissipation`. If sustained inflow > `HeatDissipation`, no equilibrium → saturation → instant thermal spike + EM burst.

| Property | Unit | Notes |
|---|---|---|
| `CapacityJ` | joules | Max heat before saturation |
| `StoredHeatJ` | joules | Current stored heat |
| `TransferRate` | watts | Max incoming rate from coolant |
| `HeatDissipation` | watts | Max dissipation rate (at full capacity); actual = `HeatDissipation × (StoredHeatJ / CapacityJ)` |

---

## Special components

### Coolant system

Pure transport medium — **no thermal mass of its own**. Moves heat from component thermal masses to the HyperspaceHeatSink.

- `HeatFlowPerComponent` — max transport rate from a single component (**watts**)
- `CoolantLeakage` — fractional fill loss per second (coolant modelled as 0–1 level)
- Consumable: Coolant fluid

### Exhaust

Mostly passive. Expels degenerate matter produced when ionised metal rod matter is passed through hyperspace. Production scales with engine thrust. Degenerate matter is separated out and expelled.

- Deposits build up inside exhaust pipes — needs periodic cleaning
- Cleaning material can be harvested for strange crystals (sellable)
- No cleaning / buildup reduces engine efficiency
- `DegenerateMaterialBuildUp` — **0.0–1.0** dimensionless; 0 = clean, 1 = fully blocked

---

## Hull

### The ship hull
- `HullType`
- Captain's log

### Exterior panels
- `Material` (enum)
- Protection stats per weapon type (projectile, various energy weapons, etc.)
- `Weight` — calculated from protection stats and material
- `Damage`

---

## Sensors

**Passive sensors:** measure external pressure, heat, radiation, gravity, etc. No power required from bus. Components have internal passive sensors for heat, power consumption, efficiency, and damage.

**Active sensors:** require a command on the CommandBus to return a value; may require bus power; some take time to charge or gather data (e.g. planet mineral scanner — large energy pulse, results may lag several seconds).

### Solar heat irradiance

- `SolarHeatSensor` is a replaceable passive ship component requiring no main-bus power.
- Publishes `{Name}.Irradiance` at 4 Hz through `ScalarTelemetry` as raw `W/m²`
  (`PhysicalQuantity.Irradiance`).
- The SensorData provider uses the generated star's actual surface temperature and radius,
  plus centre-to-ship distance: `σT⁴(R/d)²`. Spectral class is not used as a substitute for
  the generated temperature.
- The reading is incoming bolometric heat flux for a black surface normal to the rays. It is
  currently before celestial occlusion, atmospheric attenuation, ship orientation, projected
  area, and material absorptivity.
- Actual heat added to a ship/component will later be calculated in watts from this input and
  the exposed object's geometry/material. It is not connected to thermal stores yet.
- This measurement is separate from ionising radiation and from the command-triggered,
  normalized solar-spectrum scan. The spectrum remains its own data product.

---

## Consumables

| Consumable | Used by | Notes |
|---|---|---|
| Reactor fuel | Power core | Scales with output level. Siphon from stars (high risk, high yield). |
| Metal rods | Engine | Scales with thrust offset; idle = none. Strange crystals occasionally form in exhaust. |
| Coolant fluid | Coolant loop | Constant minor leakage. Heat transport efficiency scales with fill level (0–1). Topped up instantly at stations; coolant system repair takes time. |
| Ammunition | Weapons | Not yet designed. |
| Repair materials | Hull + components | Not yet designed. |
