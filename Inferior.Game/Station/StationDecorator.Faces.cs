using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Face analysis ─────────────────────────────────────────────────────────

    // internal, not private: Brief Z1's ComputeZones/AssignZoneTypes are internal test
    // hooks (StationWindowGridTests-style pure-helper testing) and need to expose this
    // type in their signatures.
    internal readonly struct FaceInfo(
        Vector3 localNormal,
        Vector3 localCenter,
        Vector3 localRight,
        Vector3 localUp,
        float   width,
        float   height,
        bool    isExposed)
    {
        public readonly Vector3 LocalNormal = localNormal;
        public readonly Vector3 LocalCenter = localCenter;
        public readonly Vector3 LocalRight  = localRight;
        public readonly Vector3 LocalUp     = localUp;
        public readonly float   Width       = width;
        public readonly float   Height      = height;
        public readonly bool    IsExposed   = isExposed;
    }

    private static FaceInfo[] ComputeFaces(PlacedModule mod)
    {
        // Custom-mesh modules: derive face info from the separate hull mesh (Brief U1 —
        // mod.HullMesh contains only load-bearing hull geometry, so its whole FaceCount is
        // the right limit; no sub-range needed since decoration never shares this mesh).
        if (mod.Definition.MeshFactory != null && mod.HullMesh != null)
        {
            int limit  = mod.HullMesh.FaceCount;
            var result = new FaceInfo[limit];
            for (int i = 0; i < limit; i++)
            {
                Vector3 n              = mod.HullMesh.LocalFaceNormal(i);
                var (center, w, h)     = mod.HullMesh.GetFaceBounds(i);
                var (right, up)        = TangentFrame(n);
                bool blocked           = IsFaceBlocked(mod, n);
                result[i]              = new FaceInfo(n, center, right, up, w, h, !blocked);
            }
            return result;
        }

        // Default: derive 6 axis-aligned faces from the bounding box.
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        (Vector3 n, float w, float h)[] faceData =
        [
            ( Vector3.UnitX,  bb.Z, bb.Y),
            (-Vector3.UnitX,  bb.Z, bb.Y),
            ( Vector3.UnitY,  bb.X, bb.Z),
            (-Vector3.UnitY,  bb.X, bb.Z),
            ( Vector3.UnitZ,  bb.X, bb.Y),
            (-Vector3.UnitZ,  bb.X, bb.Y),
        ];

        var res = new FaceInfo[6];
        for (int i = 0; i < 6; i++)
        {
            var (n, w, h)   = faceData[i];
            Vector3 center  = new(n.X * half.X, n.Y * half.Y, n.Z * half.Z);
            var (right, up) = TangentFrame(n);
            bool blocked    = IsFaceBlocked(mod, n);
            res[i]          = new FaceInfo(n, center, right, up, w, h, !blocked);
        }
        return res;
    }

    private static bool IsFaceBlocked(PlacedModule mod, Vector3 faceNormal)
    {
        foreach (var port in mod.Definition.Ports)
        {
            if (Vector3.Dot(port.OutwardNormal, faceNormal) < 0.9f) continue;
            if (port == mod.AttachmentPort)   return true;
            if (mod.ChildPorts.Contains(port)) return true;
        }
        return false;
    }

    // ── Brief Z3 Fix A': per-zone exposure ──────────────────────────────────────
    //
    // Visual-tuning values (not correctness), named and reported per the brief's own ask.
    // Clearance keeps decoration from butting directly against a neighbour's edge.
    // Plane tolerance is generous enough to cover chamfer/connector geometry between two
    // flush-attached modules, tight enough to exclude an unrelated module elsewhere on a
    // large station whose (u,v) footprint happens to coincide by coincidence.
    private const float ZoneExposureClearanceMetres        = 0.5f;
    private const float ZoneExposureNeighborPlaneTolerance = 2.0f;

    // Recomputes each zone's IsExposed from actual neighbour-footprint overlap, rather than
    // every zone inheriting the parent face's single blocked/unblocked boolean. Conservative
    // by design (the brief's own "the rule"): a zone is blocked if its rectangle overlaps a
    // neighbour's projected footprint AT ALL, however small — no ">50% covered" fraction,
    // since that would flip small faces and break the bit-identical gate.
    //
    // Only ever called for zoned (multi-zone) faces, from Decorate()'s multi-zone branch —
    // the unzoned/single-zone path keeps face-level IsExposed untouched, per the brief's own
    // explicit scope ("This fix concerns the zoned path only"). This is also what preserves
    // ordinary modules automatically: a single-zone face IS the whole face, so this method
    // is simply never invoked there, not merely invoked-and-agreeing.
    //
    // No new neighbour-lookup data structure needed: PlacedModule.Transform (module-to-
    // station-local) and Definition.BoundingBox (full extents) are enough to reconstruct
    // every other module's footprint on demand by geometry, since no reverse "which module
    // is attached at this port" link exists anywhere in the generator today.
    private static FaceInfo[] RefineZoneExposure(
        PlacedModule mod, FaceInfo parentFace, FaceInfo[] zones, IReadOnlyList<PlacedModule> modules)
    {
        // Cheap short-circuit: if the parent face was never blocked at all, no neighbour
        // touches it anywhere, so every zone is trivially exposed — skip the neighbour scan
        // entirely for the common case (most zoned faces on a well-connected hub are NOT
        // the attachment faces).
        if (parentFace.IsExposed) return zones;

        var footprints = new List<(float minU, float maxU, float minV, float maxV)>();
        Matrix invTransform  = Matrix.Invert(mod.Transform);
        float  facePlaneDist = Vector3.Dot(parentFace.LocalCenter, parentFace.LocalNormal);

        foreach (var other in modules)
        {
            if (ReferenceEquals(other, mod)) continue;

            Vector3 halfOther = other.Definition.BoundingBox * 0.5f;
            Matrix  otherToModLocal = other.Transform * invTransform;

            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;
            float minN = float.MaxValue, maxN = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new(
                    (i & 1) == 0 ? -halfOther.X : halfOther.X,
                    (i & 2) == 0 ? -halfOther.Y : halfOther.Y,
                    (i & 4) == 0 ? -halfOther.Z : halfOther.Z);
                Vector3 p = Vector3.Transform(corner, otherToModLocal);

                float u = Vector3.Dot(p, parentFace.LocalRight);
                float v = Vector3.Dot(p, parentFace.LocalUp);
                float n = Vector3.Dot(p, parentFace.LocalNormal);

                if (u < minU) minU = u; if (u > maxU) maxU = u;
                if (v < minV) minV = v; if (v > maxV) maxV = v;
                if (n < minN) minN = n; if (n > maxN) maxN = n;
            }

            // Does this neighbour actually sit at/near THIS face's plane? Two flush-attached
            // modules share a plane with no gap, so whichever of the neighbour's near/far
            // bound along the normal is closer to the face plane should sit right on it.
            float nearSide = MathF.Abs(minN - facePlaneDist) <= MathF.Abs(maxN - facePlaneDist) ? minN : maxN;
            if (MathF.Abs(nearSide - facePlaneDist) > ZoneExposureNeighborPlaneTolerance) continue;

            footprints.Add((minU - ZoneExposureClearanceMetres, maxU + ZoneExposureClearanceMetres,
                            minV - ZoneExposureClearanceMetres, maxV + ZoneExposureClearanceMetres));
        }

        // The coarse per-face flag said blocked, but no neighbour geometry actually reaches
        // this plane (shouldn't normally happen — IsFaceBlocked and this are both driven by
        // the same attachment data — but if it does, every zone stays exposed rather than
        // silently blocking on stale information).
        if (footprints.Count == 0) return zones;

        var refined = new FaceInfo[zones.Length];
        for (int i = 0; i < zones.Length; i++)
        {
            var zone = zones[i];
            float zu     = Vector3.Dot(zone.LocalCenter, parentFace.LocalRight);
            float zv     = Vector3.Dot(zone.LocalCenter, parentFace.LocalUp);
            float zHalfW = zone.Width  * 0.5f;
            float zHalfH = zone.Height * 0.5f;

            bool blocked = false;
            foreach (var (minU, maxU, minV, maxV) in footprints)
            {
                bool overlapU = (zu + zHalfW) > minU && (zu - zHalfW) < maxU;
                bool overlapV = (zv + zHalfH) > minV && (zv - zHalfH) < maxV;
                if (overlapU && overlapV) { blocked = true; break; }
            }

            refined[i] = new FaceInfo(
                zone.LocalNormal, zone.LocalCenter, zone.LocalRight, zone.LocalUp,
                zone.Width, zone.Height, isExposed: !blocked);
        }
        return refined;
    }

    // Returns true when the face is a ship-landing surface on a docking module.
    private static bool IsDockingPadFace(PlacedModule mod, FaceInfo face)
    {
        foreach (var port in mod.Definition.Ports)
            if (port.IsDocking && Vector3.Dot(face.LocalNormal, port.OutwardNormal) > 0.9f)
                return true;
        return false;
    }

    private static (Vector3 right, Vector3 up) TangentFrame(Vector3 n)
    {
        Vector3 hint  = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(hint, n));
        Vector3 up    = Vector3.Normalize(Vector3.Cross(n, right));
        return (right, up);
    }

    // Returns a local-space point on a face using normalised UV coords in [-0.5, 0.5].
    private static Vector3 LocalPoint(FaceInfo face, float cu, float cv, float offset)
        => face.LocalCenter
         + face.LocalRight  * (cu * face.Width)
         + face.LocalUp     * (cv * face.Height)
         + face.LocalNormal * offset;

    // Returns a local-space point on a face using absolute metre offsets from face centre.
    private static Vector3 LocalPointAbs(FaceInfo face, float u, float v, float offset)
        => face.LocalCenter
         + face.LocalRight  * u
         + face.LocalUp     * v
         + face.LocalNormal * offset;

    // Transforms a module-local face point to station-relative space via mod.Transform.
    // Used to compute WorldPosition values for StationLightInfo.
    private static Vector3 StationPoint(PlacedModule mod, FaceInfo face, float u, float v, float offset)
        => Vector3.Transform(LocalPointAbs(face, u, v, offset), mod.Transform);

    // ── Face occupancy ────────────────────────────────────────────────────────

    // Tracks rectangular regions already occupied on a face (absolute metre offsets).
    private sealed class FaceOccupancy
    {
        private readonly List<(float u0, float v0, float u1, float v1)> _regions = [];

        // Brief D-Z2 Measurement 2: read-only observability for the per-zone content dump —
        // "how many placement attempts actually succeeded on this zone" (a tank row/pair
        // claims one region for the whole cluster, so this undercounts individual tanks
        // within a row, but it's a real, decision-logic-free signal: zero here despite a
        // TankFarm/CommsArray type means every attempt was rejected, not merely under-rolled).
        public int RegionCount => _regions.Count;

        public bool IsClear(float u0, float v0, float u1, float v1, float margin = 0.15f)
        {
            float mu0 = u0 - margin, mv0 = v0 - margin;
            float mu1 = u1 + margin, mv1 = v1 + margin;
            return !_regions.Any(r =>
                mu1 > r.u0 && mu0 < r.u1 &&
                mv1 > r.v0 && mv0 < r.v1);
        }

        public void Occupy(float u0, float v0, float u1, float v1)
            => _regions.Add((u0, v0, u1, v1));

        public bool TryOccupy(float cu, float cv, float halfW, float halfH, float margin = 0.15f)
        {
            if (!IsClear(cu - halfW, cv - halfH, cu + halfW, cv + halfH, margin))
                return false;
            Occupy(cu - halfW, cv - halfH, cu + halfW, cv + halfH);
            return true;
        }
    }


    // ── Texture helpers ───────────────────────────────────────────────────────

    private static SurfaceTexture TextureFor(string category) => category switch
    {
        "hab" or "luxury"            => SurfaceTexture.CleanPanel,
        "science" or "military"
        or "core"                    => SurfaceTexture.TechPanel,
        "industrial" or "fuel"       => SurfaceTexture.IndustrialPanel,
        "cargo"                      => SurfaceTexture.CargoPanel,
        _                            => SurfaceTexture.CleanPanel,
    };


    // ── Colour helpers ────────────────────────────────────────────────────────

    private static Color DarkenColor(Color c, float factor) => new(
        (int)(c.R * factor),
        (int)(c.G * factor),
        (int)(c.B * factor),
        c.A);

    internal static Color LightenColor(Color c, float factor) => new(
        (byte)Math.Min(c.R * factor, 255),
        (byte)Math.Min(c.G * factor, 255),
        (byte)Math.Min(c.B * factor, 255),
        c.A);

}
