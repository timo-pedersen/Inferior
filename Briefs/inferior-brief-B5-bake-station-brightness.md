# Brief B5 — Bake Station Brightness Values

> Short. Closes the station-brightness thread. Timo has tuned across red, white, yellow and
> blue star systems and on a deliberately colourful station; values below are settled.

## The values

| Parameter | Value | Note |
|---|---|---|
| Ambient | **0.1** | up from 0.09 — small, but **not cosmetic**; see below |
| Variant value floor | **0.2** | up from P1's 0.15 |
| Variant compression | **1.0** | full |
| Saturation falloff | **1.0** | full coupling |
| **Decoration brightness multiplier** | **1.0** | **untouched — leave at default** |

**Single source of truth:** all five live in one named constants block, and the panel reads
its initial values from that same block (the B3 rule — panel and defaults cannot drift, and
a future constants edit is immediately live in the panel).

**Keep the panel.** It earned its place and tuning may continue.

## What the values mean, for the record

The decoration multiplier staying at **1.0** is the headline. Decoration was never globally
too dark — the ~0.50 vertex-colour multiply is real arithmetic but was not what Timo was
seeing. **This was a variant-tail problem:** greeble only ever read as invisible on modules
that rolled a near-black texture (Nova Anchorage's bay rolled a post-offset `BaseColour` of
literal (0,0,0), ~17 mean luminance against a ~130 median).

**Ambient 0.09 → 0.1 is small but genuinely load-bearing** — Timo reports it is clearly
visible on certain stations, Nova Anchorage among them. Ambient is a *floor*, so its effect
on a surface is inversely proportional to how bright that surface already is. On a
median-luminance module the diffuse term dominates and ambient is noise; on the near-black
bay (texture ~17, whole surface computing to single-digit luminance) ambient is effectively
the **only** light, so an 11% change in it is an 11% change in everything. Human brightness
discrimination being roughly logarithmic compounds this: a small absolute step at the bottom
of the range reads clearly, while the same step near the top is invisible.

So it is not that some stations are ambient-sensitive by nature — **dark surfaces are
ambient-sensitive**, and Nova Anchorage has one. Same root cause as the rest of this thread,
surfacing through a third channel.

**Comment this in the constants block.** `0.1` must not read as a rounded-up default that
someone later nudges casually; note that it was tuned deliberately and that small changes are
visible on dark-variant modules.

Compression at full strength does the real work — lifting the lower tail *smoothly*, so dark
modules become legible while staying distinguishable from each other. Nova Anchorage's dark
module now reads as **a genuinely dark module rather than disappearing into black**, which is
the correct outcome: the variance from S2b-2 is preserved, only the pathological end is
fixed.

Saturation falloff at 1.0 fully decouples brightness from apparent vibrancy — lifting HSV
value no longer intensifies colour. Over-saturated red-star stations are improved.

## Verification

1. Build clean, full suite passes.
2. Panel initial values match the baked constants exactly — same source, no duplicated
   literals.
3. Launching fresh gives the tuned appearance with no panel interaction.
4. Determinism unchanged: same seed plus same constants gives the same textures.
5. **Timo's gate:** Nova Anchorage's mega bay reads as a dark-but-legible module while the
   rest of the station is unchanged; a colourful station is not over-saturated.

## Non-goals

- No further tuning by hand.
- No decoration brightness change — 1.0 is the answer, not a placeholder.
- Nothing from the follow-up list below.

## After B5

**Greeble texture families** is next and now well-motivated by Timo's own framing:
*greeble should read like any other textured surface, but should not inherit texture or
brightness from the module it sits on.* Decoration currently samples `mod.TextureInstance`,
so it inherits both the colour bleed (a green module tinting its pipes green) and the
variant's darkness. Mechanism fork still open — atlas vs split draws vs a shader flag — and
note that **not all decoration should be decoupled**: panel seams, edge trim and chamfers
read as part of the wall and should keep wearing the module's identity.

Then, roughly in order:

- **Architectural-vs-equipment decoration audit** — chamfers are currently affected by the
  decoration multiplier and arguably belong on the hull path; Timo likes the resulting
  variety, so this is deferred but recorded.
- **Planet baked-terminator fix** — planet lighting is baked into the sphere's local frame at
  construction, so a rotating or orbiting planet's terminator stops tracking the star. Gets
  visibly wrong within hours of play.
- **Lighting distance falloff + exposure adaptation.** Neither exists; they arrive together.
  **Design note from Timo's partial-eclipse observation:** adaptation should not merely
  restore brightness. As illumination falls, **reduce saturation and raise contrast**,
  mimicking rod-dominated adapted vision — the strange, cinematic quality of eclipse light.
  Outer-system light should feel *odd*, not just dim, giving distant orbits their own visual
  identity.
  **Caution:** once ambient becomes distance-dependent rather than constant, **dark-variant
  modules will be the most sensitive surfaces in the game to that curve** — a 0.01 change is
  already visible on them today. Whatever adaptation does at low illumination, they will show
  it first and most, so gate the curve against a dark-variant station, not a median one.
- **Dark-side legibility** — station light spill as a local ambient contribution, brighter
  emissive lights (wishlist #8: too dark, too short-range, clips into geometry), skybox haze.
