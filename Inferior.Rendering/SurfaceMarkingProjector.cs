using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Defines a projected surface marking: a 2D coordinate frame plus a finite projection depth,
/// used to project text/graphics from a shared atlas onto arbitrary receiver geometry without
/// modifying that geometry. See <see cref="SurfaceMarkingGeometry"/> for the clipping/
/// tessellation step that consumes this against real receiver triangles.
///
/// Origin is the marking's lower-left-near corner: u (along Right) spans [0, Width],
/// v (along Up) spans [0, Height], w (along Forward, the projection direction) spans
/// [0, Depth]. AtlasRegion is the (uMin, vMin, uMax, vMax) sub-rectangle of the shared atlas
/// texture this marking samples — most markings share one atlas rather than each owning a
/// unique texture.
/// </summary>
public readonly record struct SurfaceMarkingProjector(
    Vector3 Origin,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    float Width,
    float Height,
    float Depth,
    Vector4 AtlasRegion,
    Color Tint)
{
    /// <summary>
    /// Derives a proper-handed (Right, Up, Forward) frame from a projection direction and a
    /// rough up-reference — the same "two directions in, third derived" idiom as
    /// <c>PlanarTextGeometry.DeriveFrame</c>. Callers choose placement/orientation but cannot
    /// pass an already-reflected or degenerate frame through.
    /// </summary>
    public static SurfaceMarkingProjector Create(
        Vector3 origin,
        Vector3 forward,
        Vector3 upReference,
        float width,
        float height,
        float depth,
        Vector4 atlasRegion,
        Color tint)
    {
        if (!IsFinite(origin))
            throw new ArgumentException("Projector origin must be finite.", nameof(origin));
        if (!IsFinite(forward) || forward.LengthSquared() <= 1e-12f)
            throw new ArgumentException(
                "Projector forward direction must be finite and non-zero.", nameof(forward));
        if (!IsFinite(upReference) || upReference.LengthSquared() <= 1e-12f)
            throw new ArgumentException(
                "Projector up reference must be finite and non-zero.", nameof(upReference));
        if (!float.IsFinite(width) || width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!float.IsFinite(depth) || depth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(depth));

        Vector3 fwd = Vector3.Normalize(forward);
        Vector3 planarUp = upReference - fwd * Vector3.Dot(upReference, fwd);
        if (planarUp.LengthSquared() <= 1e-10f)
            throw new ArgumentException(
                "Projector up reference must not be parallel to the forward direction.",
                nameof(upReference));

        Vector3 up = Vector3.Normalize(planarUp);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, up));

        // Defensive orthonormality check. Construction guarantees this given finite,
        // non-degenerate inputs — a failure here means a NaN or near-zero input slipped past
        // the checks above, not a real "reflected frame" the caller could have chosen.
        if (MathF.Abs(Vector3.Dot(right, up)) > 1e-3f ||
            MathF.Abs(Vector3.Dot(right, fwd)) > 1e-3f ||
            MathF.Abs(Vector3.Dot(up, fwd)) > 1e-3f)
            throw new InvalidOperationException("Projector frame is not orthogonal.");

        return new SurfaceMarkingProjector(origin, right, up, fwd, width, height, depth, atlasRegion, tint);
    }

    private static bool IsFinite(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
