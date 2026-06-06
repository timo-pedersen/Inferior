# Inferior — Ship Design Reference

## Core Philosophy

A ship is more or less an empty shell. Almost everything is components — engines, sensors, 
power bus, gyro, weapons. A ship without a power bus cannot fly; 
engines cannot start without power. This is simulation, not abstraction.

When acquired, a ship comes with a sane minimal component loadout — just enough 
to undock and fly. Everything beyond that is player investment.

Ships are persistent physical objects in the universe, not player inventory. 
A sold ship retains its history, its captain's log, its wear. Someone buys 
a ship with a past.

---

## Ship Identity

A ship instance is a unique object in the universe.  
A ship class is the template that defines what it can be.

Approximately 20 fixed hull types are planned. No player-designed hulls.

Ship class determines:
- Available size of components that can be installed
- Hardpoint locations
- Cockpit placement
- Base mass (hull only, no components)
- Mesh and visual identity

---

## Size Classes

Four size classes planned. Determines what engines, equipment and components 
can be installed. Size class is stubbed in initial implementation.

---

## Equipment

All installed equipment is functional — there are no pure cosmetic items except paint.
Customisation comes from:
- Hull element choice (grade, rarity, cosmetic matching across panels)
- Equipment (shield antennas, scanners, weapons, etc. — all affect simulation)
- Power circuit tuning — the primary personal investment; what separates a veteran 
  from someone who bought the same hull yesterday

---

## Minimal Flyable Ship (current implementation target)
Ship
├── Position        (DVec3)
├── Velocity        (DVec3)
├── Thrust          (DVec3)
├── Mass            (double — hull + components)
├── TurnRate        (calculated property, see below)
└── SizeClass       (enum, stubbed)

Everything else — power system, panels, cargo, wiring, damage — is stubbed 
or deferred until the simulation layer that requires it is implemented.

---

## Turn Rate

Turn rate is a **calculated property**, not a fixed value.  
It emerges from installed components:
- Engine type and its built-in gyro capability
- Whether an optional dedicated gyro is installed
- Ship mass (hull + components)

The drive provides baseline rotational authority through its gyroscopic effect — 
the ship can always turn without a dedicated gyro installed. An optional gyro 
component enhances that, and is available (and effective) on ships where mass 
or engine choice makes the baseline insufficient.

Turn rate is **asymmetric** — pitch up and pitch down have different rates.  
This is intentional and physically motivated.

Down pitch is the same as left and right yaw. Up pitch can be faster, 
depending on engine type — most engines allow additional downward thrust, 
making upward pitch faster. This thrust vector also supports planetary landing.

Implement as a property with a getter from day one, even when stubbed with 
a hardcoded value. This avoids refactoring when the power and engine systems 
are implemented.

---

## Cockpit Placement

The cockpit is not necessarily at the centre of mass. It is defined as a 
**vector offset from CoM** in ship coordinate space.

Examples:
- Exploration vessel — cockpit at the nose
- Large freighter — cockpit offset to the side (Millennium Falcon-style) 
  or underneath (ED Type-7 style)
- Military capital ship — cockpit far back, top of hull

**The camera follows the cockpit position, not the centre of mass.**  
The ship rotates around CoM. This means cockpit placement has direct impact 
on flight feel — a side-mounted cockpit on a large freighter will feel 
fundamentally different from a nose-mounted explorer cockpit. This is a 
feature, not a problem.

Cockpit offset is a ship class property defined in ship class definition.
It is cheap to implement now and painful to retrofit later.

Additional cameras are available on the ship. The cockpit camera is the default, but 
players can switch to other cameras. Not to be implemented now, but a prep for future 
enhancement.

---

## Power System Dependency

A ship requires many components to function, eg a power bus, a reactor. Without it:
- Engines cannot start
- Sensors cannot post to bus
- Ship is inert

Power bus simulation is **not yet implemented**. For the minimal flyable ship, 
power is stubbed — assume power is present and available. The architecture 
should anticipate the power system without requiring it to exist yet.

---

## Debug Camera

The debug camera is a permanent development tool, not a placeholder for the 
ship camera. It remains available for universe inspection, scene debugging, 
and rapid traversal during development. The ship has its own camera attached 
to the cockpit position. These are two separate systems.

---

## What Is Stubbed

- Size class (enum exists, not yet enforced)
- Power bus (assumed present for minimal ship)
- Component slots and installation rules
- Hull panels and damage system (spec: `inferior-design.md` → Hull & damage; `inferior-classes.md` → HullElement, InternalComponent)
- Captain's log (spec: `inferior-design.md` → Commander & ship identity → Captain's log)
- Ship persistence and ownership transfer (spec: `inferior-design.md` → Ship loss & respawn flow)
- Internal wiring and power priority settings (spec: `inferior-design.md` → Power system)
- Panel layout configuration