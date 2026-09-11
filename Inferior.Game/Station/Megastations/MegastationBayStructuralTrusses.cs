using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationBayStructuralTrussHostKind { Wall, Ceiling }
public enum MegastationBayCeilingTrussOrientation { LeftRight, EntranceRear }

public sealed record MegastationBayStructuralTruss(
    string Identity,
    string FieldIdentity,
    MegastationBayStructuralTrussHostKind HostKind,
    string HostIdentity,
    BoundaryFaceKey SupportingFace,
    Vector3 Start,
    Vector3 End,
    StructuralTrussSpec Spec,
    float ColourBlend,
    bool CastsStellarShadow);

public sealed record MegastationBayStructuralTrussAttachment(
    string Identity,
    string TrussIdentity,
    Vector3 Centre,
    Vector3 Size,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    float ColourBlend,
    bool CastsStellarShadow);

public sealed record MegastationBayStructuralTrussField(
    string Identity,
    MegastationBayStructuralTrussHostKind HostKind,
    string HostIdentity,
    StructuralTrussCrossSection Family,
    float ColourBlend,
    MegastationBayCeilingTrussOrientation? CeilingOrientation,
    IReadOnlyList<MegastationBayStructuralTruss> Trusses);

public sealed record MegastationBayStructuralTrussDiagnostics(
    int AlgorithmVersion,
    int WallFieldCount,
    int CeilingFieldCount,
    int WallTrussCount,
    int CeilingTrussCount,
    int HardConflictRejectCount,
    int OperationalClearanceRejectCount,
    int SupportRejectCount,
    int SoftWindowOverlapCount,
    int VisibleTriangleCount,
    int CasterTriangleCount,
    long PlanningMilliseconds,
    string Summary,
    string Signature);

public sealed record MegastationBayStructuralTrussPlan(
    int AlgorithmVersion,
    int Seed,
    IReadOnlyList<MegastationBayStructuralTrussField> Fields,
    IReadOnlyList<MegastationBayStructuralTruss> Trusses,
    IReadOnlyList<MegastationBayStructuralTrussAttachment> Attachments,
    MegastationBayStructuralTrussDiagnostics Diagnostics);

public static class MegastationBayStructuralTrussPlanner
{
    public const int AlgorithmVersion = 1;

    public static MegastationBayStructuralTrussPlan Plan(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingDistrictPlan landing,
        MegastationBayFacilityPlan facilities,
        MegastationMegaShelfPlan shelves)
    {
        _ = shelves; // Shelf bodies may intentionally cross wall truss runs.
        var stopwatch = Stopwatch.StartNew();
        int seed = MegastationSeed.Derive(interior.Seed, "bay-structural-trusses:v1");
        var fields = new List<MegastationBayStructuralTrussField>();
        var attachments = new List<MegastationBayStructuralTrussAttachment>();
        int hardRejects = 0;
        int operationalRejects = 0;
        int supportRejects = 0;
        int softOverlaps = 0;

        foreach (MegastationBayWallSurface wall in
                 MegastationBayHabitationPlanner.CreateWalls(interior))
        {
            int wallSeed = MegastationSeed.Derive(seed, $"wall-field:{wall.Identity}");
            if (Unit(wallSeed, "presence") > .86f)
                continue;
            StructuralTrussCrossSection family = Unit(wallSeed, "family") < .56f
                ? StructuralTrussCrossSection.Box : StructuralTrussCrossSection.Triangular;
            float fieldWidth = MathHelper.Lerp(7f, 11.5f, Unit(wallSeed, "width"));
            float fieldDepth = MathHelper.Lerp(5f, 8.5f, Unit(wallSeed, "depth"));
            float colourBlend = MathHelper.Lerp(.22f, .68f, Unit(wallSeed, "colour"));
            int desired = wall.Width < 180f ? 1 : wall.Width < 300f ? 2
                : wall.Width < 470f ? 3 : 4;
            if (Unit(wallSeed, "count-down") < .18f)
                desired = Math.Max(1, desired - 1);
            string fieldIdentity = $"interior/bay-truss:v1/{wall.Identity}";
            var placements = new List<(int Slot, Candidate Candidate)>();
            for (int slot = 0; slot < desired; slot++)
            {
                float t = desired == 1 ? .5f : (slot + 1f) / (desired + 1f);
                float nominalX = MathHelper.Lerp(-wall.Width * .42f, wall.Width * .42f, t);
                Candidate? best = null;
                for (int attempt = 0; attempt < 13; attempt++)
                {
                    int child = MegastationSeed.Derive(wallSeed, $"slot:{slot}:attempt:{attempt}");
                    float searchStep = MathF.Max(7f, wall.Width / MathF.Max(12f, desired * 5f));
                    float signedStep = attempt == 0 ? 0f
                        : ((attempt & 1) == 1 ? 1f : -1f) * ((attempt + 1) / 2) * searchStep;
                    float x = nominalX + signedStep + (Unit(child, "jitter") - .5f) * 2.5f;
                    float width = fieldWidth;
                    float depth = fieldDepth;
                    if (MathF.Abs(x) + width * .5f + 3f > wall.Width * .5f)
                        continue;
                    if (!TryWallSupport(wall, x, width, occupancy.Grid, topology,
                            out BoundaryFace? face, out float minY, out float maxY))
                    {
                        supportRejects++;
                        continue;
                    }
                    Vector2 runCentre = new(x, (minY + maxY) * .5f);
                    Vector2 runSize = new(width, maxY - minY);
                    if (facilities.Reservations.Any(r => IsHard(r.Kind)
                            && SameWall(wall, r.WallIdentity)
                            && Overlaps(runCentre, runSize, r.Centre, r.Size, 2f)))
                    {
                        hardRejects++;
                        continue;
                    }
                    if (landing.SiteReservations.Any(r => SameWall(wall, r.HostWallIdentity)
                            && Overlaps(runCentre, runSize, r.WallCentre, r.WallSize, 3f))
                        || IntersectsPadSetback(wall, x, width, depth, minY, maxY, landing))
                    {
                        operationalRejects++;
                        continue;
                    }
                    int overlap = facilities.Reservations.Count(r =>
                        r.Kind == MegastationBayHabitationReservationKind.PlainWindowBand
                        && SameWall(wall, r.WallIdentity)
                        && Overlaps(runCentre, runSize, r.Centre, r.Size, 0f));
                    float score = overlap * 1000f + MathF.Abs(x - nominalX);
                    if (best is null || score < best.Score)
                        best = new(face!, x, minY, maxY, width, depth, child, overlap, score);
                }
                if (best is null)
                    continue;
                softOverlaps += best.SoftOverlap;
                placements.Add((slot, best));
            }
            if (placements.Count > 0)
            {
                float commonMinY = placements.Max(item => item.Candidate.MinY);
                float commonMaxY = placements.Min(item => item.Candidate.MaxY);
                if (commonMaxY - commonMinY < 60f)
                {
                    (int Slot, Candidate Candidate) longest = placements
                        .OrderByDescending(item => item.Candidate.MaxY - item.Candidate.MinY)
                        .First();
                    placements = [longest];
                    commonMinY = longest.Candidate.MinY;
                    commonMaxY = longest.Candidate.MaxY;
                }
                MegastationBayStructuralTruss[] accepted = placements.Select(item =>
                    BuildWallTruss(fieldIdentity, wall, family, item.Slot,
                        item.Candidate with { MinY = commonMinY, MaxY = commonMaxY },
                        wallSeed, colourBlend)).ToArray();
                foreach (MegastationBayStructuralTruss truss in accepted)
                    AddWallAttachments(truss, wall, attachments);
                fields.Add(new(fieldIdentity, MegastationBayStructuralTrussHostKind.Wall,
                    wall.Identity, family, colourBlend, null, accepted));
            }
        }

        MegastationBayStructuralTrussField? ceiling = BuildCeilingField(
            interior, occupancy.Grid, topology, landing, shelves, seed,
            ref operationalRejects, ref supportRejects);
        if (ceiling is not null)
            fields.Add(ceiling);

        MegastationBayStructuralTruss[] trusses = fields.SelectMany(field => field.Trusses).ToArray();
        stopwatch.Stop();
        int triangles = trusses.Sum(item => MegastationMegaShelfPlanner.EstimateTrussTriangles(item.Spec))
            + attachments.Count * 12;
        string signature = Signature(fields, attachments);
        var diagnostics = new MegastationBayStructuralTrussDiagnostics(
            AlgorithmVersion,
            fields.Count(field => field.HostKind == MegastationBayStructuralTrussHostKind.Wall),
            fields.Count(field => field.HostKind == MegastationBayStructuralTrussHostKind.Ceiling),
            trusses.Count(item => item.HostKind == MegastationBayStructuralTrussHostKind.Wall),
            trusses.Count(item => item.HostKind == MegastationBayStructuralTrussHostKind.Ceiling),
            hardRejects, operationalRejects, supportRejects, softOverlaps,
            triangles, triangles, stopwatch.ElapsedMilliseconds,
            string.Join(",", fields.Select(field =>
                $"{field.HostIdentity}:{field.Trusses.Count}x{field.Family}")), signature);
        return new(AlgorithmVersion, seed, fields, trusses, attachments, diagnostics);
    }

    private static MegastationBayStructuralTruss BuildWallTruss(
        string fieldIdentity, MegastationBayWallSurface wall,
        StructuralTrussCrossSection family, int index, Candidate candidate,
        int fieldSeed, float colourBlend)
    {
        float length = candidate.MaxY - candidate.MinY;
        Vector3 centre = wall.Centre + wall.Right * candidate.X
            + wall.Up * ((candidate.MinY + candidate.MaxY) * .5f)
            + wall.Normal * (candidate.Depth * .5f + .35f);
        Vector3 localX = -wall.Right;
        Vector3 localY = wall.Normal;
        Vector3 localZ = wall.Up;
        Matrix frame = Frame(localX, localY, localZ, centre);
        float chord = MathHelper.Lerp(.75f, 1.18f, Unit(fieldSeed, "chord"));
        float brace = MathHelper.Lerp(.38f, MathF.Min(.68f, chord), Unit(fieldSeed, "brace"));
        var spec = new StructuralTrussSpec(length, candidate.Width, candidate.Depth,
            family, chord, brace, MathHelper.Lerp(12f, 20f, Unit(fieldSeed, "bay")), frame);
        return new($"{fieldIdentity}/run:{index}", fieldIdentity,
            MegastationBayStructuralTrussHostKind.Wall, wall.Identity,
            candidate.Face.Key,
            centre - localZ * (length * .5f), centre + localZ * (length * .5f),
            spec, colourBlend, CastsStellarShadow: true);
    }

    private static void AddWallAttachments(
        MegastationBayStructuralTruss truss, MegastationBayWallSurface wall,
        List<MegastationBayStructuralTrussAttachment> attachments)
    {
        float blockHeight = MathHelper.Clamp(truss.Spec.Width * .38f, 2.8f, 4.8f);
        Vector3 size = new(truss.Spec.Width * 1.18f, truss.Spec.Height * 1.10f, blockHeight);
        for (int end = -1; end <= 1; end += 2)
        {
            Vector3 centre = (end < 0 ? truss.Start : truss.End) - wall.Up * end * (blockHeight * .18f);
            attachments.Add(new($"{truss.Identity}/attachment:{(end < 0 ? "lower" : "upper")}",
                truss.Identity, centre, size, -wall.Right, wall.Normal, wall.Up,
                truss.ColourBlend, true));
        }
    }

    private static MegastationBayStructuralTrussField? BuildCeilingField(
        MegastationInteriorPlan interior, SliceGrid grid, BoundaryTopology topology,
        MegastationLandingDistrictPlan landing, MegastationMegaShelfPlan shelves, int rootSeed,
        ref int operationalRejects, ref int supportRejects)
    {
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 right = Vector3.Normalize(interior.PortalRight);
        Vector3 inward = Vector3.Normalize(-interior.OutwardNormal);
        int seed = MegastationSeed.Derive(rootSeed, "ceiling-field");
        BoundaryFace[] allFaces = topology.Faces
            .Where(face => face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction), -up) > .9999f)
            .OrderBy(face => face.Key).ToArray();
        if (allFaces.Length == 0)
        {
            supportRejects++;
            return null;
        }
        var planeGroup = allFaces
            .Select(face => (Face: face, Points: face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
            .GroupBy(item => (int)MathF.Round(Vector3.Dot(item.Points[0], up) * 100f))
            .OrderByDescending(group => group.Sum(item => FaceArea(item.Face, grid)))
            .ThenByDescending(group => group.Key)
            .First().ToArray();
        BoundaryFace[] faces = planeGroup.Select(item => item.Face).ToArray();
        Vector3[] points = planeGroup.SelectMany(item => item.Points).ToArray();
        MegastationBayCeilingTrussOrientation orientation = Unit(seed, "orientation") < .5f
            ? MegastationBayCeilingTrussOrientation.LeftRight
            : MegastationBayCeilingTrussOrientation.EntranceRear;
        Vector3 lengthAxis = orientation == MegastationBayCeilingTrussOrientation.LeftRight
            ? right : inward;
        Vector3 offsetAxis = orientation == MegastationBayCeilingTrussOrientation.LeftRight
            ? inward : right;
        float lengthMin = points.Min(point => Vector3.Dot(point, lengthAxis));
        float lengthMax = points.Max(point => Vector3.Dot(point, lengthAxis));
        float offsetMin = points.Min(point => Vector3.Dot(point, offsetAxis));
        float offsetMax = points.Max(point => Vector3.Dot(point, offsetAxis));
        float plane = planeGroup.Average(item => Vector3.Dot(item.Points[0], up));
        float span = lengthMax - lengthMin;
        float distribution = offsetMax - offsetMin;
        if (span < 90f || distribution < 35f)
            return null;
        int count = distribution < 180f ? 1 : distribution < 340f ? 2
            : distribution < 560f ? 3 : distribution < 820f ? 4 : 5;
        StructuralTrussCrossSection family = Unit(seed, "family") < .52f
            ? StructuralTrussCrossSection.Box : StructuralTrussCrossSection.Triangular;
        float fieldWidth = MathHelper.Lerp(7f, 12f, Unit(seed, "width"));
        float fieldHeight = MathHelper.Lerp(5.5f, 9f, Unit(seed, "height"));
        float fieldChord = MathHelper.Lerp(.82f, 1.28f, Unit(seed, "chord"));
        float fieldBrace = MathHelper.Lerp(.40f, MathF.Min(.72f, fieldChord), Unit(seed, "brace"));
        float fieldBay = MathHelper.Lerp(14f, 22f, Unit(seed, "bay"));
        float colourBlend = MathHelper.Lerp(.22f, .68f, Unit(seed, "colour"));
        string fieldIdentity = "interior/bay-truss:v1/ceiling-field";
        var supportedRuns = new List<(int Index, float Offset, BoundaryFace Host,
            float Minimum, float Maximum)>();
        for (int index = 0; index < count; index++)
        {
            int child = MegastationSeed.Derive(seed, $"run:{index}");
            float t = (index + 1f) / (count + 1f);
            float offset = MathHelper.Lerp(offsetMin, offsetMax, t)
                + (Unit(child, "jitter") - .5f) * MathF.Min(9f, distribution / (count + 1f) * .16f);
            if (!TryContiguousRun(faces, grid, lengthAxis, offsetAxis, offset, fieldWidth + 2f,
                    out BoundaryFace? host, out float supportedMin, out float supportedMax))
            {
                supportRejects++;
                continue;
            }
            if (supportedMax - supportedMin < 81.2f)
            {
                supportRejects++;
                continue;
            }
            supportedRuns.Add((index, offset, host!, supportedMin, supportedMax));
        }
        if (supportedRuns.Count == 0)
            return null;

        // Every run in a roof field uses one manufactured component size. Individual
        // centre positions may differ where authoritative boundary patches are offset,
        // but length and all cross-section/member dimensions remain identical.
        const float inset = .6f;
        float length = supportedRuns.Min(run => run.Maximum - run.Minimum) - inset * 2f;
        var trusses = new List<MegastationBayStructuralTruss>(supportedRuns.Count);
        foreach ((int index, float offset, BoundaryFace host,
                     float supportedMin, float supportedMax) in supportedRuns)
        {
            Vector3 faceNormal = -up;
            Vector3 centre = lengthAxis * ((supportedMin + supportedMax) * .5f)
                + offsetAxis * offset + up * plane + faceNormal * (fieldHeight * .5f + .45f);
            Vector3 localY = faceNormal;
            Vector3 localZ = lengthAxis;
            Vector3 localX = Vector3.Normalize(Vector3.Cross(localY, localZ));
            Matrix frame = Frame(localX, localY, localZ, centre);
            var spec = new StructuralTrussSpec(length, fieldWidth, fieldHeight, family,
                fieldChord, fieldBrace, fieldBay, frame);
            Vector3 start = centre - localZ * (length * .5f);
            Vector3 end = centre + localZ * (length * .5f);
            Vector3 boundsSize = new(fieldWidth, fieldHeight, length);
            if (IntersectsMacro(centre, localX, localY, localZ, boundsSize,
                    interior.ArrivalManeuverExclusion)
                || shelves.Shelves.Any(shelf => shelf.ClearanceVolume.ReservesLandingOperations
                    && BoxesIntersect(centre, localX, localY, localZ, boundsSize,
                        shelf.ClearanceVolume.Centre, shelf.ClearanceVolume.Right,
                        shelf.ClearanceVolume.Up, shelf.ClearanceVolume.Forward,
                        shelf.ClearanceVolume.Size)))
            {
                operationalRejects++;
                continue;
            }
            trusses.Add(new($"{fieldIdentity}/run:{index}", fieldIdentity,
                MegastationBayStructuralTrussHostKind.Ceiling, "bay-ceiling", host!.Key,
                start, end, spec, colourBlend, true));
        }
        return trusses.Count == 0 ? null : new(fieldIdentity,
            MegastationBayStructuralTrussHostKind.Ceiling, "bay-ceiling", family,
            colourBlend, orientation, trusses);
    }

    private static bool TryWallSupport(
        MegastationBayWallSurface wall, float x, float width, SliceGrid grid,
        BoundaryTopology topology, out BoundaryFace? face, out float minY, out float maxY)
    {
        var candidates = topology.Faces
            .Where(item => item.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(item.Direction), wall.Normal) > .9999f)
            .Select(item => (Face: item, Points: item.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
            .Where(item => MathF.Abs(Vector3.Dot(item.Points[0] - wall.Centre, wall.Normal)) < .01f)
            .Select(item => (item.Face,
                MinX: item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Right)),
                MaxX: item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Right)),
                MinY: item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Up)),
                MaxY: item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Up))))
            .Where(item => x - width * .5f - 1f >= item.MinX
                && x + width * .5f + 1f <= item.MaxX)
            .OrderBy(item => item.MinY).ThenBy(item => item.Face.Key).ToArray();
        if (candidates.Length == 0)
        {
            face = null; minY = maxY = 0f; return false;
        }
        (float Min, float Max, BoundaryFace Face) best = default;
        int start = 0;
        while (start < candidates.Length)
        {
            float runMin = candidates[start].MinY;
            float runMax = candidates[start].MaxY;
            BoundaryFace representative = candidates[start].Face;
            int next = start + 1;
            while (next < candidates.Length && candidates[next].MinY <= runMax + .02f)
            {
                runMax = MathF.Max(runMax, candidates[next].MaxY);
                next++;
            }
            if (runMax - runMin > best.Max - best.Min)
                best = (runMin, runMax, representative);
            start = next;
        }
        face = best.Face;
        minY = best.Min + 2.2f;
        maxY = best.Max - 2.2f;
        return maxY - minY >= 60f;
    }

    private static bool TryContiguousRun(
        IReadOnlyList<BoundaryFace> faces, SliceGrid grid,
        Vector3 lengthAxis, Vector3 offsetAxis, float offset, float requiredWidth,
        out BoundaryFace? representative, out float minimum, out float maximum)
    {
        var intervals = faces.Select(face => (Face: face, Points: face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
            .Select(item => (item.Face,
                MinLength: item.Points.Min(point => Vector3.Dot(point, lengthAxis)),
                MaxLength: item.Points.Max(point => Vector3.Dot(point, lengthAxis)),
                MinOffset: item.Points.Min(point => Vector3.Dot(point, offsetAxis)),
                MaxOffset: item.Points.Max(point => Vector3.Dot(point, offsetAxis))))
            .Where(item => offset - requiredWidth * .5f >= item.MinOffset - .01f
                && offset + requiredWidth * .5f <= item.MaxOffset + .01f)
            .OrderBy(item => item.MinLength).ThenBy(item => item.Face.Key).ToArray();
        representative = null;
        minimum = maximum = 0f;
        if (intervals.Length == 0)
            return false;
        int start = 0;
        while (start < intervals.Length)
        {
            float runMin = intervals[start].MinLength;
            float runMax = intervals[start].MaxLength;
            BoundaryFace face = intervals[start].Face;
            int next = start + 1;
            while (next < intervals.Length && intervals[next].MinLength <= runMax + .02f)
            {
                runMax = MathF.Max(runMax, intervals[next].MaxLength);
                next++;
            }
            if (representative is null || runMax - runMin > maximum - minimum)
            {
                representative = face;
                minimum = runMin;
                maximum = runMax;
            }
            start = next;
        }
        return representative is not null;
    }

    private static bool IntersectsPadSetback(
        MegastationBayWallSurface wall, float x, float width, float depth,
        float minY, float maxY, MegastationLandingDistrictPlan landing)
    {
        Vector3 centre = wall.Centre + wall.Right * x
            + wall.Up * ((minY + maxY) * .5f) + wall.Normal * depth * .5f;
        Vector3[] corners = BoxCorners(centre, -wall.Right, wall.Normal, wall.Up,
            new(width, depth, maxY - minY));
        float minR = corners.Min(point => Vector3.Dot(point, landing.DistrictRight));
        float maxR = corners.Max(point => Vector3.Dot(point, landing.DistrictRight));
        float minF = corners.Min(point => Vector3.Dot(point, landing.PreferredHeading));
        float maxF = corners.Max(point => Vector3.Dot(point, landing.PreferredHeading));
        return landing.Pads.Any(pad => minR < pad.BuildingSetbackClearance.RightMaximum
            && maxR > pad.BuildingSetbackClearance.RightMinimum
            && minF < pad.BuildingSetbackClearance.ForwardMaximum
            && maxF > pad.BuildingSetbackClearance.ForwardMinimum);
    }

    private static bool IntersectsMacro(
        Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 size,
        MegastationInteriorMacroExclusionVolume volume)
        => BoxesIntersect(centre, right, up, forward, size,
            volume.Centre, volume.Right, volume.Up, volume.Forward, volume.Size);

    private static bool BoxesIntersect(
        Vector3 aCentre, Vector3 aRight, Vector3 aUp, Vector3 aForward, Vector3 aSize,
        Vector3 bCentre, Vector3 bRight, Vector3 bUp, Vector3 bForward, Vector3 bSize)
    {
        (Vector3 aMin, Vector3 aMax) = Bounds(aCentre, aRight, aUp, aForward, aSize);
        (Vector3 bMin, Vector3 bMax) = Bounds(bCentre, bRight, bUp, bForward, bSize);
        return aMin.X < bMax.X && aMax.X > bMin.X && aMin.Y < bMax.Y
            && aMax.Y > bMin.Y && aMin.Z < bMax.Z && aMax.Z > bMin.Z;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(
        Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 size)
    {
        Vector3[] corners = BoxCorners(centre, right, up, forward, size);
        return (new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
            new(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)));
    }

    private static Vector3[] BoxCorners(
        Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 size)
    {
        Vector3 r = right * size.X * .5f;
        Vector3 u = up * size.Y * .5f;
        Vector3 f = forward * size.Z * .5f;
        return [centre-r-u-f, centre-r-u+f, centre-r+u-f, centre-r+u+f,
            centre+r-u-f, centre+r-u+f, centre+r+u-f, centre+r+u+f];
    }

    private static bool IsHard(MegastationBayHabitationReservationKind kind)
        => kind != MegastationBayHabitationReservationKind.PlainWindowBand;
    private static bool SameWall(MegastationBayWallSurface wall, string identity)
        => string.Equals(wall.Identity, identity, StringComparison.Ordinal);
    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
    private static float FaceArea(BoundaryFace face, SliceGrid grid)
    {
        Vector3[] p = face.Vertices.Select(v => BoundaryTopologyBuilder.Position(grid, v)).ToArray();
        return Vector3.Distance(p[0], p[1]) * Vector3.Distance(p[1], p[2]);
    }
    private static Matrix Frame(Vector3 x, Vector3 y, Vector3 z, Vector3 centre) => new(
        x.X,x.Y,x.Z,0f, y.X,y.Y,y.Z,0f, z.X,z.Y,z.Z,0f,
        centre.X,centre.Y,centre.Z,1f);
    private static float Unit(int seed, string domain)
        => (uint)MegastationSeed.Derive(seed, domain) / (float)uint.MaxValue;
    private static string Signature(
        IEnumerable<MegastationBayStructuralTrussField> fields,
        IEnumerable<MegastationBayStructuralTrussAttachment> attachments)
    {
        var text = new StringBuilder(512).Append(AlgorithmVersion);
        foreach (MegastationBayStructuralTrussField field in fields)
        {
            text.Append('|').Append(field.Identity).Append(':').Append((int)field.Family);
            foreach (MegastationBayStructuralTruss truss in field.Trusses)
                text.Append('|').Append(truss.Identity).Append(':')
                    .Append(truss.Start.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(truss.Start.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(truss.Start.Z.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(truss.Spec.Length.ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (MegastationBayStructuralTrussAttachment attachment in attachments)
            text.Append('|').Append(attachment.Identity).Append(':').Append(attachment.Centre);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed record Candidate(BoundaryFace Face, float X, float MinY, float MaxY,
        float Width, float Depth, int Seed, int SoftOverlap, float Score);
}
