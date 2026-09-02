using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

/// <summary>
/// B4a authority. All points are station-local metres; Right x Up = Outward.
/// Depth is positive INTO the vessel. No camera/world-up or decoration seed is involved.
/// Collision is explicitly deferred: this is a geometric flight envelope, not a collider.
/// </summary>
public sealed record BolonAmbassadorBayPlan(
    string Identity, int VesselIndex, int HostFaceIndex, int CornerAxis,
    Vector3 MouthCenter, Vector3 Right, Vector3 Up, Vector3 Outward,
    float ClearWidth, float ClearHeight, float ChamferDepth, float ThroatLength,
    float BayFrontWidth, float BayWidth, float BayHeight, float ExpansionLength,
    float BayLength, float ApproachClearance, string Signature)
{
    // Keep the B4a sizing/selection envelope frozen. B4a.1 changes only the
    // presentation inside it, never the clear slot, throat endpoints or chamber.
    public const float ChamferInset = 3f;
    public float MouthWidth => ClearWidth + ChamferInset;
    public float MouthHeight => ClearHeight + ChamferInset;
    public float VisibleChamferDepth => ChamferDepth * .5f;
    public float OuterRevealDepth => ChamferDepth - VisibleChamferDepth;
    public float BayStartDepth => ChamferDepth + ThroatLength;
    public Vector3 Down => -Up;
    public Vector3 Point(float x, float y, float depth)
        => MouthCenter + Right * x + Up * y - Outward * depth;
    public Vector3 Coordinates(Vector3 point)
    {
        Vector3 d = point - MouthCenter;
        return new(Vector3.Dot(d, Right), Vector3.Dot(d, Up), -Vector3.Dot(d, Outward));
    }
    public bool ReservesFace(int vessel, int face)
        => vessel == VesselIndex && face == HostFaceIndex;
    public bool InApproachReservation(Vector3 point, float radius = 0f)
    {
        Vector3 p = Coordinates(point);
        return p.Z >= -1500f - radius && p.Z <= ChamferDepth + radius
            && MathF.Abs(p.X) <= MouthWidth / 2f + 12f + radius
            && MathF.Abs(p.Y) <= MouthHeight / 2f + 12f + radius;
    }
    public Vector3[] MouthCorners() => Rectangle(MouthWidth, MouthHeight, 0f);
    public Vector3[] Rectangle(float width, float height, float depth)
        => [Point(-width / 2, -height / 2, depth), Point(width / 2, -height / 2, depth),
            Point(width / 2, height / 2, depth), Point(-width / 2, height / 2, depth)];
    public Vector3[] Octagon(float width, float depth)
    {
        float x = width / 2f, y = BayHeight / 2f;
        return [Point(-x * .70f, -y, depth), Point(x * .70f, -y, depth),
            Point(x, -y * .30f, depth), Point(x, y * .30f, depth),
            Point(x * .70f, y, depth), Point(-x * .70f, y, depth),
            Point(-x, y * .30f, depth), Point(-x, -y * .30f, depth)];
    }
    public float RearPortWidth => 20f;
    public float RearPortHeight => 8f;
    public float RearPortChamferDepth => .75f;
    public float RearPortCorridorLength => 7f;
    public float BayEndDepth => BayStartDepth + BayLength;
    public Vector3[] RearPortRectangle(float width, float height, float depth)
        => Rectangle(width, height, depth).Select(q => q + Up * (-BayHeight / 2f + height / 2f)).ToArray();

    public IReadOnlyList<MegastationApproachFixture> ApproachFixtures()
    {
        int seed = MegastationSeed.Derive(MegastationSeed.Root(Signature, 1), "ambassador-approach:v1");
        float length = 1400f + (float)new Random(MegastationSeed.Derive(seed, "length")).NextDouble() * 200f;
        float angle = .7f + (float)new Random(MegastationSeed.Derive(seed, "half-angle")).NextDouble() * .5f;
        return (from horizontal in new[] { -1, 1 }
                from vertical in new[] { -1, 1 }
                select MegastationApproachFixtures.Create($"{Identity}/approach", horizontal, vertical,
                    Point(horizontal * (MouthWidth / 2f - 16f), vertical * (MouthHeight / 2f + 10f), 0),
                    Right, Up, Outward, 11f, new Color(170, 135, 71), new Color(198, 164, 103),
                    new Color(152, 118, 58), length, angle)).ToArray();
    }
}

public static class BolonAmbassadorBayPlanner
{
    public const float EntranceClearHeight = 22f;
    // B3a's deepest iris is 10.5m plus its backing. Keep the bay behind that
    // existing vocabulary without clearing or changing neighbouring faces.
    private const float ShellMargin = 16f;

    public static BolonAmbassadorBayPlan Plan(BolonMegastationPlan station,
        CancellationToken cancellationToken = default)
    {
        int seed = MegastationSeed.Derive(MegastationSeed.Root(station.StationIdentity, 2),
            "bolon-ambassador-bay:v1");
        var attached = station.Relationships.SelectMany(r => new[] { (r.A, r.FaceA), (r.B, r.FaceB) }).ToHashSet();
        var candidates = new List<BolonAmbassadorBayPlan>();
        foreach (BolonVesselPlan vessel in station.Vessels.Where(v => v.ScaleClass != BolonVesselScaleClass.Secondary))
        foreach (BolonAttachmentFace face in BolonMegastationGenerator.AttachmentFaces.Where(f => f.SideCount == 6))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attached.Contains((vessel.Index, face.Index))) continue;
            string id = $"{vessel.Identity}/ambassador:v1/face:{face.Index}";
            var rng = new Random(MegastationSeed.Derive(seed, id));
            int axis = rng.Next(3);
            Vector3 center = vessel.Position + Vector3.Transform(face.LocalCenter * vessel.Radius, vessel.Orientation);
            Vector3 n = Vector3.Transform(face.LocalNormal, vessel.Orientation);
            Vector3 u = Vector3.Normalize(Vector3.Transform(
                BolonMegastationGenerator.GetAttachmentFaceVertices(face.Index)[axis] - face.LocalCenter, vessel.Orientation));
            Vector3 v = Vector3.Normalize(Vector3.Cross(n, u));
            var candidate = new BolonAmbassadorBayPlan(id, vessel.Index, face.Index, axis,
                center, u, v, n, 1, EntranceClearHeight, 3f + (float)rng.NextDouble() * 3f,
                5f, 1, 1, EntranceClearHeight * 3f, 40f, vessel.Radius * 1.05f, 0f, "");
            // Solve against ALL real C60 half-spaces, not a sphere or inscribed circle.
            float mouthWidth = MaximumWidth(w => candidate.Rectangle(w, candidate.ClearHeight + 2f * BolonAmbassadorBayPlan.ChamferInset, 0f)
                .All(p => InsideVessel(vessel, p, 0f, face.Index)), vessel.Radius * 2f);
            candidate = candidate with { ClearWidth = mouthWidth * .94f - 2f * BolonAmbassadorBayPlan.ChamferInset - 12f };
            if (candidate.ClearWidth < 80f) continue;
            candidate = candidate with { BayFrontWidth = candidate.ClearWidth + 4f };
            float mainWidth = MaximumWidth(w => new[] { candidate.BayStartDepth + candidate.ExpansionLength,
                    candidate.BayStartDepth + candidate.BayLength }
                .All(d => candidate.Octagon(w, d).All(p => InsideVessel(vessel, p, ShellMargin))), vessel.Radius * 2f);
            candidate = candidate with { BayWidth = mainWidth * .96f };
            if (candidate.BayWidth < candidate.ClearWidth * 1.2f
                || !candidate.Octagon(candidate.BayFrontWidth, candidate.BayStartDepth)
                    .All(p => InsideVessel(vessel, p, ShellMargin, face.Index))) continue;
            // An inflated broad corridor must miss every other ball and connector.
            float clearance = ApproachClearance(station, candidate);
            if (clearance <= 0f) continue;
            candidate = candidate with { ApproachClearance = clearance };
            candidates.Add(candidate);
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("B4a could not fit an unobstructed ambassador bay; refusing an obstructed fallback.");
        BolonAmbassadorBayPlan chosen = candidates.OrderByDescending(c => c.ApproachClearance)
            .ThenByDescending(c => c.BayWidth)
            .ThenBy(c => MegastationSeed.Derive(seed, c.Identity)).First();
        return chosen with { Signature = Signature(chosen) };
    }

    internal static bool InsideVessel(BolonVesselPlan vessel, Vector3 point, float margin, int exceptFace = -1)
    {
        Vector3 local = Vector3.Transform(point - vessel.Position, Quaternion.Inverse(vessel.Orientation));
        return BolonMegastationGenerator.AttachmentFaces.All(f => f.Index == exceptFace
            || Vector3.Dot(local - f.LocalCenter * vessel.Radius, f.LocalNormal) <= -margin + .002f);
    }

    private static float MaximumWidth(Func<float, bool> fits, float upper)
    {
        float lower = 0;
        for (int i = 0; i < 24; i++)
        {
            float mid = (lower + upper) / 2;
            if (fits(mid)) lower = mid; else upper = mid;
        }
        return lower;
    }

    private static float ApproachClearance(BolonMegastationPlan station, BolonAmbassadorBayPlan bay)
    {
        float clearance = 1500f;
        foreach (BolonVesselPlan other in station.Vessels.Where(v => v.Index != bay.VesselIndex))
        {
            Vector3 p = bay.Coordinates(other.Position);
            Vector3 nearest = Vector3.Clamp(p,
                new(-(bay.ClearWidth + 2f * BolonAmbassadorBayPlan.ChamferInset) / 2f - 12f, -40f, -1500f),
                new((bay.ClearWidth + 2f * BolonAmbassadorBayPlan.ChamferInset) / 2f + 12f, 40f, 0f));
            clearance = MathF.Min(clearance, Vector3.Distance(p, nearest) - other.Radius - 8f);
        }
        foreach (BolonVesselRelationship link in station.Relationships)
        {
            // Conservative segment vs inflated corridor AABB; includes direct joins.
            Vector3 a = bay.Coordinates(station.Vessels[link.A].Position);
            Vector3 b = bay.Coordinates(station.Vessels[link.B].Position);
            float radius = link.ConnectorRadius + 8f;
            float originalHalfWidth = (bay.ClearWidth + 2f * BolonAmbassadorBayPlan.ChamferInset) / 2f;
            Vector3 min = new(-originalHalfWidth - 12f - radius, -40f - radius, -1500f - radius);
            Vector3 max = new(originalHalfWidth + 12f + radius, 40f + radius, radius);
            if (SegmentBox(a, b, min, max)) return -1f;
        }
        return clearance;
    }

    private static bool SegmentBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
    {
        float enter = 0, exit = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z;
            float delta = axis == 0 ? b.X - a.X : axis == 1 ? b.Y - a.Y : b.Z - a.Z;
            float low = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float high = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(delta) < 1e-5f) { if (start < low || start > high) return false; continue; }
            float t0 = (low - start) / delta, t1 = (high - start) / delta;
            enter = MathF.Max(enter, MathF.Min(t0, t1)); exit = MathF.Min(exit, MathF.Max(t0, t1));
            if (enter > exit) return false;
        }
        return true;
    }

    private static string Signature(BolonAmbassadorBayPlan p)
    {
        string text = FormattableString.Invariant($"{p.Identity}|{p.CornerAxis}|{p.MouthCenter.X:R},{p.MouthCenter.Y:R},{p.MouthCenter.Z:R}|{p.Right.X:R},{p.Right.Y:R},{p.Right.Z:R}|{p.Up.X:R},{p.Up.Y:R},{p.Up.Z:R}|{p.ClearWidth:R}|{p.ClearHeight:R}|{p.ChamferDepth:R}|{p.ThroatLength:R}|{p.BayFrontWidth:R}|{p.BayWidth:R}|{p.BayHeight:R}|{p.ExpansionLength:R}|{p.BayLength:R}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

public static class BolonAmbassadorBayMeshBuilder
{
    public static void Emit(StationModuleMesh mesh, BolonAmbassadorBayPlan p)
    {
        mesh.CurrentMaterialFamily = SystemMaterialFamilyId.BrushedMetal;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(SystemMaterialFamilyId.BrushedMetal).TileSizeMeters;
        Vector3[] inner = p.Rectangle(p.ClearWidth, p.ClearHeight, p.ChamferDepth);
        Vector3[] throat = p.Rectangle(p.ClearWidth, p.ClearHeight, p.BayStartDepth);
        // The hull-matched reveal/chamfer is emitted by BolonSurfaceMeshBuilder.
        Join(mesh, p, inner, throat, new Color(223, 239, 255, 255));
        Vector3[] front = p.Octagon(p.BayFrontWidth, p.BayStartDepth);
        Vector3[] wide = p.Octagon(p.BayWidth, p.BayStartDepth + p.ExpansionLength);
        Vector3[] back = p.Octagon(p.BayWidth, p.BayStartDepth + p.BayLength);
        // Front bulkhead is an octagon MINUS the rectangular passage. Subtract using
        // the same convex clipping routine as the hull; no invisible throat cap.
        BolonSurfaceMeshBuilder.EmitAmbassadorBulkhead(mesh, front, throat, -p.Outward,
            new Color(191, 205, 223, 220));
        Join(mesh, p, front, wide, new Color(212, 224, 238, 235));
        Join(mesh, p, wide, back, new Color(212, 224, 238, 235));
        Vector3[] port = p.RearPortRectangle(p.RearPortWidth, p.RearPortHeight, p.BayEndDepth);
        // A floor-touching aperture is a notch, not an interior polygon hole.
        // Partition it explicitly so rotated, coincident floor edges cannot create
        // microscopic clipping slivers. All boundaries reuse the actual ring points.
        Vector3 topLeft = p.Point(-p.RearPortWidth / 2f, p.BayHeight / 2f, p.BayEndDepth);
        Vector3 topRight = p.Point(p.RearPortWidth / 2f, p.BayHeight / 2f, p.BayEndDepth);
        RearWall([back[0], port[0], topLeft, back[5], back[6], back[7]]);
        RearWall([port[1], back[1], back[2], back[3], back[4], topRight]);
        RearWall([port[3], port[2], topRight, topLeft]);
        Vector3[] portInner = p.RearPortRectangle(p.RearPortWidth - 1.5f, p.RearPortHeight - .75f,
            p.BayEndDepth + p.RearPortChamferDepth);
        Vector3[] termination = p.RearPortRectangle(p.RearPortWidth - 1.5f, p.RearPortHeight - .75f,
            p.BayEndDepth + p.RearPortChamferDepth + p.RearPortCorridorLength);
        JoinPort(port, portInner, new Color(169, 180, 196, 170));
        JoinPort(portInner, termination, new Color(98, 107, 120, 100));
        Quad(mesh, termination[0], termination[1], termination[2], termination[3], p.Outward, new Color(25, 29, 36, 65));

        void RearWall(Vector3[] polygon)
        {
            for (int i = 1; i < polygon.Length - 1; i++)
                Triangle(mesh, polygon[0], polygon[i], polygon[i + 1], p.Outward, new Color(202, 217, 236, 230));
        }

        void JoinPort(Vector3[] a, Vector3[] b, Color colour)
        {
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 centre = (a[i] + a[j] + b[i] + b[j]) * .25f;
                Vector3 q = p.Coordinates(centre);
                Vector3 target = p.Point(0, -p.BayHeight / 2f + p.RearPortHeight / 2f, q.Z);
                Quad(mesh, a[i], a[j], b[j], b[i], target - centre, colour);
            }
        }
    }

    private static void Join(StationModuleMesh mesh, BolonAmbassadorBayPlan p,
        Vector3[] a, Vector3[] b, Color colour)
    {
        for (int i = 0; i < a.Length; i++)
        {
            int j = (i + 1) % a.Length;
            Vector3 mid = (a[i] + a[j] + b[i] + b[j]) * .25f;
            Vector3 local = p.Coordinates(mid);
            Vector3 inward = -p.Right * local.X - p.Up * local.Y;
            // A fixed face-tone distinction is architectural, not a baked stellar term.
            // It keeps floor, ceiling and corner facets readable with the high floor.
            float tone = a.Length == 8 ? i switch { 0 => .70f, 1 or 7 => .80f, 2 or 6 => .92f, _ => 1f } : 1f;
            Color c = new((int)(colour.R * tone), (int)(colour.G * tone), (int)(colour.B * tone), colour.A);
            Quad(mesh, a[i], a[j], b[j], b[i], inward, c);
        }
    }
    internal static void Quad(StationModuleMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color colour)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) mesh.AddQuad(a, d, c, b, colour);
        else mesh.AddQuad(a, b, c, d, colour);
    }
    internal static void Triangle(StationModuleMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-5f) return;
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) mesh.AddTriangle(a, c, b, colour);
        else mesh.AddTriangle(a, b, c, colour);
    }
}
