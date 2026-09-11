using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationBayVentFamily { LouverBank, ProjectingDuct }

public sealed record MegastationBayPipeBank(
    string Identity, Vector2 Start, Vector2 End, int PipeCount,
    float Diameter, float Spacing, float SurfaceOffset, float SupportSpacing,
    Vector2 DropEnd, bool UsesTrussSupport);

public sealed record MegastationBaySecondaryUtilityInstallation(
    string Identity, string WallIdentity, string SourceNetworkIdentity,
    MegastationBayPipeBank Pipes,
    Vector2 ManifoldPosition, Vector2 ManifoldSize, float ManifoldDepth,
    Vector2 VentPosition, Vector2 VentSize, float VentDepth,
    MegastationBayVentFamily VentFamily,
    Vector2 HatchPosition, Vector2 HatchSize,
    float LadderBottom, float LadderTop, float LadderX,
    int PaletteVariant);

public sealed record MegastationBaySecondaryUtilityDiagnostics(
    int AlgorithmVersion, int ActiveWallCount, int InstallationCount,
    int PipeBankCount, int PipeCount, int PipeSupportCount,
    int ManifoldCount, int LouverBankCount, int ProjectingDuctCount,
    int HatchCount, int LadderCount, int PlatformCount,
    float TotalPipeLength, int HardConflictRejectCount, int WindowConflictScore,
    long PlanningMilliseconds, int VisibleVertexCount, int VisibleTriangleCount,
    int MajorCasterTriangleCount, string WallSummary, string Signature);

public sealed record MegastationBaySecondaryUtilityPlan(
    int AlgorithmVersion, int Seed,
    IReadOnlyList<MegastationBaySecondaryUtilityInstallation> Installations,
    MegastationBaySecondaryUtilityDiagnostics Diagnostics);

public sealed record MegastationBaySecondaryUtilityMeshResult(
    IReadOnlyList<int> ReceiverFaces,
    MegastationBaySecondaryUtilityDiagnostics Diagnostics);

public static class MegastationBaySecondaryUtilityPlanner
{
    public const int AlgorithmVersion = 1;

    public static MegastationBaySecondaryUtilityPlan Plan(
        MegastationInteriorPlan interior,
        MegastationLandingDistrictPlan landing,
        MegastationBayFacilityPlan facilities,
        MegastationBayStructuralTrussPlan trusses,
        MegastationBayUtilityPlan cableUtilities)
    {
        var stopwatch = Stopwatch.StartNew();
        int seed = MegastationSeed.Derive(interior.Seed, "bay-wall-secondary-utilities:v1");
        Dictionary<string, MegastationBayWallSurface> walls =
            MegastationBayHabitationPlanner.CreateWalls(interior)
                .ToDictionary(wall => wall.Identity, StringComparer.Ordinal);
        var installations = new List<MegastationBaySecondaryUtilityInstallation>();
        int hardRejects = 0, windowScore = 0;

        foreach (IGrouping<string, MegastationBayUtilityNetwork> wallNetworks in
                 cableUtilities.Networks.GroupBy(network => network.WallIdentity))
        {
            if (!walls.TryGetValue(wallNetworks.Key, out MegastationBayWallSurface? wall))
                continue;
            MegastationBayUtilityNetwork[] ordered = wallNetworks
                .OrderBy(network => network.Identity, StringComparer.Ordinal).ToArray();
            int desired = Math.Min(2, ordered.Length);
            for (int index = 0; index < desired; index++)
            {
                MegastationBayUtilityNetwork network = ordered[index];
                int child = MegastationSeed.Derive(seed, $"{wall.Identity}/installation:{index}");
                if (TryPlanInstallation(wall, network, trusses, landing, facilities,
                        child, index, ref hardRejects, out var installation,
                        out int windows))
                {
                    installations.Add(installation!);
                    windowScore += windows;
                }
            }
        }

        stopwatch.Stop();
        int supports = installations.Sum(item => Math.Max(2,
            (int)MathF.Floor(Vector2.Distance(item.Pipes.Start, item.Pipes.End)
                / item.Pipes.SupportSpacing) + 1));
        string summary = string.Join(",", walls.Values
            .Where(wall => wall.Kind != MegastationBayWallKind.Entrance)
            .Select(wall => $"{wall.Kind}:{installations.Count(item => item.WallIdentity == wall.Identity)}"));
        var diagnostics = new MegastationBaySecondaryUtilityDiagnostics(
            AlgorithmVersion,
            installations.Select(item => item.WallIdentity).Distinct().Count(),
            installations.Count, installations.Count,
            installations.Sum(item => item.Pipes.PipeCount), supports,
            installations.Count,
            installations.Count(item => item.VentFamily == MegastationBayVentFamily.LouverBank),
            installations.Count(item => item.VentFamily == MegastationBayVentFamily.ProjectingDuct),
            installations.Count, installations.Count, installations.Count,
            installations.Sum(item => Vector2.Distance(item.Pipes.Start, item.Pipes.End)
                * item.Pipes.PipeCount
                + Vector2.Distance(item.Pipes.End, item.Pipes.DropEnd)),
            hardRejects, windowScore, stopwatch.ElapsedMilliseconds,
            0, 0, 0, summary, Signature(installations));
        return new(AlgorithmVersion, seed, installations, diagnostics);
    }

    private static bool TryPlanInstallation(
        MegastationBayWallSurface wall, MegastationBayUtilityNetwork network,
        MegastationBayStructuralTrussPlan trusses,
        MegastationLandingDistrictPlan landing, MegastationBayFacilityPlan facilities,
        int seed, int ordinal, ref int hardRejects,
        out MegastationBaySecondaryUtilityInstallation? result, out int windowScore)
    {
        result = null;
        windowScore = 0;
        MegastationBayCableBundle? source = network.Bundles
            .Where(bundle => bundle.Level == MegastationBayUtilityRunLevel.Trunk)
            .OrderByDescending(bundle => bundle.Length).FirstOrDefault();
        if (source is null)
            return false;

        Candidate? best = null;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float length = MathHelper.Lerp(86f, 154f, Unit(seed, $"length:{attempt}"));
            length = MathF.Min(length, source.Length * .72f);
            float x = MathHelper.Lerp(source.Start.X + length * .5f,
                source.End.X - length * .5f, Unit(seed, $"x:{attempt}"));
            float side = ((attempt + ordinal) & 1) == 0 ? 1f : -1f;
            float y = source.Start.Y + side * MathHelper.Lerp(12f, 23f,
                Unit(seed, $"y:{attempt}"));
            float height = MathHelper.Lerp(34f, 52f, Unit(seed, $"height:{attempt}"));
            Vector2 centre = new(x, y + side * height * .16f);
            Vector2 size = new(length + 22f, height);
            if (MathF.Abs(centre.X) + size.X * .5f > wall.Width * .5f - 8f
                || MathF.Abs(centre.Y) + size.Y * .5f > wall.Height * .5f - 8f)
                continue;
            if (HardConflict(wall.Identity, centre, size, landing, facilities))
            {
                hardRejects++;
                continue;
            }
            int windows = SoftWindowScore(wall.Identity, centre, size, facilities);
            float score = windows * 4000f - length
                + MathF.Abs(y - source.Start.Y) * .2f;
            if (best is null || score < best.Score)
                best = new(x, y, side, length, height, windows, score);
        }
        if (best is null)
            return false;

        windowScore = best.Windows;
        string identity = $"interior/bay-secondary-utilities:v1/{wall.Identity}/installation:{ordinal}";
        int pipeCount = 2 + (int)((uint)MegastationSeed.Derive(seed, "pipe-count") % 3u);
        float diameter = MathHelper.Lerp(.34f, .92f, Unit(seed, "diameter"));
        if (Unit(seed, "major-pipe") < .12f)
            diameter = MathHelper.Lerp(1.05f, 1.55f, Unit(seed, "major-diameter"));
        diameter *= MathHelper.Lerp(1.55f, 2.0f, Unit(seed, "diameter-scale"));
        float spacing = diameter * MathHelper.Lerp(1.55f, 2.05f, Unit(seed, "spacing"));
        Vector2 start = new(best.X - best.Length * .5f, best.Y);
        Vector2 end = new(best.X + best.Length * .5f, best.Y);
        float drop = best.Side * MathHelper.Lerp(9f, 17f, Unit(seed, "drop"));
        Vector2 dropEnd = new(end.X, end.Y + drop);
        bool trussSupported = trusses.Trusses.Any(truss =>
            truss.HostIdentity == wall.Identity
            && MathF.Abs(Vector3.Dot(truss.Spec.Transform.Translation - wall.Centre, wall.Right)
                - best.X) < best.Length * .6f);
        var pipes = new MegastationBayPipeBank(
            $"{identity}/pipe-bank", start, end, pipeCount, diameter, spacing,
            1.0f + diameter * .5f, MathHelper.Lerp(19f, 28f, Unit(seed, "supports")),
            dropEnd, trussSupported);
        Vector2 manifoldSize = new(MathHelper.Lerp(6.5f, 9.5f, Unit(seed, "manifold-width")),
            MathHelper.Lerp(5.0f, 7.5f, Unit(seed, "manifold-height")));
        Vector2 manifold = dropEnd + new Vector2(0f, best.Side * manifoldSize.Y * .28f);
        MegastationBayVentFamily ventFamily = ordinal % 2 == 0
            ? MegastationBayVentFamily.ProjectingDuct
            : MegastationBayVentFamily.LouverBank;
        Vector2 ventSize = ventFamily == MegastationBayVentFamily.ProjectingDuct
            ? new(MathHelper.Lerp(11f, 17f, Unit(seed, "vent-width")),
                MathHelper.Lerp(8f, 13f, Unit(seed, "vent-height")))
            : new(MathHelper.Lerp(10f, 15f, Unit(seed, "vent-width")),
                MathHelper.Lerp(3.2f, 5f, Unit(seed, "vent-height")));
        Vector2 vent = manifold + new Vector2(-ventSize.X * .18f,
            best.Side * (manifoldSize.Y + ventSize.Y) * .62f);
        Vector2 hatchSize = new(2.2f, 3.4f);
        Vector2 hatch = vent + new Vector2(ventSize.X * .5f + 4.5f,
            best.Side * MathF.Max(1f, (ventSize.Y - hatchSize.Y) * .25f));
        float ladderTop = hatch.Y - best.Side * (hatchSize.Y * .5f + .8f);
        float ladderLength = MathHelper.Lerp(17f, 34f, Unit(seed, "ladder-length"));
        float ladderBottom = ladderTop - best.Side * ladderLength;
        float lowerLimit = -wall.Height * .5f + 4f;
        float upperLimit = wall.Height * .5f - 4f;
        ladderBottom = MathHelper.Clamp(ladderBottom, lowerLimit, upperLimit);
        ladderTop = MathHelper.Clamp(ladderTop, lowerLimit, upperLimit);
        result = new(identity, wall.Identity, network.Identity, pipes,
            manifold, manifoldSize, MathHelper.Lerp(1.2f, 1.9f, Unit(seed, "manifold-depth")),
            vent, ventSize, ventFamily == MegastationBayVentFamily.ProjectingDuct ? 2.2f : .75f,
            ventFamily, hatch, hatchSize, ladderBottom, ladderTop, hatch.X,
            (int)((uint)MegastationSeed.Derive(seed, "palette") % 5u));
        return true;
    }

    private static bool HardConflict(string wallIdentity, Vector2 centre, Vector2 size,
        MegastationLandingDistrictPlan landing, MegastationBayFacilityPlan facilities)
        => facilities.Reservations.Any(item => item.WallIdentity == wallIdentity
                && item.Kind != MegastationBayHabitationReservationKind.PlainWindowBand
                && Overlaps(centre, size, item.Centre, item.Size, 3f))
            || landing.SiteReservations.Any(item => item.HostWallIdentity == wallIdentity
                && Overlaps(centre, size, item.WallCentre, item.WallSize, 5f));

    private static int SoftWindowScore(string wallIdentity, Vector2 centre, Vector2 size,
        MegastationBayFacilityPlan facilities)
        => facilities.Reservations.Count(item => item.WallIdentity == wallIdentity
            && item.Kind == MegastationBayHabitationReservationKind.PlainWindowBand
            && Overlaps(centre, size, item.Centre, item.Size, 4f));

    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
    private static float Unit(int seed, string domain)
        => (uint)MegastationSeed.Derive(seed, domain) / (float)uint.MaxValue;

    private static string Signature(IEnumerable<MegastationBaySecondaryUtilityInstallation> items)
    {
        var text = new StringBuilder(1024).Append(AlgorithmVersion);
        foreach (var item in items)
            text.Append('|').Append(item.Identity).Append(':').Append(item.Pipes.Start)
                .Append(':').Append(item.Pipes.End).Append(':').Append(item.Pipes.PipeCount)
                .Append(':').Append(item.Pipes.Diameter.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(item.ManifoldPosition).Append(':').Append(item.VentPosition)
                .Append(':').Append((int)item.VentFamily).Append(':').Append(item.HatchPosition)
                .Append(':').Append(item.LadderBottom.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(item.LadderTop.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed record Candidate(float X, float Y, float Side, float Length,
        float Height, int Windows, float Score);
}

public static class MegastationBaySecondaryUtilityMeshBuilder
{
    public static MegastationBaySecondaryUtilityMeshResult Append(
        StationModuleMesh mesh, MegastationBaySecondaryUtilityPlan plan,
        IReadOnlyList<MegastationBayWallSurface> walls,
        MegastationSystemMaterialAssignment? materials)
    {
        int firstVertex = mesh.VertexCount, firstTriangle = mesh.IndexCount / 3;
        int majorTriangles = 0;
        var receiverFaces = new List<int>();
        foreach (var item in plan.Installations)
        {
            MegastationBayWallSurface wall = walls.Single(w => w.Identity == item.WallIdentity);
            Color structural = materials?.Palette.SecondaryTint ?? new Color(78, 83, 84);
            Color pipe = PipeColour(item.PaletteVariant);
            Color dark = new(24, 27, 28);
            Color panel = ProceduralMaterialCpuGenerator.Blend(structural, pipe, .13f);

            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
            mesh.CurrentMaterialFamily = SystemMaterialFamilyId.BrushedMetal;
            Vector2 pipeDirection2 = Vector2.Normalize(item.Pipes.End - item.Pipes.Start);
            Vector3 pipeDirection = wall.Right * pipeDirection2.X + wall.Up * pipeDirection2.Y;
            Vector3 bankAcross = wall.Up;
            float radius = item.Pipes.Diameter * .5f;
            for (int index = 0; index < item.Pipes.PipeCount; index++)
            {
                float offset = (index - (item.Pipes.PipeCount - 1) * .5f) * item.Pipes.Spacing;
                Vector3 start = Point(wall, item.Pipes.Start, item.Pipes.SurfaceOffset)
                    + bankAcross * offset;
                Vector3 end = Point(wall, item.Pipes.End, item.Pipes.SurfaceOffset)
                    + bankAcross * offset;
                AddReceiver(() => mesh.AddPrismPipe(start, end, radius, 8, pipe, true, true));
            }

            int supports = Math.Max(2, (int)MathF.Floor(
                Vector2.Distance(item.Pipes.Start, item.Pipes.End) / item.Pipes.SupportSpacing) + 1);
            float bankWidth = (item.Pipes.PipeCount - 1) * item.Pipes.Spacing + item.Pipes.Diameter;
            for (int support = 0; support < supports; support++)
            {
                float t = support / (float)(supports - 1);
                Vector2 local = Vector2.Lerp(item.Pipes.Start, item.Pipes.End, t);
                Matrix bracket = WallFrame(wall,
                    Point(wall, local, item.Pipes.SurfaceOffset - radius * .78f));
                mesh.CurrentMaterialFamily = SystemMaterialFamilyId.HeavyIndustrialPlate;
                AddReceiver(() => mesh.AddOrientedBox(bracket,
                    new(.52f, .34f, bankWidth + .65f), structural));
            }

            // The rigid drop and block elbow visually connect the bank to its manifold.
            Vector3 elbow = Point(wall, item.Pipes.End, item.Pipes.SurfaceOffset);
            Vector3 drop = Point(wall, item.Pipes.DropEnd, item.Pipes.SurfaceOffset);
            mesh.CurrentMaterialFamily = SystemMaterialFamilyId.BrushedMetal;
            AddReceiver(() => mesh.AddPrismPipe(elbow, drop, radius * 1.08f, 8, pipe, true, true));
            Matrix elbowFrame = WallFrame(wall, elbow);
            AddReceiver(() => mesh.AddOrientedBox(elbowFrame,
                new(item.Pipes.Diameter * 1.45f, item.Pipes.Diameter * 1.45f,
                    item.Pipes.Diameter * 1.45f), pipe));

            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMajor;
            mesh.CurrentMaterialFamily = SystemMaterialFamilyId.HeavyIndustrialPlate;
            int beforeMajor = mesh.IndexCount;
            Matrix manifold = WallFrame(wall, Point(wall, item.ManifoldPosition,
                item.ManifoldDepth * .5f + .12f));
            AddReceiver(() => mesh.AddOrientedBox(manifold,
                new(item.ManifoldSize.X, item.ManifoldDepth, item.ManifoldSize.Y), panel));
            majorTriangles += (mesh.IndexCount - beforeMajor) / 3;
            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
            for (int port = 0; port < item.Pipes.PipeCount; port++)
            {
                float y = (port - (item.Pipes.PipeCount - 1) * .5f) * item.Pipes.Spacing;
                Vector3 portCentre = Point(wall,
                    item.ManifoldPosition + new Vector2(-item.ManifoldSize.X * .34f, y),
                    item.ManifoldDepth + .16f);
                Vector3 portTip = portCentre + wall.Normal * .25f;
                AddReceiver(() => mesh.AddPrismPipe(portCentre, portTip,
                    radius * .82f, 8, pipe, true, true));
            }

            EmitVent(item, wall, mesh, structural, dark, pipe, receiverFaces, ref majorTriangles);
            EmitHatchAndAccess(item, wall, mesh, structural, dark, pipe, receiverFaces);
        }
        return new(receiverFaces, plan.Diagnostics with
        {
            VisibleVertexCount = mesh.VertexCount - firstVertex,
            VisibleTriangleCount = mesh.IndexCount / 3 - firstTriangle,
            MajorCasterTriangleCount = majorTriangles,
        });

        void AddReceiver(Action add)
        {
            int first = mesh.FaceCount;
            add();
            for (int face = first; face < mesh.FaceCount; face++) receiverFaces.Add(face);
        }
    }

    private static void EmitVent(MegastationBaySecondaryUtilityInstallation item,
        MegastationBayWallSurface wall, StationModuleMesh mesh, Color structural,
        Color dark, Color accent, List<int> faces, ref int majorTriangles)
    {
        bool major = item.VentFamily == MegastationBayVentFamily.ProjectingDuct;
        mesh.CurrentDecorClass = major ? DecorClass.MegastationInteriorMajor
            : DecorClass.MegastationInteriorMinor;
        mesh.CurrentMaterialFamily = major ? SystemMaterialFamilyId.HeavyIndustrialPlate
            : SystemMaterialFamilyId.DullStructuralMetal;
        int before = mesh.IndexCount;
        AddBox(item.VentPosition, item.VentSize, item.VentDepth, structural);
        if (major) majorTriangles += (mesh.IndexCount - before) / 3;
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        AddBox(item.VentPosition, item.VentSize * new Vector2(.82f, .72f),
            item.VentDepth + .16f, dark, .08f);
        int slats = major ? 8 : 5;
        for (int index = 0; index < slats; index++)
        {
            float y = MathHelper.Lerp(-item.VentSize.Y * .28f, item.VentSize.Y * .28f,
                (index + .5f) / slats);
            AddBox(item.VentPosition + new Vector2(0f, y),
                new(item.VentSize.X * .76f, MathF.Max(.16f, item.VentSize.Y * .045f)),
                item.VentDepth + .34f, accent, .10f);
        }
        void AddBox(Vector2 local, Vector2 size, float offset, Color colour, float depth = .24f)
        {
            int first = mesh.FaceCount;
            Matrix frame = WallFrame(wall, Point(wall, local, offset));
            mesh.AddOrientedBox(frame, new(size.X, depth, size.Y), colour);
            for (int face = first; face < mesh.FaceCount; face++) faces.Add(face);
        }
    }

    private static void EmitHatchAndAccess(MegastationBaySecondaryUtilityInstallation item,
        MegastationBayWallSurface wall, StationModuleMesh mesh, Color structural,
        Color dark, Color accent, List<int> faces)
    {
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        mesh.CurrentMaterialFamily = SystemMaterialFamilyId.PaintedCoatedMetal;
        AddBox(item.HatchPosition, item.HatchSize, .34f, structural);
        AddBox(item.HatchPosition, item.HatchSize - new Vector2(.34f, .34f), .53f, dark, .18f);
        AddBox(item.HatchPosition + new Vector2(item.HatchSize.X * .27f, 0f),
            new(.12f, .62f), .67f, accent, .12f);

        float low = MathF.Min(item.LadderBottom, item.LadderTop);
        float high = MathF.Max(item.LadderBottom, item.LadderTop);
        float ladderOffset = .72f;
        float halfWidth = .25f;
        mesh.CurrentMaterialFamily = SystemMaterialFamilyId.BrushedMetal;
        AddPipe(new(item.LadderX - halfWidth, low), new(item.LadderX - halfWidth, high), .045f);
        AddPipe(new(item.LadderX + halfWidth, low), new(item.LadderX + halfWidth, high), .045f);
        int rungs = Math.Max(2, (int)MathF.Floor((high - low) / .30f) + 1);
        for (int rung = 0; rung < rungs; rung++)
        {
            float y = MathHelper.Lerp(low, high, rung / (float)(rungs - 1));
            AddPipe(new(item.LadderX - halfWidth, y), new(item.LadderX + halfWidth, y), .035f);
        }

        float platformY = item.LadderTop;
        AddBox(new(item.LadderX, platformY), new(2.5f, .22f), 1.0f, structural, 1.8f);
        Vector3 railLeft = Point(wall, new(item.LadderX - 1.1f, platformY + 1.05f), 1.75f);
        Vector3 railRight = Point(wall, new(item.LadderX + 1.1f, platformY + 1.05f), 1.75f);
        AddWorldPipe(Point(wall, new(item.LadderX - 1.1f, platformY), 1.75f), railLeft, .045f);
        AddWorldPipe(Point(wall, new(item.LadderX + 1.1f, platformY), 1.75f), railRight, .045f);
        AddWorldPipe(railLeft, railRight, .045f);

        void AddBox(Vector2 local, Vector2 size, float offset, Color colour, float depth = .20f)
        {
            int first = mesh.FaceCount;
            mesh.AddOrientedBox(WallFrame(wall, Point(wall, local, offset)),
                new(size.X, depth, size.Y), colour);
            for (int face = first; face < mesh.FaceCount; face++) faces.Add(face);
        }
        void AddPipe(Vector2 start, Vector2 end, float radius)
            => AddWorldPipe(Point(wall, start, ladderOffset), Point(wall, end, ladderOffset), radius);
        void AddWorldPipe(Vector3 start, Vector3 end, float radius)
        {
            int first = mesh.FaceCount;
            mesh.AddPrismPipe(start, end, radius, 6, structural, true, true);
            for (int face = first; face < mesh.FaceCount; face++) faces.Add(face);
        }
    }

    private static Vector3 Point(MegastationBayWallSurface wall, Vector2 local, float offset)
        => wall.Centre + wall.Right * local.X + wall.Up * local.Y + wall.Normal * offset;
    private static Matrix WallFrame(MegastationBayWallSurface wall, Vector3 centre) => new(
        -wall.Right.X,-wall.Right.Y,-wall.Right.Z,0f,
        wall.Normal.X,wall.Normal.Y,wall.Normal.Z,0f,
        wall.Up.X,wall.Up.Y,wall.Up.Z,0f,
        centre.X,centre.Y,centre.Z,1f);
    private static Color PipeColour(int variant) => variant switch
    {
        1 => new Color(112, 91, 55),
        2 => new Color(91, 63, 58),
        3 => new Color(61, 76, 89),
        4 => new Color(112, 111, 99),
        _ => new Color(73, 78, 79),
    };
}
