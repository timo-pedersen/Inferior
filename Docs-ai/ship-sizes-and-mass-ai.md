# Inferior — Ship Size Classes & Mass Estimation

> Compressed reference for AI. Full version in Docs/inferior-ship-sizes-and-mass.md.

**This doc is authoritative for ship size classes and length ranges.**
`Docs-ai/ship-ai.md` previously defined a conflicting scheme
(Small/Medium/Large/Capital, 20–300m+) — it's been updated to defer to this doc's
classes (Shuttle/Small/Medium/Large, 6–72m; Capital deferred, unsized). That doc still
carries its own component-slot/hull-count figures, flagged there as stale pending
recalibration against these ranges.

---

## Method

Classes are sized around cargo-container capacity, not real-world analogy. Capital
ships out of scope. Dimensions are **max design envelopes** — actual hulls vary in
proportion and needn't hit the max in every dimension.

- Container: 2.5×2.5×6.0m, stacked with zero gap.
- Cargo hold = container stack + 1m clearance on every side except floor.
- Crew area: +6m to length (floor, not typical value).
- Engines: +1.5m/side → +3m to width (shuttles use smaller side-mounted engines).
- Final envelopes rounded up to round numbers, not left as raw minimums — guarantees
  stated cargo capacity always fits while leaving in-universe designers freedom.

**Landing pads** (36m width / 20m height lore-fixed): 36×36m (small/medium), 36×72m
(large).

## Class envelopes

| Class | Length | Width | Height | Containers | Engines |
|---|---|---|---|---|---|
| Shuttle | 10m max (6m typical) | 4m max | 4m max | none (crew only) | small, side-mounted |
| Small | 12–20m | 6–15m | 4–6m | 1–4 | standard |
| Medium | 26–36m | 17.5–36m | 12m | 8–30 | standard |
| Large | up to 72m | up to 36m | up to 20m | up to 120 | standard |

Max design cuboids: Shuttle 10×4×4, Small 20×20×6, Medium 36×36×12, Large 72×36×20.
Rules can bend upward for cargo-focused specialists — the cuboid is the hard
constraint, not the container count. Hulls aren't flying cuboids.

**Carrying:** Large can carry some Small internally; Medium can carry Shuttles.
Internal bay sizing is separate/later — doesn't need to fit the class max.

## Mass estimation (ballpark only, for engine/thrust tuning)

| Assumption | Value |
|---|---|
| Container wall / density | 4cm / 1.0 t/m³ → **~5t empty** |
| Cargo density (typical / max) | 2.0 t/m³ → ~60t typical / ~100t worst-case |
| **Container loaded mass** | **65t typical / 105t worst-case** |
| Hull wall / density | 5cm / 1.0 t/m³ |
| Systems multiplier | 1× hull structure mass (engines/reactor/life support/crew — roughest assumption here) |
| **Empty ship mass** | **2 × hull structure mass** |

Hull structure mass = outer surface area × wall thickness × density. Loaded mass =
empty + (container count × container loaded mass).

## Worked examples

| Ship | Envelope (L×W×H) | Containers | Empty | Loaded typical | Loaded worst-case |
|---|---|---|---|---|---|
| Shuttle typical | 6×3×3 | — | ~9t | — | — |
| Shuttle max | 10×4×4 | — | ~20t | — | — |
| Small smallest | 14×8×4 | 1 | ~40t | ~105t | ~145t |
| Small largest | 20×20×6 | 4 | ~128t | ~388t | ~548t |
| Medium smallest | 20×12×8 | 8 | ~100t | ~620t | ~940t |
| Medium largest | 36×36×12 | 30 | ~432t | ~2,380t | ~3,580t |
| Large smallest* | 38×20×6 | 30 | ~222t | ~2,170t | ~3,370t |
| Large largest | 72×36×20 | 120 | ~950t | ~8,750t | ~13,550t |

\* Large's minimum container arrangement is an unconfirmed assumption (1×6×5 = 30,
same count as Medium's max) — no stated minimum exists for Large the way Medium has one.

## Open items

- Systems-mass multiplier (1:1 with structure) is the biggest lever in these numbers —
  revisit once component specs exist to sum directly.
- Large's minimum container arrangement — confirm or replace.
- Internal bay sizing for carried ships — deferred.
- Capital ships — deferred.
