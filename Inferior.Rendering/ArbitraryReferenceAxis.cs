using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Shared "pick a world axis that isn't near-parallel to this direction" primitive, used to
/// seed a cross product when building an orthonormal basis from a single direction (a face
/// normal, a pipe/truss long axis, a dish/antenna tilt axis, ...) with no other directional
/// input available.
///
/// A1 inventory finding: ~20 production call sites across Inferior.Game and
/// Inferior.Rendering each independently rewrote this exact one-line switch, and the
/// switch-to-avoid-parallel threshold drifted to three different values (0.85 / 0.9 / 0.99)
/// with no documented reason for the difference. This consolidates only that one line - the
/// basis each caller builds on top of it (which axis is "first", which cross-product order,
/// UV-projection basis vs. a box/pipe cross-section frame) genuinely differs by call site and
/// is deliberately left alone; every call site kept its own existing threshold rather than
/// being silently retuned onto a single "canonical" value, since a different threshold can
/// change the resulting basis orientation for a near-vertical input and that's a real
/// behaviour/visual decision, not pure refactoring.
/// </summary>
public static class ArbitraryReferenceAxis
{
    public static Vector3 For(Vector3 direction, float parallelThreshold)
        => MathF.Abs(direction.Y) < parallelThreshold ? Vector3.UnitY : Vector3.UnitX;
}
