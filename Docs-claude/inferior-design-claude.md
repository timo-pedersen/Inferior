# Inferior — Design Overview

> Compressed reference for Claude. Full version with rationale in docs/inferior-design.md.

---

## Project basics

| Item | Value |
|------|-------|
| Name | Inferior |
| Engine | MonoGame 3.8.4 |
| Language | C# / .NET 10 |
| Platform | Windows (for now) |
| Units | Metres, 0.01 m precision |
| Galaxy | 2048 stars, fixed seed, deterministic per-star seed from coords |
| Visual style | Low-poly flat-shaded 3D, original Elite aesthetic |
| Rendering | BasicEffect only — no custom shaders |

---

## Design philosophy

- Emergent complexity from simple interacting systems — no scripted special cases
- The ship is a *relationship*, not gear. Mastery > wallet.
- Simulate deeply, surface readably. Full fidelity under the hood, meters and warning lights on top.
- Losing is a story, not a punishment.
- Iterative build — get a triangle flying first, layer complexity on top.
- Do not lock design early. Avoid hardcoding anything that can be data-driven.

Key difference from Elite Dangerous: ED ships are a stat sheet you buy. In Inferior, your ship is a personal investment of knowledge and tuning time. A griefer can buy the same hull, but not the 200 hours of understanding.

---

## Rendering & aesthetics

Lighting targets (initial, intentionally left open for future improvement):

| Element | Target |
|---|---|
| Directional light | One per system, colour and intensity from star type |
| Ambient | 5–10% — space is dark |
| Specular | Ship hull, station solar panels, windows |
| Planet terminator | Self-shadowing sphere |
| Ship shadow | On nearby surfaces when landing |

---

## Flight & physics

### Newtonian flight with flight assist
Full Newtonian physics. No hard speed cap — the drive's standing-wave stability degrades at high offsets, giving a natural soft ceiling. "Flight assist off" available as an expert mode.

### The drive
Not a rocket. Creates a **standing wave of ionised metal ions** partially switched through A-band hyperspace. The asymmetry produces thrust.

- Fed by **metal rods** (reaction mass)
- Driven by generator power — more power = stronger wave = more thrust
- Both forward and reverse thrust from the same mechanism
- Rotation comes from gyroscopic effect of the drive — no separate thrusters
- Thrust ramps via **wave offset** — not instant
- Fuel (metal rods) consumption scales with offset
- Exhaust: **degenerate matter** (normal matter with hyperspace particles on nuclei)
- Degenerate deposits build up in exhaust — needs periodic cleaning; strange crystals can form
- Optional downward-thrust tube curve on most engines — enables faster upward pitch and planetary landing

### Gravitational field alignment
The drive aligns to the local gravitational field. The ship automatically inherits the reference frame of the nearest large gravitational body. This solves:
- The inter-system velocity discontinuity problem
- Natural speed cap: in deep space, alignment becomes unstable at high offsets

### Alpha red
When two drives run together: one as `+`, one as `−`. Alpha red flows between them. Nearly impossible to contain — leaks as red streaks, a visible tell of ship power state. Can be guided through carbon crystals to drive a gyro for extra rotational authority.

### Collision detection
- Swept tube/capsule for fast-moving objects (missiles, high-closing-speed ships)
- Simple sphere for slow/large objects (planets, stations)
- Speed-dependent: switch to swept when relative velocity exceeds threshold
- Per-panel hit testing — each hull face has its own hitbox

### Hyperspace interference (replaces mass lock)
Cannot initiate hyperspace jump when interference radius is exceeded. Radius based on ship size and power core output — not nearby object mass. Ships produce far more hyperspace interference than asteroids or planets; lore holds up.

### Fuel siphoning
Fuel can be siphoned directly from stars by opening a small hyperspace portal into the interior. Pressure is required to fill tanks — too shallow takes too long, too deep damages the siphon and risks the ship. Players learn the pressure curve by feel. `StarPhysics.cs` implements the pressure curve function.

---

## Power system

### Model: water flow
Generator = pump. Main bus = pipe. Consumers and capacitors draw from it. When demand exceeds supply, components are starved by priority.

**Units:** Rate = **watts** (J/s). Stored = **joules**. Each tick: `energy (J) = power (W) × dt`. A bus has both a throughput ceiling (watts — wire gauge) and a buffer (joules — tank). These are separate properties.

### Architecture
- One main power bus fed by reactor
- Converters tap the bus and produce specialised energy types (alpha red, weapon charge, shield V-band, etc.)
- **Battery-backed (outside simulation):** ship computer, panels/switches, lights, doors, displays, life support — never subject to power starvation
- Priority order: `Critical` (artificial gravity) → `High` (navigation) → `Normal` (weapons) → `Low` (luxury)
- `PowerPriorityManager` enforces starvation order; each consumer registers with a priority level
- **Capacitors buffer burst demand.** When firing a weapon, energy drains from the capacitor. **Fire rate is never hard-coded — it emerges from capacitor charge state.**

### No voltage / impedance simulation
Only watts flow, efficiency, and heat matter. Superconducting bus segments generate zero heat. Only converters and active consumers generate heat.

### Generator
- Throttleable 0–100% output
- Fuel consumption scales with output
- Heat generation follows a curve (not a cliff):

| Output % | Heat multiplier |
|----------|----------------|
| 0–60% | 1.0× (efficient range) |
| 60–80% | 1.5× (getting warm) |
| 80–90% | 2.5× (sustainable short-term) |
| 90–100% | 4.0× (redline, damage accumulates) |

### Cold start
Emerges naturally from simulation starting at zero — no scripted sequence. All units have startup timers or capacitors that charge from generator. Priority system determines order. Player can launch before shields are fully charged.

### Silent running

Two distinct signatures:
- **Thermal** — heat from hull/components. Hard to hide — physics. Active sensors detect this.
- **EM** — radiation from power currents. Controllable.

To go silent: throttle generator to minimum, run on capacitor reserves, cut non-essentials. EM drops dramatically but capacitors drain — hard time limit. When capacitors empty, generator must spin up and EM spikes.

Passive sensors detect EM only. Running silent against passive-only patrols works. Against active sensor sweeps it doesn't — the hull is still there thermally.

---

## Heat management

```
Component generates heat  (heat = power × (1 − efficiency))
    │  Heat stays local in component thermal mass until coolant removes it.
    │  When local thermal mass saturates → damage + heat signature.
    ▼
Coolant transports heat → HyperspaceHeatSink
    (efficiency scales with coolant level 0–1; rate per component capped by HeatFlowPerComponent)
    (coolant has NO thermal mass — pure transport medium)
    │
    ▼
HyperspaceHeatSink dissipates to hyperspace
    (rate proportional to fill: HeatDissipation × StoredHeatJ / CapacityJ)
    (equilibrium fill = heat inflow / HeatDissipation)
    (saturation → instant spike into realspace + EM burst — every passive sensor lights up)
```

**Implementation rules:**
- No passive heat dissipation via hull or individual components — all heat routes via coolant to sink only
- If coolant level is zero, cooling efficiency is zero — heat rises unchecked
- Damage → lower efficiency → more heat → worse cooling → more damage (spiral)
- Coolant leaks slowly; topped up at stations instantly; coolant system repair takes time

---

## Hull & damage

**The ship mesh IS the damage model.** Each polygon face is a hull element. Hit testing is per-face. No separate damage system.

Hull elements:
- Come in standard topological types (triangle, quad, large quad) — produced in standard sizes because post-processing exotic hull material is hard
- Have integrity, replacement cost, rarity, compatible hull types
- Stored in a **panel registry** — elements are referenced by ships, not owned by them
- The low-poly aesthetic is canon, not a limitation

**Visual states per face:**

| Integrity | State | Visual |
|-----------|-------|--------|
| > 0.8 | Intact | Normal hull colour |
| > 0.5 | Damaged | Slightly darker |
| > 0.2 | Critical | Visible scoring, heat discolouration |
| 0.0 | Failed | Black / transparent — open space |

**Angular hit factor:** Damage = cosine of incoming angle. Glancing hits deflect; 90° = full energy. Geometry IS gameplay.

**Internal component penetration:** Each internal component declares which hull faces protect it.
`Penetration probability = (1 − elementIntegrity)²`
Pristine panel → near zero. Destroyed panel → near certain. A ship can theoretically fly with all panels gone — it fails when internals fail or overheat.

**Component fragility:** Fuel tank = extreme (explosion propagates). Shield generator = high. Power bus = moderate. Structural frame = low.

### Shields
- Two per ship normally (top + bottom umbrella shape), each equippable separately with different stats
- **Always on** when started — never binary. Capacitor % drives everything.
- Damage reduction = `Capacitor × MaxReduction` (MaxReduction ~0.85 — even full shields let 15% through)
- Power draw increases as capacitor depletes
- Heat per hit increases at low charge → spiral risk
- Shield only "dies" if the shield generator hardware is destroyed
- **Cold start is the only dramatic shield event** — watching the needle climb from zero
- Ground contact or contact with other ships depletes the capacitor rapidly
- **Station rules:** shields prohibited — station won't open docking bay with shields up. An aggressive act.
- Shields have a radius — not all of the hull may be covered; uncovered panels receive full damage

---

## Commander & ship identity

Reputation, history, and consequences follow the **player**, not the ship. A criminal cannot buy a clean ship and start fresh.

**Sanity:** Degrades on emergency jumps, recovers at stations. Effects are subtle — never gameplay-breaking: HUD gauge flicker, comms distortion, NPC comments. Doctor treatment available.

**Ship loss & respawn:**
```
Hull integrity critical
    → [One second of chaos — alarms, failing systems, screen corruption]
    → Emergency beacon activates (mandatory, single-use)
    → Lovecraftian tunnel (25–30 seconds — not the clean jump)
    → Hard cut white
    → Insurance terminal
Commander in replacement ship:
    Sanity: −15%
    Log: blank
    Paint: standard white
    Panels: same grade, condition reset
    Equipment: preserved, condition 80%
```

**Calibration backup:** Ship tuning (power distribution presets, drive calibration, scanner sensitivity, HUD layout) saves automatically every time you dock. 95% transfers to replacement ship. The 5% that doesn't is intentional — gives the player something familiar to re-tune.

**Log recovery:** The lost ship's log remains in the wreck's computer. Player can return to the wreck location within 61 hours to recover it — salvagers clear the area after that. A risk/reward decision if the wreck is in hostile space.

**Captain's log:** Automatic entries (system entry, combat, anomaly discovery, trades). Each entry has in-game date, location, type (e.g. `Navigation`, `PlayerNote`), and text. Player notes attachable. Backed up manually at stations.

---

## DataBus architecture

One static hub, eight typed buses. Sim thread publishes; main thread drains once per `Game.Update()`. `CommandBus` runs in reverse (main thread publishes, sim thread drains at top of each tick).

Eight buses: `System` (status messages), `Instruments` (live doubles), `InstrumentState` (damage/efficiency), `InstrumentRanges` (min/max at startup), `Radar` (contacts), `RadarLost` (contact IDs), `Spectra` (spectrum scan results), `Target` (selected target changes).

Topic convention: `ComponentName.ValueName`. Multiple instances: `ComponentName_N.ValueName`.

---

## Galaxy & hyperspace

### Three hyperspace modes (open design)

| Mode | Range | Feel | Possible region |
|------|-------|------|-----------------|
| Flat hyperspace | Medium | 2D compressed space, gravity shadows, navigatable | Inner core |
| Tunnel hyperspace | Short | 1D corridor, lateral hazards, fixed exit | Outer arms |
| Pseudo-3D chase cam | Long / deep | Ship from behind, highest risk | Unknown regions |

Modes may correspond to different technologies, distances, or regions — not yet finalised.

### In-system jumps
Player targets body or free point. Ship aligns, executes. Interruptible (gravity well, damage, manual cancel). Drop-out distance varies with drive quality.

---

## Economy & careers

### Consumables

| Item | Used by |
|------|---------|
| Reactor fuel | Power core; siphon from stars (high risk/yield) |
| Metal rods | Engine (reaction mass) |
| Ammunition | Weapons |
| Repair materials | Hull patches, component repair |
| Coolant | Lost through leakage |

### Crystal anomaly career
1. Survey ship detects anomaly (specialist scanner required)
2. Deploy crystal seeding equipment
3. Return weeks later — harvest
4. Sell to foundry or process independently

Different anomaly types → different crystal properties. Discovery of new anomaly type has genuine economic value.

### Hull panel economy

| Grade | Source | Availability |
|-------|--------|-------------|
| Standard | Common anomaly types | Everywhere |
| Military grade | Licensed foundries | Restricted |
| Exotic | Known anomaly locations, often contested | Scarce |

Panel condition is visible externally — ship history readable at a glance.

---

## World state model

Two procedural + two exception tiers:

| Tier | Storage | Examples |
|---|---|---|
| Procedural baseline | Never stored — regenerated from seed | Stars, planets, NPC spawns |
| Exception list 1 | Stored | Designed systems, placed objects, custom moons |
| Exception list 2 | Stored (delta only) | Destroyed bases (permanent), crashed ships (decay timer) |

**Randomness-as-simulation:** NPC events seeded on `(systemID + timeWindow)` — the universe feels consistent without simulating it in full. Small debris (crashed ships etc.) decay after days/weeks of game time.

---

## Open design questions

- Hyperspace mode geometry for flat and tunnel types (gravity shadows, Voronoi topology)
- Faction / reputation system design
- Internal component hit probability — confirmed direction: `(1 − elementIntegrity)²`, not yet fully spec'd
- Multiplayer: not planned but architecture should not exclude it — no time compression in flight
- Generator fuel: nuclear (no refuel) or consumable? Undecided
- Lore epoch / time scale for in-game date display
