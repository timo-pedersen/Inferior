# Engine System Design

## Purpose
Engines in Inferior are physical propulsion machines, not cosmetic exhaust attachments.
An engine installation represents a replaceable, serviceable ship component with:
- physical geometry;
- mounting constraints;
- propulsion characteristics;
- thermal behavior;
- resource consumption;
- damage state;
- maintenance state;
- visual identity.

The visual appearance of an engine should communicate its capabilities and intended use.

### Core principles
Engines are replaceable and repairable machines
An engine is a large mechanical component installed into a dedicated ship mount.
Replacing an engine is comparable to replacing a major vehicle subsystem, not repairing a cosmetic hull component.

Examples:
damaged engine → replace or repair propulsion unit;
obsolete engine → retrofit compatible replacement;
damaged pair → individual engine replacement possible.

## Engine families

Engine family:
The manufactured design (Mule, Needle, SlothHauler).

Engine variant:
A family adapted for a mount standard.

Engine instance:
A specific installed physical object with damage, wear and state.

## Mount system

### Ship ownership of mounts
Ships define their propulsion architecture through fixed engine mounts.
A ship does not accept arbitrary engines based on size.
Instead:
Ship
 |
 +-- Engine Mount Standard
        |
        +-- Compatible Engine Variants

A ship with mount H5 can only install engines manufactured for H5.

## Mount standards
A mount standard defines:
- physical attachment geometry;
- structural requirements;
- service interfaces;
- allowed engine envelope;
- exhaust clearance;
- electrical/fuel connections;
- orientation.

The mount provides the ship's propulsion identity.

### Engine variants
An engine family may be manufactured for multiple mount standards.
Example:
SlothHauler 5

    SlothHauler 5-H5
        Mount: H5

    SlothHauler 5-XG3
        Mount: Xiang-3

    SlothHauler 5-UF13
        Mount: UFSVEM-13

These are related engines but are separate manufactured variants.

## Mount standard distribution

__Civilian__
Ship category	Mount standards
Shuttle	1
Small	2
Medium	5
Large	3

__Military__
Military mounts are oversized and specialized.
Ship category	Mount standards
Shuttle	1
Small	1
Medium	2
Large	1

Total: 11 civilian mounts, 4 military. 

## List of mount standards
Rarity, abbreivation and comments in paranthesis

Shuttle	1
- H-Shuttle (all civilian shuttles, part of the "H" series of mounts)

Small
- Eriksson (common, abr "E")
- H2 (common)

Medium
- Oberheim-A (rare, abr "OBR-A")
- Xiang-3 (historic, old ship models, rare, abr "XG")
- H5  (common)
- H6 (common)
- XH-6 (high performance H6, less common)

Large
- Oberheim-B (rare, abr "OBR-B")
- H10 (common)
- XH-10 (high perfomance H10, less common)

Military Shuttle (all common):
- UFSVEM-8 (abr "UF8")

Military Small:
- UFSVEM-13 (abr "UF13")

Military Medium:
- UFSVEM-50 (abr "UF50")
- UFSVEM-56 (abr "UF56")

Military Large:
- UFSVEM-99 (abr "UF99")

UFSVEM stands for "United Federation Standard Vehicle Engine Mount", a very old imperial standard still in use.

## Military mount geometry
Military propulsion mounts have a distinctive geometry.

Civilian engines:
- aligned with ship axis;
- conventional arrangement.

Military engines:
- mount angled approximately 30 degrees backward;
- visually resemble a lowercase "y" arrangement rather than a capital "T".
- The mount defines this geometry.
- The engine itself does not know whether it is civilian or military.
- Engines are aligned with ship axis just like on civilian ships. The mount tube is angled 30 degrees.

## Engine pair system
Engines are generated and installed as symmetric propulsion groups. The most common installation is 
a pair, but ships may contain multiple pairs or specialized single-engine configurations.

Generation begins from an engine pair definition.

E.g: Engine Pair Template
        |
        +-------------+
        |             |
 Left Engine      Right Engine
 Instance         Instance

The generator creates:
- matching engine family;
- mirrored geometry;
- compatible mount;
- initial symmetry.

After installation, engines are independent physical objects.

### Engine independence
Individual engines may differ after installation.

Examples:
- damage;
- heat state;
- wear;
- efficiency;
- operational state.

A pair may contain:
Left Engine:
    100% output

Right Engine:
    damaged
    reduced output

The ship should physically experience asymmetric propulsion unless flight control compensates.

## Resource abstraction

### Fuel
Individual engines may physically contain local fuel storage.
However, simulation treats fuel as a shared ship resource.

Reason:
- simpler simulation;
- easier balancing;
- little gameplay value from individual fuel levels.

Lore explanation:
The ship manages fuel distribution between propulsion units.

##Engine parameters

Engine definitions contain physical and gameplay characteristics.

### Propulsion
- forward thrust;
- reverse thrust;
- vertical thrust (optional, may be zero for some rare or small engines);
- boost thrust factor;
- boost heat generation;
- boost fuel consumption;
- pitch authority;
- yaw authority;
- roll authority.

### Resource usage
- fuel consumption;
- fuel efficiency;
- power consumption;
- power efficiency.
- Thermal behavior
- Engines generate heat, esp when boosting.

Inferior does not use external radiators.
Heat is managed by an internal thermal transport system and ultimately dumped into hyperspace.

### Engine properties include:
- heat generation;
- thermal mass;
- thermal transport efficiency.
- reliability
- expected service life;
- maintenance difficulty;
- failure tolerance; 

Large engines generally have:
- larger thermal capacity;
- heavier construction;
- slower heat build-up

## Alpha Red system

### Alpha Red generation
Some engines produce Alpha Red as a by-product of operation.
Alpha Red is not a primary fuel or reactor type.
It is an additional usable energy phenomenon.

Alpha red requires at least two working engines to work.

### Engine property:
AlphaRedProduction: Boolean.

### Alpha Gyro module
Some ships may contain an Alpha Gyro module.
If:
- installed engines (at least 2) produces Alpha Red;
- ship contains compatible gyro module;
- then additional rotational control authority becomes available.

### The gyro module has:
Primary stat:
- gyro authority bonus.
Secondary:
- mass;
- heat generation;
- thermal transport efficiency.

Cheap gyro modules:
- higher heat;
- lower efficiency.

Advanced modules:
- compact;
- efficient;
- expensive.

## Exhaust system

Exhaust is part of engine geometry.

Engines define exhaust emitter locations.
Exhaust is not universally assumed to be on the rear face.

For a right-side engine, possible exhaust surfaces:
- rear;
- top;
- bottom;
- right side.

Preferred placement:
- rear half of engine;
- mechanically plausible.

This allows varied engine designs while maintaining physical consistency.

## Engine lighting
All engines have orientation lights.
These are not navigation lights.
They exist for:
- identification;
- maintenance;
- orientation.

Required:
- Front light: white.
- Rear light: red.

No port/starboard convention is used because ships operate freely in three dimensions.

## Service access
Medium and larger engines are designed for internal servicing.

Service access is a ship design requirement, not an engine property alone.
Ships with internal engine access must provide continuous service routes between crew areas and engine mounts.

Ship architecture should provide:
- crawl access;
- maintenance routes;
- connection access.

Minimum conceptual human access:
approximately 0.6 to 0.7 m width;
approximately 0.7 m height.

Engine mounts include service pathways for:
- power;
- fuel;
- control;
- thermal systems.

## Visual design rules
Engine appearance should communicate function.

High thrust
Expected:
- large structures;
- strong mounts;
- heavy construction.

High efficiency
Expected:
- compact;
- refined;
- expensive appearance.

High thermal mass
Expected:
- large internal volume;
- thick structures.

No external radiators.

Military
Expected:
- armour;
- redundancy;
- oversized mounts;
- aggressive geometry.

Cheap industrial
Expected:
- accessible service panels;
- robust construction;
- simple geometry.

## Initial implementation scope

The first implementation should prove:
- engine generation;
- mount compatibility;
- paired generation;
- mirrored geometry;
- engine rendering;
- exhaust placement;
- engine lights.

Gameplay systems such as:
- damage;
- replacement;
- economy;
- advanced thermal simulation;

can follow later.


