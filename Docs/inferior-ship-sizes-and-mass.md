# Inferior — Ship Size Classes & Mass Estimation

> Design reference. Ship size classes are defined by cargo-container capacity, not by
> analogy to real-world vehicles. Capital ships are explicitly out of scope (low
> priority; won't dock under normal circumstances). All dimensions are **maximum
> design envelopes** — actual hulls within a class are free to vary in proportion
> (wide vs. long) and need not approach the maximum in every dimension at once.

---

## Design method

Container: 2.5m × 2.5m × 6.0m (established elsewhere), stacked with zero gap between
containers ("no gaps" rule). Cargo hold is a cuboid sized to the container stack plus
a fixed 1m clearance on every side **except the floor** (containers sit directly on
deck). Crew area adds a flat **+6m** to length (frequently more in practice — this is
a floor, not a typical value). Engines add **+1.5m per side → +3m** to width (except
shuttles, which use smaller side-mounted engines).

Final class envelopes are rounded up from these rule-derived minimums to convenient,
distinctive round numbers — deliberately, not as a precision exercise. This gives
in-universe ship designers real freedom within each class while guaranteeing the
class's stated cargo capacity always physically fits.

**Landing pads** — two sizes, chosen to match the class envelopes exactly:
- **36m × 36m** — small and medium ships (small's 20×20 max footprint fits with margin)
- **36m × 72m** — large ships

The 36m width and 20m height are treated as lore-fixed constants (historical bay/pad
standard, inherited from an earlier Age) shared across all three pad-relevant classes.

---

## Class envelopes

| Class | Length | Width | Height | Containers | Engines |
|---|---|---|---|---|---|
| Shuttle | 10m max (6m typical) | 4m max (3m typical) | 4m max (3m typical) | None — passenger/crew only | Small, side-mounted |
| Small | 12–20m | 6–15m | 4–6m | 1–4 (1 tall × 1 long × up to 4 wide) | Standard |
| Medium | 26–36m | 17.5–36m | 12m | 8–30 (up to 2 tall × 5 wide × 3 long) | Standard |
| Large | up to 72m | up to 36m | up to 20m | up to 120 (up to 4 tall × 6 wide × 5 long) | Standard |

**Max design cuboids** (the hard ceiling for each class):
- Shuttle: 10 × 4 × 4
- Small: 20 × 20 × 6
- Medium: 36 × 36 × 12
- Large: 72 × 36 × 20

Rules can bend upward for dedicated specialists (e.g. a cargo-focused large hull
exceeding 120 containers) now that the outer envelope is fixed — the cuboid is the
constraint, not the container count. No hull is a flying cuboid; these are design
envelopes, not shapes.

**Carrying relationships**: Large ships can carry certain Small ships internally;
Medium ships can carry Shuttles. Internal bay sizing is a separate, later design
question — a carried ship's actual bay doesn't need to fit the *maximum* envelope of
its class, only whatever a specific hull's designers chose to build in.

---

## Mass estimation — method and assumptions

**Purpose**: ballpark figures only, for early engine/thrust and flight-model tuning.
Not intended as precise engineering.

| Assumption | Value |
|---|---|
| Container wall thickness | 4cm |
| Container shell density | 1.0 t/m³ (lightweight exotic alloy) |
| **Empty container mass** | **~5 tonnes** |
| Typical cargo density | 2.0 t/m³ |
| **Typical loaded cargo mass** | **~60 tonnes** (given) |
| **Container max rating** | **~100 tonnes** cargo (worst case) |
| Ship hull wall thickness | 5cm |
| Ship hull shell density | 1.0 t/m³ |
| **Systems mass multiplier** | **1× hull structure mass** (engines, reactor, life support, crew, racking — this is the roughest assumption here) |
| **Empty ship mass** | **2 × hull structure mass** |

Container loaded mass: **65 tonnes typical / 105 tonnes worst-case**.

Hull structure mass = outer surface area × wall thickness × density. Empty ship mass
doubles that to account for internal systems. Loaded mass adds container count ×
container loaded mass on top.

---

## Worked examples

| Ship | Envelope (L×W×H) | Containers | Empty mass | Loaded (typical) | Loaded (worst-case) |
|---|---|---|---|---|---|
| Shuttle (typical) | 6 × 3 × 3 | — | ~9 t | — | — |
| Shuttle (max) | 10 × 4 × 4 | — | ~20 t | — | — |
| Small — smallest | 14 × 8 × 4 | 1 | ~40 t | ~105 t | ~145 t |
| Small — largest | 20 × 20 × 6 | 4 | ~128 t | ~388 t | ~548 t |
| Medium — smallest | 20 × 12 × 8 | 8 | ~100 t | ~620 t | ~940 t |
| Medium — largest | 36 × 36 × 12 | 30 | ~432 t | ~2,380 t | ~3,580 t |
| Large — smallest* | 38 × 20 × 6 | 30 | ~222 t | ~2,170 t | ~3,370 t |
| Large — largest | 72 × 36 × 20 | 120 | ~950 t | ~8,750 t | ~13,550 t |

\* You didn't specify a minimum container arrangement for Large the way you did for
Medium (down to 2×2×2). I assumed a single-layer, full-footprint arrangement (1 tall ×
6 wide × 5 long = 30 containers) as a reasonable low end — notably the same container
count as Medium's own maximum, which reads as a sensible class-transition point
("smallest Large ≈ largest Medium, just spread into one layer"). Worth confirming or
replacing if you have a different minimum in mind.

**Sanity checks**: cargo mass dominates loaded mass at every scale except the smallest
(1-container) case, which feels right — an empty small utility hull shouldn't be
mostly cargo-shaped dead weight. The largest Large hull's cargo-to-empty ratio (~8–13×)
is in the same ballpark as real bulk carriers' deadweight-to-lightship ratios, which is
a reasonable sanity anchor even though the underlying construction (exotic alloy vs.
steel) is completely different.

---

## Open items

- Systems-mass multiplier (currently 1:1 with structure) is the single biggest lever
  in these numbers — worth revisiting once engine/reactor/life-support component specs
  exist and can be summed directly instead of estimated as a ratio.
- Large ship's minimum container arrangement — confirm or replace the assumption above.
- Internal bay sizing for carried Small ships / Shuttles — deferred, not yet designed.
- Capital ships — explicitly deferred per this session's discussion.
