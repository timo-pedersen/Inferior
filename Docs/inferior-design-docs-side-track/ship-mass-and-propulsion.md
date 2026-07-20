# Ship Mass and Propulsion

This document defines the physical mass model for ships and the way installed engines 
contribute force and rotational authority.

It is a design authority for future ship-mass, centre-of-mass, and engine-aggregation work. 
It does not prescribe final balance numbers.

---

# Purpose

Ships should behave as physical assemblies rather than hull definitions with arbitrary acceleration values.

A configured ship consists of:

- a structural hull;
- hull panels;
- built-in equipment;
- installed modules;
- installed engines;
- attached cargo containers;
- consumables and temporary payloads later.

Every one of these may contribute mass. Installed engines contribute propulsion. Cargo may change total ship mass dramatically.

The intended result is that:

- empty and loaded ships feel meaningfully different;
- a one-container micro-hauler can become several times heavier when loaded;
- a medium hauler carrying dense cargo may gain hundreds of tonnes;
- engine count, engine type, installation and operational state matter;
- centre of mass may shift when modules or cargo change;
- handling remains understandable and inspectable in the UI.

---

# Design goals

The system should:

- produce believable ballpark hull masses;
- aggregate mass from installed physical components;
- aggregate propulsion from installed engines rather than hull-specific constants;
- allow cargo to dominate ship mass where appropriate;
- support a derived centre of mass;
- preserve the existing simulation-authority rules;
- remain simple enough to tune in a spreadsheet;
- leave room for later damage, module failure and asymmetric thrust.

The system should not:

- hide cargo mass behind artificial handling preservation;
- derive mass from rendered triangle count;
- hard-code ship names into thrust calculations;
- require full rigid-body stress simulation;
- require detailed fuel or thermal simulation in the first implementation;
- assume that all ships have symmetric engine layouts.

---

# Mass ownership model

The simulation-owned ship derives its current mass from authored and installed physical data.

```text
Ship current mass
    = hull structural mass
    + built-in equipment mass
    + installed module mass
    + installed engine mass
    + attached container tare mass
    + attached container contents mass
    + consumables and temporary payloads later
```

Current mass is derived state. It must not become a separately mutable authority.

The hull owns its structural baseline. Each installed component owns its own physical contribution.

```text
HullDefinition
    +-- BareHullMass
    +-- HullReferenceCentreOfMass
    +-- BuiltInComponents
    +-- Mount definitions

Configured Ship
    +-- Installed cockpit
    +-- Installed engines
    +-- Installed modules
    +-- Attached containers
```

---

# Hull mass estimation

## Why bounding-box volume is wrong

A hull's axis-aligned bounding box includes empty space around broad, tapered, irregular and asymmetrical shapes. Using it directly would systematically overestimate ships such as Beren.

Hull mass should instead be estimated from approximate enclosed physical volume.

## Recommended estimation method

For each hull:

1. Estimate enclosed structural volume.
2. Choose an effective structural density appropriate to construction.
3. Calculate a baseline structural mass.
4. Apply an authored adjustment based on the actual design.
5. Record the final bare-hull mass explicitly.

```text
Estimated structural mass
    = approximate enclosed hull volume
    × effective structural density

Final bare-hull mass
    = estimated structural mass
    × authored design adjustment
```

The resulting number is an authored design value. The calculation is a method for reaching a sensible starting point, not a runtime requirement.

## Estimating enclosed volume

Preferred methods, in descending order:

1. Volume of a closed hull mesh, if the authored hull is watertight enough to calculate it reliably.
2. Sum of simple solids such as boxes, wedges, prisms and cylinders.
3. Section-based approximation using several cross-sectional areas along the ship length.
4. Visual estimate from canonical cargo containers and known module dimensions.

The method should be documented in the balancing spreadsheet so later changes can be understood.

## Effective structural density

Effective structural density represents more than raw material density. It includes:

- external shell;
- internal framing;
- bulkheads;
- structural reinforcement;
- permanent ducts and conduits;
- shielding that belongs to the hull;
- fixed access structure;
- unavoidable manufacturing overhead.

Initial calibration ranges:

| Construction class | Effective structural density |
|---|---:|
| Lightweight advanced | 0.12-0.18 t/m³ |
| Standard civilian | 0.18-0.28 t/m³ |
| Cheap industrial | 0.25-0.40 t/m³ |
| Heavy or armoured | 0.40-0.70 t/m³ |

These are fictional balancing ranges, not statements about real future materials.

## Price and construction

Price class must not be a direct mass multiplier.

A cheap ship may be heavy because it uses crude structural technology. A luxury ship may use expensive lightweight construction but carry a heavy interior fit-out.

Useful authored factors include:

- structural technology;
- construction robustness;
- armour level;
- interior fit-out;
- maintainability and redundancy;
- manufacturing quality.

The final bare-hull mass remains explicit.

---

# Panels and hull pieces

Rendered polygons do not have individual mass.

Mesh subdivision, chamfers and decorative tessellation must never change physics.

A panel contributes separately only when it is an authored physical component with its own identity, for example:

- replaceable armour panel;
- detachable hull plate;
- door assembly;
- external structural frame.

Ordinary hull surfaces are already represented by bare-hull mass.

---

# Component mass

Every installed physical component should eventually expose at least:

```text
Mass
Ship-local centre-of-mass position
```

Examples:

- cockpit module;
- engine;
- artificial-gravity / inertial-dampening module;
- reactor;
- power distribution module;
- life-support module;
- cargo interface;
- sensor array;
- attached cargo container.

The first implementation may allow some small or not-yet-modelled systems to remain part of built-in equipment mass. 
The long-term direction is explicit component mass where the component has gameplay identity.

---

# Derived centre of mass

The ship's combined centre of mass should be derived from all contributing masses.

For component `i`:

```text
TotalMass = Σ mass[i]

CentreOfMass
    = Σ (mass[i] × shipLocalPosition[i])
      / TotalMass
```

The bare hull contributes:

- its bare-hull mass;
- its authored reference centre of mass.

Attached cargo contributes at its actual attachment position.

This allows centre of mass to shift when:

- a heavy engine is replaced;
- cargo is loaded asymmetrically;
- one side-mounted container is detached;
- a reactor or other heavy module changes position;
- future damage removes physical components.

The first implementation does not need full inertia-tensor simulation. A derived centre 
of mass is still valuable for presentation, diagnostics and later force/torque work.

---

# Installed-engine contribution

Each installed engine contributes physical and propulsion values.

Minimum engine-definition contributions:

- mass;
- maximum primary thrust;
- maximum down thrust;
- rotational force / rotational authority;
- local thrust directions;
- operational state and efficiency.

Conceptually:

```text
Engine primary force
    = transformed primary thrust direction
    × maximum primary thrust
    × commanded output
    × operational efficiency

Engine down force
    = transformed down-thrust direction
    × maximum down thrust
    × commanded down-thrust output
    × operational efficiency
```

The mount and installed engine determine the final ship-local or world-space directions.

The ship's total force is the sum of installed-engine contributions:

```text
Ship force = Σ installed engine forces
```

No hull-specific logic such as "Beren has four engines" is allowed.

## Rotational authority

For the initial system, rotational force is deliberately simplified.

Each operational engine contributes an authored scalar or axis-aware rotational-authority value. 
The ship aggregates these contributions into the central rotational control value used by existing flight physics.

```text
Total rotational authority
    = Σ installed engine rotational contribution
```

This does not yet simulate exact torque from each engine's lever arm.

A later extension may calculate:

```text
Torque = offset from centre of mass × applied force
```

The initial model should not prevent that future extension.

---
# Engine asymmetry torque and thrust contribution 

When there is an asymmetry in an engine configuration, eg when one engine is damaged or we have a hull
with just one engine, then we have a penalty on engine trust and torque.

Thrust vectors from engines do not change with un-balanced engines. No "right drift" for left engine damage.
Ship computer makes sure of this by compensating. 
But if one engine is damaged and has lowewr output, the the other engine has to work harder to 
maintain thrust vector, by applying rotation.
This lowers overall thrust, and also available torque.

## Implementation
Engine un-balance may be calculated as a number between 0.0 to 1.0, where 0 is totally balanced, and 
1.0 is is half of the engines not applying thrust. In a four engine ship with three engines gone (not supplying 
thrust), this unbalance may go up to 2.0, which gives a relatively big penalty.

A penalty is applied according to some rules, say:

Thrust: 1.0 unbalance -> 50% less thrust available
Torque: - 1.0 unbalance -> 25% less torque

Number will be tuned later to adjust feel.

# Single engine ships and penalty
Small or medium ships may be single engine ships. They are, as a general rule, equipped with 
a larger class engine mount than is normal for for their size, to compensate for the unbalance penalty.

Single-engine ships require special handling because the engine must normally be offset from the centre 
of mass to protect the ship and crew from harmful operating radiation and exhaust-related hazards.

A single offset engine must spend part of its authority maintaining the intended trajectory and orientation.

They have an engine unbalance set to a certain number by default, the HullImbalanceFactor, a number owned 
by the hull. Normal hulls have a very low unbalance factor. Single engine ships may have an HullImbalanceFactor 
set to 0.8 or 1.0 per default.

Numbers will be tuned later.

Suggested hull-authored factors, calculated from HullImbalanceFactor:

```text
SingleEnginePrimaryThrustEfficiency
SingleEngineRotationalEfficiency
```

For a one-engine configuration:

```text
Effective primary thrust
    = installed engine thrust
    × hull single-engine primary efficiency

Effective rotational authority
    = installed engine rotational contribution
    × hull single-engine rotational efficiency
```

Initial balancing may use a 25-50% penalty, but exact values are authored per hull after testing.

For two or more engines, fully operational, contributions are normally fully additive unless the 
specific mount geometry or hull design defines another penalty.

The penalty must not be hidden inside Mule, Needle or any other engine definition, because the same engine may be efficient in a symmetric multi-engine installation.

---

# Inertial dampening and acceleration limits

Artificial gravity includes inertial dampening as an implicit capability.

The artificial-gravity / inertial-dampening module may therefore impose a maximum safe ship acceleration.

This creates meaningful module choice:

- a basic module supports lower acceleration;
- an expensive or heavy module supports higher acceleration;
- switching or upgrading the module changes usable performance;
- a failed module may force severe acceleration limits.

A value around 10 g is plausible for a strong initial module, but exact values are balancing data.

The physical engine force remains real. The flight-control system limits commanded output to remain 
within the active safety envelope.

Conceptually:

```text
Physical acceleration capability
    = available force / current mass

Commanded acceleration limit
    = minimum of:
        physical acceleration capability
        inertial-dampener limit
        hull structural limit later
        crew safety policy later
```

Safety bypass, crew injury and structural overstress are future extensions.

The mass must never be altered to preserve comfortable handling.

---

# Cargo mass effects

A canonical container can hold approximately 30 m³ and has a maximum gross mass of 100 tonnes.

This means cargo may dominate ship mass.

Examples:

```text
Asterisk empty mass:        perhaps 35-60 t
One maximum container:      100 t gross
Loaded mass:                perhaps 135-160 t

Beren empty mass:           perhaps 300-550 t
Nine maximum containers:    900 t gross
Loaded mass:                perhaps 1,200-1,450 t
```

These are calibration examples, not final values.

The intended behaviour is:

- empty ships may be lively;
- fully loaded ships may accelerate, rotate much more slowly and have centre of mass shifted;
- braking distance changes honestly;
- engine matching is judged against intended maximum operating mass;
- poor engine/module choices may produce an underpowered ship.

---

# Mass and thrust balancing spreadsheet

A spreadsheet is the primary design tool for mass and propulsion balancing.

Code does not need to import it initially.

## Hulls sheet

Recommended columns:

- Hull ID;
- display name;
- length;
- width;
- height;
- estimated enclosed volume;
- estimation method;
- construction class;
- effective structural density;
- estimated structural mass;
- authored adjustment;
- final bare-hull mass;
- bare-hull centre of mass;
- built-in equipment mass;
- cargo slots;
- maximum cargo gross mass;
- HullImbalanceFactor;

## Modules sheet

Recommended columns:

- Definition ID;
- module type;
- display name;
- mass;
- local centre of mass;
- power requirement;
- inertial-dampening limit where applicable;
- price class;
- built-in or replaceable;
- notes.

## Engines sheet

Recommended columns:

- Engine definition ID;
- display name;
- mass;
- maximum primary thrust;
- maximum down thrust;
- rotational contribution;
- power requirement;
- intended visual size class;
- notes.

## Configured ships sheet

Derived columns:

- Hull ID;
- installed module configuration;
- installed engines;
- empty mass;
- maximum loaded mass;
- total primary thrust;
- total down thrust;
- total rotational authority;
- empty acceleration;
- maximum-loaded acceleration;
- inertial-dampener limit;
- effective commanded acceleration;
- payload fraction;
- centre-of-mass shift examples.

The key formulas are:

```text
Acceleration = Force / Mass
Payload fraction = Payload mass / Empty ship mass
```

The spreadsheet should make mismatched engines, implausible hull masses and extreme handling immediately visible.

---

# Snapshot and UI requirements

The simulation should publish enough immutable data to explain ship behaviour:

- current total mass;
- empty/configured mass where useful;
- cargo mass;
- current centre of mass;
- total available primary thrust;
- total available down thrust;
- total rotational authority;
- inertial-dampening limit;
- current acceleration limit;
- installed-engine operational states.

Rendering and UI consume snapshots. They do not recalculate authoritative mass or force from presentation data.

---

# Initial implementation phases

## Phase 1: authored physical values

- establish the balancing spreadsheet;
- assign bare-hull mass to Aries, Asterisk and Beren;
- assign mass and thrust values to Mule and Needle;
- assign masses to existing cockpit modules;
- define canonical empty-container tare mass;
- assign a temporary built-in equipment mass to each hull.

## Phase 2: mass aggregation

- derive configured ship mass from hull and installed components;
- include installed engines and cockpit;
- expose current mass through simulation snapshots;
- add focused tests.

## Phase 3: engine aggregation

- sum installed-engine force contributions;
- sum down-thrust contribution;
- sum rotational contribution;
- apply hull-owned single-engine penalties;
- preserve current flight controls while replacing hull-specific propulsion constants.

## Phase 4: centre of mass

- derive ship-local centre of mass;
- include attached cargo and module positions;
- expose it to diagnostics and schematic UI;
- defer exact inertia-tensor and torque simulation.

## Phase 5: inertial-dampener limit

- add module-authored safe acceleration;
- clamp commanded acceleration through flight-control logic;
- expose the limiting reason to UI;
- defer injury, structural failure and safety bypass.

---

# Tests

Focused tests should cover:

- current mass equals the sum of all physical contributors;
- replacing an engine changes both mass and available force;
- one Mule contributes once and four Needles contribute four independent values;
- disabling one engine removes only that engine's operational contribution;
- single-engine hull penalties apply only where configured;
- cargo mass is included without softening;
- asymmetric cargo shifts centre of mass;
- translation and orientation do not alter ship-local aggregate mass;
- snapshot values match simulation-owned values;
- ship-specific names are absent from generic aggregation logic.

---

# Future extensions

- exact thrust-induced torque;
- inertia tensors;
- fuel and reaction mass;
- thermal limits;
- engine radiation and exclusion zones;
- module damage and reduced efficiency;
- structural acceleration limits;
- crew injury and safety bypass;
- automatic trajectory correction cost derived from installation geometry;
- load-distribution warnings;
- docking and landing load limits.

---

# Design invariants

1. Cargo mass is real and is never softened to preserve handling.
2. Current ship mass is derived from physical contributors.
3. Rendered triangle count never affects mass.
4. Installed engines contribute individually.
5. No ship name is special-cased in generic thrust aggregation.
6. Single-engine penalties are hull/configuration properties, not engine properties.
7. Centre of mass may shift when modules or cargo change.
8. Artificial gravity/inertial dampening may limit usable acceleration without changing underlying force or mass.
9. Simulation owns authoritative mass and propulsion state.
10. UI and rendering consume immutable snapshots.

---

===== End of main document =========================================

# Appendix A - Open balance decisions

The following require spreadsheet work and in-engine testing:

- final bare-hull masses for Aries, Asterisk and Beren;
- built-in equipment masses;
- Mule and Needle mass and force values;
- canonical container tare mass;
- exact single-engine penalties;
- initial inertial-dampener acceleration limits;
- whether down thrust is engine-local, ship-local or represented by separate directional endpoints;
- how much rotational handling depends on mass before full inertia modelling;
- whether the flight-control system preserves acceleration commands or thrust-percentage commands when mass changes.
