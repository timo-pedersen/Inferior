## 1. Retrospective: station lighting and shadows

### Starting point

The goal was a static station shadow system driven by the system star:

- stations cast shadows onto themselves;
- hull modules, windows, pipes, containers, antennas, and other greeble participate;
- shadows remain sharp enough to preserve station-scale detail;
- the station uses one fitted shadow map in station-local metre space;
- dynamic ship shadows and planetary shadows were deferred.

The initial implementation used a **2048×2048 8-bit `Color` render target** with explicitly encoded linear depth.

### What initially worked

The first version was visibly useful:

- major station modules cast recognisable shadows;
- the shadow direction broadly agreed with the star;
- the station silhouette in the light-camera view looked correct;
- both hull and at least some greeble participated;
- the result gave the station significantly more depth.

It was already a functioning shadow system, but with visible defects:

- acne and regular striping on low-angle faces;
- unstable or incomplete small-greeble shadows;
- some container and antenna shadows appeared detached from their casters;
- the 8-bit depth target imposed coarse depth steps over a roughly 150-metre fitted range.

The useful baseline was therefore:

> Working station-scale shadows with poor precision and bias behaviour.

### Where it began to regress

The first attempts concentrated on removing acne through bias and receiver displacement.

A large slope-scaled normal offset reduced striping, but introduced a new and worse visual error:

- attached containers appeared to float one to one-and-a-half metres above their surfaces;
- antenna and dish shadows moved away from their casters;
- light leaked around module silhouettes;
- the offset moved receiver lookups by as much as several shadow texels.

Precision was then increased:

- the shadow target changed to `SurfaceFormat.Single`;
- explicit linear depth was retained;
- the fitted depth range was tightened;
- bias values were greatly reduced.

That was theoretically correct, but the visible result became much worse:

- most shadows almost disappeared;
- fine acne remained;
- some greeble shadows became inconsistent or absent;
- it became unclear whether the problem was depth precision, transforms, caster rendering, receiver lookup, or bias.

A stale process initially complicated diagnosis because an old runtime still reported a `Color` target. A fresh process confirmed that the `Single` target was eventually active. Higher precision removed one weakness, but did not solve the fundamental striping.

### Diagnostic work that produced reliable conclusions

A substantial diagnostic ladder was added:

- light-camera solid silhouette;
- caster coverage;
- receiver UV grid;
- receiver and sampled-caster depth;
- raw depth delta;
- slope factor;
- module and mesh-class identification;
- caster-owner matching;
- selected-module hull-only depth;
- isolated bias and normal-offset modes;
- frozen shadow generation for exact comparisons.

Those diagnostics established several important facts.

#### Transform and coverage paths were broadly correct

- The light-camera solid silhouette and caster coverage agreed.
- Receiver UVs were coherent across modules and greeble.
- Receiver depth and sampled caster depth matched spatially.
- Caster and receiver paths used the same module transforms.
- The affected module geometry did not contain an obvious duplicate-triangle or transform defect.

#### The stripe defect was genuine hull self-shadowing

The recurring bands remained when the caster map contained only module #5’s twelve hull triangles.

That eliminated:

- decoration;
- glass;
- other modules;
- parent/child overlap;
- competing caster ownership;
- 8-bit quantisation as the immediate cause.

The stripes were caused by comparing a continuously interpolated receiver depth with a point-sampled caster depth representing the centre of a shadow texel on the same plane.

#### Receiver-plane correction solved the stripe mechanism

A Reach-compatible analytic receiver-plane correction was derived from:

- the light-space receiver normal;
- the orthographic projection dimensions;
- the normalised depth span;
- the actual shadow-UV Y inversion.

The analytic gradients matched gradients derived from transformed face corners to floating-point accuracy.

In frozen comparisons, the correction:

- removed the dense self-shadow striping;
- preserved genuine shadow boundaries;
- did not shift the parabolic antenna shadow;
- did not require a receiver normal offset.

This was the clearest successful result of the week.

#### The large normal offset caused detachment

Frozen comparisons showed that normal-offset-only and the then-current production combination were nearly identical.

The normal offset:

- moved receiver UVs by fractions of a texel to almost three texels, depending on face orientation;
- changed receiver depth by centimetres to more than ten centimetres;
- altered contact-shadow shape;
- produced silhouette leakage and peter-panning.

The existing depth bias contributed little compared with the receiver normal offset.

#### Zero bias and a 3-mm safety bias were visually equivalent

Two production-like previews were tested:

- analytic receiver-plane correction with zero bias;
- analytic receiver-plane correction with a constant 3-mm equivalent bias.

No reliable visual difference was found in the tested cases.

### What remained unresolved

The analytic correction solved the broad self-shadow striping, but the system was not complete.

Small greeble still showed inconsistent casting:

- some box shadows were much narrower or shorter than their casters;
- some small casters produced no visible shadow;
- similar geometry did not always behave consistently.

There was also an unresolved station-scale case around the undersides of modules #5 and #6:

- module #6 appeared plausibly shadowed by station mass;
- parts of module #5 appeared shadowed despite no obvious occluder;
- caster-owner diagnostics indicated that large regions sampled other-module ownership;
- the exact owning modules and geometric plausibility were not resolved.

At that point the investigation had become disproportionately expensive. The work was quarantined rather than continuing to accumulate experimental code and stale documentation on the main development line.

### Final assessment

What worked:

- the original 8-bit implementation proved the overall shadow-map concept;
- the shadow camera, transforms, and broad caster/receiver mapping were mostly correct;
- `SurfaceFormat.Single` was the correct precision direction;
- analytic receiver-plane correction successfully addressed same-plane striping;
- frozen comparisons and owner diagnostics became reliable tools.

What failed:

- treating acne primarily through a large receiver normal offset;
- assuming higher target precision alone would cure the problem;
- changing several precision, fitting, and bias variables before isolating the root cause;
- allowing experimental diagnostics and production behaviour to become intertwined;
- continuing into small-greeble problems before the broad receiver model was settled.

What remains worth preserving:

- the analytic plane-correction derivation;
- the frozen diagnostic facility;
- module/hull isolation;
- caster-owner inspection;
- the lesson that receiver displacement must not be used to conceal point-sampling error.

---

