# Physical Cargo Containers and Attachment

This document defines cargo containers as physical world entities with identity, mass, contents, movement, attachment state, interface data and economic value.

Containers are not abstract inventory slots. Their contents may be represented numerically, but the container itself exists physically in the world.

---

# Purpose

Cargo containers should support:

- physical mass and movement;
- loading and unloading ships;
- attachment to ships, stations and other containers;
- declared and actual contents;
- ownership, manifests and access control;
- manufacturer identity and age;
- wear, damage, resale and recycling;
- beginner salvage gameplay;
- visible exterior identification.

A container may be:

- empty;
- loaded;
- free-floating;
- attached to a station;
- attached to a ship;
- attached to another container;
- legally owned;
- abandoned;
- valuable as cargo, equipment or scrap.

---

# Design goals

The system should:

- make cargo physically consequential;
- enforce both volume and mass limits;
- preserve stable container identity over time;
- allow containers to move independently in space;
- use one attachment model for ships, stations and container groups;
- expose data and commands through an in-world interface;
- support old, worn and defunct-manufacturer containers;
- allow empty containers to retain value;
- remain compatible with future economy, customs and salvage systems.

The system should not:

- turn contents into individually simulated physical objects;
- make attachment a UI-only inventory operation;
- merge container mass into a ship without preserving container identity;
- require a general rigid-body collision engine in the first implementation;
- treat military, government and border-control access as simple numeric encryption strength;
- make empty containers free.

---

# Canonical container standard

The canonical interspace cargo container has approximately:

- 30 m³ usable internal volume;
- enough internal height and width for people to enter and work inside;
- a hard maximum gross mass of 100 tonnes;
- two door faces carrying public mass displays and identification.

The 100-tonne limit is an interspace regulatory limit.

It includes:

```text
Container gross mass
    = tare mass
    + contents mass
```

Suggested initial calibration:

```text
Tare mass:               5 t
Maximum contents mass:  95 t
Maximum gross mass:    100 t
```

The final tare mass remains a balance decision.

A container must satisfy both limits:

```text
Used contents volume <= 30 m³
Gross mass <= 100 t
```

Dense cargo may reach the mass limit while leaving most volume unused. Light cargo may fill the volume long before reaching 100 tonnes.

The container dimensions should not be reduced merely to make ship mass easier to balance.

---

# Container ownership model

A container has four main groups of data:

```text
Container
    +-- Physical identity
    +-- Contents and manifest
    +-- Operational/interface state
    +-- Condition and economic state
```

The simulation owns the live entity and its physical/attachment state.

Definitions provide immutable data for container standards, manufacturers and cargo types.

---

# Physical identity

A container should have stable identity fields such as:

- persistent container ID / serial number;
- container-standard definition ID;
- manufacturer ID;
- manufacture date;
- tare mass;
- maximum gross mass;
- exterior dimensions;
- door-face orientation;
- brand style and exterior marking references.

The persistent ID must remain stable through:

- attachment;
- detachment;
- sale;
- transport;
- saving and loading;
- recycling until the entity is destroyed.

---

# Contents

Contents are simulated as numbers and metadata, not as individual objects.

A container may hold one cargo type initially. Mixed cargo may be added later if needed.

Suggested content state:

```text
CargoDefinitionId
DeclaredDescription
Quantity
UnitType
ContentsMass
UsedVolume
ManifestId or shipment reference
```

Examples:

- gold;
- bulk-packed personal devices;
- consumer home appliances;
- frozen hamburgers;
- industrial lubricant;
- refined metal feedstock;
- machine components;
- agricultural seed stock.

## Cargo definitions

A cargo definition supplies:

- definition ID;
- display name;
- default declared description;
- quantity unit;
- mass per unit;
- packed volume per unit;
- category;
- hazard classification later;
- legality/customs classification later;
- temperature/storage requirements later.

From those values:

```text
ContentsMass = quantity × mass per unit
UsedVolume = quantity × packed volume per unit
GrossMass = tare mass + contents mass
```

Loading must fail clearly when either mass or volume would exceed the container limit.

---

# Exterior public display

Each door face has an external display or marking area showing public information.

Minimum public display:

- actual current gross mass;
- container serial number;
- manufacturer name or mark;
- manufacture date;
- basic container-standard mark.

The actual mass display is authoritative and not merely declared cargo mass.

The two door faces should display the same essential identity and weight information.

Additional exterior appearance may include:

- manufacturer colours;
- font/style key;
- logo/mark;
- wear and repainting;
- legal inspection markings;
- owner/carrier markings later.

Exterior text and branding are presentation derived from container identity and manufacturer data.

---

# Attachment state

"Attached" is not a packing state. It is a physical relationship.

Use one generic attachment model:

```text
Free

Attached
    ParentEntityId
    ParentAttachmentPointId
    ParentLocalPosition
    ParentLocalOrientation
```

Valid parents may include:

- ship;
- station;
- cargo rack;
- another container;
- future tug or handling machine.

The attached container retains its identity and contents.

## Attached behaviour

When attached:

- its world pose is derived from its parent and local attachment transform;
- its mass contributes to the parent's mass aggregation where appropriate;
- its independent movement is disabled;
- attach/detach commands are simulation-owned;
- the attachment point may define allowed orientation and capacity.

## Detachment behaviour

When detached:

- it becomes an independent world entity;
- it inherits the parent's local point velocity;
- it inherits appropriate angular motion;
- its own mass and centre of mass become active independently;
- no discontinuous world-space stop is allowed.

Conceptually:

```text
Detached linear velocity
    = parent linear velocity
    + parent angular velocity × attachment offset
    + commanded release velocity
```

The first implementation may omit sophisticated release impulses, but it must preserve parent motion coherently.

## Container-to-container attachment

Containers may attach to one another through compatible faces or dedicated coupling points.

The resulting group may be represented as an attachment tree rather than merged into one entity.

The system must prevent:

- attachment cycles;
- one attachment point being occupied twice;
- exceeding attachment-point limits;
- illegal orientation when keyed attachment is introduced.

---

# Free-space physical state

A free container should have:

- world position;
- world orientation;
- linear velocity;
- angular velocity;
- current mass;
- collision bounds later;
- persistence identity.

Initial simulation can remain simple:

- inertial movement;
- no propulsion;
- no broad rigid-body collision response;
- no atmospheric behaviour;
- optional debug placement and impulse commands.

The existing debug containers can evolve into real container entities through this model.

---

# Container interface

Containers expose an electronic interface. Ships require a compatible interface module to communicate with them.

The ship-side interface may provide:

- scan nearby containers;
- read public identity;
- read actual gross mass;
- request declared manifest;
- request ownership/shipping data;
- attach;
- detach;
- lock;
- unlock;
- transfer custody later;
- submit customs credentials later.

Commands flow through the existing command-bus pattern.

The interface does not directly mutate presentation objects.

---

# Security and access

Encryption strength and access domain are separate concepts.

## Encryption strength

Suggested initial levels:

```text
None
Level1
Level2
Restricted
```

## Access policy/domain

Suggested domains:

```text
Public
Owner
Carrier
Customs
BorderControl
Government
Military
```

A container may expose different data or commands to different domains.

Examples:

- actual gross mass: public;
- manufacturer and serial: public;
- declared cargo manifest: owner/carrier/customs;
- attach/detach control: owner/carrier or current authorised handler;
- military manifest: military;
- border inspection record: border control/customs;
- government-sealed cargo data: government.

The first implementation should use deterministic game-rule credential checks, not simulated cryptography.

Military, government and border control are not merely higher numbers on one encryption enum.

---

# Manifest and declaration

A container distinguishes between:

- actual contents;
- declared contents;
- shipping manifest;
- public physical mass.

This allows future gameplay involving:

- incorrect declarations;
- smuggling;
- customs inspection;
- stolen cargo;
- sealed government cargo;
- carrier responsibility.

Initial implementation may keep actual and declared contents identical while preserving separate fields in the model where practical.

---

# Wear and damage

Wear and damage are distinct.

## Wear

Wear represents age and accumulated use:

- surface wear;
- latch fatigue;
- seal aging;
- display degradation;
- attachment-cycle history;
- general loss of resale value.

## Damage

Damage represents current physical faults:

- deformed shell;
- broken coupling;
- failed display;
- compromised seal;
- damaged door;
- reduced gross-mass rating later.

The first implementation may expose one condition value for simplicity, but the domain should not permanently equate wear with damage.

Suggested initial presentation:

```text
WearLevel: 0.0-1.0
DamageLevel: 0.0-1.0
```

Exact scale direction must be consistent and documented.

---

# Economic value and recycling

An empty container has value because it is a reusable manufactured object.

Value may depend on:

- manufacturer;
- age;
- container standard;
- wear;
- damage;
- ownership/legal status;
- market location;
- current demand later.

Possible outcomes:

- sell as a usable empty container;
- return to owner for a deposit or fee;
- sell to a carrier;
- recycle for material value;
- repair and resell later;
- retain for personal cargo use.

Abandoned empty containers can therefore provide plausible beginner income without being magical loot objects.

A recycling unit consumes the container entity and yields credits or recyclable material according to condition and mass.

---

# Manufacturer and date integration

Each container references:

- a deterministic manufacturer definition;
- a valid manufacture date within that manufacturer's active period.

The manufacturer registry and calendar are defined in `game-calendar-and-manufacturers.md`.

Defunct manufacturers remain valid because old containers persist.

A generated container must not receive a manufacture date outside the manufacturer's active range.

---
# RNG distribution and likelyhood

In game there are reasonably millions of Containers, which cannot be simulated or persisted.
Therefore container occurances has to be RNG spawned.

Older containers have an increasing rarity with age, to less than one percent from the first aeon, ~5% from the second.

---
# Persistence

Container persistence should include:

- persistent ID;
- standard definition ID;
- manufacturer ID;
- manufacture date as numeric calendar data;
- contents and quantity;
- actual and declared manifest data;
- wear and damage;
- security/access state;
- ownership/custody later;
- free-space physical state or attachment relation.

Attached containers persist by stable parent identity and attachment point where possible.

Loading must resolve invalid or missing parents coherently rather than silently deleting mass.

---

# Snapshot and UI requirements

The simulation should publish enough immutable data for inspection:

- container identity;
- manufacturer display data;
- manufacture date display value;
- gross, tare and contents mass;
- used and available volume;
- declared content where authorised;
- attachment state;
- wear and damage;
- access result;
- current owner/custodian later.

The UI must clearly distinguish:

- information that is public;
- information that is declared;
- information that is verified/actual;
- information that is inaccessible.

---

# Initial implementation phases

## Phase 1: physical container entity

- canonical container definition;
- persistent ID;
- tare mass and 100-tonne gross limit;
- free-space pose and velocity;
- one or two test cargo definitions;
- contents mass and volume validation;
- snapshot publication.

## Phase 2: attachment

- ship attachment points;
- station attachment points;
- generic parent/local-transform relationship;
- attach/detach commands;
- inherited motion on release;
- mass contribution to attached ship.

## Phase 3: interface

- public scan;
- identity and actual-mass readout;
- manifest request;
- attach/detach through a ship interface module;
- basic authorisation domains.

## Phase 4: identity and condition

- manufacturer registry integration;
- manufacture dates;
- exterior branding;
- wear and damage;
- resale and recycling values.

## Phase 5: richer logistics

- ownership and custody;
- shipping manifests;
- contracts;
- customs and border-control access;
- container-to-container groups;
- repair and inspection.

---

# Tests

Focused tests should cover:

- gross mass equals tare plus contents mass;
- loading fails above 30 m³;
- loading fails above 100 tonnes gross;
- dense cargo can hit mass before volume;
- light cargo can hit volume before mass;
- attachment preserves container identity;
- attached mass contributes to ship mass;
- detachment removes mass from the ship and preserves world motion;
- attachment cycles are rejected;
- public actual mass remains readable regardless of manifest access;
- manufacturer date falls inside active range;
- persistence round-trip preserves identity, contents, condition and attachment.

---

# Future extensions

- physical collision and impact damage;
- decompression and seal state;
- hazardous cargo;
- temperature-controlled containers;
- power-demanding containers;
- customs scanning and smuggling;
- theft and ownership disputes;
- container repair;
- specialised container sizes;
- cargo transfer between containers;
- grapples, tugs and handling drones;
- orbital decay or environmental hazards later.

---

# Design invariants

1. A container is a physical entity, not an inventory slot.
2. Contents may be numerical, but container mass and movement are real.
3. Both volume and gross-mass limits are enforced.
4. The 100-tonne limit includes tare mass.
5. Attached containers retain identity.
6. Attachment uses one parent/local-transform model.
7. Detaching preserves parent motion coherently.
8. Actual gross mass is public physical information.
9. Encryption strength and access domain are separate.
10. Empty containers retain economic value.
11. Wear and damage are conceptually distinct.
12. Simulation owns attachment and physical state.

---

===== End of main document =========================================

# Appendix A - Open decisions

- final exterior dimensions and tare mass;
- exact door and attachment-face geometry;
- whether one container can initially hold mixed cargo;
- quantity-unit representation;
- ownership/custody model;
- exact access-policy matrix;
- how attached container groups contribute to centre of mass;
- collision bounds and release clearance;
- initial resale and recycling formulas;
- how exterior text is rendered and cached;
- whether actual gross mass can ever be hidden by illegal tampering.
