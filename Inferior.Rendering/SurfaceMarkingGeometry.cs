using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Clips receiver triangles against a <see cref="SurfaceMarkingProjector"/>'s finite volume and
/// emits new, separate decal geometry — the receiver mesh itself is never read back into and
/// never modified. This is CPU-generated overlay geometry (a decal mesh drawn as an ordinary
/// additional pass), not a runtime screen-space/deferred decal system.
///
/// For every eligible receiver triangle intersecting the projector volume: transform into
/// projector-local (u, v, w) space, clip against the marking's width/height/depth bounds with
/// Sutherland-Hodgman, fan-triangulate the surviving convex polygon, and generate marking UVs
/// from the projector-local coordinates. No incidence-angle cutoff — a wall perpendicular to
/// the primary marking plane still receives geometry (its projected coordinate degenerates to
/// roughly constant in one axis, which is the intended "the marking belongs to the object's
/// surface" look, not a physical spray-paint simulation).
/// </summary>
public static class SurfaceMarkingGeometry
{
    // Triangles at or below this cross-product-length-squared are treated as degenerate
    // slivers (can arise exactly at a clip boundary) and dropped rather than emitted.
    private const float MinimumTriangleAreaSquared = 1e-12f;

    /// <summary>
    /// Projects <paramref name="projector"/>'s marking onto <paramref name="receiverVertices"/>/
    /// <paramref name="receiverIndices"/>. <paramref name="isReceiverTriangle"/>, if given, is
    /// called with each receiver triangle's index (0-based, not multiplied by 3) and should
    /// return false to exclude it — the receiver-selection mechanism the design calls for,
    /// deliberately left as "accept everything" (null) for a receiver mesh where every
    /// triangle is a valid target. Never assume every triangle in the volume is a valid
    /// receiver merely because this parameter was omitted; omitting it is a caller's explicit
    /// choice for a specific mesh, not a system-wide default.
    /// </summary>
    public static (VertexPositionNormalColorTexture[] Vertices, short[] Indices) Project(
        in SurfaceMarkingProjector projector,
        VertexPositionNormalColorTexture[] receiverVertices,
        short[] receiverIndices,
        Func<int, bool>? isReceiverTriangle = null)
    {
        ArgumentNullException.ThrowIfNull(receiverVertices);
        ArgumentNullException.ThrowIfNull(receiverIndices);
        if (receiverIndices.Length % 3 != 0)
            throw new ArgumentException(
                "Receiver index count must be a multiple of 3.", nameof(receiverIndices));

        var outVertices = new List<VertexPositionNormalColorTexture>();
        var outIndices = new List<short>();

        int triangleCount = receiverIndices.Length / 3;
        for (int t = 0; t < triangleCount; t++)
        {
            if (isReceiverTriangle != null && !isReceiverTriangle(t))
                continue;

            int ia = receiverIndices[t * 3];
            int ib = receiverIndices[t * 3 + 1];
            int ic = receiverIndices[t * 3 + 2];

            ClipTriangle(
                projector,
                receiverVertices[ia], receiverVertices[ib], receiverVertices[ic],
                outVertices, outIndices);
        }

        return (outVertices.ToArray(), outIndices.ToArray());
    }

    private readonly record struct ClipVertex(Vector3 Position, float U, float V, float W);

    private static void ClipTriangle(
        SurfaceMarkingProjector projector,
        VertexPositionNormalColorTexture a,
        VertexPositionNormalColorTexture b,
        VertexPositionNormalColorTexture c,
        List<VertexPositionNormalColorTexture> outVertices,
        List<short> outIndices)
    {
        // Flat per-triangle normal — the established convention for every generated mesh in
        // this project (GeometryBuilder/StationModuleMesh both emit one normal per triangle,
        // replicated across its 3 vertices). Taken once, never interpolated, and carried
        // through clipping unchanged — this is what "preserve the receiver normal" means here.
        Vector3 normal = a.Normal;

        List<ClipVertex> polygon =
        [
            ToClipVertex(projector, a.Position),
            ToClipVertex(projector, b.Position),
            ToClipVertex(projector, c.Position),
        ];

        // The receiver triangle is fed in its own existing vertex order and never reordered
        // below — Sutherland-Hodgman clipping and fan triangulation both preserve traversal
        // order, so the output triangles automatically wind the same way the source triangle
        // did. No global CW/CCW convention needs to be hardcoded here.
        polygon = ClipAgainstPlane(polygon, static v => -v.U);                    // keep u >= 0
        polygon = ClipAgainstPlane(polygon, v => v.U - projector.Width);          // keep u <= width
        if (polygon.Count < 3) return;
        polygon = ClipAgainstPlane(polygon, static v => -v.V);                    // keep v >= 0
        polygon = ClipAgainstPlane(polygon, v => v.V - projector.Height);         // keep v <= height
        if (polygon.Count < 3) return;
        polygon = ClipAgainstPlane(polygon, static v => -v.W);                    // keep w >= 0
        polygon = ClipAgainstPlane(polygon, v => v.W - projector.Depth);          // keep w <= depth
        if (polygon.Count < 3) return;

        for (int i = 1; i < polygon.Count - 1; i++)
        {
            ClipVertex p0 = polygon[0], p1 = polygon[i], p2 = polygon[i + 1];
            Vector3 cross = Vector3.Cross(p1.Position - p0.Position, p2.Position - p0.Position);
            if (cross.LengthSquared() <= MinimumTriangleAreaSquared)
                continue;

            AppendTriangle(projector, normal, p0, p1, p2, outVertices, outIndices);
        }
    }

    private static ClipVertex ToClipVertex(in SurfaceMarkingProjector projector, Vector3 position)
    {
        Vector3 offset = position - projector.Origin;
        return new ClipVertex(
            position,
            Vector3.Dot(offset, projector.Right),
            Vector3.Dot(offset, projector.Up),
            Vector3.Dot(offset, projector.Forward));
    }

    // Sutherland-Hodgman single-plane clip: signedDistance <= 0 is "inside" (kept). Vertices
    // are visited and emitted in their existing order — never reordered — so the polygon's
    // winding is preserved exactly.
    private static List<ClipVertex> ClipAgainstPlane(
        List<ClipVertex> polygon, Func<ClipVertex, float> signedDistance)
    {
        if (polygon.Count == 0)
            return polygon;

        var result = new List<ClipVertex>(polygon.Count + 1);
        for (int i = 0; i < polygon.Count; i++)
        {
            ClipVertex current = polygon[i];
            ClipVertex next = polygon[(i + 1) % polygon.Count];
            float dCurrent = signedDistance(current);
            float dNext = signedDistance(next);
            bool currentInside = dCurrent <= 0f;
            bool nextInside = dNext <= 0f;

            if (currentInside)
                result.Add(current);

            if (currentInside != nextInside)
            {
                float t = dCurrent / (dCurrent - dNext);
                result.Add(LerpClipVertex(current, next, t));
            }
        }
        return result;
    }

    private static ClipVertex LerpClipVertex(ClipVertex a, ClipVertex b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t),
        MathHelper.Lerp(a.U, b.U, t),
        MathHelper.Lerp(a.V, b.V, t),
        MathHelper.Lerp(a.W, b.W, t));

    private static void AppendTriangle(
        in SurfaceMarkingProjector projector, Vector3 normal,
        ClipVertex a, ClipVertex b, ClipVertex c,
        List<VertexPositionNormalColorTexture> outVertices, List<short> outIndices)
    {
        if (outVertices.Count + 3 > short.MaxValue)
            throw new InvalidOperationException(
                "Surface marking geometry exceeds the 16-bit index range.");

        short baseIndex = (short)outVertices.Count;
        outVertices.Add(MakeVertex(projector, normal, a));
        outVertices.Add(MakeVertex(projector, normal, b));
        outVertices.Add(MakeVertex(projector, normal, c));
        outIndices.Add(baseIndex);
        outIndices.Add((short)(baseIndex + 1));
        outIndices.Add((short)(baseIndex + 2));
    }

    private static VertexPositionNormalColorTexture MakeVertex(
        in SurfaceMarkingProjector projector, Vector3 normal, ClipVertex v)
    {
        float u = projector.Width  > 0f ? v.U / projector.Width  : 0f;
        float w = projector.Height > 0f ? v.V / projector.Height : 0f;
        // NOT flipped, despite a top-left-origin texture normally wanting "projector Up ->
        // smaller V": TextMarkingAtlas's source (TextPainter.DrawText) writes glyph row 0 (the
        // visual TOP of a glyph) toward the LARGEST buffer/texture row — i.e. that atlas is
        // itself already vertically flipped relative to ordinary top-left-origin convention
        // (confirmed by inspection: rendering the raw buffer as ASCII art shows every glyph
        // upside down when printed row-0-first). Mapping projector Up directly to atlasV
        // (not 1-w) compensates for that, so text reads right-side-up in world space. Do not
        // "fix" this by flipping TextPainter itself — it's shared with real station panel
        // text (StationTextureRegistry) and changing its convention would flip that instead.
        float atlasU = MathHelper.Lerp(projector.AtlasRegion.X, projector.AtlasRegion.Z, u);
        float atlasV = MathHelper.Lerp(projector.AtlasRegion.Y, projector.AtlasRegion.W, w);
        return new VertexPositionNormalColorTexture(
            v.Position, normal, projector.Tint, new Vector2(atlasU, atlasV));
    }
}
