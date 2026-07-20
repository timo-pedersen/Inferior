# Ship Engines, Thrust, Mass and Landing Capability

This document consolidates the current design for ship mass, installed-engine output, engine harmonies,
shared thrust allocation, rotational authority and planetary landing capability.

It records both the intended model and the current provisional numbers. The formulas and ownership rules
are considered design decisions. Numeric tuning values remain provisional unless explicitly described as
current runtime facts.

The purpose is not to model complete rigid-body spacecraft physics. The purpose is to produce a coherent,
predictable simulation in which:

- installed engines matter individually;
- mass honestly affects acceleration;
- cargo can radically change handling;
- ship dimensions affect rotation;
- engine harmony controls both acceleration and speed ceiling;
- simultaneous thrust commands compete for finite engine output;
- planetary landability follows naturally from thrust, mass and gravity;
- no authored `CanLand` flag is required;
- no anti-gravity is introduced.

---

# 1. Design principles

## 1.1 Honest mass

Cargo mass must not be softened to preserve handling.

A ship that becomes two, three or five times heavier when loaded should accelerate and rotate accordingly.
This is a feature, not a balancing problem to hide.

The current ship mass is derived from physical contributors:

```text
Bare hull structural mass
+ fixed built-in systems
+ installed cockpit or bridge
+ installed engines
+ installed modules
+ attached containers
+ container contents
+ consumables later
= current ship mass
```

The simulation owns the derived current mass. It must not be stored as a second independently mutable value.

## 1.2 Installed engines are authoritative

A ship does not receive thrust because its hull ID says that it has a certain performance.

Each installed operational engine contributes numeric values. The ship sums those contributions.

Forbidden examples:

```text
Beren has four engines, therefore thrust = 4 × global thrust.
```

```text
Antega uses a special ship-wide thrust constant.
```

Required model:

```text
Installed engine definition
+ installed engine state
+ mount orientation
+ selected harmony
= engine contribution
```

```text
Ship contribution = sum of all operational installed-engine contributions
```

## 1.3 No unnecessary rigid-body complexity

The simulation deliberately avoids:

- full inertia tensors;
- gyroscopic coupling;
- individual manoeuvring thruster simulation;
- engine mount torque-arm calculations;
- structural flex;
- fluid or atmospheric simulation in the engine model;
- arbitrary ship-specific acceleration rules.

The chosen model uses:

- summed force vectors;
- one scalar rotational torque contribution per engine;
- three box-derived rotational inertias;
- a stable ship body origin;
- a later calculated centre of mass.

This captures the gameplay-relevant behaviour without adding large amounts of fragile simulation detail.

---

# 2. Units

Use the following units consistently in authored data, runtime diagnostics and balancing sheets:

```text
Mass:                 kilograms
Force:                newtons
Torque:               newton-metres
Distance:             metres
Linear acceleration:  metres per second squared
Angular velocity:     radians per second
Angular acceleration: radians per second squared
Gravity:              multiples of standard Earth gravity, g
```

Standard gravity:

```text
g0 = 9.80665 m/s²
```

Useful balancing identity:

```text
1 kN / 1 tonne = 1 m/s²
```

---

# 3. Hull mass estimation

## 3.1 Purpose

A hull needs an authored bare structural mass. Before that value is known, an estimate is needed to place
the ship in the correct general mass range.

The estimate is a design aid, not runtime physics.

## 3.2 Approximate method

Do not use the ship's axis-aligned bounding-box volume as structural volume. That badly overestimates broad,
thin or irregular ships.

Use approximate enclosed hull volume, derived from:

- a closed hull mesh where practical; or
- a set of simple authored volumes such as boxes, wedges and cylinders.

Then:

```text
Estimated structural mass
    = approximate enclosed hull volume
    × effective structural density
```

```text
Final authored bare-hull mass
    = estimated structural mass
    × design adjustment
```

Effective structural density represents more than literal hull material. It includes:

- shell;
- framing;
- bulkheads;
- internal structural members;
- shielding;
- cabling and ducts;
- permanently integrated systems not represented as replaceable modules.

Suggested initial ranges:

| Construction type | Effective structural density |
|---|---:|
| Lightweight | 120–180 kg/m³ |
| Standard | 180–280 kg/m³ |
| Cheap industrial | 250–400 kg/m³ |
| Heavy or armoured | 400–700 kg/m³ |

These are balancing ranges, not claims about future metallurgy.

## 3.3 Price and mass

Price class must not be a direct mass multiplier.

A cheap ship may be heavy because it uses ordinary robust materials. A luxury ship may use an expensive
light structure but then add furnishings, sound insulation and redundant systems.

Prefer separate design factors:

```text
Structural technology
Construction robustness
Armour level
Interior fit-out
Installed systems
```

## 3.4 Avoid double counting

The hull mass must have a documented boundary.

If a built-in system is already represented in effective structural density, do not add it again as a module.
If a reactor, bridge, engine or other component has an explicit installed mass, it must not also be hidden in
the bare-hull mass estimate.

Rendered polygon count has no relationship to physical mass.

---

# 4. Current and provisional ship masses

## 4.1 Current runtime facts

### Aries

Current diagnostics:

```text
Bare hull:              72,000 kg
Other configured mass:   1,200 kg
Installed engines:       4,800 kg
Current empty mass:     78,000 kg
```

Current aggregate propulsion at maximum output:

```text
Forward force:          312 kN
Forward acceleration:   4.0 m/s²
Maneuvering force:      156 kN
Maneuver acceleration:  2.0 m/s²
```

### Antega

Current diagnostics:

```text
Bare hull:               3,200,000 kg
Other configured mass:       1,200 kg
Four Atlas engines:        384,000 kg
Current empty mass:       3,585,200 kg
```

Each Atlas currently has:

```text
Dry mass: 96,000 kg
```

At maximum harmony, the configured Antega currently produces approximately:

```text
Total forward force:      14.341 MN
Forward acceleration:      4.0 m/s²
Lift acceleration:         2.0 m/s²
Lift-only hover rating:     0.204 g
Conservative hover rating:  0.163 g
```

The conservative diagnostic divides the theoretical hover rating by a temporary 1.25 control-reserve factor.

## 4.2 Earlier design ranges, not runtime authority

These estimates were used to establish scale before the numeric system existed. They remain useful as broad
reference but must not override actual configured data.

### Asterisk

```text
Bare structure: 20–35 tonnes
Complete empty ship: roughly 35–60 tonnes
Maximum cargo: one container, up to 100 tonnes gross
```

Asterisk should experience an extreme loaded-mass change. That is intentional.

### Beren

```text
Bare structure: roughly 180–300 tonnes
Complete empty ship: roughly 300–550 tonnes
Maximum cargo: nine containers, up to 900 tonnes gross
Maximum loaded mass: plausibly 1,200–1,450 tonnes
```

These values must be recalibrated from the actual configured hull and modules.

---

# 5. Cargo mass

## 5.1 Canonical container

Each standard container provides:

```text
Internal volume: approximately 30 m³
Maximum gross mass: 100 tonnes
```

The gross limit includes the container itself.

A provisional example:

```text
Container tare mass:      5 tonnes
Maximum contents mass:   95 tonnes
Maximum gross mass:     100 tonnes
```

Tare mass remains to be finalized.

The maximum average density at the gross limit is:

```text
100 tonnes / 30 m³ ≈ 3.33 tonnes per cubic metre
```

This is a regulatory maximum, not the assumed density of ordinary cargo.

Both volume and gross mass must be enforced:

```text
Used volume ≤ 30 m³
Gross mass ≤ 100 tonnes
```

Dense goods reach the mass limit before filling the container. Low-density goods fill the volume first.

## 5.2 Ship capacities

Current intended capacities:

| Ship | Container capacity |
|---|---:|
| Asterisk | 1 |
| Beren | 9 |
| Antega | 120 |

Maximum theoretical cargo mass:

```text
Asterisk:  100 tonnes
Beren:     900 tonnes
Antega: 12,000 tonnes
```

## 5.3 Loaded Antega

At maximum regulatory cargo mass:

```text
Empty Antega:   3,585.2 tonnes
Maximum cargo: 12,000.0 tonnes
Loaded total:  15,585.2 tonnes
```

With unchanged maximum engine output:

```text
Forward acceleration:
14.341 MN / 15.5852 million kg
≈ 0.92 m/s²
```

With a 0.50 lift fraction:

```text
Lift acceleration:
7.1705 MN / 15.5852 million kg
≈ 0.46 m/s²
≈ 0.047 g theoretical hover capability
```

Conservative hover rating with 25% reserve:

```text
≈ 0.038 g
```

This means a fully loaded Antega can operate only on extremely low-gravity bodies. That is desirable.

---

# 6. Engine numeric model

Each engine definition contributes at minimum:

```text
MassKg
MaximumForwardThrustN
ReverseThrustFraction
LateralThrustFraction
LiftThrustFraction
MaximumRotationalTorqueNm
HarmonyCount
MinimumThrustFraction
MinimumSpeedCeilingMps
MaximumSpeedCeilingMps
```

The directional values are fractions of maximum forward thrust.

Derived maximums:

```text
Maximum reverse thrust
    = MaximumForwardThrustN × ReverseThrustFraction

Maximum lateral thrust
    = MaximumForwardThrustN × LateralThrustFraction

Maximum lift thrust
    = MaximumForwardThrustN × LiftThrustFraction
```

`LiftThrustN` describes force applied to the ship. The corresponding exhaust points downward.

The engine definition must not store qualitative thrust labels as simulation authority. Qualitative labels may
remain for presentation.

---

# 7. Current provisional engine data

The current implemented values are deliberately provisional.

| Engine | Harmonies | Minimum output | Reverse | Lateral | Lift |
|---|---:|---:|---:|---:|---:|
| Mule | 8 | 0.10 | 1.00 | 0.50 | 0.75 |
| Needle | 16 | 0.10 | 1.00 | 0.50 | 0.75 |
| Atlas | 10 | 0.10 | 1.00 | 0.25 | 0.50 |

All currently use provisional speed-ceiling endpoints:

```text
Minimum ceiling:     50 m/s
Maximum ceiling: 25,600 m/s
```

These endpoints are not accepted balance.

Current observed problems include:

- low harmonies accelerating too weakly for practical station approach;
- low-harmony speed ceilings being too high;
- the relationship between station-approach handling and harmony needing substantial tuning;
- all engines sharing the same speed endpoint range despite different roles.

The implemented formulas and ownership model are accepted. The tuning is not finished.

---

# 8. Engine harmonies

## 8.1 Meaning

Harmony controls both:

- available engine output;
- speed ceiling.

Low harmony provides low thrust and a low speed ceiling. High harmony provides high thrust and a high ceiling.

Harmony count controls resolution, not maximum output.

Examples:

- old or military engines: few coarse harmonies, large jumps, high peak output;
- modern civilian engines: many fine harmonies, precise control, often lower peak output for their class;
- specialized engines may use different counts while sharing the same formula.

## 8.2 Military engine philosophy

Military organizations favour older coarse-harmony engine principles because they provide:

- high output;
- fewer delicate operating states;
- broad fuel tolerance;
- the ability to run on poor conductive material in emergencies.

They normally use expensive military-optimized fuel rods, but can consume almost any suitable metal when necessary.

Modern civilian engines tend to provide finer harmony control at the cost of:

- lower absolute output for comparable mass or class;
- greater fuel specialization;
- dependence on purpose-made metal rods.

Fuel compatibility and fuel consumption are not yet implemented.

## 8.3 Quadratic harmony curve

For selected harmony `h`:

```text
h = 1 ... HarmonyCount
```

Normalize:

```text
x = (h - 1) / (HarmonyCount - 1)
```

Quadratic curve:

```text
curve = x²
```

Output multiplier:

```text
ThrustMultiplier =
    MinimumThrustFraction
    + (1 - MinimumThrustFraction) × curve
```

Speed ceiling:

```text
SpeedCeiling =
    MinimumSpeedCeiling
    + (MaximumSpeedCeiling - MinimumSpeedCeiling) × curve
```

The same curve is used for:

- forward thrust;
- reverse thrust;
- lateral thrust;
- lift thrust;
- rotational torque;
- speed ceiling.

This creates fine control at low harmonies and increasingly large output increases toward the top.

More harmonies produce smaller steps while preserving the same endpoints.

## 8.4 Current tuning warning

The formula is accepted. The endpoint values and harmony counts remain subject to balancing.

In particular, harmony 2 currently does not yet provide a satisfactory combination of:

- station-approach acceleration;
- station-approach speed ceiling;
- precision;
- braking authority.

Do not change the formula merely to fix bad provisional endpoints.

---

# 9. Shared translational thrust envelope

## 9.1 Reason

Forward, reverse, lateral and lift thrust are not independent unlimited systems.

Applying lift must reduce the engine capacity available for forward or reverse acceleration. Simultaneous commands
share one finite output envelope.

## 9.2 Command vector

Let normalized translation commands be:

```text
f = longitudinal command
l = lateral command
v = vertical command
```

Each lies in:

```text
-1 ... +1
```

Compute command usage:

```text
usage = sqrt(f² + l² + v²)
```

If:

```text
usage <= 1
```

leave commands unchanged.

If:

```text
usage > 1
```

normalize:

```text
f = f / usage
l = l / usage
v = v / usage
```

Then apply each channel's harmony-scaled maximum.

## 9.3 Examples

```text
Full forward:
    100% forward
```

```text
Full forward + full lift:
    70.71% forward
    70.71% lift
```

```text
Full forward + full lateral + full lift:
    57.74% of each channel
```

The change is continuous. There is no arbitrary priority rule.

## 9.4 Per-engine evaluation

Each installed engine evaluates:

```text
selected harmony
+ directional fractions
+ normalized shared command
+ engine installation orientation
= engine-local force contribution
```

The ship sums transformed force vectors.

This supports:

- mixed engine definitions;
- different harmonies per engine later;
- unusual mount orientations;
- future disabled or damaged engines;
- no hull-specific thrust logic.

## 9.5 Mixed engine speed ceiling

The current mixed-engine rule is:

```text
Ship speed ceiling
    = lowest ceiling among operational installed engines
```

This prevents a lower-harmony engine from being dragged beyond its operating range.

The policy may be revisited if mixed-engine gameplay demonstrates a better model.

---

# 10. Translation controls

The translation controls produce force commands. They do not directly move the ship or assign velocity.

Current design:

```text
Forward/reverse controls:
    longitudinal engine output

A/D:
    lateral thrust

F/R:
    ordinary vertical thrust using lateral-strength output

Space:
    stronger positive lift channel in the same direction as the existing R control
```

`R + Space` remains one vertical command and must not exceed full lift allocation.

The opposite vertical direction uses ordinary lateral-strength thrust.

Space is not:

- a separate engine;
- an impulse;
- an additional independent thrust pool;
- a direct acceleration constant.

---

# 11. Rotation

## 11.1 Engine contribution

Each installed engine contributes one scalar:

```text
MaximumRotationalTorqueNm
```

Harmony scales it through the same quadratic output multiplier.

Torque is summed across operational engines.

Rotation does not currently consume the shared translational envelope. That interaction may be tested later.

Do not make high harmony reduce rotational torque merely because low-harmony manoeuvring might seem intuitive.
That is likely to feel wrong when the ship needs authority at high speed.

## 11.2 Simplified box inertia

Configured ship-local dimensions:

```text
W = width
H = height
L = length
M = current ship mass
```

The configured bounds include major physical modules such as engines and bridge.

Three scalar inertias:

```text
Pitch around local X:
Ixx = M × (H² + L²) / 12
```

```text
Yaw around local Y:
Iyy = M × (W² + L²) / 12
```

```text
Roll around local Z:
Izz = M × (W² + H²) / 12
```

Angular acceleration:

```text
Pitch acceleration = EffectiveTorque / Ixx
Yaw acceleration   = EffectiveTorque / Iyy
Roll acceleration  = EffectiveTorque / Izz
```

Consequences:

- long thin ships roll more easily than they pitch or yaw;
- wide ships resist yaw;
- tall ships resist pitch;
- loaded ships accelerate rotationally more slowly;
- more engines add rotational authority.

This is not a full inertia tensor.

## 11.3 Assisted rotation

Current assisted control interprets input as target angular velocity.

```text
TargetAngularVelocity
    = normalized input × maximum assisted angular rate
```

Actual angular velocity approaches the target at the available angular acceleration:

```text
AngularVelocity =
    MoveTowards(
        AngularVelocity,
        TargetAngularVelocity,
        AvailableAngularAcceleration × dt)
```

The same torque is used to start and stop rotation.

Returning controls to neutral:

- requests zero angular velocity;
- causes torque-limited braking;
- does not stop rotation instantly.

Flight-assist-off torque controls are deferred.

## 11.4 Current control mapping

```text
Mouse Y:
    pitch

Mouse X:
    roll

Q / E:
    yaw
```

Translational controls remain separate.

---

# 12. Designed single-engine ships

A purpose-designed single-engine ship suffers continuous efficiency loss because its engine is necessarily offset
from the centre of mass and must spend authority maintaining trajectory.

The penalty belongs to the hull or propulsion layout, not the engine.

Provisional Asterisk values:

```text
Forward efficiency:      0.75
Maneuvering efficiency:  0.75
Rotation efficiency:     0.60
```

These values apply only because Asterisk is explicitly authored as a designed single-engine installation.

A multi-engine ship temporarily operating on one surviving engine does not receive this compensation model.
That is a damaged asymmetric configuration and will require separate behaviour later.

---

# 13. Centre of mass

## 13.1 Derived value

The future ship-local centre of mass is:

```text
CentreOfMass =
    sum(component mass × component ship-local position)
    / total mass
```

Contributors include:

- hull structural mass at authored hull centre of mass;
- engines;
- cockpit or bridge;
- fixed modules;
- replaceable modules;
- attached containers;
- container contents.

## 13.2 Stable body origin

Two concepts must remain distinct:

```text
Body origin:
    stable reference for hull geometry, mounts and authored transforms

Centre of mass:
    derived physical point used by simulation
```

`Ship.Position` must not silently change meaning.

When a centre-of-mass pivot is eventually implemented, changing cargo or modules must not teleport the visible hull.

## 13.3 Current status

Current propulsion uses total mass.

Current rotation uses total mass and configured box dimensions.

Physical rotation around the calculated centre of mass is deferred. The stable body origin remains the current pivot.

---

# 14. Planetary landing capability

## 14.1 No landability flag

A ship's ability to hover or land follows from:

```text
Current mass
Installed operational engines
Selected harmony
Engine lift fraction
Ship orientation
Boost state later
Local gravity
```

No authored `CanLand` boolean is needed.

## 14.2 Hover requirement

Required force to hover:

```text
RequiredHoverForce = CurrentMass × LocalGravity
```

Theoretical lift-only hover gravity:

```text
MaximumHoverGravityG =
    AvailableLiftForce
    / CurrentMass
    / 9.80665
```

A temporary conservative diagnostic uses 25% control reserve:

```text
SafeLandingGravityG =
    MaximumHoverGravityG / 1.25
```

This is a planning estimate, not a hard legal limit.

## 14.3 Antega

Current empty Antega at maximum harmony:

```text
Lift acceleration:          2.0 m/s²
Theoretical hover gravity:  0.204 g
Conservative rating:        0.163 g
```

Therefore an empty Antega can operate on small low-gravity moons.

It cannot lift-only hover on an ordinary 1g world.

A fully loaded Antega falls to approximately:

```text
Theoretical hover gravity: 0.047 g
Conservative rating:       0.038 g
```

This makes orbital freight infrastructure and transfer craft meaningful.

## 14.4 Nose-up landings

Lift thrusters are not the only force that can oppose gravity.

Planetary vertical acceleration is derived from all world-space forces:

```text
Net vertical acceleration =
    dot(total world force, world up)
    / mass
    - local gravity
```

If the pilot pitches the ship so the main engines point upward, forward thrust contributes to lift.

Example using empty Antega at maximum harmony:

```text
Forward acceleration: 4.0 m/s²
Lift acceleration:    2.0 m/s²
```

Full forward plus full lift shares the thrust envelope:

```text
Forward allocation: 0.7071
Lift allocation:    0.7071
```

If both forces align vertically:

```text
Vertical thrust acceleration
    = 4.0 × 0.7071 + 2.0 × 0.7071
    ≈ 4.24 m/s²
    ≈ 0.43 g
```

Thus skilled piloting can extend landability beyond the lift-only rating.

Boost may increase this further when it is integrated into the same force model.

This is not a special landing mechanic. It is ordinary force-vector arithmetic.

## 14.5 Expected ship roles

These are design expectations, not hard rules.

| Ship type | Typical intended landing capability |
|---|---|
| Shuttle | 1–3g depending on role |
| Small ship | 1g loaded; 2–3g on capable designs |
| Medium ship | 1g loaded; around 2g when light or specialized; 3g exceptional |
| Large ship | low gravity, or under 2g when purpose-built |
| Antega-scale cargo hauler | low-gravity moons and orbital infrastructure |

Some large passenger or military ships should be able to land on roughly 1g and below 2g worlds.

They achieve this through ordinary design trade-offs:

- high lift fraction;
- powerful engines;
- low payload fraction;
- more engines;
- strong structure;
- expensive inertial damping;
- specialized landing systems later.

They do not require anti-gravity.

## 14.6 Three-gravity worlds

A 3g world is extreme for humans.

A ship may be physically able to produce enough thrust while still lacking:

- adequate occupant protection;
- adequate structure;
- adequate cargo attachment strength;
- adequate landing gear.

Three-gravity operation should therefore be meaningful and specialized.

---

# 15. Artificial gravity and inertial damping

The artificial-gravity and inertial-damping system is a fixed integrated hull installation.

It is embedded through the ship's roof, walls and occupied structure.

Characteristics:

```text
Mass
Continuous power consumption
Maximum protected acceleration
Upgrade class
Refit time and cost
```

It has no ordinary cockpit command and is not a freely removable plug-in module.

It can be upgraded through expensive and time-consuming refit work.

The system protects occupants from acceleration. It does not:

- reduce ship mass;
- reduce planetary gravity acting on the hull;
- reduce required hover thrust;
- reduce landing-gear load;
- eliminate structural load;
- create anti-gravity.

A future ship may be physically capable of 15g acceleration while its dampener safely protects occupants only to 10g.

The control system may cap crew-safe commanded acceleration while the dampener is powered.

---

# 16. Current implementation status

Implemented:

- numeric engine mass;
- additive installed-engine force;
- engine installation orientation;
- numeric forward, reverse, lateral and lift output;
- numeric rotational torque;
- per-engine harmony state;
- quadratic harmony curve;
- harmony-scaled thrust;
- harmony-scaled rotational torque;
- harmony-scaled speed ceiling;
- shared normalized translational thrust envelope;
- lowest operational engine ceiling for mixed configurations;
- single-engine hull efficiencies;
- ship mass including engine mass;
- force divided by mass acceleration;
- three scalar box inertias;
- torque-limited angular velocity;
- assisted rotation;
- propulsion and rotation diagnostics;
- hover-capability diagnostics.

Current verification at the end of the harmony pass:

```text
Release build: 0 warnings, 0 errors
Tests:         407 / 407 passed
```

Not implemented:

- planetary gravity;
- actual hover assist;
- atmospheric flight;
- fuel inventory;
- fuel compatibility;
- fuel consumption;
- heat;
- reactor output and power allocation;
- boost integration into the shared engine envelope;
- rotation competing for shared engine output;
- physical cargo mass;
- container attachment;
- physical centre-of-mass pivot;
- landing gear and structural landing limits;
- inertial-dampener gameplay limits.

---

# 17. Tuning agenda

The architecture is in a good state. The next engine work should be tuning rather than another large model change.

## 17.1 Harmony endpoints

Tune per engine:

```text
MinimumThrustFraction
MinimumSpeedCeiling
MaximumSpeedCeiling
HarmonyCount
MaximumForwardThrust
Directional fractions
```

The current common `50–25,600 m/s` ceiling range is placeholder data.

## 17.2 Station approach

The low harmonies must support:

- useful acceleration;
- useful braking;
- a ceiling low enough for precision;
- no long periods spent waiting for speed changes.

Harmony 2 currently has too little acceleration and too high a ceiling.

Do not solve this by weakening cargo mass or bypassing force/mass physics.

## 17.3 Ship differentiation

Tune each configured ship against:

```text
Empty forward acceleration
Loaded forward acceleration
Reverse acceleration
Lateral acceleration
Lift acceleration
Pitch acceleration
Yaw acceleration
Roll acceleration
Time to assisted target rate
Low-harmony station behaviour
Maximum-harmony travel behaviour
```

## 17.4 Landing targets

Tune engines and hulls so the resulting physical capability roughly supports:

- Asterisk and ordinary small ships: practical 1g operation where intended;
- Beren and medium ships: 1g loaded, higher gravity depending on load and design;
- specialized medium or large ships: under 2g;
- Antega: low-gravity moons only.

These remain emergent outcomes, not categorical permissions.

---

# 18. Design invariants

1. Cargo mass is never softened to preserve handling.
2. Current ship mass is derived from physical contributors.
3. Installed engines provide propulsion; hull IDs do not.
4. Engine mass contributes to ship mass.
5. Harmony uses one quadratic curve for thrust, torque and speed ceiling.
6. Harmony count controls resolution, not peak output.
7. Directional thrust derives from maximum forward thrust and authored fractions.
8. Simultaneous translation commands share one normalized thrust envelope.
9. Lift consumes capacity that would otherwise be available to forward, reverse or lateral thrust.
10. Engine force is transformed through the installed engine orientation.
11. Rotation uses summed torque and three box-derived scalar inertias.
12. The rotational model is deliberately not full rigid-body physics.
13. Single-engine efficiency penalties belong to the designed hull layout.
14. Landability is derived from mass, thrust, harmony, orientation and local gravity.
15. Nose-up main thrust and boost may extend landing capability through ordinary vector physics.
16. Large ships may be unable to land on ordinary planets.
17. Specialized large passenger or military ships may land through ordinary engineering trade-offs.
18. Artificial gravity and inertial damping protect occupants; they do not remove gravity or thrust requirements.
19. The stable body origin and derived centre of mass remain distinct concepts.
20. Numeric tuning remains provisional until accepted in-engine.
