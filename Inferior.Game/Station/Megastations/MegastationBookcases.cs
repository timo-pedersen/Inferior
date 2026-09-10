using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public static partial class MegastationMegaShelfPlanner
{
    private const float BookcaseMinimumClearVolume =
        MegastationLandingDistrictPlanner.IntegratedSiteArchitectureClearHeight + 4f;
    private const float BookcaseFloorClearance = 28f;
    // Keep the upper rack well below the irregular structural roof cells, not
    // merely below the nominal cavity envelope. This also leaves the strong
    // breathing room above a bookcase requested by the composition.
    private const float BookcaseRoofClearance = 126f;
    // Matches the landing operating-volume expansion so a broad rack does not
    // reserve through perpendicular boundary walls at either end.
    private const float BookcaseWallMargin = 20f;
    private const float BookcaseRunStep = 8f;
    // Bookcase levels are wall-attached and may use the compact integrated-site
    // envelope. Keep these dimensions derived from the landing planner rather
    // than duplicating its pad, margin, and frontage assumptions here.
    private static readonly Vector2 MinimumIntegratedLandingBody =
        MegastationLandingDistrictPlanner.MinimumShelfBodySize(
            MegastationLandingSiteLayout.WallIntegrated);
    private static float MinimumLandingWidth => MinimumIntegratedLandingBody.X;
    private static float MinimumLandingDepth => MinimumIntegratedLandingBody.Y;

    internal static float[] BookcaseElevations(
        float minimum,
        float maximum,
        int levelCount,
        float thickness)
    {
        if (levelCount is < 3 or > 5 || maximum <= minimum)
            return [];
        float gap = (maximum - minimum - BookcaseFloorClearance
            - BookcaseRoofClearance - levelCount * thickness) / (levelCount - 1f);
        if (gap < BookcaseMinimumClearVolume)
            return [];
        return Enumerable.Range(0, levelCount)
            .Select(index => minimum + BookcaseFloorClearance + thickness
                + index * (gap + thickness))
            .ToArray();
    }

    internal static IReadOnlyList<float[]> BookcaseElevationOptions(
        float minimum,
        float maximum,
        int desiredLevelCount,
        float thickness)
    {
        var options = new List<float[]>();
        for (int count = Math.Clamp(desiredLevelCount, 3, 5); count >= 3; count--)
        {
            float[] elevations = BookcaseElevations(minimum, maximum, count, thickness);
            if (elevations.Length != 0)
                options.Add(elevations);
        }
        return options;
    }

    private static BookcaseDiscovery DiscoverBookcase(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingSiteStructuralClearance clearance,
        IReadOnlyList<MegastationBayWallSurface> walls,
        int shelfSeed,
        MegastationShelfMacroLayout? forcedLayout,
        CancellationToken cancellationToken)
    {
        if (forcedLayout == MegastationShelfMacroLayout.None)
            return BookcaseDiscovery.Empty;
        int seed = MegastationSeed.Derive(shelfSeed, "bookcase:v1");
        if (forcedLayout is null && Unit(seed, "presence") >= .42f)
            return BookcaseDiscovery.Empty;

        MegastationShelfMacroLayout[] layouts = forcedLayout is { } forced
            ? [forced]
            : Enum.GetValues<MegastationShelfMacroLayout>()
                .Where(layout => layout != MegastationShelfMacroLayout.None)
                .OrderByDescending(layout => Unit(seed, $"layout:{layout}"))
                .ToArray();
        var aggregate = new BookcaseDiscovery();
        foreach (MegastationShelfMacroLayout layout in layouts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BookcaseBuildResult result = TryBuildBookcase(
                layout, interior, occupancy, topology, clearance, walls, seed);
            aggregate.Count(result.Reject);
            if (result.Arrangement is null)
                continue;
            aggregate.Levels.AddRange(result.Levels);
            aggregate.Arrangement = result.Arrangement;
            return aggregate;
        }
        return aggregate;
    }

    private static BookcaseBuildResult TryBuildBookcase(
        MegastationShelfMacroLayout layout,
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingSiteStructuralClearance clearance,
        IReadOnlyList<MegastationBayWallSurface> walls,
        int seed)
    {
        MegastationBayWallSurface? back = walls.FirstOrDefault(
            wall => wall.Kind == MegastationBayWallKind.Rear);
        MegastationBayWallSurface? left = walls.FirstOrDefault(
            wall => wall.Kind == MegastationBayWallKind.Left);
        MegastationBayWallSurface? rightWall = walls.FirstOrDefault(
            wall => wall.Kind == MegastationBayWallKind.Right);
        MegastationBayWallSurface? entrance = MegastationBayHabitationPlanner
            .CreateWalls(interior).FirstOrDefault(
                wall => wall.Kind == MegastationBayWallKind.Entrance);
        MegastationBayWallSurface[] hosts = layout switch
        {
            MegastationShelfMacroLayout.Back when back is not null => [back],
            MegastationShelfMacroLayout.Left when left is not null => [left],
            MegastationShelfMacroLayout.Right when rightWall is not null => [rightWall],
            MegastationShelfMacroLayout.SymmetricSides
                when left is not null && rightWall is not null => [left, rightWall],
            MegastationShelfMacroLayout.U
                when left is not null && back is not null && rightWall is not null
                => [left, back, rightWall],
            _ => [],
        };
        if (hosts.Length == 0 || back is null || entrance is null)
            return BookcaseBuildResult.Rejected(BookcaseReject.WallContinuity);

        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 canonicalRight = Vector3.Normalize(interior.PortalRight);
        Vector3 inward = Vector3.Normalize(-interior.OutwardNormal);
        (float rMinimum, float rMaximum) = ProjectionSpan(interior.CavityEnvelope,
            canonicalRight);
        (float uMinimum, float uMaximum) = ProjectionSpan(interior.CavityEnvelope, up);
        (float dMinimum, float dMaximum) = ProjectionSpan(interior.CavityEnvelope, inward);
        float cavityWidth = rMaximum - rMinimum;
        float cavityDepth = dMaximum - dMinimum;
        float sideProjection = MathHelper.Clamp(cavityWidth * .20f,
            MinimumLandingWidth, 178f);
        float centralMinimum = MathF.Max(360f,
            SupportedShipEnvelopeStandards.LargeWidth + 220f);
        sideProjection = MathF.Min(sideProjection,
            MathF.Max(MinimumLandingWidth, (cavityWidth - centralMinimum) * .5f));
        float backProjection = MathHelper.Clamp(cavityDepth * .30f,
            MinimumLandingDepth, 230f);
        float representativeLength = hosts.Any(wall => wall.Kind == MegastationBayWallKind.Rear)
            ? cavityWidth : cavityDepth;
        float representativeProjection = hosts.Any(wall =>
            wall.Kind is MegastationBayWallKind.Left or MegastationBayWallKind.Right)
            ? sideProjection : backProjection;
        float thickness = MathF.Min(2.5f, ShelfThickness(
            MegastationMegaShelfFamily.Bookcase,
            representativeLength, representativeProjection, seed));

        int desiredLevels = 3 + PositiveMod(
            MegastationSeed.Derive(seed, $"levels:{layout}"), 3);
        IReadOnlyList<float[]> elevationOptions = BookcaseElevationOptions(
            uMinimum, uMaximum, desiredLevels, thickness);
        if (elevationOptions.Count == 0)
            return BookcaseBuildResult.Rejected(BookcaseReject.InsufficientHeight);

        StructuralTrussCrossSection trussFamily = Unit(seed, $"truss-family:{layout}") < .55f
            ? StructuralTrussCrossSection.Box
            : StructuralTrussCrossSection.Triangular;
        string identity = $"bookcase:{layout.ToString().ToLowerInvariant()}";
        List<ShelfCandidate>? allLevels = null;
        List<string>? runSummaries = null;
        float[]? elevations = null;
        BookcaseReject lastReject = BookcaseReject.WallContinuity;
        foreach (float[] option in elevationOptions)
        {
            List<ShelfCandidate>? synchronizedLevels = null;
            List<string>? synchronizedRuns = null;
            float? sharedSideLength = null;
            for (int synchronizationAttempt = 0; synchronizationAttempt < 64;
                 synchronizationAttempt++)
            {
                var trialLevels = new List<ShelfCandidate>();
                var trialRuns = new List<string>();
                var sideRunLengths = new List<float>();
                bool valid = true;
                foreach (MegastationBayWallSurface wall in hosts)
                {
                    float projection = wall.Kind == MegastationBayWallKind.Rear
                        ? backProjection : sideProjection;
                    WallRun run = layout == MegastationShelfMacroLayout.U
                        && wall.Kind == MegastationBayWallKind.Rear
                        && left is not null && rightWall is not null
                        ? JoinedBackRun(back, left, rightWall, sideProjection)
                        : InitialRun(wall, back, entrance, occupancy.Grid, topology);
                    WallStackBuildResult stack = TryBuildLongestStack(
                        identity, wall, run, projection, option, thickness,
                        interior, occupancy, topology, clearance, seed,
                        wall.Kind is MegastationBayWallKind.Left
                            or MegastationBayWallKind.Right ? sharedSideLength : null);
                    if (stack.Levels.Count == 0)
                    {
                        lastReject = stack.Reject;
                        valid = false;
                        break;
                    }
                    trialLevels.AddRange(stack.Levels);
                    trialRuns.Add($"{wall.Kind}:{stack.RunLength:F1}m");
                    if (wall.Kind is MegastationBayWallKind.Left
                        or MegastationBayWallKind.Right)
                        sideRunLengths.Add(stack.RunLength);
                }
                if (!valid)
                    break;
                if (sideRunLengths.Count == 2
                    && MathF.Abs(sideRunLengths[0] - sideRunLengths[1]) > .01f)
                {
                    sharedSideLength = sideRunLengths.Min();
                    continue;
                }
                synchronizedLevels = trialLevels;
                synchronizedRuns = trialRuns;
                break;
            }
            if (synchronizedLevels is null || synchronizedRuns is null)
                continue;
            allLevels = synchronizedLevels;
            runSummaries = synchronizedRuns;
            elevations = option;
            break;
        }
        if (allLevels is null || runSummaries is null || elevations is null)
            return BookcaseBuildResult.Rejected(lastReject);
        int levelCount = elevations.Length;

        if (layout is MegastationShelfMacroLayout.SymmetricSides
            or MegastationShelfMacroLayout.U)
        {
            ShelfCandidate[] sideLevels = allLevels.Where(level =>
                    level.PrimaryWall.Kind is MegastationBayWallKind.Left
                        or MegastationBayWallKind.Right)
                .ToArray();
            float[] lengths = sideLevels.Select(level => level.Body.Size.X)
                .DistinctBy(value => MathF.Round(value, 2)).ToArray();
            if (lengths.Length != 1)
                return BookcaseBuildResult.Rejected(BookcaseReject.Symmetry);
        }

        // The macro owns exact rear-corner contacts, but no bodies may overlap
        // within the installation. Side runs in U layouts begin at the back
        // shelf's front edge so their coplanar top faces never z-fight.
        for (int first = 0; first < allLevels.Count; first++)
        for (int second = first + 1; second < allLevels.Count; second++)
        {
            ShelfCandidate a = allLevels[first];
            ShelfCandidate b = allLevels[second];
            bool sameLevel = MathF.Abs(Vector3.Dot(
                a.Body.Centre - b.Body.Centre, up)) < thickness + .1f;
            bool uCornerJoin = layout == MegastationShelfMacroLayout.U && sameLevel
                && a.PrimaryWall.Kind != b.PrimaryWall.Kind;
            if (!uCornerJoin && StructuralVolumesIntersect(a.Body, b.Body, 0f))
                return BookcaseBuildResult.Rejected(BookcaseReject.Structural);
        }

        float[] tops = allLevels.Take(levelCount)
            .Select(level => Vector3.Dot(
                level.Body.Centre + level.Body.Up * level.Body.Size.Y * .5f, up))
            .ToArray();
        string summary = $"Shelf Macro Layout: {layout}; levels={levelCount}; "
            + $"elevations={string.Join(',', tops.Select(value => value.ToString("F1")))}; "
            + $"runs={string.Join(',', runSummaries)}; truss={trussFamily}";
        var arrangement = new MegastationBookcaseArrangement(
            identity, layout, levelCount, tops,
            hosts.Select(wall => wall.Identity).ToArray(),
            allLevels.Select(level => level.Identity).ToArray(),
            trussFamily, summary);
        return new(allLevels, arrangement, BookcaseReject.None);
    }

    private static WallStackBuildResult TryBuildLongestStack(
        string bookcaseIdentity,
        MegastationBayWallSurface wall,
        WallRun initialRun,
        float projection,
        IReadOnlyList<float> elevations,
        float thickness,
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingSiteStructuralClearance clearance,
        int seed,
        float? maximumLength = null)
    {
        BookcaseReject lastReject = BookcaseReject.WallContinuity;
        float minimumProjection = wall.Kind == MegastationBayWallKind.Rear
            ? MinimumLandingDepth : MinimumLandingWidth;
        for (float currentProjection = projection;
             currentProjection >= MathF.Max(minimumProjection, projection * .58f);
             currentProjection -= 8f)
        {
        float length = MathF.Min(initialRun.Length, maximumLength ?? float.MaxValue);
        float minimumUsefulLength = MathF.Max(
            wall.Kind == MegastationBayWallKind.Rear
                ? MinimumLandingWidth : MinimumLandingDepth,
            wall.Width
            * (wall.Kind == MegastationBayWallKind.Rear ? .30f : .36f));
        while (length >= minimumUsefulLength)
        {
            float front = initialRun.BackCoordinate
                - initialRun.DirectionToBack * length;
            float anchoredCentre = (initialRun.BackCoordinate + front) * .5f;
            float minimumCentre = -wall.Width * .5f + BookcaseWallMargin + length * .5f;
            float maximumCentre = wall.Width * .5f - BookcaseWallMargin - length * .5f;
            float[] centres = wall.Kind == MegastationBayWallKind.Rear
                && !initialRun.FixedCentre
                ? Enumerable.Range(0, Math.Max(1,
                        (int)MathF.Floor((maximumCentre - minimumCentre) / BookcaseRunStep) + 1))
                    .Select(index => minimumCentre + index * BookcaseRunStep)
                    .Append(maximumCentre).Append(anchoredCentre).Distinct().ToArray()
                : [anchoredCentre];
            foreach (float centre in centres)
            {
            var levels = new List<ShelfCandidate>();
            bool valid = true;
            for (int index = 0; index < elevations.Count; index++)
            {
                int levelSeed = MegastationSeed.Derive(seed,
                    $"{bookcaseIdentity}:{wall.Kind}:level:{index}");
                float wallCentreHeight = Vector3.Dot(wall.Centre, wall.Up);
                Vector3 topRoot = wall.Centre + wall.Right * centre
                    + wall.Up * (elevations[index] - wallCentreHeight);
                Vector3 bodyCentre = topRoot + wall.Normal * (currentProjection * .5f)
                    - wall.Up * (thickness * .5f);
                string identity = $"mega-shelf:{bookcaseIdentity}:"
                    + $"{wall.Kind.ToString().ToLowerInvariant()}:level:{index}";
                var body = new MegastationInteriorStructuralSolid(
                    $"{identity}/body", MegastationInteriorStructuralRole.MegaShelf,
                    bodyCentre, new(length, thickness, currentProjection),
                    wall.Right, wall.Up, wall.Normal, true, true);
                if (!TryFindRootSupport(body, wall, occupancy.Grid, topology,
                        out BoundaryFaceKey[] support))
                {
                    lastReject = BookcaseReject.WallContinuity;
                    valid = false;
                    break;
                }
                CandidateBuildResult result = TryCreateCandidate(
                    interior, clearance, MegastationMegaShelfFamily.Bookcase,
                    identity, [wall], support, body,
                    BandForLevel(index, elevations.Count), levelSeed, 1f);
                if (result.Candidate is null || result.Candidate.LandingSurface is null)
                {
                    lastReject = result.Reject == CandidateReject.Arrival
                        ? BookcaseReject.Arrival : BookcaseReject.Structural;
                    valid = false;
                    break;
                }
                levels.Add(result.Candidate);
            }
            if (valid)
                return new(levels, length, BookcaseReject.None);
            }
            if (initialRun.FixedCentre)
                break;
            length -= BookcaseRunStep;
        }
        }
        return new([], 0f, lastReject);
    }

    private static WallRun InitialRun(
        MegastationBayWallSurface wall,
        MegastationBayWallSurface back,
        MegastationBayWallSurface entrance,
        SliceGrid grid,
        BoundaryTopology topology)
    {
        (float actualMinimum, float actualMaximum) = WallAxisExtent(wall, grid, topology);
        if (wall.Kind == MegastationBayWallKind.Rear)
            return new(actualMaximum - BookcaseWallMargin,
                actualMaximum - actualMinimum - BookcaseWallMargin * 2f, 1f);
        float backCoordinate = Vector3.Dot(back.Centre - wall.Centre, wall.Right);
        float entranceCoordinate = Vector3.Dot(entrance.Centre - wall.Centre, wall.Right);
        float direction = MathF.Sign(backCoordinate - entranceCoordinate);
        float backLimit = direction > 0f ? actualMaximum : actualMinimum;
        float entranceLimit = direction > 0f ? actualMinimum : actualMaximum;
        float anchoredBack = backLimit - direction * BookcaseWallMargin;
        float physicalFront = entranceCoordinate + direction * BookcaseWallMargin;
        float boundedFront = direction > 0f
            ? MathF.Max(entranceLimit + BookcaseWallMargin, physicalFront)
            : MathF.Min(entranceLimit - BookcaseWallMargin, physicalFront);
        float length = MathF.Abs(anchoredBack - boundedFront);
        return new(anchoredBack, length, direction);
    }

    private static WallRun JoinedBackRun(
        MegastationBayWallSurface back,
        MegastationBayWallSurface left,
        MegastationBayWallSurface right,
        float sideProjection)
    {
        float leftInner = Vector3.Dot(
            left.Centre + left.Normal * sideProjection - back.Centre, back.Right);
        float rightInner = Vector3.Dot(
            right.Centre + right.Normal * sideProjection - back.Centre, back.Right);
        // Keep a narrow construction seam at the butt joint. Exact floating-point
        // contact between independently oriented boxes can otherwise be classified
        // as overlap and, more importantly, risks coplanar rasterization artifacts.
        const float joinSeam = .5f;
        float minimum = MathF.Min(leftInner, rightInner) + joinSeam;
        float maximum = MathF.Max(leftInner, rightInner) - joinSeam;
        return new(maximum, maximum - minimum, 1f, FixedCentre: true);
    }

    private static (float Minimum, float Maximum) WallAxisExtent(
        MegastationBayWallSurface wall,
        SliceGrid grid,
        BoundaryTopology topology)
    {
        float[] values = topology.Faces
            .Where(face => face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction),
                    wall.Normal) > .9999f)
            .SelectMany(face => face.Vertices.Select(vertex =>
                BoundaryTopologyBuilder.Position(grid, vertex)))
            .Where(point => MathF.Abs(Vector3.Dot(
                point - wall.Centre, wall.Normal)) < .01f)
            .Select(point => Vector3.Dot(point - wall.Centre, wall.Right))
            .ToArray();
        return values.Length == 0
            ? (-wall.Width * .5f, wall.Width * .5f)
            : (values.Min(), values.Max());
    }

    private static MegastationMegaShelfVerticalBand BandForLevel(int index, int count)
    {
        float position = (index + 1f) / (count + 1f);
        return position < .34f ? MegastationMegaShelfVerticalBand.Low
            : position > .66f ? MegastationMegaShelfVerticalBand.High
            : MegastationMegaShelfVerticalBand.Mid;
    }

    private readonly record struct WallRun(
        float BackCoordinate,
        float Length,
        float DirectionToBack,
        bool FixedCentre = false);

    private readonly record struct WallStackBuildResult(
        IReadOnlyList<ShelfCandidate> Levels,
        float RunLength,
        BookcaseReject Reject);

    private readonly record struct BookcaseBuildResult(
        IReadOnlyList<ShelfCandidate> Levels,
        MegastationBookcaseArrangement? Arrangement,
        BookcaseReject Reject)
    {
        public static BookcaseBuildResult Rejected(BookcaseReject reject)
            => new([], null, reject);
    }

    private enum BookcaseReject
    {
        None,
        InsufficientHeight,
        Arrival,
        WallContinuity,
        Structural,
        Symmetry,
    }

    private sealed class BookcaseDiscovery
    {
        public static BookcaseDiscovery Empty => new();
        public List<ShelfCandidate> Levels { get; } = [];
        public MegastationBookcaseArrangement? Arrangement { get; set; }
        public int InsufficientHeightRejectCount { get; private set; }
        public int ArrivalRejectCount { get; private set; }
        public int WallContinuityRejectCount { get; private set; }
        public int StructuralRejectCount { get; private set; }
        public int SymmetryRejectCount { get; private set; }

        public void Count(BookcaseReject reject)
        {
            switch (reject)
            {
                case BookcaseReject.InsufficientHeight: InsufficientHeightRejectCount++; break;
                case BookcaseReject.Arrival: ArrivalRejectCount++; break;
                case BookcaseReject.WallContinuity: WallContinuityRejectCount++; break;
                case BookcaseReject.Structural: StructuralRejectCount++; break;
                case BookcaseReject.Symmetry: SymmetryRejectCount++; break;
            }
        }
    }
}
