using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationInteriorStructuralRole
{
    MegaShelf,
    MegaShelfTruss,
}

public enum MegastationMegaShelfSurfaceRole
{
    Top,
    Underside,
    FrontEdge,
    SideEdge,
    WallRoot,
}

public sealed record MegastationInteriorStructuralSolid(
    string Identity,
    MegastationInteriorStructuralRole Role,
    Vector3 Centre,
    Vector3 Size,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    bool CastsStellarShadow,
    bool CastsStaticArtificialShadow,
    float ExposedCornerClip = 0f,
    int ExposedCornerRightSign = 0);

public sealed record MegastationLandingSurface(
    string Identity,
    Vector3 Centre,
    Vector3 Up,
    Vector3 Right,
    Vector3 Forward,
    Vector2 UsableSize,
    float EdgeMargin,
    string StructuralOwnerIdentity,
    bool IsMainFloor);

public sealed record MegastationMegaShelfMarker(
    string Identity,
    MegastationMegaShelfSurfaceRole SurfaceRole,
    Vector3 Centre,
    Vector3 Size,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    Color Colour,
    float Illumination,
    bool IsCorner,
    bool IsLowerEdge);

internal readonly record struct MegastationMegaShelfFaceColours(
    Color Top,
    Color Side,
    Color Underside);

public sealed record MegastationMegaShelfTruss(
    string Identity,
    StructuralTrussSpec Spec,
    bool CastsStellarShadow,
    bool CastsStaticArtificialShadow);

public enum MegastationMegaShelfUsage
{
    Structural,
    LandingCapable,
}

public enum MegastationMegaShelfFamily
{
    Cantilever,
    Corner,
    FullSpan,
    Bookcase,
}

public enum MegastationShelfMacroLayout
{
    None,
    Back,
    Left,
    Right,
    SymmetricSides,
    U,
}

public enum MegastationMegaShelfVerticalBand
{
    Low,
    Mid,
    High,
}

public sealed record MegastationMegaShelfClearanceVolume(
    Vector3 Centre,
    Vector3 Size,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    bool ReservesLandingOperations);

public sealed record MegastationMegaShelf(
    string Identity,
    MegastationMegaShelfFamily Family,
    string HostWallIdentity,
    IReadOnlyList<string> HostWallIdentities,
    BoundaryFaceKey[] SupportingFaces,
    MegastationInteriorStructuralSolid Body,
    MegastationLandingSurface? LandingSurface,
    MegastationMegaShelfUsage Usage,
    MegastationMegaShelfVerticalBand VerticalBand,
    MegastationMegaShelfClearanceVolume ClearanceVolume,
    IReadOnlyList<MegastationMegaShelfSurfaceRole> SurfaceRoles,
    IReadOnlyList<MegastationMegaShelfMarker> Markers,
    IReadOnlyList<MegastationMegaShelfTruss> Trusses,
    int Seed,
    Vector3? ExposedCornerPoint = null);

public sealed record MegastationBookcaseArrangement(
    string Identity,
    MegastationShelfMacroLayout Layout,
    int LevelCount,
    IReadOnlyList<float> Elevations,
    IReadOnlyList<string> HostWallIdentities,
    IReadOnlyList<string> ShelfIdentities,
    StructuralTrussCrossSection TrussFamily,
    string Summary);

public sealed record MegastationMegaShelfDiagnostics(
    int AlgorithmVersion,
    int CandidateCount,
    int AcceptedCount,
    int SupportRejectCount,
    int StructuralRejectCount,
    int ArrivalExclusionRejectCount,
    int ShelfOverlapRejectCount,
    int VerticalSeparationRejectCount,
    int OperatingClearanceRejectCount,
    int LandingDowngradeCount,
    int FullSpanFlightClearanceRejectCount,
    int CompositionRejectCount,
    int CantileverCandidateCount,
    int CornerCandidateCount,
    int FullSpanCandidateCount,
    int CornerViableCandidateCount,
    int FullSpanViableCandidateCount,
    int CantileverAcceptedCount,
    int CornerAcceptedCount,
    int FullSpanAcceptedCount,
    int LowCandidateCount,
    int MidCandidateCount,
    int HighCandidateCount,
    int LowAcceptedCount,
    int MidAcceptedCount,
    int HighAcceptedCount,
    float MinimumWidth,
    float MaximumWidth,
    float MinimumProjection,
    float MaximumProjection,
    float MinimumThickness,
    float MaximumThickness,
    int VisibleTriangleCount,
    int CasterTriangleCount,
    int MarkerCount,
    int CornerMarkerCount,
    int LowerEdgeMarkerCount,
    int TrussCount,
    int BoxTrussCount,
    int TriangularTrussCount,
    int TrussVisibleTriangleCount,
    int TrussCasterTriangleCount,
    string Summary,
    string Signature,
    MegastationShelfMacroLayout MacroLayout = MegastationShelfMacroLayout.None,
    int BookcaseCount = 0,
    int BookcaseShelfCount = 0,
    int BookcaseInsufficientHeightRejectCount = 0,
    int BookcaseArrivalRejectCount = 0,
    int BookcaseWallContinuityRejectCount = 0,
    int BookcaseStructuralRejectCount = 0,
    int BookcaseSymmetryRejectCount = 0,
    string MacroSummary = "");

public sealed record MegastationMegaShelfPlan(
    int AlgorithmVersion,
    int Seed,
    IReadOnlyList<MegastationMegaShelf> Shelves,
    IReadOnlyList<MegastationInteriorStructuralSolid> StructuralSolids,
    IReadOnlyList<MegastationLandingSurface> LandingSurfaces,
    IReadOnlyList<MegastationMegaShelfMarker> Markers,
    IReadOnlyList<MegastationMegaShelfTruss> Trusses,
    IReadOnlyList<MegastationBookcaseArrangement> Bookcases,
    MegastationMegaShelfDiagnostics Diagnostics);

public readonly record struct MegastationMegaShelfDevelopmentOptions(
    int? ForcedShelfCount,
    bool ForceLandingSiteOnShelf,
    MegastationShelfMacroLayout? ForcedMacroLayout = null)
{
    public static MegastationMegaShelfDevelopmentOptions Runtime => new(
        ForcedShelfCount: null,
        ForceLandingSiteOnShelf: false,
        ForcedMacroLayout: null
    );
}

public static partial class MegastationMegaShelfPlanner
{
    public const int AlgorithmVersion = 8;
    public const float LandingEdgeMargin = 12f;
    private const float MinimumFloorSeparation = 86f;
    internal const float MinimumCeilingSeparation = 86f;
    private const float CandidateArea = 18_000f;

    public static MegastationMegaShelfPlan Plan(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationMegaShelfDevelopmentOptions options,
        CancellationToken cancellationToken = default)
    {
        int seed = MegastationSeed.Derive(interior.Seed, "mega-shelves:v1");
        var shelves = new List<MegastationMegaShelf>();
        int candidates = 0;
        int supportRejects = 0;
        int structuralRejects = 0;
        int arrivalRejects = 0;
        int overlapRejects = 0;
        int separationRejects = 0;
        int operatingClearanceRejects = 0;
        int landingDowngrades = 0;
        int fullSpanFlightClearanceRejects = 0;
        int compositionRejects = 0;
        int lowCandidates = 0;
        int midCandidates = 0;
        int highCandidates = 0;
        int cantileverCandidates = 0;
        int cornerCandidates = 0;
        int fullSpanCandidates = 0;
        int cornerViableCandidates = 0;
        int fullSpanViableCandidates = 0;
        MegastationLandingSiteStructuralClearance clearance =
            MegastationLandingSiteStructuralClearance.Create(occupancy, interior);
        MegastationBayWallSurface[] allWalls = MegastationBayHabitationPlanner
            .CreateWalls(interior);
        MegastationBayWallSurface[] walls = allWalls
            .Where(wall => wall.IsEligible || wall.Kind == MegastationBayWallKind.Entrance)
            .ToArray();
        float activeDensity = CompositionDensity(interior,
            allWalls.Where(wall => wall.IsEligible).ToArray(), seed);

        BookcaseDiscovery bookcaseDiscovery = options.ForcedShelfCount is null
                || options.ForcedMacroLayout is not null
            ? DiscoverBookcase(interior, occupancy, topology, clearance,
                allWalls.Where(wall => wall.IsEligible).ToArray(), seed,
                options.ForcedMacroLayout, cancellationToken)
            : BookcaseDiscovery.Empty;
        foreach (ShelfCandidate level in bookcaseDiscovery.Levels)
        {
            MegastationMegaShelfMarker[] levelMarkers = BuildMarkers(
                level.Identity, level.Body,
                MegastationSeed.Derive(level.Seed, "edge-markers:v1"),
                MegastationMegaShelfFamily.Bookcase, level.HostWalls).ToArray();
            MegastationMegaShelfTruss[] levelTrusses = BuildTrusses(
                    level.Identity, level.Body,
                    MegastationSeed.Derive(level.Seed, "trusses:v1"),
                    force: true, family: MegastationMegaShelfFamily.Bookcase,
                    forcedCrossSection: bookcaseDiscovery.Arrangement?.TrussFamily)
                .Where(truss => TrussFits(truss, level.Body, [],
                    interior, clearance))
                .ToArray();
            shelves.Add(CreateShelf(level, levelMarkers, levelTrusses));
        }

        var pool = new List<ShelfCandidate>();
        foreach (MegastationBayWallSurface wall in walls)
        {
            float usableHeight = MathF.Max(0f,
                wall.Height - MinimumFloorSeparation - MinimumCeilingSeparation);
            int opportunityCount = Math.Max(1,
                (int)MathF.Ceiling(wall.Width * usableHeight / CandidateArea));
            int wallSeed = MegastationSeed.Derive(seed, $"candidates:{wall.Identity}");
            for (int opportunity = 0; opportunity < opportunityCount; opportunity++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates++;
                cantileverCandidates++;
                int candidateSeed = MegastationSeed.Derive(
                    wallSeed, $"opportunity:{opportunity}");
                MegastationMegaShelfVerticalBand verticalBand = VerticalBandForOpportunity(
                    opportunity, wallSeed);
                switch (verticalBand)
                {
                    case MegastationMegaShelfVerticalBand.Low: lowCandidates++; break;
                    case MegastationMegaShelfVerticalBand.Mid: midCandidates++; break;
                    case MegastationMegaShelfVerticalBand.High: highCandidates++; break;
                }
                float scaleBias = MathF.Pow(Unit(candidateSeed, "scale"), 1.15f);
                float width = MathHelper.Lerp(130f, 312f, scaleBias);
                float projection = MathHelper.Lerp(92f, 300f,
                    MathF.Pow(Unit(candidateSeed, "projection"), 1.35f));
                float thickness = ShelfThickness(
                    MegastationMegaShelfFamily.Cantilever, width, projection, candidateSeed);
                width = MathF.Min(width, wall.Width - 56f);

                float wallBottom = -wall.Height * .5f;
                float wallTop = wall.Height * .5f;
                float topMinimum = wallBottom + MinimumFloorSeparation + thickness;
                float topMaximum = wallTop - MinimumCeilingSeparation;
                if (width < 128f || topMinimum >= topMaximum)
                {
                    supportRejects++;
                    continue;
                }
                (float bandMinimum, float bandMaximum) = VerticalBandInterval(
                    topMinimum, topMaximum, verticalBand, wallSeed);
                float top = MathHelper.Lerp(bandMinimum, bandMaximum,
                    MathHelper.Lerp(.08f, .92f, Unit(candidateSeed, "height")));
                float xMinimum = -wall.Width * .5f + width * .5f + 24f;
                float xMaximum = wall.Width * .5f - width * .5f - 24f;
                if (xMinimum > xMaximum)
                {
                    supportRejects++;
                    continue;
                }
                float x = MathHelper.Lerp(xMinimum, xMaximum, Unit(candidateSeed, "position"));
                Vector2 rootCentre = new(x, top - thickness * .5f);
                Vector2 rootSize = new(width, thickness);
                if (!TryFindExactSupport(wall, rootCentre, rootSize, occupancy.Grid,
                        topology, out BoundaryFaceKey[] supportingFaces))
                {
                    supportRejects++;
                    continue;
                }

                Vector3 topRoot = wall.Centre + wall.Right * x + wall.Up * top;
                Vector3 bodyCentre = topRoot + wall.Normal * (projection * .5f)
                    - wall.Up * (thickness * .5f);
                Vector3 bodySize = new(width, thickness, projection);
                if (interior.ArrivalManeuverExclusion.IntersectsOrientedBox(
                        bodyCentre, wall.Right, wall.Up, wall.Normal, bodySize))
                {
                    arrivalRejects++;
                    continue;
                }
                if (!clearance.IsOrientedBoxClear(
                        bodyCentre, wall.Right, wall.Up, wall.Normal, bodySize,
                        out _))
                {
                    structuralRejects++;
                    continue;
                }

                string identity = $"mega-shelf:{wall.Identity}:{opportunity:00}";
                var solid = new MegastationInteriorStructuralSolid(
                    $"{identity}/body",
                    MegastationInteriorStructuralRole.MegaShelf,
                    bodyCentre,
                    bodySize,
                    wall.Right,
                    wall.Up,
                    wall.Normal,
                    CastsStellarShadow: true,
                    CastsStaticArtificialShadow: true);
                Vector3 topCentre = topRoot + wall.Normal * (projection * .5f);
                Vector3 canonicalRight = Vector3.Normalize(interior.PortalRight);
                Vector3 canonicalForward = Vector3.Normalize(interior.OutwardNormal);
                float canonicalWidth = MathF.Abs(Vector3.Dot(wall.Right, canonicalRight)) * width
                    + MathF.Abs(Vector3.Dot(wall.Normal, canonicalRight)) * projection;
                float canonicalDepth = MathF.Abs(Vector3.Dot(wall.Right, canonicalForward)) * width
                    + MathF.Abs(Vector3.Dot(wall.Normal, canonicalForward)) * projection;
                var proposedLandingSurface = new MegastationLandingSurface(
                    $"{identity}/landing-surface",
                    topCentre,
                    wall.Up,
                    canonicalRight,
                    canonicalForward,
                    new(MathF.Max(0f, canonicalWidth - LandingEdgeMargin * 2f),
                        MathF.Max(0f, canonicalDepth - LandingEdgeMargin * 2f)),
                    LandingEdgeMargin,
                    solid.Identity,
                    IsMainFloor: false);
                bool landingCapable = MegastationLandingDistrictPlanner.CanHostMinimumSite(
                    proposedLandingSurface.UsableSize);
                MegastationBayWallSurface[] hostWalls = [wall];
                if (landingCapable && !OperatingVolumeIsClear(
                        clearance, solid, hostWalls, landing: true))
                {
                    landingCapable = false;
                    landingDowngrades++;
                }
                if (!landingCapable && !OperatingVolumeIsClear(
                        clearance, solid, hostWalls, landing: false))
                {
                    operatingClearanceRejects++;
                    continue;
                }
                MegastationMegaShelfClearanceVolume clearanceVolume = CreateClearanceVolume(
                    solid, hostWalls, landingCapable);
                bool active = Unit(candidateSeed, "composition-presence") < activeDensity;
                pool.Add(new(
                    identity, MegastationMegaShelfFamily.Cantilever,
                    wall, hostWalls, supportingFaces, solid,
                    landingCapable ? proposedLandingSurface : null,
                    clearanceVolume, verticalBand, candidateSeed,
                    Unit(candidateSeed, "composition-priority"), active));
            }
        }

        CandidateDiscovery cornerDiscovery = DiscoverCornerCandidates(
            interior, occupancy, topology, clearance, walls, seed, activeDensity,
            cancellationToken);
        AddDiscovery(cornerDiscovery);
        CandidateDiscovery fullSpanDiscovery = DiscoverFullSpanCandidates(
            interior, occupancy, topology, clearance, walls, seed, activeDensity,
            cancellationToken);
        AddDiscovery(fullSpanDiscovery);

        void AddDiscovery(CandidateDiscovery discovery)
        {
            pool.AddRange(discovery.Candidates);
            candidates += discovery.CandidateCount;
            supportRejects += discovery.SupportRejectCount;
            structuralRejects += discovery.StructuralRejectCount;
            arrivalRejects += discovery.ArrivalRejectCount;
            operatingClearanceRejects += discovery.OperatingRejectCount;
            landingDowngrades += discovery.LandingDowngradeCount;
            fullSpanFlightClearanceRejects += discovery.FlightClearanceRejectCount;
            lowCandidates += discovery.LowCandidateCount;
            midCandidates += discovery.MidCandidateCount;
            highCandidates += discovery.HighCandidateCount;
            cornerCandidates += discovery.CornerCandidateCount;
            fullSpanCandidates += discovery.FullSpanCandidateCount;
            cornerViableCandidates += discovery.Candidates.Count(candidate =>
                candidate.Family == MegastationMegaShelfFamily.Corner);
            fullSpanViableCandidates += discovery.Candidates.Count(candidate =>
                candidate.Family == MegastationMegaShelfFamily.FullSpan);
        }

        while (pool.Count > 0
            && (options.ForcedShelfCount is not int requestedLimit
                || shelves.Count < Math.Max(0, requestedLimit)))
        {
            bool forceMore = options.ForcedShelfCount is int forced
                && shelves.Count < Math.Max(0, forced);
            ShelfCandidate[] eligible = pool.Where(candidate => candidate.Active || forceMore)
                .Where(candidate => shelves.All(existing =>
                    !StructuralVolumesIntersect(existing.Body, candidate.Body, 18f)
                    && !ClearanceVolumesIntersect(existing.ClearanceVolume,
                        candidate.ClearanceVolume)))
                .Where(candidate => !options.ForceLandingSiteOnShelf || shelves.Count > 0
                    || candidate.LandingSurface is not null)
                .OrderByDescending(candidate => CompositionScore(candidate, shelves, interior))
                .ThenBy(candidate => candidate.Identity, StringComparer.Ordinal)
                .ToArray();
            if (eligible.Length == 0)
                break;

            ShelfCandidate selected = eligible[0];
            pool.Remove(selected);
            MegastationMegaShelfMarker[] shelfMarkers = BuildMarkers(
                selected.Identity, selected.Body,
                MegastationSeed.Derive(selected.Seed, "edge-markers:v1"),
                selected.Family, selected.HostWalls).ToArray();
            MegastationMegaShelfTruss[] shelfTrusses = BuildTrusses(
                    selected.Identity, selected.Body,
                    MegastationSeed.Derive(selected.Seed, "trusses:v1"),
                    force: options.ForceLandingSiteOnShelf && shelves.Count == 0,
                    family: selected.Family)
                .Where(truss => TrussFits(truss, selected.Body, shelves,
                    interior, clearance))
                .ToArray();
            shelves.Add(CreateShelf(selected, shelfMarkers, shelfTrusses));

            foreach (ShelfCandidate rejected in pool.ToArray())
            {
                if (StructuralVolumesIntersect(selected.Body, rejected.Body, 18f)
                    || shelfTrusses.Any(truss => StructuralVolumesIntersect(
                        TrussSolid(truss), rejected.Body, 3f)))
                {
                    overlapRejects++;
                    pool.Remove(rejected);
                }
                else if (ClearanceVolumesIntersect(
                    selected.ClearanceVolume, rejected.ClearanceVolume))
                {
                    separationRejects++;
                    pool.Remove(rejected);
                }
            }

            if (options.ForcedShelfCount is int exact && shelves.Count >= Math.Max(0, exact))
                break;
        }
        compositionRejects = pool.Count(candidate => !candidate.Active);

        MegastationInteriorStructuralSolid[] solids = shelves.Select(item => item.Body).ToArray();
        MegastationLandingSurface[] landingSurfaces = shelves
            .Where(item => item.LandingSurface is not null)
            .Select(item => item.LandingSurface!)
            .ToArray();
        MegastationMegaShelfMarker[] markers = shelves
            .SelectMany(item => item.Markers).ToArray();
        MegastationMegaShelfTruss[] trusses = shelves
            .SelectMany(item => item.Trusses).ToArray();
        float[] widths = shelves.Select(item => item.Body.Size.X).ToArray();
        float[] projections = shelves.Select(item => item.Body.Size.Z).ToArray();
        float[] thicknesses = shelves.Select(item => item.Body.Size.Y).ToArray();
        string summary = string.Join('/', shelves.Select(item =>
            $"{item.Identity}[{item.Family}]@{string.Join('+', item.HostWallIdentities)}:" +
            $"{F(item.Body.Size.X)}x{F(item.Body.Size.Z)}x{F(item.Body.Size.Y)}:" +
            $"elev={F(Vector3.Dot(item.Body.Centre + item.Body.Up * item.Body.Size.Y * .5f, interior.PortalUp))}:" +
            $"band={item.VerticalBand}:usage={item.Usage}:support={item.SupportingFaces.Length}:" +
            $"trusses={item.Trusses.Count}"));
        string signature = Signature(seed, shelves);
        int trussTriangles = trusses.Sum(truss => EstimateTrussTriangles(truss.Spec));
        var diagnostics = new MegastationMegaShelfDiagnostics(
            AlgorithmVersion,
            candidates,
            shelves.Count,
            supportRejects,
            structuralRejects,
            arrivalRejects,
            overlapRejects,
            separationRejects,
            operatingClearanceRejects,
            landingDowngrades,
            fullSpanFlightClearanceRejects,
            compositionRejects,
            cantileverCandidates,
            cornerCandidates,
            fullSpanCandidates,
            cornerViableCandidates,
            fullSpanViableCandidates,
            shelves.Count(shelf => shelf.Family == MegastationMegaShelfFamily.Cantilever),
            shelves.Count(shelf => shelf.Family == MegastationMegaShelfFamily.Corner),
            shelves.Count(shelf => shelf.Family == MegastationMegaShelfFamily.FullSpan),
            lowCandidates,
            midCandidates,
            highCandidates,
            shelves.Count(shelf => shelf.VerticalBand == MegastationMegaShelfVerticalBand.Low),
            shelves.Count(shelf => shelf.VerticalBand == MegastationMegaShelfVerticalBand.Mid),
            shelves.Count(shelf => shelf.VerticalBand == MegastationMegaShelfVerticalBand.High),
            Min(widths), Max(widths),
            Min(projections), Max(projections),
            Min(thicknesses), Max(thicknesses),
            shelves.Count * 12,
            shelves.Count * 12,
            markers.Length,
            markers.Count(marker => marker.IsCorner),
            markers.Count(marker => marker.IsLowerEdge),
            trusses.Length,
            trusses.Count(truss => truss.Spec.CrossSection == StructuralTrussCrossSection.Box),
            trusses.Count(truss => truss.Spec.CrossSection == StructuralTrussCrossSection.Triangular),
            trussTriangles,
            trusses.Where(truss => truss.CastsStellarShadow)
                .Sum(truss => EstimateTrussTriangles(truss.Spec)),
            summary,
            signature,
            bookcaseDiscovery.Arrangement?.Layout ?? MegastationShelfMacroLayout.None,
            bookcaseDiscovery.Arrangement is null ? 0 : 1,
            bookcaseDiscovery.Levels.Count,
            bookcaseDiscovery.InsufficientHeightRejectCount,
            bookcaseDiscovery.ArrivalRejectCount,
            bookcaseDiscovery.WallContinuityRejectCount,
            bookcaseDiscovery.StructuralRejectCount,
            bookcaseDiscovery.SymmetryRejectCount,
            bookcaseDiscovery.Arrangement?.Summary ?? "Shelf Macro Layout: None");
        MegastationBookcaseArrangement[] bookcases = bookcaseDiscovery.Arrangement is null
            ? [] : [bookcaseDiscovery.Arrangement];
        return new(AlgorithmVersion, seed, shelves, solids, landingSurfaces, markers,
            trusses, bookcases, diagnostics);
    }

    private static MegastationMegaShelf CreateShelf(
        ShelfCandidate candidate,
        IReadOnlyList<MegastationMegaShelfMarker> markers,
        IReadOnlyList<MegastationMegaShelfTruss> trusses)
        => new(
            candidate.Identity,
            candidate.Family,
            candidate.PrimaryWall.Identity,
            candidate.HostWalls.Select(wall => wall.Identity).ToArray(),
            candidate.SupportingFaces,
            candidate.Body,
            candidate.LandingSurface,
            candidate.LandingSurface is null
                ? MegastationMegaShelfUsage.Structural
                : MegastationMegaShelfUsage.LandingCapable,
            candidate.VerticalBand,
            candidate.ClearanceVolume,
            Enum.GetValues<MegastationMegaShelfSurfaceRole>(),
            markers,
            trusses,
            candidate.Seed,
            ExposedCornerPoint(candidate.Body));

    private static CandidateDiscovery DiscoverCornerCandidates(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingSiteStructuralClearance clearance,
        IReadOnlyList<MegastationBayWallSurface> walls,
        int seed,
        float activeDensity,
        CancellationToken cancellationToken)
    {
        var result = new CandidateDiscovery();
        MegastationBayWallSurface[] rootWalls = walls.Where(wall =>
            wall.Kind is MegastationBayWallKind.Rear or MegastationBayWallKind.Entrance)
            .ToArray();
        if (rootWalls.Length == 0)
            return result;
        Vector3 right = Vector3.Normalize(interior.PortalRight);
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 inward = Vector3.Normalize(-interior.OutwardNormal);
        (float rMinimum, float rMaximum) = ProjectionSpan(interior.CavityEnvelope, right);
        (float uMinimum, float uMaximum) = ProjectionSpan(interior.CavityEnvelope, up);
        (float dMinimum, float dMaximum) = ProjectionSpan(interior.CavityEnvelope, inward);
        float cavityWidth = rMaximum - rMinimum;
        float cavityDepth = dMaximum - dMinimum;

        foreach (MegastationBayWallSurface rootWall in rootWalls)
        foreach (MegastationBayWallSurface side in walls.Where(wall =>
                     wall.Kind is MegastationBayWallKind.Left or MegastationBayWallKind.Right))
        for (int opportunity = 0; opportunity < 3; opportunity++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.CandidateCount++;
            result.CornerCandidateCount++;
            int candidateSeed = MegastationSeed.Derive(seed,
                $"corner:{rootWall.Identity}:{side.Identity}:opportunity:{opportunity}");
            MegastationMegaShelfVerticalBand band = VerticalBandForOpportunity(
                opportunity, MegastationSeed.Derive(seed,
                    $"corner:{rootWall.Identity}:{side.Identity}"));
            result.CountBand(band);
            float width = MathF.Min(cavityWidth - 48f,
                MathHelper.Lerp(150f, MathF.Min(330f, cavityWidth * .58f),
                    Unit(candidateSeed, "width")));
            float depth = MathF.Min(cavityDepth - 48f,
                MathHelper.Lerp(130f, MathF.Min(285f, cavityDepth * .52f),
                    Unit(candidateSeed, "depth")));
            float thickness = ShelfThickness(
                MegastationMegaShelfFamily.Corner, width, depth, candidateSeed);
            float topMinimum = uMinimum + MinimumFloorSeparation + thickness;
            float topMaximum = uMaximum - MinimumCeilingSeparation;
            if (width < 128f || depth < 112f || topMinimum >= topMaximum)
            {
                result.SupportRejectCount++;
                continue;
            }
            (float bandMinimum, float bandMaximum) = VerticalBandInterval(
                topMinimum, topMaximum, band, candidateSeed);
            float top = MathHelper.Lerp(bandMinimum, bandMaximum,
                MathHelper.Lerp(.08f, .92f, Unit(candidateSeed, "height")));
            bool left = side.Kind == MegastationBayWallKind.Left;
            float rCentre = left ? rMinimum + width * .5f : rMaximum - width * .5f;
            bool rear = rootWall.Kind == MegastationBayWallKind.Rear;
            float dCentre = rear ? dMaximum - depth * .5f : dMinimum + depth * .5f;
            Vector3 centre = right * rCentre + inward * dCentre
                + up * (top - thickness * .5f);
            string identity = $"mega-shelf:corner:{rootWall.Kind.ToString().ToLowerInvariant()}:" +
                $"{side.Kind.ToString().ToLowerInvariant()}:" +
                $"{opportunity:00}";
            float cornerClip = MathHelper.Lerp(5f, 10f, Unit(candidateSeed, "corner-clip"));
            int exposedSide = Vector3.Dot(side.Normal, rootWall.Right) > 0f ? 1 : -1;
            var body = new MegastationInteriorStructuralSolid(
                $"{identity}/body", MegastationInteriorStructuralRole.MegaShelf,
                centre, new(width, thickness, depth), rootWall.Right, up, rootWall.Normal,
                true, true, cornerClip, exposedSide);
            MegastationBayWallSurface[] hosts = [rootWall, side];
            if (!TryFindRootSupport(body, rootWall, occupancy.Grid, topology, out var rearFaces)
                || !TryFindRootSupport(body, side, occupancy.Grid, topology, out var sideFaces))
            {
                result.SupportRejectCount++;
                continue;
            }
            BoundaryFaceKey[] support = rearFaces.Concat(sideFaces).Distinct().Order().ToArray();
            AddCandidateResult(result, TryCreateCandidate(interior, clearance,
                MegastationMegaShelfFamily.Corner, identity, hosts, support, body, band,
                candidateSeed, activeDensity * .85f));
        }
        return result;
    }

    private static CandidateDiscovery DiscoverFullSpanCandidates(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingSiteStructuralClearance clearance,
        IReadOnlyList<MegastationBayWallSurface> walls,
        int seed,
        float activeDensity,
        CancellationToken cancellationToken)
    {
        var result = new CandidateDiscovery();
        MegastationBayWallSurface? left = walls.FirstOrDefault(
            wall => wall.Kind == MegastationBayWallKind.Left);
        MegastationBayWallSurface? rightWall = walls.FirstOrDefault(
            wall => wall.Kind == MegastationBayWallKind.Right);
        if (left is null || rightWall is null)
            return result;
        Vector3 right = Vector3.Normalize(interior.PortalRight);
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 inward = Vector3.Normalize(-interior.OutwardNormal);
        (float rMinimum, float rMaximum) = ProjectionSpan(interior.CavityEnvelope, right);
        (float uMinimum, float uMaximum) = ProjectionSpan(interior.CavityEnvelope, up);
        (float dMinimum, float dMaximum) = ProjectionSpan(interior.CavityEnvelope, inward);
        float span = rMaximum - rMinimum;
        float cavityDepth = dMaximum - dMinimum;
        int familySeed = MegastationSeed.Derive(seed, "full-span:left-right");
        OpposingSupportPatch[] supportPatches = FindOpposingSupportPatches(
            left, rightWall, right, inward, up, occupancy.Grid, topology);

        for (int opportunity = 0; opportunity < 30; opportunity++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.CandidateCount++;
            result.FullSpanCandidateCount++;
            int candidateSeed = MegastationSeed.Derive(
                familySeed, $"opportunity:{opportunity}");
            MegastationMegaShelfVerticalBand band = VerticalBandForOpportunity(
                opportunity, familySeed);
            result.CountBand(band);
            float depth = MathF.Min(cavityDepth * .46f,
                MathHelper.Lerp(64f, 160f, Unit(candidateSeed, "depth")));
            float thickness = ShelfThickness(
                MegastationMegaShelfFamily.FullSpan, span, depth, candidateSeed);
            float topMinimum = uMinimum + MinimumFloorSeparation + thickness;
            float topMaximum = uMaximum - MinimumCeilingSeparation;
            if (span < 240f || depth < 60f || topMinimum >= topMaximum)
            {
                result.SupportRejectCount++;
                continue;
            }
            (float bandMinimum, float bandMaximum) = VerticalBandInterval(
                topMinimum, topMaximum, band, familySeed);
            OpposingSupportPatch[] eligiblePatches = supportPatches
                .Where(patch => patch.SpanLength >= MathF.Max(240f, span * .68f))
                .Where(patch => patch.Width >= 24f && patch.Height >= 4f)
                .Where(patch => patch.CentreDepth >= dMinimum + cavityDepth * .64f)
                .Where(patch => patch.CentreDepth >= dMinimum + depth * .5f + 12f
                    && patch.CentreDepth <= dMaximum - depth * .5f - 12f)
                .Where(patch => patch.CentreHeight + thickness * .5f >= bandMinimum
                    && patch.CentreHeight + thickness * .5f <= bandMaximum)
                .OrderBy(patch => patch.Identity, StringComparer.Ordinal)
                .ToArray();
            if (eligiblePatches.Length == 0)
            {
                result.SupportRejectCount++;
                continue;
            }
            OpposingSupportPatch patch = eligiblePatches[PositiveMod(
                MegastationSeed.Derive(candidateSeed, "support-patch"),
                eligiblePatches.Length)];
            float top = patch.CentreHeight + thickness * .5f;
            if (!FullSpanLeavesNavigablePassage(
                    uMinimum, uMaximum, top, thickness))
            {
                result.FlightClearanceRejectCount++;
                continue;
            }
            float dCentre = patch.CentreDepth;
            string identity = $"mega-shelf:full-span:left-right:{opportunity:00}";
            var body = new MegastationInteriorStructuralSolid(
                $"{identity}/body", MegastationInteriorStructuralRole.MegaShelf,
                right * patch.SpanCentre + inward * dCentre
                    + up * (top - thickness * .5f),
                new(patch.SpanLength, thickness, depth), right, up, inward, true, true);
            MegastationBayWallSurface[] hosts = [left, rightWall];
            float attachmentWidth = MathF.Min(
                MathHelper.Clamp(depth * .30f, 24f, 44f), patch.Width * .9f);
            if (attachmentWidth < 20f)
            {
                result.SupportRejectCount++;
                continue;
            }
            if (!TryFindRootSupport(body, left, occupancy.Grid, topology,
                    out BoundaryFaceKey[] leftFaces, depth, thickness)
                || !TryFindRootSupport(body, rightWall, occupancy.Grid, topology,
                    out BoundaryFaceKey[] rightFaces, depth, thickness))
            {
                result.SupportRejectCount++;
                continue;
            }
            BoundaryFaceKey[] support = leftFaces.Concat(rightFaces)
                .Distinct().Order().ToArray();
            AddCandidateResult(result, TryCreateCandidate(interior, clearance,
                MegastationMegaShelfFamily.FullSpan, identity, hosts, support, body, band,
                candidateSeed, activeDensity * .15f));
        }
        return result;
    }

    private static CandidateBuildResult TryCreateCandidate(
        MegastationInteriorPlan interior,
        MegastationLandingSiteStructuralClearance clearance,
        MegastationMegaShelfFamily family,
        string identity,
        IReadOnlyList<MegastationBayWallSurface> hostWalls,
        BoundaryFaceKey[] supportingFaces,
        MegastationInteriorStructuralSolid body,
        MegastationMegaShelfVerticalBand band,
        int candidateSeed,
        float activeProbability)
    {
        if (interior.ArrivalManeuverExclusion.IntersectsOrientedBox(
                body.Centre, body.Right, body.Up, body.Forward, body.Size))
            return new(null, CandidateReject.Arrival, false);
        if (!clearance.IsOrientedBoxClear(
                body.Centre, body.Right, body.Up, body.Forward, body.Size, out _))
            return new(null, CandidateReject.Structural, false);

        MegastationLandingSurface proposedLandingSurface = CreateLandingSurface(
            interior, identity, body);
        bool landingCapable = MegastationLandingDistrictPlanner.CanHostMinimumSite(
            proposedLandingSurface.UsableSize);
        bool downgraded = false;
        if (landingCapable && !OperatingVolumeIsClear(
                clearance, body, hostWalls, landing: true))
        {
            landingCapable = false;
            downgraded = true;
        }
        if (!landingCapable && !OperatingVolumeIsClear(
                clearance, body, hostWalls, landing: false))
            return new(null, CandidateReject.Operating, downgraded);

        MegastationMegaShelfClearanceVolume clearanceVolume = CreateClearanceVolume(
            body, hostWalls, landingCapable);
        bool active = Unit(candidateSeed, "composition-presence")
            < MathHelper.Clamp(activeProbability, 0f, 1f);
        return new(new ShelfCandidate(
            identity, family, hostWalls[0], hostWalls, supportingFaces, body,
            landingCapable ? proposedLandingSurface : null,
            clearanceVolume, band, candidateSeed,
            Unit(candidateSeed, "composition-priority"), active),
            CandidateReject.None, downgraded);
    }

    internal static MegastationLandingSurface CreateLandingSurface(
        MegastationInteriorPlan interior,
        string identity,
        MegastationInteriorStructuralSolid body)
    {
        Vector3 canonicalRight = Vector3.Normalize(interior.PortalRight);
        Vector3 canonicalForward = Vector3.Normalize(interior.OutwardNormal);
        Vector2 usableSize = LandingUsableSize(body, canonicalRight, canonicalForward);
        return new(
            $"{identity}/landing-surface",
            body.Centre + body.Up * (body.Size.Y * .5f),
            body.Up,
            canonicalRight,
            canonicalForward,
            usableSize,
            LandingEdgeMargin,
            body.Identity,
            IsMainFloor: false);
    }

    internal static Vector2 LandingUsableSize(
        MegastationInteriorStructuralSolid body,
        Vector3 canonicalRight,
        Vector3 canonicalForward)
    {
        float canonicalWidth = MathF.Abs(Vector3.Dot(body.Right, canonicalRight)) * body.Size.X
            + MathF.Abs(Vector3.Dot(body.Forward, canonicalRight)) * body.Size.Z;
        float canonicalDepth = MathF.Abs(Vector3.Dot(body.Right, canonicalForward)) * body.Size.X
            + MathF.Abs(Vector3.Dot(body.Forward, canonicalForward)) * body.Size.Z;
        // A centred rectangle reduced on both local axes is the largest simple
        // conservative support that cannot enter the clipped outer-corner triangle.
        float clipAllowance = body.ExposedCornerClip;
        return new(
            MathF.Max(0f, canonicalWidth - LandingEdgeMargin * 2f - clipAllowance),
            MathF.Max(0f, canonicalDepth - LandingEdgeMargin * 2f - clipAllowance));
    }

    private static void AddCandidateResult(
        CandidateDiscovery discovery,
        CandidateBuildResult result)
    {
        if (result.Candidate is { } candidate)
            discovery.Candidates.Add(candidate);
        if (result.Downgraded)
            discovery.LandingDowngradeCount++;
        switch (result.Reject)
        {
            case CandidateReject.Structural: discovery.StructuralRejectCount++; break;
            case CandidateReject.Arrival: discovery.ArrivalRejectCount++; break;
            case CandidateReject.Operating: discovery.OperatingRejectCount++; break;
        }
    }

    internal static bool FullSpanLeavesNavigablePassage(
        float cavityMinimum,
        float cavityMaximum,
        float shelfTop,
        float shelfThickness)
    {
        float required = MathF.Max(
            SupportedShipEnvelopeStandards.LargeHeight,
            SupportedShipEnvelopeStandards.LargeWidth) + 84f;
        float below = shelfTop - shelfThickness - cavityMinimum;
        float above = cavityMaximum - shelfTop;
        return below >= required || above >= required;
    }

    internal static bool TryFindRootSupport(
        MegastationInteriorStructuralSolid body,
        MegastationBayWallSurface wall,
        SliceGrid grid,
        BoundaryTopology topology,
        out BoundaryFaceKey[] supportingFaces,
        float? attachmentWidth = null,
        float? attachmentHeight = null)
    {
        float normalExtent = MathF.Abs(Vector3.Dot(body.Right, wall.Normal))
                * body.Size.X * .5f
            + MathF.Abs(Vector3.Dot(body.Up, wall.Normal)) * body.Size.Y * .5f
            + MathF.Abs(Vector3.Dot(body.Forward, wall.Normal)) * body.Size.Z * .5f;
        Vector3 rootPlanePoint = body.Centre - wall.Normal * normalExtent;
        float planeShift = Vector3.Dot(rootPlanePoint - wall.Centre, wall.Normal);
        MegastationBayWallSurface actualRootPlane = wall with
        {
            Centre = wall.Centre + wall.Normal * planeShift,
        };
        Vector2 rootCentre = new(
            Vector3.Dot(body.Centre - actualRootPlane.Centre, wall.Right),
            Vector3.Dot(body.Centre - actualRootPlane.Centre, wall.Up));
        float rootWidth = attachmentWidth ??
            (MathF.Abs(Vector3.Dot(body.Right, wall.Right)) * body.Size.X
            + MathF.Abs(Vector3.Dot(body.Forward, wall.Right)) * body.Size.Z);
        return TryFindExactSupport(actualRootPlane, rootCentre,
            new(rootWidth, attachmentHeight ?? body.Size.Y), grid, topology,
            out supportingFaces);
    }

    private static (float Minimum, float Maximum) ProjectionSpan(
        MegastationInteriorVolume volume,
        Vector3 axis)
    {
        Vector3 minimum = volume.Minimum;
        Vector3 maximum = volume.Maximum;
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float x in new[] { minimum.X, maximum.X })
        foreach (float y in new[] { minimum.Y, maximum.Y })
        foreach (float z in new[] { minimum.Z, maximum.Z })
        {
            float value = Vector3.Dot(new(x, y, z), axis);
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }
        return (min, max);
    }

    private static OpposingSupportPatch[] FindOpposingSupportPatches(
        MegastationBayWallSurface first,
        MegastationBayWallSurface second,
        Vector3 spanAxis,
        Vector3 horizontalAxis,
        Vector3 up,
        SliceGrid grid,
        BoundaryTopology topology)
    {
        SupportPatch[] FirstPatches() => Patches(first);
        SupportPatch[] SecondPatches() => Patches(second);

        SupportPatch[] Patches(MegastationBayWallSurface wall)
            => topology.Faces
                .Where(face => face.SpaceKind
                        == MegastationBoundarySpaceKind.InteriorBoundary
                    && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction),
                        wall.Normal) > .9999f)
                .Select(face => (face.Key, Points: face.Vertices.Select(vertex =>
                    BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
                .Select(item => new SupportPatch(
                    item.Key,
                    Vector3.Dot(item.Points[0], spanAxis),
                    item.Points.Min(point => Vector3.Dot(point, horizontalAxis)),
                    item.Points.Max(point => Vector3.Dot(point, horizontalAxis)),
                    item.Points.Min(point => Vector3.Dot(point, up)),
                    item.Points.Max(point => Vector3.Dot(point, up))))
                .ToArray();

        var patches = new List<OpposingSupportPatch>();
        foreach (SupportPatch a in FirstPatches())
        foreach (SupportPatch b in SecondPatches())
        {
            float minimumDepth = MathF.Max(a.MinimumHorizontal, b.MinimumHorizontal);
            float maximumDepth = MathF.Min(a.MaximumHorizontal, b.MaximumHorizontal);
            float minimumHeight = MathF.Max(a.MinimumHeight, b.MinimumHeight);
            float maximumHeight = MathF.Min(a.MaximumHeight, b.MaximumHeight);
            if (maximumDepth <= minimumDepth || maximumHeight <= minimumHeight)
                continue;
            float spanMinimum = MathF.Min(a.Plane, b.Plane);
            float spanMaximum = MathF.Max(a.Plane, b.Plane);
            patches.Add(new(
                $"{a.Face}:{b.Face}",
                a.Face,
                b.Face,
                (spanMinimum + spanMaximum) * .5f,
                spanMaximum - spanMinimum,
                (minimumDepth + maximumDepth) * .5f,
                maximumDepth - minimumDepth,
                (minimumHeight + maximumHeight) * .5f,
                maximumHeight - minimumHeight));
        }
        return patches.OrderBy(patch => patch.Identity, StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<MegastationMegaShelfTruss> BuildTrusses(
        string shelfIdentity,
        MegastationInteriorStructuralSolid body,
        int seed,
        bool force = false,
        MegastationMegaShelfFamily family = MegastationMegaShelfFamily.Cantilever,
        StructuralTrussCrossSection? forcedCrossSection = null)
    {
        int count = family switch
        {
            MegastationMegaShelfFamily.FullSpan => 2
                + (body.Size.X > 850f && Unit(seed, "exceptional-third") > .86f ? 1 : 0),
            MegastationMegaShelfFamily.Corner => 2,
            MegastationMegaShelfFamily.Bookcase => 3,
            _ => 3,
        };
        StructuralTrussCrossSection trussFamily = forcedCrossSection
            ?? (Unit(seed, "cross-section-family") < .55f
                ? StructuralTrussCrossSection.Box
                : StructuralTrussCrossSection.Triangular);
        var trusses = new List<MegastationMegaShelfTruss>(count);
        float underside = -body.Size.Y * .5f;

        void Add(
            int index,
            StructuralTrussCrossSection crossSection,
            float length,
            float width,
            float height,
            Vector3 localCentre,
            Vector3 localWidth,
            Vector3 localUp,
            Vector3 localLength)
        {
            int child = MegastationSeed.Derive(seed, $"truss:{index}");
            float chord = MathHelper.Lerp(.62f, .92f, Unit(child, "chord"));
            float brace = MathHelper.Lerp(.30f, MathF.Min(.56f, chord), Unit(child, "brace"));
            float targetBay = MathHelper.Lerp(8f, 13f, Unit(child, "bay"));
            Vector3 centre = body.Centre
                + body.Right * localCentre.X
                + body.Up * localCentre.Y
                + body.Forward * localCentre.Z;
            Matrix frame = new(
                localWidth.X, localWidth.Y, localWidth.Z, 0f,
                localUp.X, localUp.Y, localUp.Z, 0f,
                localLength.X, localLength.Y, localLength.Z, 0f,
                centre.X, centre.Y, centre.Z, 1f);
            var spec = new StructuralTrussSpec(
                length, width, height, crossSection, chord, brace, targetBay, frame);
            trusses.Add(new(
                $"{shelfIdentity}/truss:{index}", spec,
                CastsStellarShadow: true,
                // The shelf slab is already the authoritative direct-light occluder.
                // Keeping open lattice out avoids replacing it with a phantom solid box.
                CastsStaticArtificialShadow: false));
        }

        if (family == MegastationMegaShelfFamily.FullSpan)
        {
            float edgeInset = MathHelper.Clamp(body.Size.Z * .035f, 2f, 5f);
            for (int index = 0; index < count; index++)
            {
                int child = MegastationSeed.Derive(seed, $"span:{index}");
                float height = MathHelper.Lerp(5.8f, 8.8f, Unit(child, "height"));
                float width = MathHelper.Lerp(4.6f, 7.4f, Unit(child, "width"));
                float z = MathHelper.Lerp(
                    -body.Size.Z * .5f + edgeInset,
                    body.Size.Z * .5f - edgeInset,
                    index / (count - 1f));
                Add(index, trussFamily,
                    body.Size.X - 16f, width, height,
                    new(0f, underside - height * .5f + .32f, z),
                    -body.Forward, body.Up, body.Right);
            }
            return trusses;
        }

        if (family == MegastationMegaShelfFamily.Bookcase)
        {
            float edgeInset = MathHelper.Clamp(
                MathF.Min(body.Size.X, body.Size.Z) * .025f, 2f, 5f);
            for (int index = 0; index < 3; index++)
            {
                int child = MegastationSeed.Derive(seed, $"bookcase-pi:{index}");
                float height = MathHelper.Lerp(5.8f, 8.6f, Unit(child, "height"));
                float width = MathHelper.Lerp(4.4f, 7.2f, Unit(child, "width"));
                if (index == 0)
                {
                    Add(index, trussFamily, body.Size.X - edgeInset * 2f,
                        width, height,
                        new(0f, underside - height * .5f + .32f,
                            body.Size.Z * .5f - edgeInset),
                        -body.Forward, body.Up, body.Right);
                }
                else
                {
                    float x = (index == 1 ? -1f : 1f)
                        * (body.Size.X * .5f - edgeInset);
                    Add(index, trussFamily, body.Size.Z - edgeInset * 2f,
                        width, height,
                        new(x, underside - height * .5f + .32f, 0f),
                        body.Right, body.Up, body.Forward);
                }
            }
            return trusses;
        }

        if (family == MegastationMegaShelfFamily.Corner)
        {
            float edgeInset = MathHelper.Clamp(
                MathF.Min(body.Size.X, body.Size.Z) * .025f, 2f, 5f);
            float clip = body.ExposedCornerClip;
            float exposedSign = body.ExposedCornerRightSign == 0
                ? 1f : body.ExposedCornerRightSign;

            int frontSeed = MegastationSeed.Derive(seed, "corner-front");
            float frontHeight = MathHelper.Lerp(5.8f, 8.6f, Unit(frontSeed, "height"));
            float frontWidth = MathHelper.Lerp(4.4f, 7.2f, Unit(frontSeed, "width"));
            Add(0, trussFamily,
                body.Size.X - clip - edgeInset * 2f, frontWidth, frontHeight,
                new(-exposedSign * clip * .5f,
                    underside - frontHeight * .5f + .32f,
                    body.Size.Z * .5f - edgeInset),
                -body.Forward, body.Up, body.Right);

            int sideSeed = MegastationSeed.Derive(seed, "corner-side");
            float sideHeight = MathHelper.Lerp(5.8f, 8.6f, Unit(sideSeed, "height"));
            float sideWidth = MathHelper.Lerp(4.4f, 7.2f, Unit(sideSeed, "width"));
            Add(1, trussFamily,
                body.Size.Z - clip - edgeInset * 2f, sideWidth, sideHeight,
                new(exposedSign * (body.Size.X * .5f - edgeInset),
                    underside - sideHeight * .5f + .32f,
                    -clip * .5f),
                body.Right, body.Up, body.Forward);
            return trusses;
        }

        float edge = MathHelper.Clamp(MathF.Min(body.Size.X, body.Size.Z) * .025f, 2f, 5f);
        float longitudinalLength = body.Size.Z - edge * 2f;
        for (int index = 0; index < 2; index++)
        {
            int child = MegastationSeed.Derive(seed, $"longitudinal:{index}");
            float height = MathHelper.Lerp(5.8f, 8.6f, Unit(child, "height"));
            float width = MathHelper.Lerp(4.4f, 7.2f, Unit(child, "width"));
            float x = (index == 0 ? -1f : 1f) * (body.Size.X * .5f - edge);
            Add(index, trussFamily,
                longitudinalLength, width, height,
                new(x, underside - height * .5f + .32f, 0f),
                body.Right, body.Up, body.Forward);
        }

        for (int index = 2; index < count; index++)
        {
            int child = MegastationSeed.Derive(seed, $"transverse:{index}");
            float height = MathHelper.Lerp(5.2f, 7.8f, Unit(child, "height"));
            float width = MathHelper.Lerp(4.2f, 6.8f, Unit(child, "width"));
            float z = body.Size.Z * .5f - edge;
            Add(index, trussFamily,
                body.Size.X - 16f, width, height,
                new(0f, underside - height * .5f + .32f, z),
                -body.Forward, body.Up, body.Right);
        }
        return trusses;
    }

    internal static float ShelfThickness(
        MegastationMegaShelfFamily family,
        float span,
        float projection,
        int seed)
    {
        float scale = MathF.Sqrt(MathF.Max(1f, span * projection));
        float scaleFactor = MathHelper.Clamp((scale - 120f) / 420f, 0f, 1f);
        float supportFactor = family == MegastationMegaShelfFamily.Cantilever ? .65f : 0f;
        return MathHelper.Clamp(1.45f + scaleFactor * 2.25f + supportFactor
            + Unit(seed, "thickness") * .65f, 1.25f, 5f);
    }

    internal static Vector3? ExposedCornerPoint(MegastationInteriorStructuralSolid body)
    {
        if (body.ExposedCornerClip <= 0f || body.ExposedCornerRightSign == 0)
            return null;
        float sign = body.ExposedCornerRightSign;
        return body.Centre + body.Up * (body.Size.Y * .5f)
            + body.Right * sign * (body.Size.X * .5f - body.ExposedCornerClip * .5f)
            + body.Forward * (body.Size.Z * .5f - body.ExposedCornerClip * .5f);
    }

    internal static int EstimateTrussTriangles(StructuralTrussSpec spec)
    {
        int chords = spec.CrossSection == StructuralTrussCrossSection.Box ? 4 : 3;
        int bays = Math.Max(1, (int)MathF.Round(spec.Length / spec.TargetBayLength));
        int members = chords + (bays + 1) * chords + bays * chords;
        return members * 12;
    }

    internal static MegastationMegaShelfFaceColours FaceColours(
        Color stationTint,
        Color secondaryTint)
    {
        Color basis = ProceduralMaterialCpuGenerator.Blend(
            stationTint, secondaryTint, .12f);
        return new(
            ScaleWithFloor(basis, 1.16f, 54),
            ScaleWithFloor(basis, 1.02f, 42),
            ScaleWithFloor(basis, .78f, 28));
    }

    private static Color ScaleWithFloor(Color colour, float scale, byte floor)
        => new(
            (byte)Math.Clamp((int)MathF.Round(colour.R * scale), floor, 255),
            (byte)Math.Clamp((int)MathF.Round(colour.G * scale), floor, 255),
            (byte)Math.Clamp((int)MathF.Round(colour.B * scale), floor, 255),
            colour.A);

    internal static IReadOnlyList<MegastationMegaShelfMarker> BuildMarkers(
        string shelfIdentity,
        MegastationInteriorStructuralSolid body,
        int seed,
        MegastationMegaShelfFamily family = MegastationMegaShelfFamily.Cantilever,
        IReadOnlyList<MegastationBayWallSurface>? hostWalls = null)
    {
        var markers = new List<MegastationMegaShelfMarker>();
        float width = body.Size.X;
        float thickness = body.Size.Y;
        float projection = body.Size.Z;
        float markerHeight = MathHelper.Clamp(thickness * .075f, .65f, 1.15f);
        float markerDepth = .24f;
        float upperOffset = thickness * .18f;
        float lowerOffset = -thickness * .28f;
        Color colour = MarkerColour(seed);

        void Add(
            string identity,
            MegastationMegaShelfSurfaceRole role,
            Vector3 centre,
            Vector3 size,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            bool corner,
            bool lower)
        {
            // Marker boxes always receive a proper right-handed edge frame.
            // Callers supply semantic outward/up; no side may mirror geometry by
            // changing only one basis vector.
            forward = Vector3.Normalize(forward);
            up = Vector3.Normalize(up);
            right = Vector3.Normalize(Vector3.Cross(up, forward));
            float illumination = corner ? .72f : lower ? .54f : .62f;
            markers.Add(new(
                $"{shelfIdentity}/edge-marker:{identity}", role,
                centre, size, right, up, forward, colour, illumination, corner, lower));
        }

        int frontCount = Math.Clamp((int)MathF.Round(width / 36f), 4, 8);
        float frontSpacing = width / (frontCount + 1f);
        for (int index = 0; index < frontCount; index++)
        {
            int child = MegastationSeed.Derive(seed, $"front:{index}");
            float length = MathHelper.Clamp(7f + Unit(child, "length") * 3.5f,
                7f, frontSpacing * .48f);
            float x = -width * .5f + frontSpacing * (index + 1f);
            Vector3 centre = body.Centre + body.Right * x + body.Up * upperOffset
                + body.Forward * (projection * .5f + markerDepth * .22f);
            Add($"front:{index}", MegastationMegaShelfSurfaceRole.FrontEdge,
                centre, new(length, markerHeight, markerDepth),
                body.Right, body.Up, body.Forward, false, false);

            if (index % 2 == 1)
            {
                Vector3 lowerCentre = body.Centre + body.Right * x + body.Up * lowerOffset
                    + body.Forward * (projection * .5f + markerDepth * .22f);
                Add($"front-lower:{index}", MegastationMegaShelfSurfaceRole.FrontEdge,
                    lowerCentre, new(length * .78f, markerHeight * .82f, markerDepth),
                    body.Right, body.Up, body.Forward, false, true);
            }
        }

        float cornerLength = MathHelper.Clamp(width * .055f, 10f, 15f);
        float cornerInset = 2f;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 frontCentre = body.Centre
                + body.Right * side * (width * .5f - cornerLength * .5f - cornerInset)
                + body.Up * upperOffset
                + body.Forward * (projection * .5f + markerDepth * .22f);
            Add($"corner-front:{side}", MegastationMegaShelfSurfaceRole.FrontEdge,
                frontCentre, new(cornerLength, markerHeight * 1.18f, markerDepth),
                body.Right, body.Up, body.Forward, true, false);

            Vector3 sideNormal = body.Right * side;
            Vector3 sideAlong = -body.Forward * side;
            Vector3 sideCentre = body.Centre
                + sideNormal * (width * .5f + markerDepth * .22f)
                + body.Up * upperOffset
                + body.Forward * (projection * .5f - cornerLength * .5f - cornerInset);
            Add($"corner-side:{side}", MegastationMegaShelfSurfaceRole.SideEdge,
                sideCentre, new(cornerLength, markerHeight * 1.18f, markerDepth),
                sideAlong, body.Up, sideNormal, true, false);

            int sideCount = Math.Clamp((int)MathF.Round(projection / 58f), 3, 6);
            float sideSpacing = projection / (sideCount + 1f);
            for (int index = 0; index < sideCount; index++)
            {
                int child = MegastationSeed.Derive(seed, $"side:{side}:{index}");
                float length = MathHelper.Clamp(7f + Unit(child, "length") * 3f,
                    7f, sideSpacing * .46f);
                float z = -projection * .5f + sideSpacing * (index + 1f);
                Vector3 centre = body.Centre
                    + sideNormal * (width * .5f + markerDepth * .22f)
                    + body.Up * upperOffset
                    + body.Forward * z;
                Add($"side:{side}:{index}", MegastationMegaShelfSurfaceRole.SideEdge,
                    centre, new(length, markerHeight, markerDepth),
                    sideAlong, body.Up, sideNormal, false, false);
            }
        }
        if (family == MegastationMegaShelfFamily.Corner && hostWalls is not null)
        {
            int supportedSide = hostWalls.Any(wall =>
                    Vector3.Dot(wall.Normal, body.Right) > .999f)
                ? -1
                : 1;
            int exposedSide = -supportedSide;
            float clip = body.ExposedCornerClip;
            MegastationMegaShelfMarker[] exposed = markers.Where(marker =>
                    marker.SurfaceRole == MegastationMegaShelfSurfaceRole.FrontEdge
                    || marker.Identity.Contains($"side:{exposedSide}:",
                        StringComparison.Ordinal)
                    || marker.Identity.EndsWith($"side:{exposedSide}",
                        StringComparison.Ordinal))
                .Where(marker =>
                {
                    float x = Vector3.Dot(marker.Centre - body.Centre, body.Right)
                        * exposedSide;
                    float z = Vector3.Dot(marker.Centre - body.Centre, body.Forward);
                    if (marker.SurfaceRole == MegastationMegaShelfSurfaceRole.FrontEdge)
                        return x + marker.Size.X * .5f
                            <= body.Size.X * .5f - clip + .001f;
                    return z + marker.Size.X * .5f
                        <= body.Size.Z * .5f - clip + .001f;
                })
                .ToArray();
            if (clip > 0f)
            {
                Vector3 outward = Vector3.Normalize(
                    body.Right * exposedSide + body.Forward);
                Vector3 centre = body.Centre
                    + body.Right * exposedSide * (body.Size.X * .5f - clip * .5f)
                    + body.Forward * (body.Size.Z * .5f - clip * .5f)
                    + body.Up * upperOffset + outward * (markerDepth * .22f);
                float length = MathF.Max(3.5f, clip * 1.41421356f - 1.5f);
                Add("corner-chamfer", MegastationMegaShelfSurfaceRole.SideEdge,
                    centre, new(length, markerHeight * 1.18f, markerDepth),
                    Vector3.Zero, body.Up, outward, true, false);
                return exposed.Concat(markers.TakeLast(1)).ToArray();
            }
            return exposed;
        }
        if (family == MegastationMegaShelfFamily.FullSpan)
        {
            MegastationMegaShelfMarker[] front = markers.Where(marker =>
                    marker.SurfaceRole == MegastationMegaShelfSurfaceRole.FrontEdge
                    && !marker.IsCorner)
                .ToArray();
            MegastationMegaShelfMarker[] rear = front.Select(marker => marker with
            {
                Identity = $"{marker.Identity}:opposite-edge",
                Centre = marker.Centre - body.Forward
                    * (body.Size.Z + marker.Size.Z * .44f),
                Forward = -body.Forward,
                Right = Vector3.Normalize(Vector3.Cross(marker.Up, -body.Forward)),
            }).ToArray();
            return front.Concat(rear).ToArray();
        }
        return markers;
    }

    private static float CompositionDensity(
        MegastationInteriorPlan interior,
        IReadOnlyList<MegastationBayWallSurface> walls,
        int seed)
    {
        float wallArea = walls.Sum(wall => wall.Width * wall.Height);
        Vector3 cavity = interior.CavityEnvelope.Size;
        float scale = MathHelper.Clamp(
            MathF.Sqrt(MathF.Max(1f, wallArea)) / 1_350f, 0f, 1f);
        float volumeScale = MathHelper.Clamp(
            MathF.Pow(MathF.Max(1f, cavity.X * cavity.Y * cavity.Z), 1f / 3f) / 900f,
            0f, 1f);
        float stationVariation = MathHelper.Lerp(.78f, 1.22f,
            Unit(seed, "composition-density"));
        return MathHelper.Clamp(MathHelper.Lerp(.16f, .44f,
            scale * .65f + volumeScale * .35f) * stationVariation, .10f, .50f);
    }

    internal static MegastationMegaShelfVerticalBand VerticalBandForOpportunity(
        int opportunity, int wallSeed)
        => (MegastationMegaShelfVerticalBand)PositiveMod(
            opportunity + PositiveMod(wallSeed, 3), 3);

    internal static (float Minimum, float Maximum) VerticalBandInterval(
        float usableMinimum,
        float usableMaximum,
        MegastationMegaShelfVerticalBand band,
        int wallSeed)
    {
        float span = MathF.Max(0f, usableMaximum - usableMinimum);
        float lowBoundary = usableMinimum + span
            * MathHelper.Lerp(.30f, .37f, Unit(wallSeed, "low-band-boundary"));
        float highBoundary = usableMinimum + span
            * MathHelper.Lerp(.63f, .70f, Unit(wallSeed, "high-band-boundary"));
        return band switch
        {
            MegastationMegaShelfVerticalBand.Low => (usableMinimum, lowBoundary),
            MegastationMegaShelfVerticalBand.Mid => (lowBoundary, highBoundary),
            MegastationMegaShelfVerticalBand.High => (highBoundary, usableMaximum),
            _ => throw new ArgumentOutOfRangeException(nameof(band)),
        };
    }

    private static bool OperatingVolumeIsClear(
        MegastationLandingSiteStructuralClearance clearance,
        MegastationInteriorStructuralSolid body,
        IReadOnlyList<MegastationBayWallSurface> hostWalls,
        bool landing)
    {
        MegastationMegaShelfClearanceVolume volume = CreateOperatingVolume(
            body, hostWalls, landing);
        return clearance.IsOrientedBoxClear(
            volume.Centre, volume.Right, volume.Up, volume.Forward, volume.Size, out _);
    }

    internal static MegastationMegaShelfClearanceVolume CreateOperatingVolume(
        Vector3 topCentre,
        MegastationBayWallSurface wall,
        float width,
        float projection,
        bool landing)
    {
        var body = new MegastationInteriorStructuralSolid(
            "operating-volume/source", MegastationInteriorStructuralRole.MegaShelf,
            topCentre, new(width, 0f, projection), wall.Right, wall.Up, wall.Normal,
            true, true);
        return CreateOperatingVolume(body, [wall], landing);
    }

    internal static MegastationMegaShelfClearanceVolume CreateOperatingVolume(
        MegastationInteriorStructuralSolid body,
        IReadOnlyList<MegastationBayWallSurface> hostWalls,
        bool landing)
    {
        float height = landing ? 80f : 44f;
        float margin = landing ? 16f : 10f;
        HorizontalMargins margins = HorizontalMarginsFor(body, hostWalls, margin);
        Vector3 topCentre = body.Centre + body.Up * (body.Size.Y * .5f);
        return new(
            topCentre + body.Up * (height * .5f)
                + body.Right * ((margins.PositiveRight - margins.NegativeRight) * .5f)
                + body.Forward * ((margins.PositiveForward - margins.NegativeForward) * .5f),
            new(body.Size.X + margins.NegativeRight + margins.PositiveRight,
                height,
                body.Size.Z + margins.NegativeForward + margins.PositiveForward),
            body.Right, body.Up, body.Forward, landing);
    }

    private static MegastationMegaShelfClearanceVolume CreateClearanceVolume(
        MegastationInteriorStructuralSolid body,
        IReadOnlyList<MegastationBayWallSurface> hostWalls,
        bool landing)
    {
        float above = landing ? 80f : 44f;
        float below = landing ? 44f : 30f;
        float margin = landing ? 16f : 10f;
        HorizontalMargins margins = HorizontalMarginsFor(body, hostWalls, margin);
        return new(
            body.Centre + body.Up * ((above - below) * .5f)
                + body.Right * ((margins.PositiveRight - margins.NegativeRight) * .5f)
                + body.Forward * ((margins.PositiveForward - margins.NegativeForward) * .5f),
            new(body.Size.X + margins.NegativeRight + margins.PositiveRight,
                body.Size.Y + above + below,
                body.Size.Z + margins.NegativeForward + margins.PositiveForward),
            body.Right, body.Up, body.Forward, landing);
    }

    private static HorizontalMargins HorizontalMarginsFor(
        MegastationInteriorStructuralSolid body,
        IReadOnlyList<MegastationBayWallSurface> hostWalls,
        float margin)
    {
        bool negativeRightSupported = hostWalls.Any(wall =>
            Vector3.Dot(wall.Normal, body.Right) > .999f);
        bool positiveRightSupported = hostWalls.Any(wall =>
            Vector3.Dot(wall.Normal, body.Right) < -.999f);
        bool negativeForwardSupported = hostWalls.Any(wall =>
            Vector3.Dot(wall.Normal, body.Forward) > .999f);
        bool positiveForwardSupported = hostWalls.Any(wall =>
            Vector3.Dot(wall.Normal, body.Forward) < -.999f);
        return new(
            negativeRightSupported ? 0f : margin,
            positiveRightSupported ? 0f : margin,
            negativeForwardSupported ? 0f : margin,
            positiveForwardSupported ? 0f : margin);
    }

    private static float CompositionScore(
        ShelfCandidate candidate,
        IReadOnlyList<MegastationMegaShelf> accepted,
        MegastationInteriorPlan interior)
    {
        float score = candidate.Priority * 1.35f;
        score += candidate.LandingSurface is null ? 0f : .24f;
        score += candidate.Family switch
        {
            MegastationMegaShelfFamily.Corner => .26f,
            // Activation is deliberately uncommon; once a span is active it must
            // compete as the bay-defining event it is, before cantilevers consume
            // the same central operating volume.
            MegastationMegaShelfFamily.FullSpan => .50f,
            _ => 0f,
        };
        float cavityDepth = MathF.Max(1f,
            MathF.Abs(Vector3.Dot(interior.CavityEnvelope.Size,
                Abs(candidate.PrimaryWall.Normal))));
        float bodyDepth = MathF.Abs(Vector3.Dot(candidate.Body.Right,
                candidate.PrimaryWall.Normal)) * candidate.Body.Size.X
            + MathF.Abs(Vector3.Dot(candidate.Body.Forward,
                candidate.PrimaryWall.Normal)) * candidate.Body.Size.Z;
        score -= bodyDepth / cavityDepth * .28f;
        if (accepted.Count == 0)
            return score;

        if (accepted.All(shelf => !shelf.HostWallIdentities.Intersect(
                candidate.HostWalls.Select(wall => wall.Identity), StringComparer.Ordinal).Any()))
            score += .58f;
        if (accepted.All(shelf => shelf.VerticalBand != candidate.VerticalBand))
            score += .82f;
        float elevation = Vector3.Dot(candidate.Body.Centre, candidate.Body.Up);
        float nearestElevation = accepted.Min(shelf => MathF.Abs(elevation
            - Vector3.Dot(shelf.Body.Centre, shelf.Body.Up)));
        score += MathHelper.Clamp(nearestElevation / 180f, 0f, 1f) * .72f;
        float nearestDistance = accepted.Min(shelf =>
            Vector3.Distance(candidate.Body.Centre, shelf.Body.Centre));
        score += MathHelper.Clamp(nearestDistance / 420f, 0f, 1f) * .34f;
        if (candidate.Family == MegastationMegaShelfFamily.FullSpan)
            score -= accepted.Count * .18f;
        return score;
    }

    internal static bool ClearanceVolumesIntersect(
        MegastationMegaShelfClearanceVolume first,
        MegastationMegaShelfClearanceVolume second)
    {
        var volume = new MegastationInteriorMacroExclusionVolume(
            "shelf-clearance", MegastationInteriorMacroExclusionRole.EntranceArrivalManeuver,
            first.Centre, first.Size, first.Right, first.Up, first.Forward, Vector3.Zero);
        return volume.IntersectsOrientedBox(second.Centre, second.Right, second.Up,
            second.Forward, second.Size);
    }

    private static bool TrussFits(
        MegastationMegaShelfTruss truss,
        MegastationInteriorStructuralSolid ownBody,
        IReadOnlyList<MegastationMegaShelf> accepted,
        MegastationInteriorPlan interior,
        MegastationLandingSiteStructuralClearance clearance)
    {
        MegastationInteriorStructuralSolid solid = TrussSolid(truss);
        if (interior.ArrivalManeuverExclusion.IntersectsOrientedBox(
                solid.Centre, solid.Right, solid.Up, solid.Forward, solid.Size)
            || !clearance.IsOrientedBoxClear(solid.Centre, solid.Right, solid.Up,
                solid.Forward, solid.Size, out _))
            return false;
        return accepted.All(shelf =>
            !StructuralVolumesIntersect(shelf.Body, solid, 2f)
            && shelf.Trusses.All(existing =>
                !StructuralVolumesIntersect(TrussSolid(existing), solid, 1f)));
    }

    private static MegastationInteriorStructuralSolid TrussSolid(
        MegastationMegaShelfTruss truss)
    {
        Matrix transform = truss.Spec.Transform;
        return new(
            truss.Identity,
            MegastationInteriorStructuralRole.MegaShelfTruss,
            transform.Translation,
            new(truss.Spec.Width, truss.Spec.Height, truss.Spec.Length),
            new(transform.M11, transform.M12, transform.M13),
            new(transform.M21, transform.M22, transform.M23),
            new(transform.M31, transform.M32, transform.M33),
            truss.CastsStellarShadow,
            truss.CastsStaticArtificialShadow);
    }

    private static Color MarkerColour(int seed)
    {
        float variation = Unit(seed, "colour");
        return new Color(
            (byte)MathHelper.Lerp(224f, 242f, variation),
            (byte)MathHelper.Lerp(218f, 236f, variation),
            (byte)MathHelper.Lerp(194f, 218f, variation));
    }

    internal static bool TryFindExactSupport(
        MegastationBayWallSurface wall,
        Vector2 centre,
        Vector2 size,
        SliceGrid grid,
        BoundaryTopology topology,
        out BoundaryFaceKey[] supportingFaces)
    {
        float requestedMinX = centre.X - size.X * .5f;
        float requestedMaxX = centre.X + size.X * .5f;
        float requestedMinY = centre.Y - size.Y * .5f;
        float requestedMaxY = centre.Y + size.Y * .5f;
        var rectangles = topology.Faces
            .Where(face => face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction), wall.Normal) > .9999f)
            .Select(face => (Face: face, Points: face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
            .Where(item => MathF.Abs(Vector3.Dot(item.Points[0] - wall.Centre, wall.Normal)) < .01f)
            .Select(item => new SupportRectangle(
                item.Face.Key,
                item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Right)),
                item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Right)),
                item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Up)),
                item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Up))))
            .Where(item => item.MaximumX > requestedMinX && item.MinimumX < requestedMaxX
                && item.MaximumY > requestedMinY && item.MinimumY < requestedMaxY)
            .ToArray();
        if (rectangles.Length == 0)
        {
            supportingFaces = [];
            return false;
        }

        float[] xs = rectangles.SelectMany(item => new[]
            {
                MathHelper.Clamp(item.MinimumX, requestedMinX, requestedMaxX),
                MathHelper.Clamp(item.MaximumX, requestedMinX, requestedMaxX),
            })
            .Append(requestedMinX).Append(requestedMaxX).Distinct().Order().ToArray();
        float[] ys = rectangles.SelectMany(item => new[]
            {
                MathHelper.Clamp(item.MinimumY, requestedMinY, requestedMaxY),
                MathHelper.Clamp(item.MaximumY, requestedMinY, requestedMaxY),
            })
            .Append(requestedMinY).Append(requestedMaxY).Distinct().Order().ToArray();
        for (int ix = 0; ix < xs.Length - 1; ix++)
        for (int iy = 0; iy < ys.Length - 1; iy++)
        {
            if (xs[ix + 1] - xs[ix] < .001f || ys[iy + 1] - ys[iy] < .001f)
                continue;
            float x = (xs[ix] + xs[ix + 1]) * .5f;
            float y = (ys[iy] + ys[iy + 1]) * .5f;
            if (!rectangles.Any(item => x >= item.MinimumX - .001f
                    && x <= item.MaximumX + .001f && y >= item.MinimumY - .001f
                    && y <= item.MaximumY + .001f))
            {
                supportingFaces = [];
                return false;
            }
        }
        supportingFaces = rectangles.Select(item => item.Face).Distinct().Order().ToArray();
        return true;
    }

    internal static bool StructuralVolumesIntersect(
        MegastationInteriorStructuralSolid first,
        MegastationInteriorStructuralSolid second,
        float margin)
    {
        (Vector3 minA, Vector3 maxA) = Bounds(
            first.Centre, first.Right, first.Up, first.Forward,
            first.Size + new Vector3(margin * 2f));
        (Vector3 minB, Vector3 maxB) = Bounds(
            second.Centre, second.Right, second.Up, second.Forward,
            second.Size + new Vector3(margin * 2f));
        return minA.X < maxB.X && maxA.X > minB.X
            && minA.Y < maxB.Y && maxA.Y > minB.Y
            && minA.Z < maxB.Z && maxA.Z > minB.Z;
    }

    internal static (Vector3 Minimum, Vector3 Maximum) Bounds(
        Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 size)
    {
        Vector3 extent = Abs(right) * (size.X * .5f)
            + Abs(up) * (size.Y * .5f)
            + Abs(forward) * (size.Z * .5f);
        return (centre - extent, centre + extent);
    }

    private static Vector3 Abs(Vector3 value)
        => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));

    private static int PositiveMod(int value, int modulus)
        => ((value % modulus) + modulus) % modulus;

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static float Min(float[] values) => values.Length == 0 ? 0f : values.Min();
    private static float Max(float[] values) => values.Length == 0 ? 0f : values.Max();
    private static string F(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string Signature(int seed, IEnumerable<MegastationMegaShelf> shelves)
    {
        var text = new StringBuilder().Append(AlgorithmVersion).Append('|').Append(seed);
        foreach (MegastationMegaShelf shelf in shelves)
        {
            text.Append('|').Append(shelf.Identity).Append(':').Append(shelf.HostWallIdentity)
                .Append(':').Append(shelf.Family)
                .Append(':').AppendJoin('+', shelf.HostWallIdentities)
                .Append('@').Append(shelf.Body.Centre).Append(':').Append(shelf.Body.Size)
                .Append(':').Append(shelf.Usage)
                .Append(':').Append(shelf.VerticalBand)
                .Append(':').Append(shelf.ClearanceVolume.Centre)
                .Append(':').Append(shelf.ClearanceVolume.Size);
            if (shelf.LandingSurface is { } landingSurface)
                text.Append(':').Append(landingSurface.Centre)
                    .Append(':').Append(landingSurface.UsableSize);
            foreach (MegastationMegaShelfMarker marker in shelf.Markers)
                text.Append(':').Append(marker.Identity).Append('@').Append(marker.Centre)
                    .Append(':').Append(marker.Size).Append(':').Append(marker.Colour.PackedValue)
                    .Append(':').Append(marker.Illumination);
            foreach (MegastationMegaShelfTruss truss in shelf.Trusses)
                text.Append(':').Append(truss.Identity).Append('@')
                    .Append(truss.Spec.CrossSection).Append(':').Append(truss.Spec.Length)
                    .Append(':').Append(truss.Spec.Width).Append(':').Append(truss.Spec.Height)
                    .Append(':').Append(truss.Spec.ChordThickness)
                    .Append(':').Append(truss.Spec.BraceThickness)
                    .Append(':').Append(truss.Spec.TargetBayLength)
                    .Append(':').Append(truss.Spec.Transform);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private readonly record struct SupportRectangle(
        BoundaryFaceKey Face,
        float MinimumX,
        float MaximumX,
        float MinimumY,
        float MaximumY);

    private readonly record struct SupportPatch(
        BoundaryFaceKey Face,
        float Plane,
        float MinimumHorizontal,
        float MaximumHorizontal,
        float MinimumHeight,
        float MaximumHeight);

    private readonly record struct OpposingSupportPatch(
        string Identity,
        BoundaryFaceKey FirstFace,
        BoundaryFaceKey SecondFace,
        float SpanCentre,
        float SpanLength,
        float CentreDepth,
        float Width,
        float CentreHeight,
        float Height);

    private readonly record struct HorizontalMargins(
        float NegativeRight,
        float PositiveRight,
        float NegativeForward,
        float PositiveForward);

    private enum CandidateReject
    {
        None,
        Structural,
        Arrival,
        Operating,
    }

    private readonly record struct CandidateBuildResult(
        ShelfCandidate? Candidate,
        CandidateReject Reject,
        bool Downgraded);

    private sealed class CandidateDiscovery
    {
        public List<ShelfCandidate> Candidates { get; } = [];
        public int CandidateCount { get; set; }
        public int SupportRejectCount { get; set; }
        public int StructuralRejectCount { get; set; }
        public int ArrivalRejectCount { get; set; }
        public int OperatingRejectCount { get; set; }
        public int LandingDowngradeCount { get; set; }
        public int FlightClearanceRejectCount { get; set; }
        public int CornerCandidateCount { get; set; }
        public int FullSpanCandidateCount { get; set; }
        public int LowCandidateCount { get; private set; }
        public int MidCandidateCount { get; private set; }
        public int HighCandidateCount { get; private set; }

        public void CountBand(MegastationMegaShelfVerticalBand band)
        {
            switch (band)
            {
                case MegastationMegaShelfVerticalBand.Low: LowCandidateCount++; break;
                case MegastationMegaShelfVerticalBand.Mid: MidCandidateCount++; break;
                case MegastationMegaShelfVerticalBand.High: HighCandidateCount++; break;
            }
        }
    }

    private sealed record ShelfCandidate(
        string Identity,
        MegastationMegaShelfFamily Family,
        MegastationBayWallSurface PrimaryWall,
        IReadOnlyList<MegastationBayWallSurface> HostWalls,
        BoundaryFaceKey[] SupportingFaces,
        MegastationInteriorStructuralSolid Body,
        MegastationLandingSurface? LandingSurface,
        MegastationMegaShelfClearanceVolume ClearanceVolume,
        MegastationMegaShelfVerticalBand VerticalBand,
        int Seed,
        float Priority,
        bool Active);
}
