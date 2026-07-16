# Documentation cleanup plan (after shadow work)

I would keep the retrospective and design specification separate from current-state documentation.

## New documents

### `docs-ai/station-lighting-shadow-retrospective.md`

Use the retrospective above.

Purpose:

- preserve what was learned;
- explain why the work was quarantined;
- prevent the same diagnostic path from being repeated;
- record which conclusions were proven and which remained uncertain.

### `docs-ai/station-lighting-shadow-design.md`

Use the design specification above.

At the top:

```text
Status: Deferred
Implementation branch: wip/station-lighting-shadows
Stable gameplay branch: recovery/no-station-lighting-shadows
```

Update branch names if the recovery work chooses different names.

## `docs-ai/!current-state.md`

This must describe only the recovered gameplay branch.

Remove or rewrite claims that station shadows are currently active.

Suggested station-rendering entry:

```text
Station lighting/shadows:
- Deferred.
- Stable branch currently uses the pre-shadow station rendering path.
- The complete experimental investigation is preserved on
  wip/station-lighting-shadows.
- Design and retrospective:
  station-lighting-shadow-design.md
  station-lighting-shadow-retrospective.md
```

Add the retained QoL state:

```text
Station navigation:
- New games start near Far Station for rapid inspection.
- Station-relative relocation is simulation-owned and addressed by station PersistenceId.
- Relocation applies a defined stand-off distance.
- Ship velocity is matched to the destination reference frame.
- Ship orientation is set to face the destination station.
- Map arrival uses the same canonical relocation path.
- A debug station-cycle control relocates between stations.
- Mouse-look state is rebased when entering gameplay to avoid stale input orientation.

Flight controls:
- Harmony selection may be changed during slipstream acceleration.
- X-Stop may be selected during afterburner.
- X-Stop damping does not begin until afterburner thrust has completed.
```

Do not document the station-cycle key until it is checked against the recovered code.

## `docs-ai/stations-ai.md`

Keep:

- station generation;
- station persistence identity;
- station position/orientation;
- canonical station-relative relocation;
- debug navigation;
- map arrival;
- startup-near-station behaviour.

Remove or mark deferred:

- `StationShadowMap` as an active component;
- F8/F9 shadow diagnostics;
- analytic correction as current production;
- `Single` shadow target as current stable behaviour;
- shadow rebuild policy as implemented;
- freeze controls.

Add a short deferred-design section linking to the two new documents.

The relocation section should make clear that there is one canonical path rather than separate startup, map, and debug implementations.

## `docs-ai/ship-ai.md`

Update flight-control state:

- slipstream acceleration no longer prevents harmony retargeting;
- X-Stop selection is accepted during afterburner;
- selecting X-Stop does not prematurely cancel afterburner;
- damping becomes effective after afterburner completes;
- relocation sets station-relative velocity and facing through simulation authority.

Remove old statements that imply these inputs are rejected or delayed at selection time.

## `docs-ai/docking-ai.md`

Update any arrival description so map and debug relocation:

- use the station’s persistent identity;
- resolve the current live station location;
- arrive at the canonical stand-off;
- face the station;
- match the appropriate reference-frame velocity.

Clarify that this is relocation/navigation support, not completed automatic docking.

## `docs-ai/design-ai.md`

At high level:

- station shadows are deferred, not part of current stable presentation;
- the desired visual intent remains;
- link to the dedicated design document;
- do not duplicate the detailed shadow experiment.

Add the principle:

```text
Visual systems must not become authoritative sources for station position,
orientation, velocity, or relocation.
```

## `docs-ai/architecture-map-ai.md`

On the recovered branch:

- remove `StationShadowMap` and shadow-effect files from the active architecture map;
- retain them only in a note describing the quarantined branch;
- document the actual station-relocation entry points;
- document where flight input accepts harmony and X-Stop changes;
- note that main/UI sends commands while simulation owns relocation and live ship state.

## `docs-ai/components-ai.md`

Remove shadow-specific active component descriptions if the components do not exist on the recovered branch.

Update only components whose responsibilities changed through the QoL work:

- station relocation command/path;
- station cycle debug input;
- map-arrival caller;
- flight-control input state.

Avoid reproducing class-by-class details already present in the architecture map.

## `docs-ai/!invariants.md`

The authority invariant remains unchanged:

```text
Main/UI → simulation:
- input and commands only.

Simulation → Main/UI:
- immutable snapshots only.

Simulation:
- sole authority for mutable live ship, station, system, relocation,
  velocity, and orientation state.
```

Add relocation-specific invariants:

```text
- All station relocation paths use the same simulation-owned canonical operation.
- Station destinations are identified by persistent identity, not stale presentation objects.
- Relocation establishes position, reference-frame velocity, and facing coherently.
- Presentation code must not independently repair or reinterpret relocation results.
```

Add shadow-design invariants only as deferred constraints, not current implementation claims:

```text
- A future shadow system must use the same station transform for caster and receiver.
- Receiver bias must not visibly move contact shadows.
```

## `CLAUDE.md`

Add branch awareness:

```text
Current stable work must not assume station shadows exist.

Station lighting and shadow experiments are quarantined on:
wip/station-lighting-shadows

Do not copy experimental shadow code back into the stable branch without an
explicitly scoped task based on station-lighting-shadow-design.md.
```

Update or remove stale worktree instructions. The old worktree folders have been removed; only branch refs may remain.

## Other documents

`lore-ai.md`, `flat-hyperspace-ai.md`, `containers-ai.md`, and `ship-sizes-and-mass-ai.md` should not need changes unless they contain direct claims about the modified controls or station-shadow implementation.

## Final documentation audit

After edits, search the entire AI-doc set for:

```text
StationShadow
shadow map
F8
F9
Ctrl+F11
Ctrl+Shift+F
FROZEN
normal offset
analytic plane
Far Station
station cycle
slipstream harmony
X-Stop
afterburner
map arrival
relocation
worktree
```

Every match should be classified as:

- current stable behaviour;
- deferred design;
- historical retrospective;
- stale and removable.

I have not edited the repository files in this chat. The text above is ready to become the two new documents and the cleanup checklist for the recovered branch.
