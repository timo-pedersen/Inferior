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
        // Custom-mesh modules: derive face info from the hull mesh geometry.
        if (mod.Definition.MeshFactory != null && mod.Mesh != null)
        {
            // Brief F1: HullFaceCount, not BaseFaceCount — decoration is placed on the
            // factory's real hull faces specifically, not on however many faces
            // BaseFaceCount now also covers (it advances to include panel-seam decoration
            // too, post Brief F1 Fix 2). Also called later from BuildInternalNormalSet
            // (AO), where using the stable hull count avoids redundantly walking every
            // seam face just to collect a normal it already shares with its parent wall.
            int limit  = mod.Mesh.HullFaceCount;
            var result = new FaceInfo[limit];
            for (int i = 0; i < limit; i++)
            {
                Vector3 n              = mod.Mesh.LocalFaceNormal(i);
                var (center, w, h)     = mod.Mesh.GetFaceBounds(i);
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
