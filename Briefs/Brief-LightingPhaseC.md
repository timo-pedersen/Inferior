# Brief — Lighting pipeline Phase C: greeble casting, one class at a time

```text
Design authority: Docs/station-lighting-pipeline-spec.md (sections 6.2, 10 Phase C)
Branch:           continue on the Phase B branch
Baseline:         Phase B is visually confirmed and committed — hull-only casting,
                  CullNone, receiver-plane correction, 5 mm bias, F6–F9 diagnostics.
                  Do NOT touch the bias, the correction, or the comparison in this
                  phase. If a class appears to "need" a bias change, stop and report —
                  that is a diagnosis moment, not a tuning moment.
History:          this phase is where the 2026 experiment drowned (inconsistent small-
                  greeble shadows chased before the hull model was settled). The hull
                  model IS now settled, and classes land one at a time with a gate
                  each. Commit per stage.
```

## Goal

Station decoration casts shadows, class by class: pipes cast onto hulls, tanks onto
faces, dishes onto modules, containers onto everything — with every decoration class
carrying an explicit documented casting policy. "Some instances happen to cast" stays
unacceptable.

## Mechanism — class-tagged caster ranges (settled design)

Decoration passes run interleaved per face (`StationDecorator.Decorate`), so one
class's triangles are scattered through the deco mesh. Therefore:

1. `StationModuleMesh` gets a current-decor-class state (small enum, e.g.
   `DecorClass`) and records `(indexStart, indexCount, class)` ranges as geometry is
   appended. `Decorate` sets the class before each `Generate*`/`Place*` call — a
   mechanical wrap of the existing pass list; sub-helpers inherit the current class
   (tank labels tag as Tanks — fine, they ride their parent). Merged geometry
   (`MergeTransformed` — containers) tags with the class current at merge.
2. `Build()` additionally returns the recorded class ranges alongside the buffers.
3. The shadow system composes ONE caster index buffer per module from the ranges of
   all casting-enabled classes (plus the existing hull casters, unchanged). Rebuilt
   only when the enabled set changes — production cost stays one caster draw per
   module's deco mesh + one per hull, per frame.
4. Casting policy is a static readonly table: `DecorClass → bool casts`, with a
   comment per class stating WHY (this table is the spec's "documented casting
   policy" made executable).

## Class policy table (initial — the deliverable makes it code)

Casts, by rollout stage:

```text
C1 structural:  Pipes, SurfacePipes, PipeBrackets
C2 equipment:   Tanks (incl. caps/greebles/labels), Containers, Greebles,
                Chimneys, VentGrilles, SolarPanels (incl. RunSolarPanelPass)
C3 antennas:    Dishes + feed assemblies + RunLargeDishPass, Antennas + LandmarkAntenna
                (mast/boom/dish cast; see thin-element rule below)
C4 windows:     Windows (frames, braces, cupolas), Hatches
```

Never casts (documented exclusions, part of the same table):

```text
PanelSeams      — flat surface decoration, no height
EdgeTrim        — hugs the hull silhouette the hull already casts
Cables/fasteners/junction boxes — sub-texel thickness, proven inconsistent-caster
                  class in the failed experiment
Lights/lenses, bay guidance lights, placards/door decoration — tiny and/or emissive
Glass           — separate mesh, transparent, per spec
Landing-pad markings — flat; pad faces are kept clear anyway
```

Thin-element rule (C3): geometry whose cross-section is smaller than ~2 shadow texels
at typical fit cannot rasterize consistently into the map. The starter station fits at
~8 cm/texel (164×41 m fit), where yagi masts/booms are fine; individual thin yagi
elements may be excluded if they prove inconsistent — that is an allowed, documented
policy outcome, not a failure. Decide per evidence (F8 overlay), record the decision
in the table comment.

## Rollout — one stage per commit, gate before the next

For each stage C1→C4:

1. Enable the stage's classes in the policy table.
2. F8 overlay: the new class silhouettes appear in the map, attached to their
   parents, and nothing else changed.
3. In-engine gate (Timo, screenshots): footprints proportionate to the caster;
   contact shadows attached (no detachment — there is no normal offset in this
   codebase, so detachment would mean a real transform bug: stop and report);
   equivalent instances behave identically; hull shadows from Phase B unchanged.
4. F6 delta view spot-check on one newly-shadowed surface: still unstructured
   mm-noise, no new banding.
5. Commit the stage.

Debug support: add a caster-stage cycle key — proposal Ctrl+F6 (VERIFY it is free in
the input code first; F3/F6–F12/Ctrl+F12/Ctrl+C are taken) cycling
`Hull only → +C1 → +C2 → +C3 → +C4/all`, with the current stage published as a
SystemMessage. This is the per-class isolation tool for the gates above and stays
afterward as a diagnostic.

## Expected risks (so they are recognized, not debugged blind)

- Greeble now self-shadows: small faces at arbitrary orientations lean harder on the
  receiver-plane correction's grazing-angle clamp. If speckle appears ONLY on tiny
  faces of a specific class, isolate with the stage cycle + F6 and report with
  screenshots before touching anything global.
- Texel density varies with station size (fit is per-station): small stations ~8 cm/
  texel, a 2 km station ~1 m/texel — small-greeble shadows will be coarser there.
  In-spec; adaptive per-distance policy belongs to Phase F, not here.
- The caster pass gains real triangle volume (full deco meshes). Measure and report
  the map cost delta via the existing generation log line; expected still well under
  a millisecond, but the number goes in the report.

## Verification & reporting

- Builds; full suite passes; per-stage gates with screenshots (Ctrl+C workflow).
- Honest language per stage: implemented / builds / inspected / needs-Timo.
- Update `Docs-ai/!current-state.md` (Phase C row with per-stage status) and
  `architecture-map-ai.md` (DecorClass tagging, caster-IB composition, stage cycle
  key) after the final stage, plus the class policy table location.
- Anything contradicting this brief or the spec: stop and report. Bias, correction,
  and comparison code are frozen for this phase.
