using System.Diagnostics;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationWindowState { Lit, Dim, Dark }

public sealed record MegastationWindowMaskRect(
    BoundaryFaceKey Face, float MinU, float MaxU, float MinV, float MaxV);

public sealed record MegastationPlanarSurfaceRegion(
    string Identity,
    string ZoneIdentity,
    int ZoneSeed,
    GridDirection Direction,
    GridDirection SourceFace,
    int PlaneGridCoordinate,
    float PlaneCoordinateMetres,
    Vector3 Normal,
    Vector3 Right,
    Vector3 Up,
    IReadOnlyList<BoundaryFaceKey> Faces,
    IReadOnlyList<MegastationWindowMaskRect> Mask,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV,
    float PhysicalArea);

public sealed record MegastationWindowBlock(
    string Identity,
    string RegionIdentity,
    int Column,
    int Row,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV,
    Color DominantColour);

public sealed record MegastationWindowInstance(
    string Identity,
    string RegionIdentity,
    string BlockIdentity,
    Vector3 Centre,
    Vector3 Normal,
    Vector3 Up,
    float Width,
    float Height,
    MegastationWindowState State,
    Color Colour);

public sealed record MegastationWindowDiagnostics(
    int HabitationZoneCount,
    int EligibleRegionCount,
    int ActiveRegionCount,
    int DarkRegionCount,
    int BlockCount,
    int WindowCount,
    int LitWindowCount,
    int DimWindowCount,
    int DarkWindowCount,
    int AbsentCandidateCount,
    float EligibleHabitationWallArea,
    float ActiveWindowArea,
    long PlanningMilliseconds,
    int MeshVertexCount = 0,
    int MeshTriangleCount = 0,
    long MeshBytes = 0,
    long MeshBuildMilliseconds = 0);

public sealed record MegastationWindowPlan(
    IReadOnlyList<MegastationPlanarSurfaceRegion> Regions,
    IReadOnlyList<MegastationWindowBlock> Blocks,
    IReadOnlyList<MegastationWindowInstance> Windows,
    MegastationWindowDiagnostics Diagnostics);

public sealed record MegastationWindowMeshBuildResult(
    StationModuleMesh Mesh,
    MegastationWindowDiagnostics Diagnostics);

internal sealed record MegastationWindowTuning(
    float ActiveRegionProbability,
    float MinimumBlockWidth,
    float MaximumBlockWidth,
    float MinimumBlockHeight,
    float MaximumBlockHeight,
    float MinimumSeparator,
    float MaximumSeparator,
    float MissingBlockProbability,
    float MinimumWindowSpacing,
    float MaximumWindowSpacing,
    float MinimumWindowScale,
    float MaximumWindowScale,
    float FootprintMargin,
    float AbsentProbability,
    float DarkProbability,
    float DimProbability)
{
    public static MegastationWindowTuning Default { get; } = new(
        ActiveRegionProbability: 0.62f,
        MinimumBlockWidth: 28f,
        MaximumBlockWidth: 72f,
        MinimumBlockHeight: 20f,
        MaximumBlockHeight: 52f,
        MinimumSeparator: 6f,
        MaximumSeparator: 12f,
        MissingBlockProbability: 0.28f,
        MinimumWindowSpacing: 3.0f,
        MaximumWindowSpacing: 4.5f,
        MinimumWindowScale: 0.38f,
        MaximumWindowScale: 0.55f,
        FootprintMargin: 0.25f,
        AbsentProbability: 0.24f,
        DarkProbability: 0.14f,
        DimProbability: 0.18f);
}

public static class MegastationWindowPlanner
{
    private const string AlgorithmKey = "windows:v1";

    public static MegastationWindowPlan Plan(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
        => Plan(grid, topology, zoning, MegastationWindowTuning.Default, cancellationToken);

    internal static MegastationWindowPlan Plan(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        MegastationWindowTuning tuning,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        MegastationPlanarSurfaceRegion[] regions = ExtractRegions(grid, topology, zoning, cancellationToken);
        var blocks = new List<MegastationWindowBlock>();
        var windows = new List<MegastationWindowInstance>();
        int activeRegions = 0;
        int absentCandidates = 0;
        float activeArea = 0f;

        foreach (MegastationPlanarSurfaceRegion region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int regionSeed = MegastationSeed.Derive(
                MegastationSeed.Derive(region.ZoneSeed, AlgorithmKey), region.Identity);
            if (Sample(regionSeed, "active") >= tuning.ActiveRegionProbability)
                continue;

            activeRegions++;
            float blockWidth = Lerp(tuning.MinimumBlockWidth, tuning.MaximumBlockWidth, Sample(regionSeed, "block-width"));
            float blockHeight = Lerp(tuning.MinimumBlockHeight, tuning.MaximumBlockHeight, Sample(regionSeed, "block-height"));
            float separatorU = Lerp(tuning.MinimumSeparator, tuning.MaximumSeparator, Sample(regionSeed, "separator-u"));
            float separatorV = Lerp(tuning.MinimumSeparator, tuning.MaximumSeparator, Sample(regionSeed, "separator-v"));
            float pitchU = blockWidth + separatorU;
            float pitchV = blockHeight + separatorV;
            float spacingU = Lerp(tuning.MinimumWindowSpacing, tuning.MaximumWindowSpacing, Sample(regionSeed, "spacing-u"));
            float spacingV = Lerp(tuning.MinimumWindowSpacing, tuning.MaximumWindowSpacing, Sample(regionSeed, "spacing-v"));
            float windowWidth = spacingU * Lerp(tuning.MinimumWindowScale, tuning.MaximumWindowScale, Sample(regionSeed, "window-width"));
            float windowHeight = spacingV * Lerp(tuning.MinimumWindowScale, tuning.MaximumWindowScale, Sample(regionSeed, "window-height"));

            int firstBlockColumn = (int)MathF.Floor(region.MinU / pitchU);
            int lastBlockColumn = (int)MathF.Ceiling(region.MaxU / pitchU) - 1;
            int firstBlockRow = (int)MathF.Floor(region.MinV / pitchV);
            int lastBlockRow = (int)MathF.Ceiling(region.MaxV / pitchV) - 1;
            for (int blockRow = firstBlockRow; blockRow <= lastBlockRow; blockRow++)
            for (int blockColumn = firstBlockColumn; blockColumn <= lastBlockColumn; blockColumn++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string blockIdentity = $"{region.Identity}/block:{blockColumn}:{blockRow}";
                if (Sample(regionSeed, $"block-present:{blockColumn}:{blockRow}") < tuning.MissingBlockProbability)
                    continue;

                float blockMinU = blockColumn * pitchU + separatorU * 0.5f;
                float blockMaxU = blockMinU + blockWidth;
                float blockMinV = blockRow * pitchV + separatorV * 0.5f;
                float blockMaxV = blockMinV + blockHeight;
                Color dominant = PickDominantColour(regionSeed, blockColumn, blockRow);
                int windowStart = windows.Count;
                int firstWindowColumn = (int)MathF.Ceiling(blockMinU / spacingU - 0.5f);
                int lastWindowColumn = (int)MathF.Floor(blockMaxU / spacingU - 0.5f);
                int firstWindowRow = (int)MathF.Ceiling(blockMinV / spacingV - 0.5f);
                int lastWindowRow = (int)MathF.Floor(blockMaxV / spacingV - 0.5f);

                for (int row = firstWindowRow; row <= lastWindowRow; row++)
                {
                    float v = (row + 0.5f) * spacingV;
                    if (v < blockMinV || v > blockMaxV) continue;
                    for (int column = firstWindowColumn; column <= lastWindowColumn; column++)
                    {
                        float u = (column + 0.5f) * spacingU;
                        if (u < blockMinU || u > blockMaxU) continue;
                        if (!ContainsFootprint(region, u, v, windowWidth, windowHeight, tuning.FootprintMargin))
                            continue;

                        string windowKey = $"window:{column}:{row}";
                        float stateRoll = Sample(regionSeed, $"{blockIdentity}:{windowKey}:state");
                        if (stateRoll < tuning.AbsentProbability)
                        {
                            absentCandidates++;
                            continue;
                        }

                        MegastationWindowState state;
                        Color colour;
                        if (stateRoll < tuning.AbsentProbability + tuning.DarkProbability)
                        {
                            state = MegastationWindowState.Dark;
                            colour = StationWindowVisuals.DarkWarm;
                        }
                        else if (stateRoll < tuning.AbsentProbability + tuning.DarkProbability + tuning.DimProbability)
                        {
                            state = MegastationWindowState.Dim;
                            colour = StationWindowVisuals.DimAmber;
                        }
                        else
                        {
                            state = MegastationWindowState.Lit;
                            colour = PickLitColour(regionSeed, blockIdentity, column, row, dominant);
                        }

                        Vector3 centre = region.Normal * (region.PlaneCoordinateMetres + 0.05f)
                            + region.Right * u + region.Up * v;
                        windows.Add(new(
                            $"{blockIdentity}/{windowKey}", region.Identity, blockIdentity,
                            centre, region.Normal, region.Up, windowWidth, windowHeight, state, colour));
                    }
                }

                if (windows.Count == windowStart)
                    continue;
                blocks.Add(new(
                    blockIdentity, region.Identity, blockColumn, blockRow,
                    blockMinU, blockMaxU, blockMinV, blockMaxV, dominant));
                activeArea += CoveredArea(region, blockMinU, blockMaxU, blockMinV, blockMaxV);
            }
        }

        stopwatch.Stop();
        MegastationWindowInstance[] orderedWindows = windows.OrderBy(window => window.Identity, StringComparer.Ordinal).ToArray();
        MegastationWindowBlock[] orderedBlocks = blocks.OrderBy(block => block.Identity, StringComparer.Ordinal).ToArray();
        var diagnostics = new MegastationWindowDiagnostics(
            zoning.Zones.Count(zone => zone.Role == MegastationZoneRole.Habitation),
            regions.Length,
            activeRegions,
            regions.Length - activeRegions,
            orderedBlocks.Length,
            orderedWindows.Length,
            orderedWindows.Count(window => window.State == MegastationWindowState.Lit),
            orderedWindows.Count(window => window.State == MegastationWindowState.Dim),
            orderedWindows.Count(window => window.State == MegastationWindowState.Dark),
            absentCandidates,
            regions.Sum(region => region.PhysicalArea),
            activeArea,
            stopwatch.ElapsedMilliseconds);
        return new(regions, orderedBlocks, orderedWindows, diagnostics);
    }

    public static MegastationPlanarSurfaceRegion[] ExtractRegions(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
    {
        var candidateFaces = zoning.Zones
            .Where(zone => zone.Role == MegastationZoneRole.Habitation && zone.Anchor.SourceFace.HasValue)
            .SelectMany(zone => zone.Faces.Select(face => (zone, face)))
            .Where(pair => IsEligibleWall(pair.zone.Anchor.SourceFace!.Value, pair.face.Direction))
            .OrderBy(pair => pair.face)
            .ToArray();
        var regions = new List<MegastationPlanarSurfaceRegion>();

        foreach (var planeGroup in candidateFaces.GroupBy(pair => (
                     pair.zone.Identity, pair.face.Direction, PlaneCoordinate(pair.face))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byFace = planeGroup.ToDictionary(pair => pair.face, pair => pair.zone);
            var remaining = new SortedSet<BoundaryFaceKey>(byFace.Keys);
            while (remaining.Count > 0)
            {
                BoundaryFaceKey first = remaining.Min;
                var component = new SortedSet<BoundaryFaceKey>();
                var queue = new Queue<BoundaryFaceKey>();
                remaining.Remove(first);
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    BoundaryFaceKey current = queue.Dequeue();
                    component.Add(current);
                    foreach (BoundaryEdgeKey edge in topology.FaceByKey[current].Edges)
                    foreach (BoundaryFaceKey neighbour in topology.EdgeByKey[edge].IncidentFaces)
                    {
                        if (byFace.ContainsKey(neighbour) && remaining.Remove(neighbour))
                            queue.Enqueue(neighbour);
                    }
                }

                MegastationSemanticZone zone = byFace[first];
                GridDirection sourceFace = zone.Anchor.SourceFace!.Value;
                Vector3 normal = BoundaryTopologyBuilder.Normal(first.Direction);
                Vector3 up = BoundaryTopologyBuilder.Normal(sourceFace);
                Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
                MegastationWindowMaskRect[] mask = component
                    .Select(face => MaskRect(grid, topology.FaceByKey[face], right, up))
                    .ToArray();
                string maskKey = string.Join('|', component.Select(FaceIdentity));
                int maskSignature = MegastationSeed.Derive(zone.Seed, maskKey);
                string identity = $"{zone.Identity}/plane:{first.Direction}:{PlaneCoordinate(first)}:" +
                    $"{component.Min.X},{component.Min.Y},{component.Min.Z}:{unchecked((uint)maskSignature):X8}";
                regions.Add(new(
                    identity, zone.Identity, zone.Seed, first.Direction, sourceFace,
                    PlaneCoordinate(first),
                    Vector3.Dot(BoundaryTopologyBuilder.Position(grid, topology.FaceByKey[first].Vertices[0]), normal),
                    normal, right, up, component.ToArray(), mask,
                    mask.Min(rect => rect.MinU), mask.Max(rect => rect.MaxU),
                    mask.Min(rect => rect.MinV), mask.Max(rect => rect.MaxV),
                    mask.Sum(rect => (rect.MaxU - rect.MinU) * (rect.MaxV - rect.MinV))));
            }
        }

        return regions.OrderBy(region => region.Identity, StringComparer.Ordinal).ToArray();
    }

    public static bool IsEligibleWall(GridDirection sourceFace, GridDirection surfaceDirection)
        => Direction.PrimaryAxis(sourceFace) != Direction.PrimaryAxis(surfaceDirection);

    public static bool ContainsFootprint(
        MegastationPlanarSurfaceRegion region,
        float centreU,
        float centreV,
        float width,
        float height,
        float margin)
    {
        float minU = centreU - width * 0.5f - margin;
        float maxU = centreU + width * 0.5f + margin;
        float minV = centreV - height * 0.5f - margin;
        float maxV = centreV + height * 0.5f + margin;
        float required = (maxU - minU) * (maxV - minV);
        float covered = CoveredArea(region, minU, maxU, minV, maxV);
        return covered >= required - MathF.Max(0.001f, required * 0.0001f);
    }

    private static float CoveredArea(
        MegastationPlanarSurfaceRegion region,
        float minU,
        float maxU,
        float minV,
        float maxV)
    {
        float covered = 0f;
        foreach (MegastationWindowMaskRect rect in region.Mask)
        {
            float overlapU = MathF.Max(0f, MathF.Min(maxU, rect.MaxU) - MathF.Max(minU, rect.MinU));
            float overlapV = MathF.Max(0f, MathF.Min(maxV, rect.MaxV) - MathF.Max(minV, rect.MinV));
            covered += overlapU * overlapV;
        }
        return covered;
    }

    private static MegastationWindowMaskRect MaskRect(
        SliceGrid grid, BoundaryFace face, Vector3 right, Vector3 up)
    {
        Vector3[] positions = face.Vertices.Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray();
        float[] u = positions.Select(position => Vector3.Dot(position, right)).ToArray();
        float[] v = positions.Select(position => Vector3.Dot(position, up)).ToArray();
        return new(face.Key, u.Min(), u.Max(), v.Min(), v.Max());
    }

    private static int PlaneCoordinate(BoundaryFaceKey face) => face.Direction switch
    {
        GridDirection.PositiveX => face.X + 1,
        GridDirection.NegativeX => face.X,
        GridDirection.PositiveY => face.Y + 1,
        GridDirection.NegativeY => face.Y,
        GridDirection.PositiveZ => face.Z + 1,
        _ => face.Z,
    };

    private static string FaceIdentity(BoundaryFaceKey face)
        => $"{face.X},{face.Y},{face.Z},{(int)face.Direction}";

    private static float Sample(int seed, string key)
        => unchecked((uint)MegastationSeed.Derive(seed, key)) / (float)uint.MaxValue;

    private static float Lerp(float minimum, float maximum, float t)
        => minimum + (maximum - minimum) * t;

    private static Color PickDominantColour(int seed, int column, int row)
    {
        float roll = Sample(seed, $"block-colour:{column}:{row}");
        return roll < 0.58f ? StationWindowVisuals.WarmWhite
            : roll < 0.82f ? StationWindowVisuals.NeutralWhite
            : roll < 0.94f ? StationWindowVisuals.CoolBlue
            : StationWindowVisuals.DimAmber;
    }

    private static Color PickLitColour(int seed, string blockIdentity, int column, int row, Color dominant)
    {
        float roll = Sample(seed, $"{blockIdentity}:window:{column}:{row}:colour");
        if (roll < 0.78f) return dominant;
        if (roll < 0.88f) return StationWindowVisuals.WarmWhite;
        if (roll < 0.95f) return StationWindowVisuals.NeutralWhite;
        return StationWindowVisuals.CoolBlue;
    }
}

public static class MegastationWindowMeshBuilder
{
    public static MegastationWindowMeshBuildResult Build(
        MegastationWindowPlan plan,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var mesh = new StationModuleMesh();
        for (int i = 0; i < plan.Windows.Count; i++)
        {
            if ((i & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            MegastationWindowInstance window = plan.Windows[i];
            Vector3 right = Vector3.Normalize(Vector3.Cross(window.Up, window.Normal));
            Vector3 halfRight = right * (window.Width * 0.5f);
            Vector3 halfUp = window.Up * (window.Height * 0.5f);
            Vector3 bottomLeft = window.Centre - halfRight - halfUp;
            Vector3 bottomRight = window.Centre + halfRight - halfUp;
            Vector3 topRight = window.Centre + halfRight + halfUp;
            Vector3 topLeft = window.Centre - halfRight + halfUp;
            Color bottom = StationWindowVisuals.GlassBottom(window.Colour);
            Color top = StationWindowVisuals.GlassTop(window.Colour);
            mesh.AddQuadGradient(
                bottomLeft, bottom, bottomRight, bottom,
                topRight, top, topLeft, top);
        }
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();
        int triangles = mesh.IndexCount / 3;
        long bytes = (long)mesh.VertexCount * VertexPositionNormalColorTexture.VertexDeclaration.VertexStride
            + (long)mesh.IndexCount * sizeof(int);
        return new(mesh, plan.Diagnostics with
        {
            MeshVertexCount = mesh.VertexCount,
            MeshTriangleCount = triangles,
            MeshBytes = bytes,
            MeshBuildMilliseconds = stopwatch.ElapsedMilliseconds,
        });
    }
}
