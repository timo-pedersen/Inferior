using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Face-local position and footprint for a placed greeble item.
// Coordinates are absolute metres from face centre along LocalRight / LocalUp.
internal readonly struct PlacedGreebleInfo(Vector2 faceLocalPos, Vector2 footprint, bool isConnectable)
{
    public readonly Vector2 FaceLocalPos  = faceLocalPos;
    public readonly Vector2 Footprint     = footprint;       // full w × h in metres
    public readonly bool    IsConnectable = isConnectable;
}

// Generates cable bundles and conduits routed between connectable greeble items on a
// module face. Geometry is added to the existing StationModuleMesh (module-local space).
// ApplyLighting bakes lighting into vertex colours after all geometry is added, so raw
// colours are passed here.
internal static class StationCableGenerator
{
    private const float CellSize        = 0.15f;   // grid resolution (metres)
    private const float ObstacleMargin  = 0.15f;   // clearance around greeble footprint
    private const float FastenerSpacing = 0.55f;   // fasteners every ~55 cm

    // Called once per exposed module face after greeble placement.
    // faceOrigin = face.LocalCenter; faceU = face.LocalRight; faceV = face.LocalUp.
    internal static void GenerateFaceCables(
        Vector3                          faceNormal,
        Vector3                          faceU,
        Vector3                          faceV,
        float                            faceWidth,
        float                            faceHeight,
        Vector3                          faceOrigin,
        IReadOnlyList<PlacedGreebleInfo> greeble,
        StationModuleMesh                mesh,
        System.Random                    rng,
        float                            surfaceOffset = 0.002f)
    {
        if (greeble.Count < 2) return;

        var grid    = BuildObstacleGrid(faceWidth, faceHeight, greeble);
        var bundles = new List<CableBundle>();
        GenerateBundles(greeble, grid, faceWidth, faceHeight, bundles, rng);

        foreach (var bundle in bundles)
        {
            foreach (var seg in bundle.Segments)
                AddSegmentGeometry(seg, bundle, faceOrigin, faceU, faceV, faceNormal, mesh, rng, surfaceOffset);

            foreach (var turn in bundle.TurnPoints)
                AddJunctionBox(turn, bundle, faceOrigin, faceU, faceV, faceNormal, mesh, surfaceOffset);

            AddEdgeClampsForBundle(bundle, faceWidth, faceHeight,
                faceOrigin, faceU, faceV, faceNormal, mesh, surfaceOffset);
        }
    }

    // ── Obstacle grid ─────────────────────────────────────────────────────────

    private static bool[,] BuildObstacleGrid(float faceWidth, float faceHeight,
        IReadOnlyList<PlacedGreebleInfo> greeble)
    {
        int cols = Math.Max(1, (int)MathF.Ceiling(faceWidth  / CellSize));
        int rows = Math.Max(1, (int)MathF.Ceiling(faceHeight / CellSize));
        var grid = new bool[cols, rows];

        foreach (var g in greeble)
        {
            float hw = g.Footprint.X * 0.5f + ObstacleMargin;
            float hh = g.Footprint.Y * 0.5f + ObstacleMargin;

            // g.FaceLocalPos from face centre; grid origin at face corner (-w/2, -h/2)
            int ci0 = Math.Max(0,        WorldToGrid(g.FaceLocalPos.X - hw + faceWidth  * 0.5f));
            int ci1 = Math.Min(cols - 1, WorldToGrid(g.FaceLocalPos.X + hw + faceWidth  * 0.5f));
            int ri0 = Math.Max(0,        WorldToGrid(g.FaceLocalPos.Y - hh + faceHeight * 0.5f));
            int ri1 = Math.Min(rows - 1, WorldToGrid(g.FaceLocalPos.Y + hh + faceHeight * 0.5f));

            for (int ci = ci0; ci <= ci1; ci++)
            for (int ri = ri0; ri <= ri1; ri++)
                grid[ci, ri] = true;
        }
        return grid;
    }

    private static int WorldToGrid(float pos) => (int)MathF.Floor(pos / CellSize);

    // Diagonal half-extent of the grid's blocked zone around a greeble.
    // The zone is rectangular (footprint/2 + ObstacleMargin in each axis); the diagonal
    // is used as a circular skip radius so every corner of the rectangle is covered.
    private static float EndpointRadius(PlacedGreebleInfo g)
    {
        float ru = g.Footprint.X * 0.5f + ObstacleMargin;
        float rv = g.Footprint.Y * 0.5f + ObstacleMargin;
        return MathF.Sqrt(ru * ru + rv * rv);
    }

    // ── Route finding ─────────────────────────────────────────────────────────

    // Returns null if no valid route found. Tries L-route then Z-route.
    // fromRadius/toRadius: blocked-zone radius of each endpoint greeble — PathClear skips
    // obstacle checks within these radii of the original greeble centres so the cable can
    // exit/enter the greeble footprint freely.
    private static List<CableSegment>? FindRoute(
        Vector2 from, float fromRadius,
        Vector2 to,   float toRadius,
        bool[,] grid, float faceWidth, float faceHeight, bool preferU)
    {
        // L-route preferred axis first
        var corner1 = preferU ? new Vector2(to.X, from.Y) : new Vector2(from.X, to.Y);
        if (PathClear2(from, corner1, to, grid, faceWidth, faceHeight,
                       from, fromRadius, to, toRadius))
            return SegmentsFromCorner(from, corner1, to);

        // L-route other axis first
        var corner2 = preferU ? new Vector2(from.X, to.Y) : new Vector2(to.X, from.Y);
        if (PathClear2(from, corner2, to, grid, faceWidth, faceHeight,
                       from, fromRadius, to, toRadius))
            return SegmentsFromCorner(from, corner2, to);

        // Z-route: midpoint detour
        float mid = preferU ? (from.X + to.X) * 0.5f : (from.Y + to.Y) * 0.5f;
        var c1 = preferU ? new Vector2(mid, from.Y) : new Vector2(from.X, mid);
        var c2 = preferU ? new Vector2(mid, to.Y)   : new Vector2(to.X,   mid);

        if (PathClear(from, c1, grid, faceWidth, faceHeight, from, fromRadius, to, toRadius) &&
            PathClear(c1,   c2, grid, faceWidth, faceHeight, from, fromRadius, to, toRadius) &&
            PathClear(c2,   to, grid, faceWidth, faceHeight, from, fromRadius, to, toRadius))
        {
            return
            [
                new CableSegment(from, c1, preferU ? CableAxis.U : CableAxis.V),
                new CableSegment(c1,   c2, preferU ? CableAxis.V : CableAxis.U),
                new CableSegment(c2,   to, preferU ? CableAxis.U : CableAxis.V),
            ];
        }

        return null;
    }

    // ep1/r1 and ep2/r2: the two original greeble endpoint positions and their blocked-zone
    // radii. Sample points within either radius are exempt from the obstacle check.
    private static bool PathClear(Vector2 a, Vector2 b,
        bool[,] grid, float faceWidth, float faceHeight,
        Vector2 ep1, float r1, Vector2 ep2, float r2)
    {
        float dist = Vector2.Distance(a, b);
        if (dist < 0.001f) return true;
        int steps = Math.Max(1, (int)(dist / (CellSize * 0.5f)));
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
            if (Vector2.Distance(p, ep1) < r1) continue;
            if (Vector2.Distance(p, ep2) < r2) continue;
            int ci = WorldToGrid(p.X + faceWidth  * 0.5f);
            int ri = WorldToGrid(p.Y + faceHeight * 0.5f);
            if (ci < 0 || ri < 0 || ci >= grid.GetLength(0) || ri >= grid.GetLength(1))
                return false;
            if (grid[ci, ri]) return false;
        }
        return true;
    }

    private static bool PathClear2(Vector2 a, Vector2 b, Vector2 c,
        bool[,] grid, float w, float h,
        Vector2 ep1, float r1, Vector2 ep2, float r2)
        => PathClear(a, b, grid, w, h, ep1, r1, ep2, r2) &&
           PathClear(b, c, grid, w, h, ep1, r1, ep2, r2);

    private static List<CableSegment> SegmentsFromCorner(Vector2 from, Vector2 corner, Vector2 to)
    {
        bool firstIsU = MathF.Abs(corner.X - from.X) > MathF.Abs(corner.Y - from.Y);
        return
        [
            new CableSegment(from,   corner, firstIsU ? CableAxis.U : CableAxis.V),
            new CableSegment(corner, to,     firstIsU ? CableAxis.V : CableAxis.U),
        ];
    }

    // ── Bundle pairing ────────────────────────────────────────────────────────

    private static void GenerateBundles(
        IReadOnlyList<PlacedGreebleInfo> greeble,
        bool[,] grid, float faceWidth, float faceHeight,
        List<CableBundle> bundles, System.Random rng)
    {
        var connectable = greeble.Where(g => g.IsConnectable).ToList();
        if (connectable.Count < 2) return;

        int pairCount = 1 + (int)(rng.NextDouble() * Math.Min(4, connectable.Count - 1));

        // Pick hub: prefer largest footprint (proxy for junction box)
        int primaryIdx = 0;
        for (int i = 1; i < connectable.Count; i++)
        {
            if (connectable[i].Footprint.X + connectable[i].Footprint.Y >
                connectable[primaryIdx].Footprint.X + connectable[primaryIdx].Footprint.Y)
                primaryIdx = i;
        }

        var used = new HashSet<int> { primaryIdx };

        for (int p = 0; p < pairCount; p++)
        {
            int target = -1;
            for (int i = 0; i < connectable.Count; i++)
                if (!used.Contains(i)) { target = i; break; }
            if (target < 0) break;
            used.Add(target);

            Vector2 from = connectable[primaryIdx].FaceLocalPos;
            Vector2 to   = connectable[target].FaceLocalPos;
            bool preferU = rng.NextDouble() < 0.6;

            float fromRadius = EndpointRadius(connectable[primaryIdx]);
            float toRadius   = EndpointRadius(connectable[target]);
            var segments = FindRoute(from, fromRadius, to, toRadius,
                grid, faceWidth, faceHeight, preferU);
            if (segments == null) continue;

            bundles.Add(CreateBundle(segments, rng));
            primaryIdx = target;
        }
    }

    private static CableBundle CreateBundle(List<CableSegment> segments, System.Random rng)
    {
        bool  isConduit = rng.NextDouble() < 0.30;
        int   count     = isConduit ? 1 : (1 + (int)(rng.NextDouble() * 5));
        float diameter  = isConduit
            ? 0.10f + (float)(rng.NextDouble() * 0.05)
            : 0.02f + (float)(rng.NextDouble() * 0.04);

        Color colour;
        if (rng.NextDouble() < 0.80)
        {
            colour = rng.NextDouble() switch {
                < 0.30 => new Color( 40,  40,  40),
                < 0.60 => new Color( 80,  80,  80),
                < 0.85 => new Color(140, 140, 140),
                _      => new Color(210, 210, 210),
            };
        }
        else
        {
            colour = (int)(rng.NextDouble() * 4) switch {
                0 => new Color(145,  50,  45),   // muted red / terracotta
                1 => new Color( 45,  75, 155),   // navy blue
                2 => new Color( 65,  95,  50),   // olive green
                _ => new Color(160, 140,  55),   // ochre / amber
            };
        }

        var turns = segments.Count > 1
            ? segments.Take(segments.Count - 1).Select(s => s.End).ToList()
            : new List<Vector2>();

        return new CableBundle(
            isConduit ? BundleType.Conduit : BundleType.FlatCables,
            count, diameter, colour, segments, turns);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static float BundleWidth(CableBundle b) =>
        b.Type == BundleType.Conduit
            ? b.CableDiameter
            : b.CableCount * b.CableDiameter + (b.CableCount - 1) * 0.01f;

    private static float BundleThickness(CableBundle b) => b.CableDiameter;

    // Convert face-local (u,v) + normal offset to module-local space.
    private static Vector3 FacePoint(Vector2 uv, float nOffset,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal)
        => faceOrigin + faceU * uv.X + faceV * uv.Y + faceNormal * nOffset;

    // ── Segment geometry ──────────────────────────────────────────────────────

    private static void AddSegmentGeometry(
        CableSegment seg, CableBundle bundle,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, System.Random rng, float surfaceOffset)
    {
        float segLen = Vector2.Distance(seg.Start, seg.End);
        if (segLen < 0.01f) return;

        float bundleW = BundleWidth(bundle);
        float thick   = BundleThickness(bundle);

        Vector3 startPos = FacePoint(seg.Start, 0f, faceOrigin, faceU, faceV, faceNormal);
        Vector3 endPos   = FacePoint(seg.End,   0f, faceOrigin, faceU, faceV, faceNormal);
        Vector3 cableDir = Vector3.Normalize(endPos - startPos);
        Vector3 facePerp = Vector3.Normalize(Vector3.Cross(cableDir, faceNormal));

        Vector3 midPos = FacePoint((seg.Start + seg.End) * 0.5f,
            surfaceOffset + thick * 0.5f, faceOrigin, faceU, faceV, faceNormal);

        // X = perpendicular in face plane, Y = along cable, Z = faceNormal
        var t = new Matrix(
            facePerp.X,   facePerp.Y,   facePerp.Z,   0,
            cableDir.X,   cableDir.Y,   cableDir.Z,   0,
            faceNormal.X, faceNormal.Y, faceNormal.Z, 0,
            midPos.X,     midPos.Y,     midPos.Z,      1);
        mesh.AddOrientedBox(t, new Vector3(bundleW, segLen, thick), bundle.BundleColour);

        AddFasteners(seg, bundle, bundleW * 0.5f, thick,
            faceOrigin, faceU, faceV, faceNormal, mesh, rng, surfaceOffset, cableDir, facePerp);
    }

    // ── Junction boxes ────────────────────────────────────────────────────────

    private static void AddJunctionBox(
        Vector2 turnPoint, CableBundle bundle,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, float surfaceOffset)
    {
        float halfW = BundleWidth(bundle) * 0.5f + 0.02f;
        float thick = BundleThickness(bundle) + 0.005f;
        Color dark  = new Color(50, 50, 55);

        Vector3 center = FacePoint(turnPoint, surfaceOffset + thick * 0.5f,
            faceOrigin, faceU, faceV, faceNormal);

        // X = faceU, Y = faceV, Z = faceNormal (same convention as FaceLocalTransform)
        var t = new Matrix(
            faceU.X,      faceU.Y,      faceU.Z,      0,
            faceV.X,      faceV.Y,      faceV.Z,      0,
            faceNormal.X, faceNormal.Y, faceNormal.Z, 0,
            center.X,     center.Y,     center.Z,      1);
        mesh.AddOrientedBox(t, new Vector3(halfW * 2, halfW * 2, thick), dark);
    }

    // ── Fasteners ─────────────────────────────────────────────────────────────

    private static void AddFasteners(
        CableSegment seg, CableBundle bundle,
        float halfBundleW, float thick,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, System.Random rng, float surfaceOffset,
        Vector3 cableDir, Vector3 facePerp)
    {
        const float FastH = 0.008f;   // fastener height above bundle
        Color grey  = new Color(150, 150, 155);
        float segLen = Vector2.Distance(seg.Start, seg.End);

        float pos = FastenerSpacing * (0.5f + (float)rng.NextDouble() * 0.3f);
        while (pos < segLen - 0.1f)
        {
            float   t2     = pos / segLen;
            Vector2 uv     = Vector2.Lerp(seg.Start, seg.End, t2);
            Vector3 centre = FacePoint(uv, surfaceOffset + thick + FastH * 0.5f,
                faceOrigin, faceU, faceV, faceNormal);

            // Transverse bar: wide in facePerp, thin along cable, thin in normal
            var ft = new Matrix(
                facePerp.X,   facePerp.Y,   facePerp.Z,   0,
                cableDir.X,   cableDir.Y,   cableDir.Z,   0,
                faceNormal.X, faceNormal.Y, faceNormal.Z, 0,
                centre.X,     centre.Y,     centre.Z,      1);
            mesh.AddOrientedBox(ft, new Vector3(halfBundleW * 2 + 0.02f, 0.03f, FastH), grey);

            pos += FastenerSpacing * (0.8f + (float)rng.NextDouble() * 0.4f);
        }
    }

    // ── Edge clamps ───────────────────────────────────────────────────────────

    private static void AddEdgeClampsForBundle(
        CableBundle bundle, float faceWidth, float faceHeight,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, float surfaceOffset)
    {
        if (bundle.Segments.Count == 0) return;
        float hw = faceWidth  * 0.5f;
        float hh = faceHeight * 0.5f;
        TryAddEdgeClamp(bundle.Segments[0].Start,   hw, hh, bundle, faceOrigin, faceU, faceV, faceNormal, mesh, surfaceOffset);
        TryAddEdgeClamp(bundle.Segments[^1].End,    hw, hh, bundle, faceOrigin, faceU, faceV, faceNormal, mesh, surfaceOffset);
    }

    private static void TryAddEdgeClamp(
        Vector2 pt, float halfFaceW, float halfFaceH,
        CableBundle bundle,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, float surfaceOffset)
    {
        const float EdgeTol = 0.10f;
        Vector2 inwardDir2D = Vector2.Zero;

        if      (pt.X < -halfFaceW + EdgeTol) inwardDir2D = new Vector2( 1,  0);
        else if (pt.X >  halfFaceW - EdgeTol) inwardDir2D = new Vector2(-1,  0);
        else if (pt.Y < -halfFaceH + EdgeTol) inwardDir2D = new Vector2( 0,  1);
        else if (pt.Y >  halfFaceH - EdgeTol) inwardDir2D = new Vector2( 0, -1);
        else return;

        AddEdgeClamp(pt, inwardDir2D, bundle, faceOrigin, faceU, faceV, faceNormal, mesh, surfaceOffset);
    }

    private static void AddEdgeClamp(
        Vector2 edgePoint, Vector2 inwardDir2D, CableBundle bundle,
        Vector3 faceOrigin, Vector3 faceU, Vector3 faceV, Vector3 faceNormal,
        StationModuleMesh mesh, float surfaceOffset)
    {
        float halfW = BundleWidth(bundle) * 0.5f + 0.01f;
        float thick = BundleThickness(bundle) + 0.01f;
        float depth = 0.04f;
        Color grey  = new Color(80, 80, 85);

        Vector2 perp2D   = new Vector2(-inwardDir2D.Y, inwardDir2D.X);
        Vector3 inward3D = faceU * inwardDir2D.X + faceV * inwardDir2D.Y;
        Vector3 perp3D   = faceU * perp2D.X     + faceV * perp2D.Y;

        // Left arm
        Vector3 lBase = FacePoint(edgePoint + perp2D * (-halfW + 0.01f),
            surfaceOffset, faceOrigin, faceU, faceV, faceNormal);
        mesh.AddOrientedBox(lBase + faceNormal * (thick * 0.5f), faceNormal, thick, 0.02f, 0.02f, grey);

        // Right arm
        Vector3 rBase = FacePoint(edgePoint + perp2D * (halfW - 0.01f),
            surfaceOffset, faceOrigin, faceU, faceV, faceNormal);
        mesh.AddOrientedBox(rBase + faceNormal * (thick * 0.5f), faceNormal, thick, 0.02f, 0.02f, grey);

        // Top bar across
        Vector3 barPt = FacePoint(edgePoint, surfaceOffset + thick,
            faceOrigin, faceU, faceV, faceNormal);
        mesh.AddOrientedBox(barPt + inward3D * (depth * 0.5f), perp3D,
            halfW * 2 + 0.02f, 0.01f, depth, grey);
    }

    // ── Data types ────────────────────────────────────────────────────────────

    private enum CableAxis { U, V }
    private enum BundleType { FlatCables, Conduit }

    private sealed record CableSegment(Vector2 Start, Vector2 End, CableAxis Axis);

    private sealed record CableBundle(
        BundleType         Type,
        int                CableCount,
        float              CableDiameter,
        Color              BundleColour,
        List<CableSegment> Segments,
        List<Vector2>      TurnPoints);
}
