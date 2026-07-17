# Inferior — Project Invariants

> Durable rules for Claude Code, Codex, other Coding agents, and human contributors.
> These are not a feature backlog and not a record of current implementation status.
> When code violates an invariant, treat that as a design/code mismatch to investigate — do not silently extend the violation.

---

## 1. Document authority

Different documents answer different questions. Do not merge conflicting claims into a compromise.

### Current implementation

For what the code currently does, use this order:

1. Repository code
2. `Docs-ai/architecture-map-ai.md`
3. `Docs-ai/!current-state.md`
4. Active subsystem design references
5. Full historical design documents

### Intended design

For what the game should do, use this order:

1. Explicit instructions in the current task/session
2. This invariants document
3. The authoritative active subsystem design reference
4. `Docs-ai/design-ai.md`
5. Full design/lore documents for rationale and background
6. Archived documents only as historical context

### Conflict rule

- Never average, merge, or creatively reconcile conflicting sources.
- State the conflict explicitly.
- Use the higher-authority source for the current task.
- If code conflicts with a newer authoritative design, report a design/code mismatch; do not rewrite the design to match old code.
- Exact code identifiers, file locations, and APIs must be verified against the repository before use.

---

## 2. Simulation authority and thread ownership

The simulation thread is the intended owner of the mutable live universe.

- Mutable ship/world simulation state has one authoritative owner.
- The renderer and UI consume immutable snapshots or derived presentation data.
- Main/UI thread sends commands, player input, selections, and requests to the simulation; it does not send competing world truth back into the simulation.
- Do not create a second mutable copy of ship position, velocity, time, body state, or station state on another thread.
- Crossing a thread boundary does not make referenced mutable objects immutable. Snapshot contents must themselves be safe to read.
- A render snapshot is presentation data, not a second simulation model.

The current code may still violate parts of this intended model. When touching world-state flow, first identify who owns and writes each value. Do not spread dual authority into new systems.

Relocation-specific rules:

- All station relocation paths (new-game start, system-map arrival, debug station cycle) use the same simulation-owned canonical operation.
- Station destinations are identified by persistent identity (`PersistenceId`), not stale presentation objects.
- Relocation establishes position, reference-frame velocity, and facing coherently in one operation.
- Presentation code must not independently repair or reinterpret relocation results.
- Visual systems must not become authoritative sources for station position, orientation, velocity, or relocation.

---

## 3. Reference frames

Reference-frame changes are coordinate transformations, not physical impulses.

- Changing the reference body must not create false acceleration or velocity discontinuities.
- Keep clear distinctions between absolute/world values, simulation-frame values, and display-relative values.
- X-Stop and similar helpers operate continuously against a moving reference where required; they are not one-time velocity snaps.
- Never infer physical acceleration solely from a change caused by switching reference frames.

---

## 4. Units and numeric meaning

Simulation values use raw SI unless a design reference explicitly defines another unit.

- Distance: metres
- Velocity: metres/second
- Acceleration: metres/second²
- Force/thrust: newtons
- Power/rate of energy flow: watts
- Stored energy: joules
- Thermal capacity: joules/kelvin
- Temperature: kelvin internally; Celsius is presentation only
- Universe positions use `DVec3`/double precision where required

Rules:

- Never use a property name that hides whether a value is power, energy, temperature, or normalized load.
- Convert `power × dt` to energy explicitly.
- UI scaling and unit conversion belong in presentation code, not the simulation.

---

## 5. Thermal system — confirmed topology vs provisional math

Confirmed gameplay topology:

`component heat → coolant transport → central hyperspace heat sink → disposal`

Also confirmed:

- Components may have local thermal mass.
- Coolant is primarily a transport medium, not a second detailed fluid simulation.
- Heat management should remain much simpler than the power-distribution system.
- Internal temperature is represented in kelvin.
- Heat transfer must conserve energy.

Not yet an invariant:

- Exact heat equations
- Exact damage curve
- Exact sink saturation/reset behaviour
- Exact baseline temperature model
- Exact passive-cooling rules

Do not treat current thermal formulas as permanent architecture. When implementing transfer, the joules removed from one store must equal the joules added to another, limited by available energy and destination capacity/state.

---

## 6. Deterministic procedural generation

Persistent procedural identity must not depend on runtime-unstable hashing or unrelated RNG consumption order.

- Do not use `System.HashCode`, `HashCode.Combine`, object hash codes, randomized string hashing, or process-dependent hashes for persistent procedural seeds.
- Use an explicitly chosen stable hash/seed derivation with fixed input encoding.
- Derive child seeds by semantic identity rather than consuming one monolithic RNG stream for unrelated systems.

Examples:

- station: structure, module geometry, windows, pipes, cables, lights, wear
- container: geometry, colour, wear, markings, contents

Adding one random window must not reshuffle unrelated station structure or container properties.

Generator revisions that intentionally change output should be versioned when compatibility matters.

---

## 7. Procedural baseline and persistent deltas

Deterministic generated content should normally be regenerated, not serialized wholesale.

- Procedural baseline comes from stable identity/seed plus generator version where needed.
- Persist only meaningful changes that must survive regeneration.
- Examples: damage, destruction, ownership, discovered state, player effects, exceptional history.
- Do not save generated station geometry merely because a station is persistent in the universe.

A persistent object can have a regenerated baseline and stored deltas at the same time.

---

## 8. Persistence boundaries

- Live gameplay objects do not know persistence DTOs or serialization formats.
- Persistence records are snapshots at a boundary, not runtime truth.
- Serialization format must not leak into the domain model.
- JSON-specific types such as `JsonElement` must not become long-term domain/persistence abstractions if JSON is temporary.
- Save snapshots must be created at a coherent simulation boundary; do not enumerate mutable simulation state from another thread mid-tick.
- The simulation owner should produce an immutable save snapshot; IO may then happen asynchronously.
- Ship location belongs to universe state, not inherently to the ship configuration record.

---

## 9. Ship identity

A ship is a persistent physical object in the universe, not player inventory.

- A ship instance is unique and may retain history, wear, ownership changes, configuration, and captain's log association.
- A hull type is a template, not the ship itself.
- Sold or abandoned ships do not become fresh stat blocks merely because ownership changes.
- Geometry and component configuration should support the idea that a ship is a relationship and accumulated knowledge, not disposable gear.

---

## 10. Geometry and transforms

For generated 3D geometry, reason in explicit spaces before writing vertices.

- Distinguish points from directions.
- Positions receive translation; normals/directions do not.
- Define local coordinate conventions and outward directions before implementation.
- Parent/child transform composition order must be explicit.
- Attachment ports are independent semantic geometry: position, orientation/normal, compatibility.
- Child attachment normals oppose parent attachment normals at a valid connection.
- Mesh rendering, collision, decoration, and attachment logic must agree on transforms.

Useful invariants/tests where applicable:

- No generated vertex contains NaN or infinity.
- No generated triangle has near-zero area unless explicitly intended.
- Exterior face winding produces the expected outward normal.
- Connected port world positions coincide within tolerance.
- Connected port normals oppose within tolerance.

Do not use epsilon shrinking as the first fix for unclear geometry ownership or collision rules.

---

## 11. Rendering architecture

The depth-tier system and geometric detail system solve different problems.

- Render pass tier is about depth precision/range.
- `DetailLevel`/LOD is about geometric and rendering cost.
- Do not couple them merely because distant objects often use less detail.
- Preserve the current multi-pass depth architecture unless a task explicitly redesigns it from first principles.
- The game's darkness, silhouettes, lit windows, sparse glow, piping/cabling, and procedural industrial detail are part of its visual identity; do not casually normalize them toward generic bright readability.

Lighting-pipeline constraints (design agreed, implementation phased — see `Docs/station-lighting-pipeline-spec.md`):

- Vertex colour never contains a directional lighting term once Phase A lands; bake-time colour is albedo × AO (+ deliberate overrides) only.
- Any shadow system must use the same authoritative station-local transforms for caster, receiver, and visible draw.
- Receiver bias must never visibly move a contact shadow; large receiver normal offsets are forbidden as an acne workaround.
- Planetary/moon shadowing is an analytic eclipse term, never geometry in a shadow map.

---

## 12. Event, state, and telemetry semantics

Do not force every published value into one retention model.

Conceptually distinguish:

- Events: every occurrence may matter
- State: only the latest value matters
- Telemetry: bounded time-series history may matter

A publisher should not need to know whether a specific UI instrument is a needle or graph, but retention must be bounded and explicit somewhere in the infrastructure.

Do not build an unbounded queue that can grow forever because one consumer is slow.

---

## 13. Scope discipline for coding agents

- Read the relevant code before proposing architecture changes.
- Prefer the smallest coherent change that solves the actual problem.
- Do not rewrite neighboring systems merely because a cleaner design is imaginable.
- Do not preserve duplicated implementations when one authoritative path should exist.
- Do not add generic frameworks without a concrete second use case.
- Do not silently fix unrelated issues; report them separately unless they block the task.
- For visual/geometry work, preserve working style and iterate on the requested problem rather than replacing the aesthetic.

### No special cases keyed on identity

A recurring failure pattern in this project: the first instance of a new kind gets a
branch keyed on its identity (`Category == "docking-bay"`, a name check, a dedicated
render path), and every later instance of the same kind silently falls outside it.
Containers, octagonal modules, and ship rendering have each been through this cycle.

Rules:

- Branch on **capability**, never identity: `MeshFactory != null`, "has normals",
  "is closed mesh" — not category strings, names, or concrete types. If code needs an
  identity check to work, that is a design smell requiring explicit sign-off in the
  brief, with a comment stating why no capability expresses it.
- When a fix lands on a general pipeline, its coverage is defined by enumeration, not
  by the fixer's memory: prefer a test or runtime warning that iterates ALL instances
  (all modules, all mesh classes, all drawn object kinds) and asserts each one
  participates. "Every placed module yields a caster", "every mesh class has an
  explicit shadow policy", "every lit object renders through LitSurface" — coverage
  assertions of this shape catch special-case drift automatically.
- An object kind may only have its own generation/render path when the design
  explicitly says so (e.g. glass), and that exception is documented at the
  participation table/policy site, not discovered in a branch condition.

---

## 14. Verification and honesty

- Never mark visual behaviour verified unless Timo has seen it in-engine or a deterministic automated test proves the relevant property.
- Distinguish: implemented, compiled, test-passing, inspected, and visually confirmed.
- A mathematically plausible result is not the same as a visually correct one.
- When uncertain, say what is known, what is inferred, and what remains unverified.
