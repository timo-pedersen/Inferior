# Inferior — Design Overview

> Living design document. Update as decisions are made or revised.
> Split into `inferior-classes.md` for code-level sketches.

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

---

## Design philosophy

**"Dwarf Fortress level simulation in space."**. 
Well that is a dream, but it's a north star for design decisions. The goal is a 
living-feeling universe with deep systems that interact in complex ways, but all 
emerging from simple rules and player choices. No dwarfs though, just spaceships.
Perhaps "Dwarf Fortress level emergent complexity from simple rules" is more accurate.

- Emergent complexity from simple interacting systems — no scripted special cases
- The ship is a *relationship*, not gear. Mastery > wallet.
- Simulate deeply, surface readably. Full fidelity under the hood, meters and warning lights on top.
- Losing is a story, not a punishment. The goal is attachment, not rage-quit.
- Iterative build — get a triangle flying first, layer complexity on top of a working foundation.
- Do not lock design early. Basic stuff first. Leave open for expansion and iteration. Avoid hardcoding anything that can be data-driven.

Key difference from Elite Dangerous: ED ships are a stat sheet you buy. In Inferior, 
your ship is a personal investment of knowledge and tuning time. A griefer can buy 
the same hull, but not the 200 hours of understanding how to run it at its ceiling.

---

## Flight & physics

### Newtonian flight with flight assist
Full Newtonian physics with a helper layer (like ED's flight assist, but lore-grounded). 
No hard speed cap — the drive's standing-wave stability degrades at high offsets, 
giving a natural soft ceiling. "Flight assist off" as an expert mode.

### The drive — not a rocket
Ship engines create a **standing wave of ionised metal ions** partially switched through 
a hyperspace plane. The asymmetry this creates in normal space produces thrust. 
Both forward and reverse thrust are possible from the same drive. Rotational 
control comes from a gyroscopic effect of the drive — no separate thrusters needed.

The tube which contains the standing wave can be slightly curved in one direction,
creating thrust in one direction perpendicular to the backwards / forwards axis. 
This allows for greater pitch in one direction, which will be up. Not all engines have
this feature.

- Fed by **metal rods** (reaction mass)
- Driven by the generator's power output (MW) — more power = stronger wave = more thrust
- Exhausts **degenerate matter** (normal matter with hyperspace subatomic particles attached to nuclei — alters material properties)
- The wave idles when the engine is on; thrust comes from adjusting the **wave offset** → thrust ramps, not instant
- Fuel consumption ramps with offset as matter becomes degenerate and is exhausted
- Some degenerate matter deposits on the exhaust — needs periodic cleaning
- Strange crystals can form in exhaust deposits (gameplay loop + lore detail)

### Gravitational field alignment
The drive aligns to the local gravitational field. The ship automatically inherits 
the reference frame of the nearest large gravitational body. This solves:
- The "hurtling through the galaxy at 200 km/s" problem
- Velocity discontinuity between star systems
- Newtonian stability near planets

Speed cap emerges naturally: at high speeds in deep space (weak gravity), alignment 
becomes unstable and inefficient.

### Alpha red energy (one of several types of energy available from ship reactor, systems and converters)
When two engines run together, they can produce **alpha red** — a special energy type 
that flows from plus to minus (one engine as +, the other as −). 
Nearly impossible to contain outside the space between the engines. 
Tends to leak, creating mostly harmless red streaks through air. 
Can be a visible tell of a ship's power state.

Alpha red can be guided through carbon crystals, or arrangement of graphene layers
to induce spin. This allows for extra control of gimbeling outside of what engines themselves
provide. Also diamond can be used for additional micro precision, but is expensive
and seldom used outside of precision scientific equipment, eg single-pixel early-universe 
telescopes, or precision mass production, eg when modifying matter on atomic scale industrially etc.

### Collision detection
- Swept tube/capsule detection for fast-moving objects (missiles, high-closing-speed ships)
- Simple sphere checks for slow/large objects (planets, stations)
- Speed-dependent: switch to swept detection when relative velocity exceeds threshold
- Ship geometry used for hit testing — each panel (in low poly ship) has separate a hitbox. Hull elements are the hit targets.

### Mass lock
Original text, obsolete but kept for spirit: 
Cannot initiate hyperspace jump while a sufficiently massive object is within range. 
Radius = sqrt(mass) × constant. Creates genuine tension — outrun, fight, or hide. 
One check, large gameplay consequence.

New text, replacing mass lock with hyperspace interference:
In-game lore will not use Mass lock, but hyperspace interference. A ship produce alot
of hyperspace related energy transfers, conversions and interfaces, causing interference. 
This disturbs hyperspace. Using mass as a lock mechanic breaks lore easily near an
asteroid or planet, a ship is much smaller. Instead, the interference radius is based on 
ship size and power state.

Likely this will be based on power core output.

---

## Power system

### Model: water flow
Generator = pump. Main bus = pipe. Consumers and capacitors draw from it. When demand 
exceeds supply, components are **starved by priority**.

Priority order is defined via a power supervisor system — each consumer registers with a 
priority level. When power is insufficient, the manager reduces power to 
lower-priority consumers first. This creates meaningful tradeoffs in combat and 
emergencies.

Default Priority order: `Critical` (life support) → `High` (navigation) → `Normal` (weapons) → `Low` (luxury).

Capacitors buffer burst demand. When firing a weapon a certain amount of energy is drained. 
Fire rate is **never hard-coded**, it emerges from charge state.

Also certain modules have a startup threshold — eg shield generator needs a minimum charge 
to start up, and won't start if the capacitor is too depleted. This creates tension in 
cold starts and emergencies, and the user may prioritize certain startups over others.

So shield startup may have the following sequence:
1. Assuming ship "turned on", ie Generator started.
1. Shield capacitor starts charging from generator output. [Message on system bus - Shield: initiating startup]
1. Once capacitor reaches 80%, shield generator can start up. [Message on system bus - Shield: Capacitor at 32% etc at regular intervals]
1. This drains the capacitor fully, and is a small sonic "boom" or impact on ship, ship might turn slightly from the surge of power, and a visual effect on the shield generator itself. [Message on system bus - Shield: Shield online]
1. Shield can now build up, [Message on system bus - Shield: Shield at 15% etc]

### No voltage/impedance simulation
Only MW flow, efficiency, and heat matter. Superconducting bus segments generate zero heat. Only converters and active consumers generate heat.

### Three buses
This section is inaccurate. 
The bus system is still being designed, but NOT the general idea is that there are 
three buses with different priority levels. There will be one main bus, which supplies
most of consumers. Other energy kinds will be produced by converters attached to main bus.

| Bus | Consumers |
|-----|-----------|
| High Power | Weapons, engines, shields (big, intermittent) |
| Low Power | Life support, sensors, comms (small, continuous) |
| Emergency | Minimal life support only — when main reactor fails |

Ship computer (ie panels, switches; not ship ai or advanced functions such as 
flight control, those require access to hyperspace and main power), lights, doors, 
and displays run on a **permanent battery outside the simulation entirely** — 
not subject to power starvation. No Half-Life flashlight syndrome.

### Generator

Produces main power. Fuel consumption and heat generation based on output level.
Fuel is a consumable. Generator can be throttled from 0–100% output. 
Higher output = more fuel, more heat.

Fuel type is a consumable, refuelling at stations. This creates a meaningful 
resource management loop. Similar to ED, fuel can be siphoned from stars interior.
A certain pressure is required to fill up fuel tanks, so siphoning from a star is 
dangerous but rewarding. Star siphoning is a high-risk, high-reward activity that 
can yield large amounts of fuel quickly, but also carries the risk of overheating 
and damage to the ship if not done carefully.

Lore behind siphoning - creating a small hyperspace portal to the core of the star.
At a certain depth the pressure is sufficient to fill fuel tanks. Going to deep damages 
the siphon and potentially the ship, but not going deep enough takes much longer time. 
Players learn the feel of it over time. Pressure curve function is implemented in 
StarPhysics.cs.

### Generator thermal curve
Not a cliff — a curve. Players learn the safe ceiling by feel.

| Output % | Heat multiplier |
|----------|----------------|
| 0–60% | 1.0× (efficient range) |
| 60–80% | 1.5× (getting warm) |
| 80–90% | 2.5× (sustainable short-term) |
| 90–100% | 4.0× (redline, damage accumulates) |

Generator is **throttleable** — player sets reactor output level. 
Higher output = more fuel + more base heat. Creates efficient cruising vs full 
combat power as a real tradeoff.

### Cold start sequence
Emerges naturally from the power simulation starting from zero. 
No scripted sequence needed. All units have a timer or a capacitor that charges from 
the generator, so startup sequence emerges from the priority system and startup 
thresholds.

1. Generator spins up
2. Artificial gravity capacitor charges (open for change)
3. Navigation, then sensors (open for change)
4. Weapons last (open for change)
5. System alert console logs each are connected to battery, and always on.

Player can launch before shields are fully charged.

### Silent running / EM signature
[Heat design still pending]

Two distinct signatures:
- **Thermal** — heat from hull/radiators. Hard to hide — physics.
- **EM** — radiation from power currents. Controllable.

To go silent: throttle generator to minimum, run on capacitor reserves, cut 
non-essential consumers. EM drops dramatically — but capacitors drain. Hard time 
limit: when capacitors empty, generator must spin up and EM spikes. Tense.

Passive sensors detect EM. Active sensors detect thermal + hull reflection. 
Running silent against passive-only patrols works. 
Against active sensor sweeps, it doesn't — your hull is still there.

---

## Heat management

### Thermal simulation, heat sink, and coolant system
Heat sink is NOT a radiator. 
A **hyperspace device** that dumps heat into a separate hyperspace plane.

```
Component generates heat and heats up locally
    │  (heat = power through × (1 - efficiency))
    ▼
Heat is transported via Coolant to Heat Capacitor (thermal mass with finite capacity)
    │  (Efficiency dependent on coolant level)
    ▼
Central thermal mass (heat capacitor)
    │  (Efficiency dependent on damage level and component stats)
    ▼
Heat sink (hyperspace device — requires power, does not generate heat for practical reasons)
```

When total heat generation exceeds coolant capacity, excess stays local. 
When local thermal mass saturates → damage + heat signature.

**Damage-efficiency feedback loop:** damage → lower efficiency → more heat → worse cooling → more damage. 
A spiral the player must manage by reducing load, improving cooling, or repairing.

Coolant system degrades under combat damage — hull breaches vent coolant, radiators 
get shot up.

Coolant is a consumable. The coolant loop is often leaking slightly, so it needs 
regular topping up at stations. System can also be repaired, but tuned in such
a way that slow leakages are often present and topping up is the most effective.
Perhaps coolant system repair takes some time, coolant top-up is instant.

Creates a meaningful resource management loop.

Implementation wise:
- All components and some other systems generate heat. Based on efficiency and 
damage state.
- Heat is transferred to a central thermal mass, a capacitor of sorts. 
Efficiency of this transport is based on the level of coolant fluid.
- Heat sink dissipates heat from heat capacitor into hyperspace, it just 
'disappears'. Has a finite capacity and flow rate. When heat exceeds capacity, it stays in the thermal mass, eventually causing damage and increasing heat signature.
- Coolant fluid level is just simulated as a number plus a leak rate.

## Hull & damage

### Hull element system
**The ship mesh IS the damage model.** Each polygon face is a hull element 
made from exotic hyperspace-doped material. Hit testing is per-face. No 
separate damage system needed — geometry and damage are the same thing.

Hull elements:
- Come in standard **topological types** (triangle, quad, large quad) 
fitting across different hull geometries
- Must be produced in standard sizes — hard to post-process. This is why ships have 
geometric designs: **canon, not a limitation**
- Have their own integrity, replacement cost, rarity, and compatible hull types
- Stored in a **panel registry** — elements are referenced by ships, not owned by them
- Panel condition visible externally — NPCs and other players can read a ship's history

**Visual states per face** (driven by element integrity):
| Integrity | State | Visual |
|-----------|-------|--------|
| > 0.8 | Intact | Normal hull colour |
| > 0.5 | Damaged | Slightly darker |
| > 0.2 | Critical | Visible scoring, heat discolouration |
| 0.0 | Failed | Black / transparent — open space |

### Angular hit factor
Damage applied as cosine of incoming angle. 
Glancing hits deflect most energy; 90° = full energy transfer. 
Angled panels deflect fire naturally — **geometry IS gameplay**.

### Internal component penetration
No 3D hitboxes. Each internal component declares which hull faces protect it.

Penetration probability when a hull face is struck = `(1 − elementIntegrity)²`

- Pristine panel → near-zero penetration chance
- 50% damaged → meaningful chance
- Destroyed panel → near-certain penetration

Internal components have **fragility** ratings. Fuel tank = extreme 
(catastrophic if hit, explosion propagates). 
Shield generator = high. Power bus = moderate. Structural frame = low.

Ship fails when internals fail or catastrophically overheat — not when all panels 
are gone. A ship can theoretically fly without panels at all.

### Shields
- **Always on** when started — never binary. Capacitor % drives everything.
- Damage reduction = `Capacitor × MaxReduction` (MaxReduction ~0.85 — even 
full shields let 15% through)
- Power draw increases as capacitor depletes (not settled)
- Heat per hit increases at low charge → low shields = thermal trouble even if capacitor 
never fully depletes
- Shield only "dies" if the shield generator hardware is destroyed
- **Cold start is the only dramatic shield event** — watching the needle climb from zero

**Station rules:**
- Shields prohibited at stations — station won't open docking bay with shields up
- Ground contact or contact with other ships depletes capacitor in reverse
- Trying to dock with shields up is an aggressive act; consequences follow

Shields are like two umbrellas, ie there are (normally) two shields per ship 
(perhaps big ships can have four), one top and one bottom.
These can be equipped separately, and have different stats. 
This creates a meaningful choice in shield configuration, and allows for 
interesting asymmetrical designs. Usually top side of hull is facing enemy, 
so top shield is more important, but some players may choose to invest in 
bottom shield for specific strategies.

Shields have a size. Not all of ship may be covered.
Uncovered shiled panels can receive damage. This creates a meaningful choice in shield 
coverage vs power investment.

---

## Commander & ship identity

### Philosophy
Reputation, history, and consequences follow the **player**, not the ship. 
Criminals cannot buy a clean ship and start fresh. Your ship is an expression of you.

### Ship models
~20 fixed hull types. No player-designed hulls. Heavy customisation through:
- Hull element choice (grade, rarity, cosmetic matching)
- Equipment (shield antennas, scanners, etc. — all functional, no pure cosmetics 
except paint)
- Power circuit tuning (this is the main personal investment)

### Commander state
Simple sanity system — deliberately light and atmospheric. 
Sanity degrades on emergency jumps, recovers at stations (faster at home station, resting).

Effects are subtle — never gameplay-breaking:
- HUD gauge flicker at low sanity
- Comms distortion
- NPC comments ("you look shaken")
- Doctor treatment available (expensive)
- Sleep in ship recovers slowly

### Ship loss & respawn flow
```
Hull integrity critical
    ↓
[One second of chaos — alarms, failing systems, screen corruption]
    ↓
Emergency beacon activates (mandatory, cannot be removed, single-use)
    ↓
[Lovecraftian tunnel — 25–30 seconds — chaotic, not the clean jump]
    ↓
[Hard cut — white]
    ↓
Insurance terminal:
"Hull loss confirmed. Replacement vessel prepared.
 Previous vessel last known position: [coordinates]
 Estimated salvage window: 61 hours"
    ↓
Commander in replacement ship:
  Sanity: −15%
  Log: blank
  Paint: standard white / unpainted
  Panels: same grade, condition reset
  Equipment: preserved, condition 80%
```

**Log recovery:** The lost ship's log remains in the wreck's computer. 
Player can return to the wreck location and recover it — a timed mission 
(salvagers clear it in ~61 hours). Risk vs reward: the wreck may be in 
hostile space.

**Calibration backup:** Ship tuning (power distribution presets, drive calibration, 
scanner sensitivity, HUD layout) saves automatically every time you dock. 
95% transfers to replacement ship. The 5% that doesn't is intentional — 
gives the player something familiar to re-tune.

### Captain's log
- Automatic entries from game events (first system entry, combat, anomaly discovery, 
notable trades)
- Player notes attachable to log entries and locations (doubles as waypoint system)
- Re-entry context when returning after a long break
- Backed up manually at stations — creating a meaningful choice after a loss
- Each log has an in-game date and location, type (eg navigation "Entering system: Antares", or PlayerNote), and a text.

---

## DataBus / instruments architecture

One static hub, multiple typed buses. Physics systems publish every frame or every few frames or 
with a fixed time interval. 

HUD widgets, damage system, and captain's log subscribe independently. 
**One publisher, many consumers — no coupling between systems.**

Sanity publishes to `DataBus.Instruments` — HUD widgets subscribe and 
occasionally render glitched values at low sanity. No special-casing.

---

## UI library

Custom retained-mode UI (not a generic WPF/WinForms clone — minimal and purposeful).

Controls: Button, Label, TextBox, Panel, Window (draggable container).

Per control: ForeColor, BackColor, TextColor, Font, FontSize, Size, Position, Visible, Focused.

Events: OnClick, OnFocus, OnMouseWheel (for bars, numeric up/downs).

Visual hover state on all controls.

`IUIRenderer` interface — swap the renderer to change the entire aesthetic. The game's space aesthetic is one renderer implementation.

Planned as a standalone library in future.

---

## Galaxy & hyperspace travel

### In-system jumps
- Player targets a body (planet, station, moon) or a free point in space
- Ship aligns automatically, executes jump
- Interruptible: gravity well too strong, "mass lock", damage to drive, manual cancel
- Drop-out distance varies with drive quality — better drives = more precise arrival
- No time-compress during jumps (multiplayer compatibility)

### Three hyperspace modes
(open discussion)

| Mode | Range | Feel |
|------|-------|------|
| Flat hyperspace | Medium | 2D compressed space, gravity shadows, navigatable — strategic |
| Tunnel hyperspace | Short | 1D corridor, lateral hazards, fast but fixed exit — tactical |
| Pseudo-3D chase cam | Long / deep | Ship from behind, highest risk, dangerous — immersive |

Modes may correspond to different technologies, distances, or regions of the galaxy. 
Inner core = flat, outer arms = tunnel networks, unknown regions = deep jumps.

---

## Economy & careers

### Consumables
| Item | Used by |
|------|---------|
| Reactor fuel | Nearly all power consuming components and instruments |
| Metal rods | Engine propellant (reaction mass + energy from generator) |
| Ammunition | Weapons |
| Repair materials | Hull patches, component repair |
| Coolant | Lost through leakage, topped up at stations |

### Crystal anomaly career
Hyperspace anomalies make crystal growth viable. Loop:
1. Survey ship detects anomaly signature (specialist scanner required)
2. Deploy crystal seeding equipment (expensive, slow)
3. Return weeks later — crystals have grown
4. Harvest and sell to foundry, or process independently

Different anomaly types produce different crystal properties (shield-affinity, drive-efficiency, etc). Discovery of a new anomaly type has genuine economic and scientific value — worth selling, worth fighting over, worth building a station near.

Career paths emerge with no scripting: anomaly surveyor, crystal farmer, independent foundry operator.

### Element / panel economy
- Standard elements: available everywhere
- Military grade: licensed foundry required
- Exotic doped elements: specific known locations, often in contested space

Geography of production creates trade routes, conflict zones, and exploration incentives. A player who discovers a new anomaly producing an unknown dopant has found something nobody has characterised yet.

**Player archetypes that emerge from one system:**
- The pragmatist — cheapest functional panels, always flying patched
- The hunter — specific rare panel sets, the ship is a trophy
- The trader — knows panel markets, arbitrages across systems
- The scavenger — salvages from derelicts, never pays full price

---

## Open questions (as of last design session)

- Internal component hitboxes, no: instead probability-based penetration from face integrity is the direction, but not fully spec'd
- Full hyperspace mode geometry for flat and tunnel types (gravity shadows, Voronoi topology)
- Faction / reputation system design
- Multiplayer: not planned but architecture should not exclude it — no time compression

