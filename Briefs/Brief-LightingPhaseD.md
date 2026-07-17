# Brief — Lighting pipeline Phase D: FocusMap + free-object participation

```text
Design authority: Docs/station-lighting-pipeline-spec.md (sections 6.1, 6.2, 10 Phase D)
Branch:           continue on the shadow branch; Phase C is complete and committed
Frozen:           bias value, receiver-plane correction mechanism, comparison logic,
                  class policy table. The correction/bias become PER-MAP parameterized
                  (same mechanism, each map's own projection dims/texel size/span) —
                  that is parameter plumbing, not tuning.
Outcome:          the ship casts a shadow onto the station and receives the station's
                  shadow; free containers stop showing sunlit grooves on shadowed
                  faces; close-range shadows get crisp (cm-scale texels near the
                  camera). After this phase there are no "free object" shadow
                  exceptions left.
```

## The two-tier model (settled)

```text
StationMap (exists): 2048² Single, fitted to the station's caster geometry,
                     station-local metre space. Unchanged fit.
FocusMap   (new):    2048² Single, fitted to a cube of ~150 m half-extent
                     (tunable constant) centred on the camera position expressed
                     in station-local coordinates. ~7 cm/texel. Regenerated every
                     frame. Active only while the camera is within a threshold
                     of the shadowed station (e.g. 5 km — tunable).
```

Casters, per frame, same class policy for both maps:

- Into StationMap: station hull + enabled deco classes (as today) + free objects
  (ship, rails containers, calibration cube) — clipped naturally if outside the fit.
- Into FocusMap: the same caster set — station geometry and free objects alike are
  clipped naturally to the focus region by the projection.

Receivers: every lit surface — station geometry AND free objects — samples
**FocusMap when the fragment lies inside its bounds, else StationMap**, with a small
blend band (~2 m, tunable) inside the focus edge lerping between the two terms so the
texel-density hand-off has no visible seam.

## Free objects in station-local space (the key new mechanism)

The shadow shader works in station-local metres. Free objects live in universe space.
Per free object per frame, CPU-side, build `objectToStationLocal` from **authoritative
universe positions** (DVec3 subtraction of station position, then station-rotation
conjugate, THEN cast to float/Matrix — never derived from render-space matrices; the
render path's camera-relative translation must not leak into shadow math). This matrix
feeds the existing `ModuleToStationLocal` shader parameter — the shader does not change
shape for free objects; only the C# supplies a different matrix.

Applies to: ship (hull/nacelle/pylon draws), rails containers, calibration cube.
These switch to the shadowed techniques and appear in the caster passes with the same
matrix. When the shadow context is inactive (no station near), they fall back to the
unshadowed techniques exactly as today.

## Per-map parameters

The shadow lookup (UV mapping, texel-centre correction, bias) gains a second parameter
set for the FocusMap: matrix, minXY, invSize, texel size, near/span. Bias stays ONE
metres-constant (5 mm), converted per map by that map's own depth span. Correction
uses each map's own projection dims. No new tuning values beyond focus half-extent,
activation distance, and blend-band width — all named constants.

## Rollout — three stages, one commit + gate each

### D1 — FocusMap, station casters only

Plumbing: second render target, camera-centred fit, per-map parameters, shader map
selection + blend band. Free objects untouched.

GATE (Timo): close-range station shadows (greeble contact, bay door edges) visibly
crisper than Phase C at the same spot; walking the camera across the focus boundary
shows no seam or popping at the blend band (F7 binary view is the sensitive test);
far shadows unchanged; both maps visible in the F8 overlay (extend it to cycle
station/focus); map-cost log now reports both maps.

### D2 — free objects cast

Ship, rails containers, calibration cube drawn into both maps via
`objectToStationLocal`. Receivers unchanged (free objects still unshadowed
themselves).

GATE (Timo): ship shadow visible sliding across station surfaces (bay approach is
the showcase); calibration cube's shadow lands on the station when sun-aligned; a
rails container's shadow crosses hull greeble; F8 overlay shows the free objects'
silhouettes; zero-caster warning still silent for modules.

### D3 — free objects receive

Ship/containers/cube switch to shadowed techniques with per-object matrices.

GATE (Timo): the impossible-container artifact is gone (inset grooves dark when the
body shadows them); station structure casts onto the ship (fly into a module's
shadow — hull darkens coherently); the calibration cube shows the station shadow
sweeping across its labelled faces (screenshot-friendly by design); cube/ship
self-shadowing sane (nacelle onto hull at grazing sun).

## Coverage enumeration (invariants §13 — mandatory)

After D3, every drawn lit-object kind either samples the shadow maps or appears in an
explicit documented exclusion list (glass, glow sprites, planets/atmosphere, skybox,
UI/debug geometry). Add the assertion where object kinds are drawn — a debug-build
check or test enumerating draw paths — so the next new object kind cannot silently
join the unshadowed tier. This closes the "containers are still special" class of
drift for shadows permanently.

## Expected risks (recognize, don't debug blind)

- Blend band: a visible line at the focus edge means the two maps disagree beyond
  texel density — check per-map correction parameters before touching anything else.
- Free-object matrix path: any offset between an object's rendered position and its
  shadow position means render-space leaked into the station-local matrix (the DVec3
  rule above). The cube is the diagnostic: its shadow must touch its base
  contact-line when placed against a surface... it floats, so instead: F9 freeze and
  verify its shadow stays fixed to the station while the object spins in place.
- Per-frame cost doubles (two maps, plus free-object draws). Expected still trivial;
  the log line reports both — numbers go in the report.
- Ship self-shadowing may expose acne on the ship's small faces (short span map
  helps: 5 mm against a ~300 m focus span is proportionally larger). If speckle
  appears ONLY on free objects, report with F6/F7 screenshots — do not touch the
  bias.

## Verification & reporting

Per stage: builds, full suite, honest verification language, screenshots via Ctrl+C.
After D3: update `Docs-ai/!current-state.md` (Phase D complete; two-tier map model;
free-object participation; exclusion list location) and `architecture-map-ai.md`.
Anything contradicting brief or spec: stop and report.
