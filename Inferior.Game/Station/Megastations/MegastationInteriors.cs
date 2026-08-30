using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public readonly record struct MegastationGridRange(int Start, int End)
{
    public int Count => End - Start;
    public bool Contains(int value) => value >= Start && value < End;
}

public sealed record MegastationInteriorVolume(
    MegastationGridRange X,
    MegastationGridRange Y,
    MegastationGridRange Z,
    Vector3 Minimum,
    Vector3 Maximum)
{
    public Vector3 Size => Maximum - Minimum;
    public bool ContainsCell(int x, int y, int z)
        => X.Contains(x) && Y.Contains(y) && Z.Contains(z);
}

public readonly record struct MegastationProtectedVoidCell(
    MegacellCoord Cell,
    MegacellVoidKind Kind);

public enum MegastationEntranceType
{
    Standard,
    Grand,
}

public sealed record MegastationInteriorDiagnostics(
    int AlgorithmVersion,
    int InteriorCount,
    GridDirection PortalDirection,
    float PortalClearWidth,
    float PortalClearHeight,
    float ThroatLength,
    Vector3 MainFlightClearSize,
    int ProtectedVoidCellCount,
    int RemovedStructuralCellCount,
    int ThroatBoundaryFaceCount,
    int InteriorBoundaryFaceCount,
    int InteriorStructuralVertexCount,
    int InteriorStructuralTriangleCount,
    int PortalVisibleVertexCount,
    int PortalVisibleTriangleCount,
    int PortalCasterVertexCount,
    int PortalCasterTriangleCount,
    long PlanningMilliseconds,
    long MeshBuildMilliseconds,
    string Signature,
    int PortalGuidanceElementCount = 0,
    int ThroatGuidanceElementCount = 0,
    int InteriorLandmarkElementCount = 0,
    int GuidanceGlowCount = 0,
    int GuidanceVisibleVertexCount = 0,
    int GuidanceVisibleTriangleCount = 0,
    int ThroatLinerElementCount = 0,
    int ThroatRibElementCount = 0,
    int ThroatMarkingElementCount = 0,
    int ThroatCasterElementCount = 0,
    float EntranceProjectionLength = 0f,
    float EntranceLocalObstructionProjection = 0f,
    float EntranceLocalSkylineHeight = 0f,
    float EntranceProjectionHeightFraction = 0f,
    string EntrancePaletteIdentity = "",
    int EntrancePrecinctReservationCount = 0,
    int ThroatTubeWallElementCount = 0,
    int ThroatCrownElementCount = 0,
    int ThroatFixtureElementCount = 0,
    int ApproachBeamCount = 0,
    int ApproachFixtureElementCount = 0,
    float ApproachBeamLength = 0f,
    float ApproachBeamHalfAngleDegrees = 0f,
    int ApproachBeamVertexCount = 0,
    int ApproachBeamTriangleCount = 0,
    Vector3 EntrancePortalUp = default,
    Vector3 EntrancePortalRight = default,
    MegastationEntranceType EntranceType = MegastationEntranceType.Standard,
    float BayClearWidth = 0f,
    float EntranceWidthFraction = 0f,
    float LargeUprightVerticalClearance = 0f,
    float LargeRolledVerticalClearance = 0f,
    float CrownOuterWidth = 0f,
    float CrownOuterHeight = 0f,
    float EntranceClearanceMargin = 0f,
    int EntranceAssemblyRemovedCellCount = 0);

public sealed record MegastationInteriorPlan(
    string Identity,
    int Seed,
    GridDirection PortalDirection,
    Vector3 PortalCentre,
    Vector3 OutwardNormal,
    Vector3 PortalRight,
    Vector3 PortalUp,
    Vector3 InteriorDownDirection,
    MegastationEntranceType EntranceType,
    float ThroatWallThickness,
    Vector2 PortalClearSize,
    MegastationInteriorVolume ThroatVolume,
    MegastationInteriorVolume MainFlightVolume,
    MegastationInteriorVolume CavityEnvelope,
    MegastationEntrancePrecinct EntrancePrecinct,
    IReadOnlyList<MegastationProtectedVoidCell> ProtectedCells,
    MegastationInteriorDiagnostics Diagnostics);

public sealed record MegastationInteriorMeshBuildResult(
    StationModuleMesh Mesh,
    MegastationInteriorDiagnostics Diagnostics);

public enum MegastationInteriorGuidanceKind
{
    PortalEdge,
    PortalCorner,
    PortalCrown,
    ThroatBand,
    InteriorLandmark,
    ThroatLiner,
    ThroatBeam,
    ThroatRib,
    ThroatTransition,
    ThroatMarking,
    ApproachFixture,
}

public enum MegastationApproachBeamVertical
{
    Upper,
    Lower,
}

public sealed record MegastationApproachGuidanceBeam(
    string Identity,
    MegastationApproachBeamVertical Vertical,
    int HorizontalSign,
    Vector3 Source,
    Vector3 Axis,
    Vector3 RadialRight,
    Vector3 RadialUp,
    Color Colour,
    float Length,
    float HalfAngleDegrees);

public sealed record MegastationInteriorGuidanceElement(
    string Identity,
    MegastationInteriorGuidanceKind Kind,
    Matrix Frame,
    Vector3 Size,
    Color Colour,
    float Illumination,
    SystemMaterialFamilyId MaterialFamily,
    bool CastsShadow)
{
    public Vector3 Centre => Frame.Translation;
}

public sealed record MegastationInteriorGuidanceMarker(
    string Identity,
    MegastationInteriorGuidanceKind Kind,
    Vector3 Position,
    Color Colour,
    float Intensity,
    Vector3? SurfaceNormal = null,
    float? GlowSizePixels = null,
    float? GlowFadeStartMeters = null,
    float? GlowFadeEndMeters = null);

public sealed record MegastationEntrancePalette(
    string Identity,
    Color Guidance,
    Color Highlight,
    Color StructuralAccent,
    Color OuterStructure,
    Color InnerStructure,
    Color CrownStructure);

public sealed record MegastationEntrancePrecinct(
    Vector3 Minimum,
    Vector3 Maximum,
    Vector3 AssemblyMinimum,
    Vector3 AssemblyMaximum,
    Vector3 OuterMouthCentre,
    float CrownOuterWidth,
    float CrownOuterHeight,
    float ClearanceMargin,
    float LocalObstructionProjection,
    float ProjectionLength,
    float LocalSkylineHeight,
    float ProjectionHeightFraction)
{
    public bool Intersects(Vector3 minimum, Vector3 maximum)
        => minimum.X < Maximum.X && maximum.X > Minimum.X
            && minimum.Y < Maximum.Y && maximum.Y > Minimum.Y
            && minimum.Z < Maximum.Z && maximum.Z > Minimum.Z;

    public bool Contains(Vector3 point)
        => point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y
            && point.Z >= Minimum.Z && point.Z <= Maximum.Z;

    public bool AssemblyIntersects(Vector3 minimum, Vector3 maximum)
        => minimum.X < AssemblyMaximum.X && maximum.X > AssemblyMinimum.X
            && minimum.Y < AssemblyMaximum.Y && maximum.Y > AssemblyMinimum.Y
            && minimum.Z < AssemblyMaximum.Z && maximum.Z > AssemblyMinimum.Z;
}

public sealed record MegastationInteriorPresentationPlan(
    int PortalGuidanceSeed,
    int ThroatGuidanceSeed,
    int InteriorLandmarkSeed,
    int ThroatLinerSeed,
    int ThroatRibsSeed,
    int ThroatMarkingsSeed,
    int ThroatFixturesSeed,
    int ApproachGuidanceSeed,
    MegastationEntrancePalette Palette,
    MegastationEntrancePrecinct Precinct,
    IReadOnlyList<MegastationInteriorGuidanceElement> Elements,
    IReadOnlyList<MegastationInteriorGuidanceMarker> Markers,
    IReadOnlyList<MegastationApproachGuidanceBeam> ApproachBeams)
{
    public int PortalElementCount => Elements.Count(element => element.Kind is
        MegastationInteriorGuidanceKind.PortalEdge
        or MegastationInteriorGuidanceKind.PortalCorner
        or MegastationInteriorGuidanceKind.PortalCrown);
    public int ThroatElementCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatBand);
    public int InteriorLandmarkCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.InteriorLandmark);
    public int ThroatLinerCount => Elements.Count(element => element.Kind is
        MegastationInteriorGuidanceKind.ThroatLiner
        or MegastationInteriorGuidanceKind.ThroatBeam);
    public int ThroatRibCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatRib);
    public int ThroatTubeWallCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatLiner);
    public int ThroatCrownCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatTransition
        && element.Identity.StartsWith("entrance/crown/", StringComparison.Ordinal));
    public int ThroatFixtureCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatBand
        && element.Identity.StartsWith("throat/fixture:", StringComparison.Ordinal));
    public int ApproachFixtureElementCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ApproachFixture);
    public int ThroatMarkingCount => Elements.Count(element =>
        element.Kind == MegastationInteriorGuidanceKind.ThroatMarking);
    public int ThroatCasterCount => Elements.Count(element => element.CastsShadow);
}

public static class MegastationInteriorPlanner
{
    private const int AlgorithmVersion = 1;
    private const float LargeEnvelopeWidth = 36f;
    private const float LargeEnvelopeHeight = 20f;
    private const float GrandSelectionProbability = .32f;

    public static MegastationInteriorPlan PlanAndApply(
        StructuralOccupancy occupancy,
        int rootSeed,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        SliceGrid grid = occupancy.Grid;
        int siteSeed = MegastationSeed.Derive(rootSeed, "interior site");
        GridDirection portalDirection = ChoosePortalDirection(grid, siteSeed);
        GridAxis entranceAxis = Direction.PrimaryAxis(portalDirection);
        GridAxis upAxis = entranceAxis == GridAxis.Y ? GridAxis.Z : GridAxis.Y;
        GridAxis widthAxis = Enum.GetValues<GridAxis>().Single(axis => axis != entranceAxis && axis != upAxis);

        MegastationGridRange cavityEntrance = CentredRange(
            grid, entranceAxis, grid.CoreRange(entranceAxis), 700f, shellCells: 3);
        MegastationGridRange cavityWidth = CentredRange(
            grid, widthAxis, grid.CoreRange(widthAxis), 620f, shellCells: 2);
        MegastationGridRange cavityHeight = CentredRange(
            grid, upAxis, grid.CoreRange(upAxis), 360f, shellCells: 2);
        MegastationGridRange flightEntrance = CentredSubrange(
            grid, entranceAxis, cavityEntrance, 520f);
        MegastationGridRange flightWidth = CentredSubrange(
            grid, widthAxis, cavityWidth, 440f);
        MegastationGridRange flightHeight = CentredSubrange(
            grid, upAxis, cavityHeight, 240f);
        MegastationGridRange standardThroatWidth = CentredSubrange(
            grid, widthAxis, flightWidth, 160f);
        MegastationGridRange standardThroatHeight = CentredSubrange(
            grid, upAxis, flightHeight, 110f);
        int morphologySeed = MegastationSeed.Derive(rootSeed, "entrance morphology:v1");
        float bayClearWidth = Span(grid, widthAxis, flightWidth);
        float requestedGrandWidthFraction = MathHelper.Lerp(
            .70f,
            .95f,
            Sample(morphologySeed, "grand width fraction"));
        float requestedGrandHeight = MathHelper.Lerp(
            40f,
            46f,
            Sample(morphologySeed, "grand clear height"));

        // H1's authoritative void remains grid-aligned. The tube wall absorbs the
        // coarse final slice increment so Grand's rendered clear height can still
        // follow the documented Large envelope instead of a particular ship mesh.
        MegastationGridRange grandThroatHeight = CentredSubrange(
            grid,
            upAxis,
            flightHeight,
            requestedGrandHeight + 32f);
        float grandStructuralHeight = Span(grid, upAxis, grandThroatHeight);
        float baseGrandWallThickness = MegastationInteriorPresentationPlanner.ComputeWallThickness(
            siteSeed,
            grandStructuralHeight,
            grandStructuralHeight);
        float grandWallThickness = MathF.Max(
            baseGrandWallThickness,
            (grandStructuralHeight - requestedGrandHeight) * .5f);
        MegastationGridRange grandThroatWidth = CentredSubrange(
            grid,
            widthAxis,
            flightWidth,
            bayClearWidth * requestedGrandWidthFraction + grandWallThickness * 2f);
        float grandStructuralWidth = Span(grid, widthAxis, grandThroatWidth);
        float grandClearWidth = grandStructuralWidth - grandWallThickness * 2f;
        float grandClearHeight = grandStructuralHeight - grandWallThickness * 2f;
        float grandWidthFraction = grandClearWidth / bayClearWidth;
        float standardStructuralWidth = Span(grid, widthAxis, standardThroatWidth);
        bool grandEligible = grandClearWidth >= standardStructuralWidth * 1.75f
            && grandWidthFraction >= .68f
            && grandWidthFraction <= .98f
            && grandClearHeight >= 40f
            && grandClearHeight <= 46.01f
            && grandWallThickness <= 48f
            && grandClearHeight > LargeEnvelopeWidth;

        // Selection has its own semantic domain: presentation palette, recessed
        // lights, and approach-beam revisions cannot switch entrance morphology.
        bool selectGrand = grandEligible
            && Sample(morphologySeed, "grand selection") < GrandSelectionProbability;
        MegastationEntranceType entranceType = selectGrand
            ? MegastationEntranceType.Grand
            : MegastationEntranceType.Standard;
        MegastationGridRange throatWidth = selectGrand
            ? grandThroatWidth
            : standardThroatWidth;
        MegastationGridRange throatHeight = selectGrand
            ? grandThroatHeight
            : standardThroatHeight;
        float standardWallThickness = MegastationInteriorPresentationPlanner.ComputeWallThickness(
            siteSeed,
            Span(grid, widthAxis, standardThroatWidth),
            Span(grid, upAxis, standardThroatHeight));
        float throatWallThickness = selectGrand
            ? grandWallThickness
            : standardWallThickness;

        int shapeSeed = MegastationSeed.Derive(rootSeed, "cavity shape");
        int removed = 0;
        var protectedCells = new Dictionary<MegacellCoord, MegacellVoidKind>();
        for (int entrance = cavityEntrance.Start; entrance < cavityEntrance.End; entrance++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int ordinal = entrance - cavityEntrance.Start;
            int third = Math.Max(1, cavityEntrance.Count / 3);
            int widthInsetLow = ordinal < third ? 1 : 0;
            int widthInsetHigh = ordinal >= cavityEntrance.Count - third ? 1 : 0;
            int heightInsetLow = ((shapeSeed & 1) == 0 && ordinal >= third && ordinal < 2 * third) ? 1 : 0;
            int heightInsetHigh = ((shapeSeed & 1) != 0 && ordinal >= third && ordinal < 2 * third) ? 1 : 0;
            int widthStart = Math.Min(cavityWidth.Start + widthInsetLow, flightWidth.Start);
            int widthEnd = Math.Max(cavityWidth.End - widthInsetHigh, flightWidth.End);
            int heightStart = Math.Min(cavityHeight.Start + heightInsetLow, flightHeight.Start);
            int heightEnd = Math.Max(cavityHeight.End - heightInsetHigh, flightHeight.End);
            for (int width = widthStart; width < widthEnd; width++)
            for (int height = heightStart; height < heightEnd; height++)
            {
                (int x, int y, int z) = Coordinates(
                    entranceAxis, entrance, widthAxis, width, upAxis, height);
                if (occupancy.ProtectEmpty(x, y, z, MegacellVoidKind.InteriorFlightVolume))
                    removed++;
                protectedCells[new MegacellCoord(x, y, z)] = MegacellVoidKind.InteriorFlightVolume;
            }
        }

        MegastationGridRange throatEntrance = Direction.Sign(portalDirection) > 0
            ? new(cavityEntrance.End, grid.Count(entranceAxis))
            : new(0, cavityEntrance.Start);
        for (int entrance = throatEntrance.Start; entrance < throatEntrance.End; entrance++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int width = throatWidth.Start; width < throatWidth.End; width++)
            for (int height = throatHeight.Start; height < throatHeight.End; height++)
            {
                (int x, int y, int z) = Coordinates(
                    entranceAxis, entrance, widthAxis, width, upAxis, height);
                if (occupancy.ProtectEmpty(x, y, z, MegacellVoidKind.EntranceThroat))
                    removed++;
                protectedCells.TryAdd(new MegacellCoord(x, y, z), MegacellVoidKind.EntranceThroat);
            }
        }

        MegastationInteriorVolume cavityVolume = Volume(
            grid, entranceAxis, cavityEntrance, widthAxis, cavityWidth, upAxis, cavityHeight);
        MegastationInteriorVolume flightVolume = Volume(
            grid, entranceAxis, flightEntrance, widthAxis, flightWidth, upAxis, flightHeight);
        MegastationInteriorVolume throatVolume = Volume(
            grid, entranceAxis, throatEntrance, widthAxis, throatWidth, upAxis, throatHeight);
        Vector3 normal = AxisVector(entranceAxis) * Direction.Sign(portalDirection);
        Vector3 up = AxisVector(upAxis);
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        if (Vector3.Dot(right, AxisVector(widthAxis)) < 0f) right = -right;
        float portalPlane = Direction.Sign(portalDirection) > 0
            ? grid.GetCellMaximum(entranceAxis, grid.CoreRange(entranceAxis).End.Value - 1)
            : grid.GetCellMinimum(entranceAxis, grid.CoreRange(entranceAxis).Start.Value);
        Vector3 portalCentre = AxisVector(entranceAxis) * portalPlane
            + AxisVector(widthAxis) * Centre(grid, widthAxis, throatWidth)
            + AxisVector(upAxis) * Centre(grid, upAxis, throatHeight);
        float structuralThroatWidth = Span(grid, widthAxis, throatWidth);
        float structuralThroatHeight = Span(grid, upAxis, throatHeight);
        float clearWidth = structuralThroatWidth;
        float clearHeight = structuralThroatHeight;
        if (selectGrand)
        {
            clearWidth -= throatWallThickness * 2f;
            clearHeight -= throatWallThickness * 2f;
        }
        Vector2 portalClear = new(clearWidth, clearHeight);
        float throatLength = MathF.Abs(
            Direction.Sign(portalDirection) > 0
                ? portalPlane - cavityVolume.Maximum.Component(entranceAxis)
                : cavityVolume.Minimum.Component(entranceAxis) - portalPlane);

        MegastationEntrancePrecinct entrancePrecinct =
            MegastationInteriorPresentationPlanner.BuildEntrancePrecinct(
                siteSeed,
                portalCentre,
                normal,
                right,
                up,
                throatVolume,
                throatWallThickness,
                occupancy);
        int assemblyRemoved = ProtectEntranceAssembly(
            occupancy,
            entrancePrecinct,
            protectedCells,
            cancellationToken);
        removed += assemblyRemoved;

        stopwatch.Stop();
        var diagnostics = new MegastationInteriorDiagnostics(
            AlgorithmVersion,
            1,
            portalDirection,
            portalClear.X,
            portalClear.Y,
            throatLength,
            flightVolume.Size,
            occupancy.ProtectedVoidCellCount,
            removed,
            0, 0, 0, 0, 0, 0, 0, 0,
            stopwatch.ElapsedMilliseconds,
            0,
            string.Empty,
            EntranceType: entranceType,
            BayClearWidth: bayClearWidth,
            EntranceWidthFraction: portalClear.X / bayClearWidth,
            LargeUprightVerticalClearance: portalClear.Y - LargeEnvelopeHeight,
            LargeRolledVerticalClearance: portalClear.Y - LargeEnvelopeWidth,
            CrownOuterWidth: entrancePrecinct.CrownOuterWidth,
            CrownOuterHeight: entrancePrecinct.CrownOuterHeight,
            EntranceClearanceMargin: entrancePrecinct.ClearanceMargin,
            EntranceAssemblyRemovedCellCount: assemblyRemoved);
        var plan = new MegastationInteriorPlan(
            entranceType == MegastationEntranceType.Standard
                ? $"interior:v{AlgorithmVersion}:{Direction.Id(portalDirection)}"
                : $"interior:v{AlgorithmVersion}:{Direction.Id(portalDirection)}:grand",
            siteSeed,
            portalDirection,
            portalCentre,
            normal,
            right,
            up,
            -up,
            entranceType,
            throatWallThickness,
            portalClear,
            throatVolume,
            flightVolume,
            cavityVolume,
            entrancePrecinct,
            protectedCells
                .OrderBy(pair => pair.Key.X)
                .ThenBy(pair => pair.Key.Y)
                .ThenBy(pair => pair.Key.Z)
                .Select(pair => new MegastationProtectedVoidCell(pair.Key, pair.Value))
                .ToArray(),
            diagnostics);
        return plan with
        {
            Diagnostics = diagnostics with { Signature = MegastationInteriorSignatureBuilder.Compute(plan) },
        };
    }

    private static GridDirection ChoosePortalDirection(SliceGrid grid, int seed)
    {
        return Enum.GetValues<GridDirection>()
            .Select(direction =>
            {
                GridAxis axis = Direction.PrimaryAxis(direction);
                GridAxis[] cross = Enum.GetValues<GridAxis>().Where(other => other != axis).ToArray();
                float depth = CoreSpan(grid, axis);
                float aperture = MathF.Min(CoreSpan(grid, cross[0]), CoreSpan(grid, cross[1]));
                int tie = MegastationSeed.Derive(seed, Direction.Id(direction));
                return (direction, score: depth * aperture, tie: unchecked((uint)tie));
            })
            .OrderByDescending(candidate => candidate.score)
            .ThenBy(candidate => candidate.tie)
            .First().direction;
    }

    private static float CoreSpan(SliceGrid grid, GridAxis axis)
    {
        Range range = grid.CoreRange(axis);
        return grid.GetCellMaximum(axis, range.End.Value - 1)
            - grid.GetCellMinimum(axis, range.Start.Value);
    }

    private static MegastationGridRange CentredRange(
        SliceGrid grid, GridAxis axis, Range allowed, float targetMetres, int shellCells)
    {
        int startLimit = allowed.Start.Value + shellCells;
        int endLimit = allowed.End.Value - shellCells;
        if (endLimit - startLimit < 3)
            throw new InvalidOperationException($"Megastation {axis} core cannot preserve an H1 structural shell.");
        return GrowCentred(grid, axis, new(startLimit, endLimit), targetMetres);
    }

    private static MegastationGridRange CentredSubrange(
        SliceGrid grid, GridAxis axis, MegastationGridRange allowed, float targetMetres)
        => GrowCentred(grid, axis, allowed, targetMetres);

    private static MegastationGridRange GrowCentred(
        SliceGrid grid, GridAxis axis, MegastationGridRange allowed, float targetMetres)
    {
        int centre = (allowed.Start + allowed.End - 1) / 2;
        int start = centre;
        int end = centre + 1;
        while (Span(grid, axis, new(start, end)) < targetMetres
               && (start > allowed.Start || end < allowed.End))
        {
            int lowCount = centre - start;
            int highCount = end - centre - 1;
            if (start > allowed.Start && (end >= allowed.End || lowCount <= highCount)) start--;
            else end++;
        }
        return new(start, end);
    }

    private static float Span(SliceGrid grid, GridAxis axis, MegastationGridRange range)
        => grid.GetCellMaximum(axis, range.End - 1) - grid.GetCellMinimum(axis, range.Start);

    private static float Centre(SliceGrid grid, GridAxis axis, MegastationGridRange range)
        => (grid.GetCellMinimum(axis, range.Start) + grid.GetCellMaximum(axis, range.End - 1)) * .5f;

    private static float Sample(int seed, string semanticIdentity)
        => unchecked((uint)MegastationSeed.Derive(seed, semanticIdentity))
            / (float)uint.MaxValue;

    private static int ProtectEntranceAssembly(
        StructuralOccupancy occupancy,
        MegastationEntrancePrecinct precinct,
        Dictionary<MegacellCoord, MegacellVoidKind> protectedCells,
        CancellationToken cancellationToken)
    {
        SliceGrid grid = occupancy.Grid;
        int removed = 0;
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector3 minimum = new(
                grid.GetCellMinimum(GridAxis.X, x),
                grid.GetCellMinimum(GridAxis.Y, y),
                grid.GetCellMinimum(GridAxis.Z, z));
            Vector3 maximum = new(
                grid.GetCellMaximum(GridAxis.X, x),
                grid.GetCellMaximum(GridAxis.Y, y),
                grid.GetCellMaximum(GridAxis.Z, z));
            if (!precinct.Intersects(minimum, maximum))
                continue;
            if (occupancy.ProtectEmpty(x, y, z, MegacellVoidKind.EntranceThroat))
                removed++;
            protectedCells.TryAdd(
                new MegacellCoord(x, y, z),
                MegacellVoidKind.EntranceThroat);
        }
        return removed;
    }

    private static MegastationInteriorVolume Volume(
        SliceGrid grid,
        GridAxis a, MegastationGridRange ar,
        GridAxis b, MegastationGridRange br,
        GridAxis c, MegastationGridRange cr)
    {
        MegastationGridRange[] ranges = new MegastationGridRange[3];
        ranges[(int)a] = ar;
        ranges[(int)b] = br;
        ranges[(int)c] = cr;
        var min = new Vector3(
            grid.GetCellMinimum(GridAxis.X, ranges[0].Start),
            grid.GetCellMinimum(GridAxis.Y, ranges[1].Start),
            grid.GetCellMinimum(GridAxis.Z, ranges[2].Start));
        var max = new Vector3(
            grid.GetCellMaximum(GridAxis.X, ranges[0].End - 1),
            grid.GetCellMaximum(GridAxis.Y, ranges[1].End - 1),
            grid.GetCellMaximum(GridAxis.Z, ranges[2].End - 1));
        return new(ranges[0], ranges[1], ranges[2], min, max);
    }

    private static (int x, int y, int z) Coordinates(
        GridAxis a, int av, GridAxis b, int bv, GridAxis c, int cv)
    {
        int[] values = new int[3];
        values[(int)a] = av;
        values[(int)b] = bv;
        values[(int)c] = cv;
        return (values[0], values[1], values[2]);
    }

    private static Vector3 AxisVector(GridAxis axis) => axis switch
    {
        GridAxis.X => Vector3.UnitX,
        GridAxis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    private static float Component(this Vector3 value, GridAxis axis) => axis switch
    {
        GridAxis.X => value.X,
        GridAxis.Y => value.Y,
        _ => value.Z,
    };
}

public static class MegastationInteriorPresentationPlanner
{
    private const float EntranceClearanceMargin = 6f;
    private const float ApproachPlateDepth = 2.2f;
    private const float ApproachHousingDepth = 7f;
    private const float ApproachBarrelDepth = 5f;
    private const float ApproachEmitterDepth = .9f;
    private const float ApproachSourceClearance = .15f;

    public static Color ApproachUpColour { get; } = new(62, 186, 255);
    public static Color ApproachDownColour { get; } = new(255, 174, 42);

    public static float ComputeWallThickness(
        int interiorSeed,
        float structuralVoidWidth,
        float structuralVoidHeight)
    {
        var linerRng = new Random(MegastationSeed.Derive(interiorSeed, "throat-liner"));
        return MathHelper.Clamp(
            MathF.Min(structuralVoidWidth, structuralVoidHeight)
                * (.085f + (float)linerRng.NextDouble() * .015f),
            10f,
            16f);
    }

    private static readonly (string Id, Color Main, Color Highlight, Color Accent)[] Palettes =
    [
        ("amber", new Color(255, 166, 38), new Color(255, 224, 142), new Color(122, 83, 34)),
        ("cyan", new Color(55, 218, 255), new Color(180, 246, 255), new Color(40, 101, 118)),
        ("red-orange", new Color(255, 78, 38), new Color(255, 190, 145), new Color(126, 49, 36)),
        ("green", new Color(80, 244, 126), new Color(196, 255, 211), new Color(42, 112, 65)),
        ("violet", new Color(176, 88, 255), new Color(230, 196, 255), new Color(82, 52, 118)),
        ("blue-white", new Color(112, 174, 255), new Color(224, 240, 255), new Color(55, 78, 122)),
        ("magenta", new Color(255, 72, 205), new Color(255, 194, 238), new Color(122, 45, 101)),
    ];

    public static MegastationInteriorPresentationPlan Plan(
        MegastationInteriorPlan interior,
        MegastationSystemMaterialAssignment? materialAssignment = null)
    {
        int portalSeed = MegastationSeed.Derive(interior.Seed, "portal-guidance");
        int throatSeed = MegastationSeed.Derive(interior.Seed, "throat-guidance");
        int landmarkSeed = MegastationSeed.Derive(interior.Seed, "interior-landmarks");
        int linerSeed = MegastationSeed.Derive(interior.Seed, "throat-liner");
        int ribsSeed = MegastationSeed.Derive(interior.Seed, "throat-ribs");
        int markingsSeed = MegastationSeed.Derive(interior.Seed, "throat-markings");
        int fixturesSeed = MegastationSeed.Derive(interior.Seed, "throat-fixtures");
        int approachSeed = MegastationSeed.Derive(
            interior.Seed,
            "approach-guidance-beams:v1");
        var selectedPalette = Palettes[PositiveMod(
            MegastationSeed.Derive(interior.Seed, "entrance-guidance-palette:v1"),
            Palettes.Length)];
        Color dominant = materialAssignment?.Palette.DominantTint ?? new Color(92, 96, 101);
        Color secondary = materialAssignment?.Palette.SecondaryTint ?? new Color(108, 111, 114);
        var palette = new MegastationEntrancePalette(
            selectedPalette.Id,
            selectedPalette.Main,
            selectedPalette.Highlight,
            selectedPalette.Accent,
            ReadableStructure(dominant, 76),
            ReadableStructure(secondary, 88),
            ReadableStructure(Color.Lerp(dominant, secondary, .35f), 96));
        MegastationEntrancePrecinct precinct = interior.EntrancePrecinct;
        var elements = new List<MegastationInteriorGuidanceElement>();
        var markers = new List<MegastationInteriorGuidanceMarker>();
        var approachBeams = new List<MegastationApproachGuidanceBeam>(4);
        float halfWidth = interior.PortalClearSize.X * .5f;
        float halfHeight = interior.PortalClearSize.Y * .5f;
        float strip = MathHelper.Clamp(MathF.Min(halfWidth, halfHeight) * .075f, 3.5f, 6f);
        float slabDepth = MathHelper.Clamp(strip * .55f, 2f, 3.5f);
        Color landmarkColour = new(78, 188, 236);
        Matrix Frame(Vector3 centre) => CreateFrame(
            interior.PortalRight, interior.PortalUp, interior.OutwardNormal, centre);

        AddConstructedThroat(interior, precinct, palette,
            linerSeed, ribsSeed, markingsSeed, fixturesSeed, approachSeed,
            elements, markers, approachBeams);

        Vector3 cavityCentre = (interior.CavityEnvelope.Minimum + interior.CavityEnvelope.Maximum) * .5f;
        float cavityDepth = MathF.Abs(Vector3.Dot(
            interior.CavityEnvelope.Size,
            interior.OutwardNormal));
        Vector3 farWall = cavityCentre - interior.OutwardNormal * (cavityDepth * .5f - 1.2f);
        float landmarkWidth = interior.MainFlightVolume.Size.ComponentAlong(interior.PortalRight) * .62f;
        float landmarkHeight = interior.MainFlightVolume.Size.ComponentAlong(interior.PortalUp) * .46f;
        for (int vertical = -1; vertical <= 1; vertical += 2)
        {
            Vector3 centre = farWall + interior.PortalUp * vertical * landmarkHeight * .42f;
            elements.Add(new(
                $"interior/far-wall/horizontal:{vertical}",
                MegastationInteriorGuidanceKind.InteriorLandmark,
                Frame(centre),
                new(landmarkWidth, strip * .8f, slabDepth),
                landmarkColour,
                .66f,
                SystemMaterialFamilyId.CleanTechnicalAlloy,
                false));
            markers.Add(new(
                $"interior/far-wall/marker:{vertical}",
                MegastationInteriorGuidanceKind.InteriorLandmark,
                centre + interior.OutwardNormal * slabDepth,
                landmarkColour,
                .58f));
        }
        elements.Add(new(
            "interior/far-wall/vertical",
            MegastationInteriorGuidanceKind.InteriorLandmark,
            Frame(farWall + interior.PortalRight * landmarkWidth * .28f),
            new(strip * .8f, landmarkHeight, slabDepth),
            landmarkColour,
            .66f,
            SystemMaterialFamilyId.CleanTechnicalAlloy,
            false));
        markers.Add(new(
            "interior/far-wall/marker:centre",
            MegastationInteriorGuidanceKind.InteriorLandmark,
            farWall + interior.OutwardNormal * slabDepth,
            landmarkColour,
            .62f));

        return new(portalSeed, throatSeed, landmarkSeed,
            linerSeed, ribsSeed, markingsSeed, fixturesSeed, approachSeed,
            palette, precinct, elements, markers, approachBeams);
    }

    public static MegastationEntrancePrecinct BuildEntrancePrecinct(
        int interiorSeed,
        Vector3 portalCentre,
        Vector3 outwardNormal,
        Vector3 portalRight,
        Vector3 portalUp,
        MegastationInteriorVolume throatVolume,
        float wallThickness,
        StructuralOccupancy? occupancy)
    {
        const float approachLength = 90f;
        const float minimumProjection = 55f;
        float structuralVoidWidth = throatVolume.Size.ComponentAlong(portalRight);
        float structuralVoidHeight = throatVolume.Size.ComponentAlong(portalUp);
        float clearWidth = structuralVoidWidth - wallThickness * 2f;
        float clearHeight = structuralVoidHeight - wallThickness * 2f;
        float crownMember = MathHelper.Clamp(
            MathF.Min(clearWidth, clearHeight) * .17f,
            20f,
            30f);
        float crownDepth = MathHelper.Clamp(crownMember * 1.35f, 28f, 42f);
        float crownOuterWidth = structuralVoidWidth + crownMember * 2f;
        float crownOuterHeight = structuralVoidHeight + crownMember * 2f;
        float protectedHalfWidth = crownOuterWidth * .5f + EntranceClearanceMargin;
        float protectedHalfHeight = crownOuterHeight * .5f + EntranceClearanceMargin;
        float portalProjection = Vector3.Dot(portalCentre, outwardNormal);
        float obstructionProjection = portalProjection;

        if (occupancy != null)
        {
            SliceGrid grid = occupancy.Grid;
            for (int x = 0; x < grid.XCount; x++)
            for (int y = 0; y < grid.YCount; y++)
            for (int z = 0; z < grid.ZCount; z++)
            {
                if (!occupancy.IsOccupied(x, y, z)) continue;
                Vector3 minimum = new(
                    grid.GetCellMinimum(GridAxis.X, x),
                    grid.GetCellMinimum(GridAxis.Y, y),
                    grid.GetCellMinimum(GridAxis.Z, z));
                Vector3 maximum = new(
                    grid.GetCellMaximum(GridAxis.X, x),
                    grid.GetCellMaximum(GridAxis.Y, y),
                    grid.GetCellMaximum(GridAxis.Z, z));
                Vector3 centre = (minimum + maximum) * .5f;
                Vector3 half = (maximum - minimum) * .5f;
                float lateralRight = MathF.Abs(Vector3.Dot(
                    centre - portalCentre, portalRight));
                float lateralUp = MathF.Abs(Vector3.Dot(
                    centre - portalCentre, portalUp));
                float cellRight = half.ComponentAlong(portalRight);
                float cellUp = half.ComponentAlong(portalUp);
                if (lateralRight - cellRight > protectedHalfWidth
                    || lateralUp - cellUp > protectedHalfHeight)
                    continue;
                float outwardExtent = Vector3.Dot(centre, outwardNormal)
                    + half.ComponentAlong(outwardNormal);
                if (outwardExtent >= portalProjection)
                    obstructionProjection = MathF.Max(obstructionProjection, outwardExtent);
            }
        }

        float skylineHeight = MathF.Max(0f, obstructionProjection - portalProjection);
        float projectionHeightFraction = MathHelper.Lerp(.25f, .75f,
            Sample(interiorSeed, "entrance-projection-height:v1"));
        float projectionLength = MathF.Max(
            minimumProjection,
            skylineHeight * projectionHeightFraction);
        Vector3 mouth = portalCentre + outwardNormal * projectionLength;
        float corridorStart = -24f;
        // Clear through every local obstruction in front of the crown, even when
        // the accepted partial skyline embedding leaves the mouth below that peak.
        // This keeps the approach fixtures and the first visible beam segment out
        // of surviving structural mass without changing entrance elevation.
        float corridorEnd = MathF.Max(
            projectionLength + approachLength,
            skylineHeight + EntranceClearanceMargin);
        float corridorLength = corridorEnd - corridorStart;
        Vector3 corridorCentre = portalCentre
            + outwardNormal * ((corridorStart + corridorEnd) * .5f);
        Vector3 boundsHalf = Abs(portalRight) * protectedHalfWidth
            + Abs(portalUp) * protectedHalfHeight
            + Abs(outwardNormal) * (corridorLength * .5f);

        // The crown is the lateral authority. Axially, include the complete crown and
        // its four fixed approach fixtures, plus the same restrained safety margin.
        const float approachFixtureDepth = ApproachPlateDepth + ApproachHousingDepth
            + ApproachBarrelDepth + ApproachEmitterDepth + ApproachSourceClearance;
        float assemblyStart = projectionLength - crownDepth * .15f - EntranceClearanceMargin;
        float assemblyEnd = projectionLength + crownDepth * .85f
            + approachFixtureDepth + EntranceClearanceMargin;
        float assemblyLength = assemblyEnd - assemblyStart;
        Vector3 assemblyCentre = portalCentre
            + outwardNormal * ((assemblyStart + assemblyEnd) * .5f);
        Vector3 assemblyHalf = Abs(portalRight) * protectedHalfWidth
            + Abs(portalUp) * protectedHalfHeight
            + Abs(outwardNormal) * (assemblyLength * .5f);
        return new(
            corridorCentre - boundsHalf,
            corridorCentre + boundsHalf,
            assemblyCentre - assemblyHalf,
            assemblyCentre + assemblyHalf,
            mouth,
            crownOuterWidth,
            crownOuterHeight,
            EntranceClearanceMargin,
            obstructionProjection,
            projectionLength,
            skylineHeight,
            projectionHeightFraction);
    }

    private static int PositiveMod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static float Sample(int seed, string semanticIdentity)
        => unchecked((uint)MegastationSeed.Derive(seed, semanticIdentity))
            / (float)uint.MaxValue;

    private static Vector3 Abs(Vector3 value)
        => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));

    private static Color ReadableStructure(Color colour, byte minimumLuminance)
    {
        byte current = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        if (current >= minimumLuminance) return colour;
        float amount = (minimumLuminance - current) / (255f - current);
        return Color.Lerp(colour, Color.White, amount);
    }

    private static void AddConstructedThroat(
        MegastationInteriorPlan interior,
        MegastationEntrancePrecinct precinct,
        MegastationEntrancePalette palette,
        int linerSeed,
        int ribsSeed,
        int markingsSeed,
        int fixturesSeed,
        int approachSeed,
        List<MegastationInteriorGuidanceElement> elements,
        List<MegastationInteriorGuidanceMarker> markers,
        List<MegastationApproachGuidanceBeam> approachBeams)
    {
        var ribRng = new Random(ribsSeed);
        var fixtureRng = new Random(fixturesSeed);
        Vector3 right = interior.PortalRight;
        Vector3 up = interior.PortalUp;
        Vector3 outward = interior.OutwardNormal;
        float structuralVoidWidth = interior.ThroatVolume.Size.ComponentAlong(right);
        float structuralVoidHeight = interior.ThroatVolume.Size.ComponentAlong(up);
        float internalLength = interior.Diagnostics.ThroatLength;
        float length = internalLength + precinct.ProjectionLength;
        float wallThickness = interior.ThroatWallThickness;
        float width = structuralVoidWidth - wallThickness * 2f;
        float height = structuralVoidHeight - wallThickness * 2f;
        Debug.Assert(width > 0f && height > 0f);
        float halfWidth = width * .5f;
        float halfHeight = height * .5f;
        float outerWidth = width + wallThickness * 2f;
        float outerHeight = height + wallThickness * 2f;
        Vector3 innerEnd = interior.PortalCentre - outward * internalLength;
        Vector3 outerEnd = precinct.OuterMouthCentre;
        _ = markingsSeed;

        // The accepted light rhythm remains, but each station is a shallow recess in
        // the four continuous tube walls rather than a frame defining the tunnel.
        float nominalSpacing = 46f + (float)ribRng.NextDouble() * 10f;
        int fixtureCount = Math.Max(3, (int)MathF.Floor(length / nominalSpacing));
        float fixtureWidth = MathHelper.Clamp(
            3.6f + (float)fixtureRng.NextDouble() * 1.4f,
            3.6f,
            5f);
        float recessDepth = MathHelper.Clamp(wallThickness * .16f, 1.6f, 2.4f);
        float backingThickness = MathHelper.Clamp(wallThickness * .07f, .7f, 1.1f);
        float remainingWallThickness = wallThickness - recessDepth - backingThickness;
        Debug.Assert(remainingWallThickness > 0f);
        float wellWallThickness = MathHelper.Clamp(recessDepth * .22f, .35f, .55f);
        float cornerClearance = MathHelper.Clamp(
            MathF.Min(width, height) * .075f,
            6f,
            10f);
        var recesses = new List<(float Start, float End, int Index)>();
        for (int fixture = 1; fixture <= fixtureCount; fixture++)
        {
            float centre = length * fixture / (fixtureCount + 1f);
            recesses.Add((centre - fixtureWidth * .5f, centre + fixtureWidth * .5f, fixture));
        }

        void Add(
            string identity,
            MegastationInteriorGuidanceKind kind,
            Matrix frame,
            Vector3 size,
            Color colour,
            SystemMaterialFamilyId family,
            bool castsShadow,
            float illumination = 0f)
            => elements.Add(new(identity, kind, frame, size, colour, illumination, family, castsShadow));

        // Split each wall only where a closed recess replaces its inner face. The
        // structural wall and luminous recess backs together cover the entire axial
        // length; neither end receives a closing face across the flight opening.
        float segmentStart = 0f;
        int segmentIndex = 0;
        foreach ((float recessStart, float recessEnd, _) in recesses)
        {
            AddTubeWallSegment(segmentStart, recessStart, segmentIndex++);
            segmentStart = recessEnd;
        }
        AddTubeWallSegment(segmentStart, length, segmentIndex);

        foreach ((float recessStart, float recessEnd, int fixtureIndex) in recesses)
        {
            float axial = (recessStart + recessEnd) * .5f;
            Vector3 station = innerEnd + outward * axial;
            float sideSpan = height - cornerClearance * 2f;
            float horizontalSpan = width - cornerClearance * 2f;
            AddVerticalWell(fixtureIndex, "left", station, -1, sideSpan);
            AddVerticalWell(fixtureIndex, "right", station, 1, sideSpan);
            AddHorizontalWell(fixtureIndex, "ceiling", station, 1, horizontalSpan);
            AddHorizontalWell(fixtureIndex, "floor", station, -1, horizontalSpan);

            // Structural closures retain opaque tube coverage around each deliberately
            // finite fixture. The luminous pieces stop before the corners, so adjacent
            // wall fixtures never share or overlap a plane.
            foreach (int side in new[] { -1, 1 })
            foreach (int vertical in new[] { -1, 1 })
            {
                Add($"throat/recess:{fixtureIndex}/side-cap:{side}:{vertical}",
                    MegastationInteriorGuidanceKind.ThroatLiner,
                    CreateFrame(right, up, outward,
                        station
                        + right * side * (halfWidth + wallThickness * .5f)
                        + up * vertical * (halfHeight - cornerClearance * .5f)),
                    new(wallThickness, cornerClearance, fixtureWidth), palette.InnerStructure,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, true);
                Add($"throat/recess:{fixtureIndex}/horizontal-cap:{side}:{vertical}",
                    MegastationInteriorGuidanceKind.ThroatLiner,
                    CreateFrame(right, up, outward,
                        station
                        + right * side * (halfWidth + wallThickness * .5f - cornerClearance * .5f)
                        + up * vertical * (halfHeight + wallThickness * .5f)),
                    new(wallThickness + cornerClearance, wallThickness, fixtureWidth),
                    palette.OuterStructure,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            }
        }

        AddCrown();
        return;

        void AddVerticalWell(
            int fixtureIndex,
            string sideName,
            Vector3 station,
            int side,
            float transverseSpan)
        {
            Vector3 radial = right * side;
            Vector3 opening = station + radial * halfWidth;
            Color outerBounce = Color.Lerp(palette.InnerStructure, palette.Guidance, .24f);
            Color deepBounce = Color.Lerp(palette.InnerStructure, palette.Guidance, .48f);
            float outerWellDepth = recessDepth * .38f;
            float deepWellDepth = recessDepth - outerWellDepth;
            Add($"throat/fixture:{fixtureIndex}/{sideName}",
                MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward,
                    opening + radial * (recessDepth + backingThickness * .5f)),
                new(backingThickness, transverseSpan, fixtureWidth), palette.Guidance,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);
            Add($"throat/recess:{fixtureIndex}/{sideName}/seal",
                MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward,
                    opening + radial * (recessDepth + backingThickness
                        + remainingWallThickness * .5f)),
                new(remainingWallThickness, transverseSpan, fixtureWidth),
                palette.OuterStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            foreach (int axialSide in new[] { -1, 1 })
            {
                Vector3 edge = outward * axialSide
                    * (fixtureWidth - wellWallThickness) * .5f;
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/axial:{axialSide}/outer",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth * .5f) + edge),
                    new(outerWellDepth, transverseSpan, wellWallThickness), outerBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .14f);
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/axial:{axialSide}/deep",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth + deepWellDepth * .5f) + edge),
                    new(deepWellDepth, transverseSpan, wellWallThickness), deepBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .32f);
            }
            foreach (int transverseSide in new[] { -1, 1 })
            {
                Vector3 edge = up * transverseSide
                    * (transverseSpan - wellWallThickness) * .5f;
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/transverse:{transverseSide}/outer",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth * .5f) + edge),
                    new(outerWellDepth, wellWallThickness,
                        fixtureWidth - wellWallThickness * 2f), outerBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .14f);
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/transverse:{transverseSide}/deep",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth + deepWellDepth * .5f) + edge),
                    new(deepWellDepth, wellWallThickness,
                        fixtureWidth - wellWallThickness * 2f), deepBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .32f);
            }
            AddWellHalos(fixtureIndex, sideName, opening, up, transverseSpan, -radial);
        }

        void AddHorizontalWell(
            int fixtureIndex,
            string sideName,
            Vector3 station,
            int side,
            float transverseSpan)
        {
            Vector3 radial = up * side;
            Vector3 opening = station + radial * halfHeight;
            Color outerBounce = Color.Lerp(palette.InnerStructure, palette.Guidance, .24f);
            Color deepBounce = Color.Lerp(palette.InnerStructure, palette.Guidance, .48f);
            float outerWellDepth = recessDepth * .38f;
            float deepWellDepth = recessDepth - outerWellDepth;
            Add($"throat/fixture:{fixtureIndex}/{sideName}",
                MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward,
                    opening + radial * (recessDepth + backingThickness * .5f)),
                new(transverseSpan, backingThickness, fixtureWidth), palette.Guidance,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);
            Add($"throat/recess:{fixtureIndex}/{sideName}/seal",
                MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward,
                    opening + radial * (recessDepth + backingThickness
                        + remainingWallThickness * .5f)),
                new(transverseSpan, remainingWallThickness, fixtureWidth),
                palette.OuterStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            foreach (int axialSide in new[] { -1, 1 })
            {
                Vector3 edge = outward * axialSide
                    * (fixtureWidth - wellWallThickness) * .5f;
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/axial:{axialSide}/outer",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth * .5f) + edge),
                    new(transverseSpan, outerWellDepth, wellWallThickness), outerBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .14f);
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/axial:{axialSide}/deep",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth + deepWellDepth * .5f) + edge),
                    new(transverseSpan, deepWellDepth, wellWallThickness), deepBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .32f);
            }
            foreach (int transverseSide in new[] { -1, 1 })
            {
                Vector3 edge = right * transverseSide
                    * (transverseSpan - wellWallThickness) * .5f;
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/transverse:{transverseSide}/outer",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth * .5f) + edge),
                    new(wellWallThickness, outerWellDepth,
                        fixtureWidth - wellWallThickness * 2f), outerBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .14f);
                Add($"throat/recess:{fixtureIndex}/{sideName}/well/transverse:{transverseSide}/deep",
                    MegastationInteriorGuidanceKind.ThroatMarking,
                    CreateFrame(right, up, outward,
                        opening + radial * (outerWellDepth + deepWellDepth * .5f) + edge),
                    new(wellWallThickness, deepWellDepth,
                        fixtureWidth - wellWallThickness * 2f), deepBounce,
                    SystemMaterialFamilyId.HeavyIndustrialPlate, false, .32f);
            }
            AddWellHalos(fixtureIndex, sideName, opening, right, transverseSpan, -radial);
        }

        void AddWellHalos(
            int fixtureIndex,
            string sideName,
            Vector3 opening,
            Vector3 transverseAxis,
            float transverseSpan,
            Vector3 inwardNormal)
        {
            int sampleCount = transverseSpan >= 80f ? 3 : transverseSpan >= 40f ? 2 : 1;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float unit = sampleCount == 1 ? 0f : sample / (sampleCount - 1f) - .5f;
                Vector3 position = opening
                    + transverseAxis * (unit * transverseSpan * .68f)
                    + inwardNormal * .15f;
                markers.Add(new(
                    $"throat/recess:{fixtureIndex}/{sideName}/halo:{sample}",
                    MegastationInteriorGuidanceKind.ThroatBand,
                    position,
                    palette.Guidance,
                    1f,
                    inwardNormal,
                    90f,
                    220f,
                    1_500f));
            }
        }

        void AddTubeWallSegment(float start, float end, int index)
        {
            float segmentLength = end - start;
            if (segmentLength <= .01f) return;
            Vector3 centre = innerEnd + outward * ((start + end) * .5f);
            Add($"throat/tube/segment:{index}/left", MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward, centre - right * (halfWidth + wallThickness * .5f)),
                new(wallThickness, height, segmentLength), palette.InnerStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add($"throat/tube/segment:{index}/right", MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward, centre + right * (halfWidth + wallThickness * .5f)),
                new(wallThickness, height, segmentLength), palette.InnerStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add($"throat/tube/segment:{index}/ceiling", MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward, centre + up * (halfHeight + wallThickness * .5f)),
                new(outerWidth, wallThickness, segmentLength), palette.OuterStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add($"throat/tube/segment:{index}/floor", MegastationInteriorGuidanceKind.ThroatLiner,
                CreateFrame(right, up, outward, centre - up * (halfHeight + wallThickness * .5f)),
                new(outerWidth, wallThickness, segmentLength), palette.OuterStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
        }

        void AddCrown()
        {
            float member = (precinct.CrownOuterWidth - outerWidth) * .5f;
            float depth = MathHelper.Clamp(member * 1.35f, 28f, 42f);
            float crownOuterWidth = precinct.CrownOuterWidth;
            float crownOuterHeight = precinct.CrownOuterHeight;
            Vector3 centre = outerEnd + outward * (depth * .35f);
            Add("entrance/crown/left", MegastationInteriorGuidanceKind.ThroatTransition,
                CreateFrame(right, up, outward, centre - right * (outerWidth * .5f + member * .5f)),
                new(member, crownOuterHeight, depth), palette.CrownStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add("entrance/crown/right", MegastationInteriorGuidanceKind.ThroatTransition,
                CreateFrame(right, up, outward, centre + right * (outerWidth * .5f + member * .5f)),
                new(member, crownOuterHeight, depth), palette.CrownStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add("entrance/crown/top", MegastationInteriorGuidanceKind.ThroatTransition,
                CreateFrame(right, up, outward, centre + up * (outerHeight * .5f + member * .5f)),
                new(outerWidth, member, depth), palette.CrownStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);
            Add("entrance/crown/bottom", MegastationInteriorGuidanceKind.ThroatTransition,
                CreateFrame(right, up, outward, centre - up * (outerHeight * .5f + member * .5f)),
                new(outerWidth, member, depth), palette.CrownStructure,
                SystemMaterialFamilyId.HeavyIndustrialPlate, true);

            // One shallow, deliberately recessed luminous inner edge. These four pieces
            // stop before the corners and cannot overlap one another or the crown face.
            float lightDepth = 1f;
            float lightWidth = MathHelper.Clamp(member * .18f, 4f, 6f);
            float lightAxial = depth * .51f;
            float sideSpan = outerHeight - lightWidth * 2f;
            float topSpan = outerWidth - lightWidth * 2f;
            Vector3 lightCentre = centre + outward * lightAxial;
            Add("entrance/crown/guidance/left", MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward, lightCentre - right * (outerWidth * .5f + lightDepth * .5f)),
                new(lightDepth, sideSpan, 1.2f), palette.Highlight,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);
            Add("entrance/crown/guidance/right", MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward, lightCentre + right * (outerWidth * .5f + lightDepth * .5f)),
                new(lightDepth, sideSpan, 1.2f), palette.Highlight,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);
            Add("entrance/crown/guidance/top", MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward, lightCentre + up * (outerHeight * .5f + lightDepth * .5f)),
                new(topSpan, lightDepth, 1.2f), palette.Highlight,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);
            Add("entrance/crown/guidance/bottom", MegastationInteriorGuidanceKind.ThroatBand,
                CreateFrame(right, up, outward, lightCentre - up * (outerHeight * .5f + lightDepth * .5f)),
                new(topSpan, lightDepth, 1.2f), palette.Highlight,
                SystemMaterialFamilyId.CleanTechnicalAlloy, false, .94f);

            AddApproachGuidanceFixtures();

            void AddApproachGuidanceFixtures()
            {
                float beamLength = MathHelper.Lerp(
                    1_400f,
                    1_600f,
                    Sample(approachSeed, "length"));
                float halfAngle = MathHelper.Lerp(
                    .7f,
                    1.2f,
                    Sample(approachSeed, "half-angle"));
                float plateSpan = MathHelper.Clamp(member * .52f, 11f, 15f);
                float plateDepth = ApproachPlateDepth;
                float housingSpan = plateSpan * .68f;
                float housingDepth = ApproachHousingDepth;
                float barrelSpan = housingSpan * .55f;
                float barrelDepth = ApproachBarrelDepth;
                float emitterDepth = ApproachEmitterDepth;
                Vector3 crownFront = centre + outward * (depth * .5f);
                float cornerRight = outerWidth * .5f + member * .5f;
                float cornerUp = outerHeight * .5f + member * .5f;

                foreach (int horizontal in new[] { -1, 1 })
                foreach (int vertical in new[] { -1, 1 })
                {
                    string corner = $"{horizontal}:{vertical}";
                    Color beamColour = vertical > 0
                        ? ApproachUpColour
                        : ApproachDownColour;
                    Vector3 mountingPoint = crownFront
                        + right * horizontal * cornerRight
                        + up * vertical * cornerUp;
                    Vector3 plateCentre = mountingPoint + outward * (plateDepth * .5f);
                    Vector3 housingCentre = mountingPoint
                        + outward * (plateDepth + housingDepth * .5f);
                    Vector3 barrelCentre = mountingPoint
                        + outward * (plateDepth + housingDepth + barrelDepth * .5f);
                    Vector3 emitterCentre = mountingPoint
                        + outward * (plateDepth + housingDepth + barrelDepth
                            + emitterDepth * .5f);
                    Vector3 source = emitterCentre
                        + outward * (emitterDepth * .5f + ApproachSourceClearance);

                    Add($"entrance/approach/fixture:{corner}/mount",
                        MegastationInteriorGuidanceKind.ApproachFixture,
                        CreateFrame(right, up, outward, plateCentre),
                        new(plateSpan, plateSpan, plateDepth), palette.CrownStructure,
                        SystemMaterialFamilyId.HeavyIndustrialPlate, true);
                    Add($"entrance/approach/fixture:{corner}/housing",
                        MegastationInteriorGuidanceKind.ApproachFixture,
                        CreateFrame(right, up, outward, housingCentre),
                        new(housingSpan, housingSpan, housingDepth), palette.StructuralAccent,
                        SystemMaterialFamilyId.CleanTechnicalAlloy, true);
                    Add($"entrance/approach/fixture:{corner}/barrel",
                        MegastationInteriorGuidanceKind.ApproachFixture,
                        CreateFrame(right, up, outward, barrelCentre),
                        new(barrelSpan, barrelSpan, barrelDepth), palette.OuterStructure,
                        SystemMaterialFamilyId.HeavyIndustrialPlate, false);
                    Add($"entrance/approach/fixture:{corner}/emitter",
                        MegastationInteriorGuidanceKind.ApproachFixture,
                        CreateFrame(right, up, outward, emitterCentre),
                        new(barrelSpan * .86f, barrelSpan * .86f, emitterDepth), beamColour,
                        SystemMaterialFamilyId.CleanTechnicalAlloy, false, .98f);

                    approachBeams.Add(new(
                        $"entrance/approach/beam:{corner}",
                        vertical > 0
                            ? MegastationApproachBeamVertical.Upper
                            : MegastationApproachBeamVertical.Lower,
                        horizontal,
                        source,
                        outward,
                        right,
                        up,
                        beamColour,
                        beamLength,
                        halfAngle));
                    markers.Add(new(
                        $"entrance/approach/source:{corner}",
                        MegastationInteriorGuidanceKind.ApproachFixture,
                        source,
                        beamColour,
                        .82f,
                        outward,
                        24f,
                        500f,
                        3_000f));
                }
            }
        }
    }

    private static Matrix CreateFrame(Vector3 x, Vector3 y, Vector3 z, Vector3 centre)
    {
        Vector3 handedZ = Vector3.Normalize(Vector3.Cross(x, y));
        Debug.Assert(MathF.Abs(Vector3.Dot(handedZ, z)) > .999f);
        return new(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            handedZ.X, handedZ.Y, handedZ.Z, 0f,
            centre.X, centre.Y, centre.Z, 1f);
    }

    private static float ComponentAlong(this Vector3 size, Vector3 axis)
        => MathF.Abs(size.X * axis.X) + MathF.Abs(size.Y * axis.Y) + MathF.Abs(size.Z * axis.Z);
}

public static class MegastationApproachBeamMeshBuilder
{
    private const int RadialFinCount = 6;

    private static readonly float[] LongitudinalFractions =
        [0f, .08f, .24f, .48f, .72f, 1f];

    private static readonly float[] CentreAlpha =
        [.09f, .08f, .064f, .043f, .021f, 0f];

    public static VertexPositionColor[] Build(
        MegastationInteriorPresentationPlan presentation)
    {
        var vertices = new List<VertexPositionColor>(
            presentation.ApproachBeams.Count
            * RadialFinCount
            * (LongitudinalFractions.Length - 1)
            * 12);
        foreach (MegastationApproachGuidanceBeam beam in presentation.ApproachBeams)
            EmitBeam(beam, vertices);
        return vertices.ToArray();
    }

    public static int VertexCount(MegastationInteriorPresentationPlan presentation)
        => presentation.ApproachBeams.Count
            * RadialFinCount
            * (LongitudinalFractions.Length - 1)
            * 12;

    private static void EmitBeam(
        MegastationApproachGuidanceBeam beam,
        List<VertexPositionColor> vertices)
    {
        Vector3 axis = Vector3.Normalize(beam.Axis);
        Vector3 radialRight = Vector3.Normalize(beam.RadialRight);
        Vector3 radialUp = Vector3.Normalize(beam.RadialUp);
        float tangent = MathF.Tan(MathHelper.ToRadians(beam.HalfAngleDegrees));
        const float sourceRadius = 1.25f;

        for (int fin = 0; fin < RadialFinCount; fin++)
        {
            float angle = MathF.PI * fin / RadialFinCount;
            Vector3 radial = Vector3.Normalize(
                radialRight * MathF.Cos(angle) + radialUp * MathF.Sin(angle));
            for (int segment = 0; segment < LongitudinalFractions.Length - 1; segment++)
            {
                CrossSection a = Section(segment);
                CrossSection b = Section(segment + 1);
                AddTriangle(vertices, a.Left, a.Centre, b.Centre,
                    a.EdgeColour, a.CentreColour, b.CentreColour);
                AddTriangle(vertices, a.Left, b.Centre, b.Left,
                    a.EdgeColour, b.CentreColour, b.EdgeColour);
                AddTriangle(vertices, a.Centre, a.Right, b.Right,
                    a.CentreColour, a.EdgeColour, b.EdgeColour);
                AddTriangle(vertices, a.Centre, b.Right, b.Centre,
                    a.CentreColour, b.EdgeColour, b.CentreColour);
            }

            CrossSection Section(int index)
            {
                float distance = beam.Length * LongitudinalFractions[index];
                float radius = sourceRadius + distance * tangent;
                Vector3 centre = beam.Source + axis * distance;
                return new(
                    centre - radial * radius,
                    centre,
                    centre + radial * radius,
                    WithAlpha(beam.Colour, 0f),
                    WithAlpha(beam.Colour, CentreAlpha[index]));
            }
        }
    }

    private static void AddTriangle(
        List<VertexPositionColor> vertices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color colourA,
        Color colourB,
        Color colourC)
    {
        vertices.Add(new(a, colourA));
        vertices.Add(new(b, colourB));
        vertices.Add(new(c, colourC));
    }

    private static Color WithAlpha(Color colour, float alpha)
        => new(colour.R, colour.G, colour.B,
            (byte)MathF.Round(255f * MathHelper.Clamp(alpha, 0f, 1f)));

    private readonly record struct CrossSection(
        Vector3 Left,
        Vector3 Centre,
        Vector3 Right,
        Color EdgeColour,
        Color CentreColour);
}

public static class MegastationInteriorMeshBuilder
{
    public static MegastationInteriorMeshBuildResult Build(
        MegastationInteriorPlan plan,
        MegastationSystemMaterialAssignment? materials,
        MegastationInteriorPresentationPlan? presentation = null,
        CancellationToken cancellationToken = default)
    {
        presentation ??= MegastationInteriorPresentationPlanner.Plan(plan);
        var stopwatch = Stopwatch.StartNew();
        var mesh = new StationModuleMesh();
        Color dominant = materials?.Palette.DominantTint ?? new Color(74, 78, 82);
        Color secondary = materials?.Palette.SecondaryTint ?? new Color(96, 101, 106);
        Color accent = materials?.Palette.AccentTint ?? new Color(132, 144, 150);
        float width = plan.PortalClearSize.X;
        float height = plan.PortalClearSize.Y;
        float bar = MathHelper.Clamp(MathF.Min(width, height) * .12f, 10f, 18f);
        float depth = MathHelper.Clamp(bar * 1.4f, 16f, 26f);
        Vector3 boxDepthAxis = Vector3.Normalize(Vector3.Cross(
            plan.PortalRight,
            plan.PortalUp));
        Debug.Assert(MathF.Abs(Vector3.Dot(boxDepthAxis, plan.OutwardNormal)) > .999f);
        Matrix Frame(Vector3 centre) => new(
            plan.PortalRight.X, plan.PortalRight.Y, plan.PortalRight.Z, 0f,
            plan.PortalUp.X, plan.PortalUp.Y, plan.PortalUp.Z, 0f,
            boxDepthAxis.X, boxDepthAxis.Y, boxDepthAxis.Z, 0f,
            centre.X, centre.Y, centre.Z, 1f);

        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMajor;
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        Vector3 frameCentre = plan.PortalCentre + plan.OutwardNormal * (depth * .15f);
        AddBox(mesh, Frame(frameCentre + plan.PortalRight * (width + bar) * .5f),
            new(bar, height + bar * 2f, depth), dominant);
        AddBox(mesh, Frame(frameCentre - plan.PortalRight * (width + bar) * .5f),
            new(bar, height + bar * 2f, depth), dominant);
        AddBox(mesh, Frame(frameCentre + plan.PortalUp * (height + bar) * .5f),
            new(width, bar, depth), secondary);
        AddBox(mesh, Frame(frameCentre - plan.PortalUp * (height + bar) * .5f),
            new(width, bar, depth), secondary);

        float buttressWidth = bar * 1.6f;
        float buttressHeight = height * .42f;
        for (int side = -1; side <= 1; side += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector3 baseCentre = frameCentre
                + plan.PortalRight * side * (width * .5f + bar * 1.35f)
                - plan.PortalUp * (height * .5f - buttressHeight * .5f)
                - plan.OutwardNormal * depth * .25f;
            AddBox(mesh, Frame(baseCentre), new(buttressWidth, buttressHeight, depth * 1.45f), dominant);
        }

        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        float innerDepth = depth * .55f;
        Vector3 innerCentre = plan.PortalCentre - plan.OutwardNormal * (depth * .55f);
        AddBox(mesh, Frame(innerCentre + plan.PortalRight * width * .42f),
            new(bar * .32f, height, innerDepth), accent);
        AddBox(mesh, Frame(innerCentre - plan.PortalRight * width * .42f),
            new(bar * .32f, height, innerDepth), accent);

        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        int illuminatedFaceStart = mesh.FaceCount;
        float marker = MathHelper.Clamp(bar * .16f, 1.8f, 3.5f);
        for (int side = -1; side <= 1; side += 2)
        for (int row = -2; row <= 2; row++)
        {
            Vector3 markerCentre = plan.PortalCentre
                + plan.PortalRight * side * (width * .5f + bar * .12f)
                + plan.PortalUp * row * (height * .18f)
                + plan.OutwardNormal * (depth * .55f);
            AddBox(mesh, Frame(markerCentre), new(marker, marker * 2.2f, marker), Color.Lerp(accent, Color.White, .35f));
        }
        int illuminatedFaceCount = mesh.FaceCount - illuminatedFaceStart;
        var illuminationRanges = new List<(int Start, int Count, float Illumination)>
        {
            (illuminatedFaceStart, illuminatedFaceCount, .82f),
        };

        foreach (MegastationInteriorGuidanceElement element in presentation.Elements
                     .OrderBy(element => element.MaterialFamily)
                     .ThenByDescending(element => element.CastsShadow)
                     .ThenBy(element => element.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetMaterial(mesh, element.MaterialFamily);
            mesh.CurrentDecorClass = element.CastsShadow
                ? DecorClass.MegastationInteriorMajor
                : DecorClass.MegastationInteriorMinor;
            int start = mesh.FaceCount;
            AddBox(mesh, element.Frame, element.Size, element.Colour);
            illuminationRanges.Add((start, mesh.FaceCount - start, element.Illumination));
        }
        mesh.ApplyIlluminationFlags();
        foreach ((int start, int count, float illumination) in illuminationRanges)
        for (int face = start; face < start + count; face++)
            mesh.SetFaceIllumination(face, illumination);
        stopwatch.Stop();

        var diagnostics = plan.Diagnostics with
        {
            PortalVisibleVertexCount = mesh.VertexCount,
            PortalVisibleTriangleCount = mesh.IndexCount / 3,
            PortalCasterVertexCount = CountCasterVertices(mesh),
            PortalCasterTriangleCount = CountCasterIndices(mesh) / 3,
            MeshBuildMilliseconds = stopwatch.ElapsedMilliseconds,
            PortalGuidanceElementCount = presentation.PortalElementCount,
            ThroatGuidanceElementCount = presentation.ThroatElementCount,
            InteriorLandmarkElementCount = presentation.InteriorLandmarkCount,
            GuidanceGlowCount = presentation.Markers.Count,
            GuidanceVisibleVertexCount = presentation.Elements.Count * 24,
            GuidanceVisibleTriangleCount = presentation.Elements.Count * 12,
            ThroatLinerElementCount = presentation.ThroatLinerCount,
            ThroatRibElementCount = presentation.ThroatRibCount,
            ThroatMarkingElementCount = presentation.ThroatMarkingCount,
            ThroatCasterElementCount = presentation.ThroatCasterCount,
            ThroatTubeWallElementCount = presentation.ThroatTubeWallCount,
            ThroatCrownElementCount = presentation.ThroatCrownCount,
            ThroatFixtureElementCount = presentation.ThroatFixtureCount,
            ApproachBeamCount = presentation.ApproachBeams.Count,
            ApproachFixtureElementCount = presentation.ApproachFixtureElementCount,
            ApproachBeamLength = presentation.ApproachBeams.FirstOrDefault()?.Length ?? 0f,
            ApproachBeamHalfAngleDegrees =
                presentation.ApproachBeams.FirstOrDefault()?.HalfAngleDegrees ?? 0f,
            ApproachBeamVertexCount = MegastationApproachBeamMeshBuilder.VertexCount(
                presentation),
            ApproachBeamTriangleCount = MegastationApproachBeamMeshBuilder.VertexCount(
                presentation) / 3,
            EntrancePortalUp = plan.PortalUp,
            EntrancePortalRight = plan.PortalRight,
            EntranceProjectionLength = presentation.Precinct.ProjectionLength,
            EntranceLocalObstructionProjection = presentation.Precinct.LocalObstructionProjection,
            EntranceLocalSkylineHeight = presentation.Precinct.LocalSkylineHeight,
            EntranceProjectionHeightFraction = presentation.Precinct.ProjectionHeightFraction,
            EntrancePaletteIdentity = presentation.Palette.Identity,
        };
        return new(mesh, diagnostics);
    }

    public static StationModuleMesh BuildStructuralCaster(
        StructuralOccupancy occupancy,
        BoundaryTopology topology)
    {
        var mesh = new StationModuleMesh();
        foreach (BoundaryFace face in topology.Faces.Where(face =>
                     face.SpaceKind != MegastationBoundarySpaceKind.InteriorBoundary))
        {
            Vector3[] p = face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(occupancy.Grid, vertex))
                .ToArray();
            mesh.AddQuad(p[0], p[1], p[2], p[3], Color.White);
        }
        mesh.ApplyIlluminationFlags();
        return mesh;
    }

    private static void SetMaterial(StationModuleMesh mesh, SystemMaterialFamilyId family)
    {
        mesh.CurrentMaterialFamily = family;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(family).TileSizeMeters;
    }

    private static void AddBox(StationModuleMesh mesh, Matrix frame, Vector3 size, Color colour)
        => mesh.AddOrientedBox(frame, size, colour);

    private static int CountCasterVertices(StationModuleMesh mesh)
        => mesh.DecorClassRanges
            .Where(range => range.decorClass == DecorClass.MegastationInteriorMajor)
            .Sum(range => range.indexCount / 6 * 4);

    private static int CountCasterIndices(StationModuleMesh mesh)
        => mesh.DecorClassRanges
            .Where(range => range.decorClass == DecorClass.MegastationInteriorMajor)
            .Sum(range => range.indexCount);
}

public static class MegastationInteriorSignatureBuilder
{
    public static string Compute(MegastationInteriorPlan plan)
    {
        var text = new StringBuilder()
            .Append(plan.Diagnostics.AlgorithmVersion).Append('|')
            .Append(plan.Identity).Append('|').Append(plan.Seed).Append('|')
            .Append((int)plan.PortalDirection).Append('|')
            .Append(plan.PortalCentre).Append('|').Append(plan.PortalClearSize).Append('|')
            .Append(plan.ThroatVolume).Append('|')
            .Append(plan.MainFlightVolume).Append('|')
            .Append(plan.CavityEnvelope).Append('|')
            .Append(plan.EntrancePrecinct.Minimum).Append('|')
            .Append(plan.EntrancePrecinct.Maximum).Append('|')
            .Append(plan.EntrancePrecinct.AssemblyMinimum).Append('|')
            .Append(plan.EntrancePrecinct.AssemblyMaximum).Append('|')
            .Append(plan.EntrancePrecinct.CrownOuterWidth).Append('|')
            .Append(plan.EntrancePrecinct.CrownOuterHeight).Append('|')
            .Append(plan.EntrancePrecinct.ClearanceMargin);
        foreach (MegastationProtectedVoidCell cell in plan.ProtectedCells)
            text.Append('|').Append(cell.Cell.X).Append(',').Append(cell.Cell.Y).Append(',')
                .Append(cell.Cell.Z).Append(':').Append((int)cell.Kind);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public static class MegastationInteriorDebug
{
    public static VertexPositionColor[]? BuildLines(
        MegastationInteriorPlan plan,
        BoundaryTopology topology,
        SliceGrid grid)
    {
#if DEBUG
        var lines = new List<VertexPositionColor>();
        AddVolume(lines, plan.ThroatVolume, new Color(255, 176, 44));
        AddVolume(lines, plan.MainFlightVolume, new Color(64, 235, 112));
        AddPortal(lines, plan, new Color(40, 220, 255));
        foreach (BoundaryFace face in topology.Faces.Where(face =>
                     face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary))
        {
            Vector3[] corners = face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex))
                .ToArray();
            for (int edge = 0; edge < 4; edge++)
                AddLine(lines, corners[edge], corners[(edge + 1) % 4], new Color(220, 72, 235));
        }
        return lines.ToArray();
#else
        return null;
#endif
    }

#if DEBUG
    private static void AddPortal(
        List<VertexPositionColor> lines,
        MegastationInteriorPlan plan,
        Color colour)
    {
        float halfWidth = plan.PortalClearSize.X * .5f;
        float halfHeight = plan.PortalClearSize.Y * .5f;
        Vector3[] corners =
        [
            plan.PortalCentre - plan.PortalRight * halfWidth - plan.PortalUp * halfHeight,
            plan.PortalCentre + plan.PortalRight * halfWidth - plan.PortalUp * halfHeight,
            plan.PortalCentre + plan.PortalRight * halfWidth + plan.PortalUp * halfHeight,
            plan.PortalCentre - plan.PortalRight * halfWidth + plan.PortalUp * halfHeight,
        ];
        for (int edge = 0; edge < 4; edge++)
            AddLine(lines, corners[edge], corners[(edge + 1) % 4], colour);
    }

    private static void AddVolume(
        List<VertexPositionColor> lines,
        MegastationInteriorVolume volume,
        Color colour)
    {
        Vector3 min = volume.Minimum;
        Vector3 max = volume.Maximum;
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];
        int[] edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];
        for (int edge = 0; edge < edges.Length; edge += 2)
            AddLine(lines, corners[edges[edge]], corners[edges[edge + 1]], colour);
    }

    private static void AddLine(
        List<VertexPositionColor> lines,
        Vector3 a,
        Vector3 b,
        Color colour)
    {
        lines.Add(new(a, colour));
        lines.Add(new(b, colour));
    }
#endif
}
