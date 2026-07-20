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

## Engine Mount Geometry

### Physical role

An engine mount is a physical ship structure, not only an attachment transform or compatibility record.

It connects the hull to the installed engine and represents:
- structural load transfer;
- engine retention and replacement interface;
- power and control connections;
- fuel connections;
- thermal transport connections;
- service access where required.

An installed engine must not appear to float beside the hull. There must be continuous visible structure between the 
hull mount region and the engine attachment plane.

Ownership
The mount belongs to the hull.

Hull
 └─ EngineMount
      └─ Installed EngineInstance

The hull definition owns:
- mount standard;
- mount root transform;
- mount geometry;
- engine attachment transform;
- service-access requirements;
- clearance envelope.

The engine variant owns the matching engine-side attachment interface.

The mount geometry remains present when an engine is removed, unless damage has physically destroyed the mount.

### Mount geometry components

A mount may contain:
- Hull root
- The reinforced region where the mount joins the hull.
- Service trunk or structural arm
- The visible structure spanning from hull to engine.

Engine attachment collar
- The interface surrounding or meeting the engine attachment plane.

Optional fairing
- Hull-owned geometry covering part of the transition.

Optional internal passage
- Required where internal engine servicing applies.

These parts may form one continuous mesh, but they should retain semantic identities.
Suggested roles:
- EngineMountRoot
- EngineMountTrunk
- EngineMountCollar
- EngineMountFairing
- EngineMountServiceAccess
- Civilian mount form
- Civilian mounts generally extend laterally from the hull and keep the engine axis parallel with the ship’s longitudinal axis.

Common shapes include:
- rectangular trunks;
- octagonal tubes;
- tapered structural arms;
- boxed service passages;
- short collars integrated into the engine casing.

For Small ships such as Aries, the mount need not contain a human crawlspace. 
It must still plausibly contain structural members, cabling, fuel handling and thermal 
transport connections.

### Military mount form
Military engines remain aligned with the ship axis.
The mount trunk angles approximately 30 degrees backward from the hull before meeting the engine, creating the characteristic 
lowercase-y silhouette.

The angled trunk is hull-owned mount geometry, not part of the generic engine family.

### Service-access dimensions
Medium and larger internally serviceable installations should provide a continuous passage through the mount where the design requires crew access.
Conceptual minimum clearance:
Width: 0.6–0.7 m (or up to 1 meters for larger ships)
Height: approximately 0.7 m (up to 2.5 meters for larger ships)

The passage does not need a fully modelled interior during the initial rendering implementation, but the exterior 
dimensions must leave plausible room for it.

### Mount standards and geometry

A mount standard defines more than compatibility metadata. It establishes the physical interface, including:
- attachment-plane dimensions;
- trunk connection envelope;
- collar dimensions;
- permitted structural-arm geometry;
- service-interface locations;
- maximum supported load;
- engine clearance envelope;
- exhaust keep-clear requirements.

Different hulls using the same standard may have differently shaped hull-side trunks or fairings, provided the engine 
attachment interface remains compatible.

Therefore:
The mount standard is shared; the complete visible mount structure is hull-specific.

This allows an H2 mount on Aries to differ visually from an H2 mount on another ship while 
accepting the same H2 engine variant.

### Engine removal
When an engine is absent:
- the hull-side mount remains visible;
- the attachment collar or exposed interface is visible;
- engine-owned geometry, lights and exhaust regions disappear;
- the remaining mount must still read as a real structural component.

Later systems may add:
- caps or protective covers;
- exposed connectors;
- damage;
- maintenance equipment.
These are deferred.

### Visual design rule
The mount should make the installed engine look:
- structurally supported;
- replaceable;
- mechanically connected;
- deliberately positioned.

It should not resemble:
- an invisible transform;
- a decorative strut with no volume;
- a thin rod incapable of carrying the engine;
- engine geometry intersecting directly into the hull without an interface.

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
- engine generation; - Done
- mount compatibility; - Done
- paired generation; - Done
- independent one-engine installation; - Done (Asterisk)
- mirrored geometry; - Done
- engine rendering; - Done
- exhaust placement; - Done
- engine lights. - Partly done

Runtime default construction is mount-count agnostic: `ShipBuilder` resolves each
hull-authored engine slot and installs one independent engine instance through
`EngineInstallationGenerator`. Aries currently supplies two Mule defaults; Asterisk
supplies one port-side Mule; Beren supplies four Needle defaults arranged as vertical
port and starboard pairs; Antega supplies four Atlas Civilian Drive defaults on H10 mounts,
also arranged as vertical port and starboard pairs. Atlas is a definition-owned 58.4 m
industrial engine with a substantial forward mount section, segmented main body, service
details, and an aft exhaust aperture. Pair-specific generation remains available where
mirrored pair validation is itself required. Debug pair cycling applies only to ships with
exactly one port and one starboard engine, avoiding ambiguous replacement on multi-engine
hulls.

Engine definitions own simulation-authoritative numeric values in SI units:

- `DryMassKg`;
- `ForwardThrustN`;
- `ManeuveringThrustN`;
- `RotationalTorqueNm`.

`EngineDefinition` requires positive finite mass and forward thrust, and non-negative
finite maneuvering thrust and rotational torque. Qualitative `EngineDesignIntent`
metadata remains descriptive only.

Provisional Stage-1 tuning:

| Family | Dry mass | Forward thrust | Maneuvering thrust | Rotational torque |
|---|---:|---:|---:|---:|
| Mule | 2,400 kg | 156,000 N | 78,000 N | 250,000 N m |
| Needle | 1,650 kg | 187,800 N | 93,900 N | 300,000 N m |
| Atlas Civilian Drive | 96,000 kg | 3,585,200 N | 896,300 N | 10,000,000 N m |

These are initial handling calibration values, not final economy, power, or balance data.
Installed engines contribute mass individually. Operational force and torque contributions
are transformed from engine-local forward (`-Z`) through each installation orientation and
summed by `ShipPropulsion`. Current runtime operational filtering uses
`1 - EngineInstance.DamageFraction`; wear is not yet a propulsion modifier. Rotational
torque is aggregated and published but current fixed pitch/yaw/roll behaviour does not use it.

Asterisk is explicitly authored as a designed single-engine layout. Its hull-owned
efficiencies are 0.75 forward, 0.75 maneuvering, and 0.60 rotation. The efficiencies do
not belong to Mule and do not activate when a normal multi-engine ship loses engines.

Gameplay systems such as:
- damage;
- replacement;
- economy;
- advanced thermal simulation;

can follow later.

## Engine Visual Design Notes

### Purpose
Engine visuals communicate propulsion characteristics, engine class, condition, and operating state.
Engines are large physical machines. Their visual appearance should reinforce their mechanical identity rather than simply represent a thrust value.
Exhaust is not a conventional rocket flame. It represents energy/plasma/field effects from the propulsion system.

### Engine Glow
All active engines have a visible idle glow.
An engine sitting powered but producing no thrust should not appear visually inactive.
Glow intensity changes with operating state:
State	Visual behaviour
Idle	Low, stable glow
Acceleration	Increased brightness
Maximum thrust	Strong glow
Boost	Very bright glow, possible colour shift and instability
Velocity correction	Increased intensity with flickering/pulsing behaviour

Glow characteristics are engine-specific.
Examples:
industrial engines: warm, broad, less refined glow;
military engines: intense, controlled glow;
high-end civilian engines: cleaner, smoother appearance.

Runtime ownership:
`SpaceSimulation` derives each installed engine's visual state from the commanded
propulsion activity. `EngineInstance` owns that mutable state, the ship snapshot copies
it for presentation, and rendering consumes only the snapshot. Rendering does not
inspect flight input or query simulation state.

Invariant:
Engine visuals represent commanded propulsion activity, not achieved vehicle
acceleration. Normal forward, reverse, lateral, and vertical acceleration commands all
use `Thrust` mode regardless of current velocity. Afterburner uses `Boost`.
Only active X-Stop correcting nonzero reference-relative velocity uses
`VelocityCorrection`. Visual state must not otherwise depend on ship velocity.

### Exhaust Effects
Exhaust is owned by the engine definition.
Each engine may define:
exhaust geometry;
colour;
spread;
particle behaviour;
intensity.

Initial visual concept:
idle: no visible exhaust particles;
acceleration: trailing exhaust/plasma;
boost: strong exhaust effect;
velocity correction: separate turbulent energy-dump effect.

Acceleration exhaust particles inherit ship velocity at creation.
This means exhaust naturally behaves correctly in space:
when accelerating, particles appear to move behind the ship;
when coasting, particles retain previous momentum.

Velocity correction uses a separate visual effect rather than simply reversing normal exhaust.

### Engine Visual Parameters
Engine definitions may contain visual parameters:

EngineVisualDefinition
{
    GlowColor;
    IdleGlow;
    MaxGlow;
    BoostGlowMultiplier;

    ExhaustType;
    ExhaustColor;
    ExhaustSpread;
    ExhaustParticleRate;

    VelocityCorrectionEffectType;
}

These values describe appearance only.
They do not define physics.

### Visual Identity
Engine appearance should communicate:
power;
refinement;
manufacturing origin;
military/civilian purpose.

Two engines with identical thrust values may look different because of:
technology generation;
manufacturer;
intended use;
cost.

A large cheap engine should look different from a compact advanced engine.

## Engine visual state invariant

Engine visuals represent commanded propulsion activity, not achieved vehicle motion.
The glow indicates what the pilot/flight system is asking the engines to do.

It must not depend on:
current ship velocity;
whether acceleration increases or decreases velocity;
speed limits;
speed tapering.

A ship travelling at maximum velocity and holding throttle should still show active engines.
Proposed state mapping

Priority order:

Boost active
    ↓
Boost visual

X-Stop active AND velocity non-zero
    ↓
Velocity correction / braking visual

Propulsion input active (WASD/RF)
    ↓
Normal thrust visual

No propulsion command
    ↓
Idle visual

Notes:
- Reverse acceleration is still normal thrust.
- Strafing is still normal thrust.
- Vertical thrust is still normal thrust.
- The engine does not know or care whether the ship is "winning" against its current velocity.
