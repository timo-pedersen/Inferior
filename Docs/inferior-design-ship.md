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



### Small
Small hulls are expected to be around 20-50m in length.

4

### Medium
Medium hulls are expected to be around 50-100m in length, suitable for a wide range of roles including exploration, combat, and cargo transport.

6

### Large
Large hulls are expected to be around 100-250m in length, designed for heavy combat, large cargo transport, or specialized roles like mining or salvage.

4

### Capital
capital hulls are expected to be 250-300m+ in length, serving as flagships, carriers, or massive freighters. These ships will have extensive component 
slots and unique capabilities, but will also require significant player investment to operate effectively.

2

---

## Size Class Limitations

Size class determines which components can be installed. This is the primary balancing
mechanism for ship progression — larger ships need larger (and heavier, more expensive)
components to run effectively.

| Size class | Component slots (approx) | Max component class | Notes |
|------------|--------------------------|--------------------|----|
| Small | ~6 | Class 2 | Fast, agile; limited power and cargo |
| Medium | ~10 | Class 4 | Versatile; balanced performance across roles |
| Large | ~16 | Class 6 | Powerful; sacrifices agility and docking access |
| Capital | ~24 | Class 8 | Massive capability; limited to open space and large stations |

**Component classes** are not yet fully defined. The table above uses placeholder
values — actual numbers will be tuned per-hull once component design is finalised.

**Size class is currently stubbed** — the enum exists (`ShipSizeClass`) and is stored on
`Ship`, but it is not yet enforced at component installation time. Enforcement comes when
component slot rules are implemented.

A ship that does not meet component requirements is not illegal — it simply cannot be
built via the normal factory path. `ShipBuilder` will accept any component in any slot
for now; validation belongs in the fitting screen logic, not the builder.

---

## Ship roles

Some ships may have a defined role or specialization, which influences their base stats, available hardpoints, and starting equipment.
Other ships may be more general-purpose, allowing players to customize them for various roles through component choices and loadouts.


### Explorer

Long jump range. Large reactor and fuel tanks. Moderate cargo capacity.

### Freighter
Large cargo capacity, slower speed and turn rate, minimal combat capability. Focus on trading and transport.

Special - can be equipped with an EMP bomb, that is dropped, which offsets the lack of offensive capability. 
The EMP bomb is a one-use item that disables nearby ships' systems for a short duration, allowing the freighter to 
escape or avoid combat. You may only have one EMP bomb equipped at a time (possibly two for special ships), and it 
needs to be bought again at station.

### Combat

The expected. Balance of offence and defence important. Moderate cargo capacity for mission rewards and loot.

### Luxury

High cost, high maintenance, but with unique aesthetics and comfort features. Not necessarily the best at combat or cargo, but a status symbol for wealthy captains.

### Utility

Repair ships, tugs, and other support vessels. Not designed for combat or long-range travel, but essential for fleet operations and station maintenance.

### Mining

Mining ships are equipped with specialized equipment for extracting resources from asteroids and planetary surfaces. They have large cargo holds for storing mined materials 
and may have limited combat capabilities to defend against pirates.

### Salvage

Salvage ships are designed for scavenging derelict vessels and space debris. They have equipment for cutting and towing, as well as enhanced 
sensors for locating salvageable materials.

### Passenger Transport

Passenger transport ships are designed for carrying people rather than cargo. They have luxurious accommodations and amenities, but may have 
limited cargo capacity and combat capabilities.

### Support

Support ships provide various services to other vessels, such as electronic warfare, reconnaissance, or medical aid. They may have specialized 
equipment for their roles and can be crucial in fleet operations.

### Science & Research

Science and research vessels are equipped with advanced sensors and laboratories for conducting experiments and gathering data. They may have limited
combat capabilities and cargo space, but are essential for exploration and scientific discovery.

### Racing

Racing ships are designed for speed and agility, with powerful engines and minimal to no cargo capacity. They are used in competitive racing events and may 
have specialized equipment for enhancing performance and maneuverability.

### Smuggler

Smuggler ships are designed for stealth and evasion, with features that help them avoid detection by authorities. They may have hidden compartments for
contraband and enhanced sensors for detecting pursuers.

### Military

Military ships are heavily armed and armored, designed for combat and defense. They have advanced weaponry, strong shields, 
and reinforced hulls, but may have limited cargo capacity and slower speeds compared to other ship types.

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