using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Centralizes "does this triangle/quad's vertex order actually face the intended direction,
/// and if not, fix it" - the exact recurring bug class Timo flagged as hitting nearly every
/// new decoration primitive (A1 inventory finding, triangle-winding cluster). A solution-wide
/// search for the tell-tale shape (`Dot(Cross(v1-v0, v2-v0), expectedNormal) &lt; 0`) found 17
/// independent, textually-different reimplementations of this exact check across
/// Inferior.Game's station/megastation decoration code - several under the identical name
/// `AddQuadFacing`/`AddTriangleFacing`, independently reinvented in at least four different
/// files. This is the one, real implementation those should have shared.
///
/// This reorders the actual vertices (the geometry that determines the rasterized/culled
/// front face), not just a stored lighting-normal attribute - see
/// StationModuleMesh.AddQuadProjected's history, which used to flip only the stored normal
/// and left the real winding (and therefore which side actually renders) wrong.
/// </summary>
public static class WindingCorrection
{
    /// <summary>
    /// Returns (v0,v1,v2), swapping v1/v2 if needed so Cross(v1-v0, v2-v0) agrees with
    /// expectedNormal. The smallest change that flips a triangle's winding without moving
    /// any point.
    /// </summary>
    public static (Vector3 V0, Vector3 V1, Vector3 V2) Triangle(
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 expectedNormal)
        => Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), expectedNormal) < 0f
            ? (v0, v2, v1)
            : (v0, v1, v2);

    /// <summary>
    /// Returns (v0,v1,v2,v3), swapping v1/v3 if needed so both split triangles (v0,v1,v2)
    /// and (v0,v2,v3) agree with expectedNormal. Swapping v1/v3 keeps the v0-v2 diagonal
    /// fixed, which is winding-equivalent to reversing the whole loop while preserving the
    /// same two split triangles callers already build the quad from.
    /// </summary>
    public static (Vector3 V0, Vector3 V1, Vector3 V2, Vector3 V3) Quad(
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 expectedNormal)
        => Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), expectedNormal) < 0f
            ? (v0, v3, v2, v1)
            : (v0, v1, v2, v3);
}
