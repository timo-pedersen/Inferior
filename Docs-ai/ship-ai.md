# Inferior — Ship Design Reference

> Compressed reference for AI. Full version in Docs/inferior-design-ship.md.

---

## Core philosophy

A ship is an empty shell. Almost everything is components — engines, sensors, power bus, gyro, weapons. A ship without a power bus cannot fly. This is simulation, not abstraction.

When acquired, a ship comes with a minimal component loadout — just enough to undock and fly. Everything beyond that is player investment.

Ships are persistent physical objects in the universe. A sold ship retains its history, captain's log, and wear. Someone buys a ship with a past.

---

## Ship identity

- ~20 fixed hull types planned. No player-designed hulls.
- A **ship instance** is a unique universe object. A **ship class** is the template.

Ship class determines: available component sizes, hardpoint locations, cockpit placement, base mass (hull only), mesh and visual identity.

---

## Size classes

Superseded by `Docs-ai/ship-sizes-and-mass-ai.md` for length/width/
height and mass — that doc's container-capacity-driven classes are authoritative. Four
classes, not five: **Shuttle, Small, Medium, Large**. Capital is explicitly out of
scope for now (low priority; won't dock under normal circumstances) — no confirmed
size exists for it, don't invent one.

| Size class | Length | Containers | Notes |
|---|---|---|---|
| Shuttle | 6–10m | none (crew only) | small, side-mounted engines |
| Small | 12–20m | 1–4 | |
| Medium | 26–36m | 8–30 | |
| Large | up to 72m | up to 120 | |

The table below (hull-type count per class, component slots, max component class)
predates this length redefinition — Small/Medium/Large used to span 20–250m, now
12–72m, and Shuttle didn't exist as a class before. Treat these as stale placeholders
that need recalibrating against the new ranges, not confirmed values:

| Size class (old) | Count | Component slots (approx) | Max component class |
|---|---|---|---|
| Small | 4 | ~6 | Class 2 |
| Medium | 6 | ~10 | Class 4 |
| Large | 4 | ~16 | Class 6 |
| Capital | 2 | ~24 | Class 8 |

Component class values were placeholders even before this — to be tuned per-hull once
component design finalises.

**`ShipSizeClass` enum exists and is stored on `Ship` but not yet enforced at component install time.** `ShipBuilder` accepts any component in any slot; validation belongs in the fitting screen, not the builder. A ship that doesn't meet requirements can't be built via normal factory path, but isn't illegal.

---

## Ship roles

| Role | Key distinguishing mechanic |
|---|---|
| Explorer | Long jump range; large reactor and fuel tanks; moderate cargo |
| Freighter | Large cargo; slow; minimal combat. Unique: EMP bomb (one-use, dropped, disables nearby ships briefly — one equipped at a time, must rebuy at station) |
| Combat | Balanced offence/defence; moderate cargo for loot and mission rewards |
| Luxury | High cost/maintenance; unique aesthetics; status symbol — not combat or cargo optimised |
| Utility | Repair ships, tugs, support vessels; not for combat or long-range |
| Mining | Specialised extraction equipment; large cargo hold; limited combat |
| Salvage | Cutting and towing equipment; enhanced sensors for locating salvage |
| Passenger transport | Luxurious accommodations; limited cargo and combat |
| Support | Electronic warfare, reconnaissance, medical aid; fleet operations role |
| Science & Research | Advanced sensors and labs; limited combat and cargo |
| Racing | Maximum speed and agility; minimal or no cargo |
| Smuggler | Stealth and evasion; hidden compartments; enhanced pursuit-detection sensors |
| Military | Heavily armed and armoured; limited cargo; slower than equivalent size classes |

---

## Equipment

All installed equipment is functional — no pure cosmetic items except paint. Customisation via:
- Hull element choice (grade, rarity, cosmetic matching)
- Equipment (shield antennas, scanners, weapons — all affect simulation)
- **Power circuit tuning** — the primary personal investment; what separates a veteran from someone who bought the same hull yesterday

---

## Turn rate

Calculated property, not a fixed value. Emerges from:
- Engine type and its built-in gyro capability
- Whether optional dedicated gyro is installed
- Ship mass (hull + components)

The drive always provides baseline rotational authority. A gyro component enhances it.

**Turn rate is asymmetric:** up-pitch can be faster than down-pitch and yaw, because most engines allow additional downward thrust (same vector used for planetary landing). Implement as a getter from day one even when stubbed — avoids refactoring later.

---

## Cockpit placement

Defined as a **vector offset from centre of mass** in ship coordinate space. The camera follows cockpit position; the ship rotates around CoM.

- Explorer: nose
- Large freighter: side or underneath (Millennium Falcon / ED Type-7 style)
- Military capital: far back, top of hull

Cockpit placement directly affects flight feel — a side-mounted cockpit on a freighter is a fundamentally different experience. This is a feature. Define on ship class; cheap to implement now, painful to retrofit.

Additional cameras planned but deferred — cockpit camera is the default; design should not preclude switching later.

---

## Hull system — vertex-first approach (in design)

Ships defined by vertices. Panels auto-generated from mesh faces. Sub-panel wireframe skeleton auto-generated from shared edges as slightly inset quads. External engines as separate component objects.

**Panel physical design:**
- ~5cm physical thickness
- Chamfered (45-degree beveled) edges creating V-groove seams
- Sub-panel framing is structurally distinct from hull panels (mundane structural vs exotic hull material — lore-justified, visually distinct)

The low-poly aesthetic is canon: exotic hull panels come in standard geometric sizes because they're hard to post-process. Ship designers work within the element grid like builders work within brick sizes.
