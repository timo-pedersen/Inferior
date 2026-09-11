using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationBayFacilityArchetype
{
    RecessedFacility,
    ProjectingGallery,
    EmbeddedBlock,
    ServiceAperture,
    DeepRecess,
    OpenGallery,
    MultiStoreyGallery,
}

public enum MegastationBayFacilityPartRole
{
    StructuralFrame,
    GallerySlab,
    ApertureFrame,
    ApertureBacking,
    PersonnelDoor,
    Railing,
    LightFixture,
}

public enum MegastationBayFacilityColourRole
{
    Dominant,
    Secondary,
    Accent,
    Dark,
}

public enum MegastationBayHabitationReservationKind
{
    Cutout,
    Gallery,
    EmbeddedFacility,
    ServiceAperture,
    PlainWindowBand,
}

public sealed record MegastationBayHabitationReservation(
    string Identity,
    string WallIdentity,
    string RegionIdentity,
    MegastationBayHabitationReservationKind Kind,
    Vector2 Centre,
    Vector2 Size,
    float MinimumProjection,
    float MaximumProjection);

public sealed record MegastationBayFacilityPart(
    string Identity,
    MegastationBayFacilityPartRole Role,
    Vector3 Centre,
    Vector3 Size,
    SystemMaterialFamilyId MaterialFamily,
    MegastationBayFacilityColourRole ColourRole,
    bool CastsArtificialShadow);

public sealed record MegastationBayWallCutout(
    string Identity,
    BoundaryFaceKey SupportingFace,
    string WallIdentity,
    Vector3 Centre,
    Vector3 Right,
    Vector3 Up,
    Vector3 Normal,
    Vector2 Size,
    float Depth);

public sealed record MegastationBayFacility(
    string Identity,
    string WallIdentity,
    string RegionIdentity,
    MegastationBayFacilityArchetype Archetype,
    Vector3 Right,
    Vector3 Up,
    Vector3 Normal,
    Vector3 EnvelopeCentre,
    Vector3 EnvelopeSize,
    float MaximumProjection,
    bool HasSecondaryForm,
    IReadOnlyList<MegastationBayFacilityPart> Parts,
    MegastationBayWallCutout? Cutout = null,
    IReadOnlyList<MegastationBayHabitationWindow>? Windows = null,
    int StoreyCount = 0);

public sealed record MegastationBayFacilityDiagnostics(
    int AlgorithmVersion,
    int EligibleRegionCount,
    int EnhancedRegionCount,
    int PlainRegionCount,
    int RecessedFacilityCount,
    int ProjectingGalleryCount,
    int EmbeddedBlockCount,
    int ServiceApertureCount,
    int SecondaryFormCount,
    int StructuralPartCount,
    int RailingPartCount,
    int ArtificialShadowPartCount,
    int CutoutCount,
    int FacilityWindowCount,
    int BalconyCount,
    float MaximumProjection,
    long PlanningMilliseconds,
    int MeshVertexCount,
    int MeshTriangleCount,
    int ShadowVertexCount,
    int ShadowTriangleCount,
    long MeshBytes,
    string Signature,
    int ReservationRejectCount = 0,
    int CutoutValidationRejectCount = 0,
    int ArtificialLightCount = 0,
    int DeepRecessCount = 0,
    int OpenGalleryCount = 0,
    int MultiStoreyGalleryCount = 0,
    int GalleryStoreyCount = 0);

public sealed record MegastationBayFacilityPlan(
    IReadOnlyList<MegastationBayFacility> Facilities,
    IReadOnlySet<string> RetainedRegionIdentities,
    IReadOnlyList<MegastationBayHabitationReservation> Reservations,
    IReadOnlyList<MegastationArtificialLight> ArtificialLights,
    MegastationBayFacilityDiagnostics Diagnostics);

public sealed record MegastationBayWallCompositionPlan(
    MegastationBayHabitationPlan Habitation,
    MegastationBayFacilityPlan Facilities);

public static class MegastationBayWallCompositionPlanner
{
    public static MegastationBayWallCompositionPlan Plan(
        MegastationInteriorPlan interior,
        MegastationLandingDistrictPlan landingDistrict,
        StructuralOccupancy occupancy,
        BoundaryTopology topology)
    {
        MegastationBayHabitationPlan candidates = MegastationBayHabitationPlanner.Plan(
            interior, landingDistrict, occupancy, topology);
        MegastationBayFacilityPlan facilities = MegastationBayFacilityPlanner.Plan(
            interior, candidates, occupancy, topology, landingDistrict);
        MegastationBayHabitationPlan habitation = MegastationBayHabitationPlanner.RetainRegions(
            candidates, facilities.RetainedRegionIdentities,
            facilities.Facilities.Select(facility => facility.RegionIdentity)
                .ToHashSet(StringComparer.Ordinal));
        return new(habitation, facilities);
    }
}

public static class MegastationBayFacilityPlanner
{
    public const int AlgorithmVersion = 3;
    private const float ReservationMargin = 2f;

    public static MegastationBayFacilityPlan Plan(
        MegastationInteriorPlan interior,
        MegastationBayHabitationPlan habitation,
        StructuralOccupancy? occupancy = null,
        BoundaryTopology? topology = null,
        MegastationLandingDistrictPlan? landingDistrict = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var facilities = new List<MegastationBayFacility>();
        var reservations = new List<MegastationBayHabitationReservation>();
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var lights = new List<MegastationArtificialLight>();
        var supportingFaces = new HashSet<BoundaryFaceKey>();
        int reservationRejects = 0;
        int cutoutValidationRejects = 0;
        int rootSeed = MegastationSeed.Derive(interior.Seed, "bay-wall-facilities:v3");

        // Allocate the strongest architecture first. This is a semantic priority,
        // not an emission-order accident: every later occupant sees its reservation.
        if (occupancy is not null && topology is not null)
        foreach (MegastationBayHabitationRegion region in habitation.Regions
                     .OrderBy(item => Unit(MegastationSeed.Derive(rootSeed, item.Identity),
                         "cutout-priority")))
        {
            int regionSeed = MegastationSeed.Derive(rootSeed, region.Identity);
            MegastationBayFacility candidate = Build(region, regionSeed, forceFeature: true);
            if (candidate.Cutout is null)
            {
                cutoutValidationRejects++;
                continue;
            }
            if (supportingFaces.Contains(candidate.Cutout.SupportingFace)
                || !TryAcceptFacility(candidate))
            {
                reservationRejects++;
                continue;
            }
            supportingFaces.Add(candidate.Cutout.SupportingFace);
            AddFacility(candidate, regionSeed);
            break;
        }

        foreach (MegastationBayHabitationRegion region in habitation.Regions
                     .OrderBy(item => item.Identity, StringComparer.Ordinal))
        {
            if (retained.Contains(region.Identity))
                continue;
            int seed = MegastationSeed.Derive(rootSeed, region.Identity);
            if (Unit(seed, "presence") >= .58f)
                continue;
            MegastationBayFacilityArchetype intendedArchetype = PickArchetype(seed);
            MegastationBayFacility candidate = Build(region, seed, forceFeature: false);
            if (IsCutoutArchetype(intendedArchetype)
                && occupancy is not null && topology is not null
                && candidate.Cutout is null)
            {
                cutoutValidationRejects++;
                continue;
            }
            if (candidate.Cutout is { } opening
                && supportingFaces.Contains(opening.SupportingFace))
            {
                reservationRejects++;
                continue;
            }
            if (!TryAcceptFacility(candidate))
            {
                reservationRejects++;
                continue;
            }
            if (candidate.Cutout is { } acceptedOpening)
                supportingFaces.Add(acceptedOpening.SupportingFace);
            AddFacility(candidate, seed);
        }

        // Plain bands fill only the authoritative wall space left by substantial
        // architecture. Conflicting lightweight regions are intentionally discarded.
        foreach (MegastationBayHabitationRegion region in habitation.Regions
                     .Where(region => !retained.Contains(region.Identity))
                     .OrderBy(region => region.Identity, StringComparer.Ordinal))
        {
            var reservation = new MegastationBayHabitationReservation(
                $"{region.Identity}/reservation:plain", region.WallIdentity,
                region.Identity, MegastationBayHabitationReservationKind.PlainWindowBand,
                region.Centre, region.Size + new Vector2(ReservationMargin * 2f), 0f, .1f);
            if (reservations.Any(existing => ReservationsOverlap(existing, reservation)))
            {
                reservationRejects++;
                continue;
            }
            reservations.Add(reservation);
            retained.Add(region.Identity);
        }

        stopwatch.Stop();
        string signature = Signature(facilities, reservations, lights);
        var diagnostics = new MegastationBayFacilityDiagnostics(
            AlgorithmVersion,
            habitation.Regions.Count,
            facilities.Count,
            retained.Count - facilities.Count,
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.RecessedFacility),
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.ProjectingGallery
                || item.Archetype == MegastationBayFacilityArchetype.OpenGallery
                || item.Archetype == MegastationBayFacilityArchetype.MultiStoreyGallery),
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.EmbeddedBlock),
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.ServiceAperture),
            facilities.Count(item => item.HasSecondaryForm),
            facilities.Sum(item => item.Parts.Count(part =>
                part.Role != MegastationBayFacilityPartRole.Railing)),
            facilities.Sum(item => item.Parts.Count(part =>
                part.Role == MegastationBayFacilityPartRole.Railing)),
            facilities.Sum(item => item.Parts.Count(part => part.CastsArtificialShadow)),
            facilities.Count(item => item.Cutout is not null),
            facilities.Sum(item => item.Windows?.Count ?? 0),
            facilities.Sum(item => item.Parts.Count(part =>
                part.Role == MegastationBayFacilityPartRole.GallerySlab)),
            facilities.Count == 0 ? 0f : facilities.Max(item => item.MaximumProjection),
            stopwatch.ElapsedMilliseconds,
            0, 0, 0, 0, 0,
            signature,
            reservationRejects,
            cutoutValidationRejects,
            lights.Count,
            facilities.Count(item => item.Archetype == MegastationBayFacilityArchetype.DeepRecess),
            facilities.Count(item => item.Archetype == MegastationBayFacilityArchetype.OpenGallery),
            facilities.Count(item => item.Archetype == MegastationBayFacilityArchetype.MultiStoreyGallery),
            facilities.Sum(item => item.StoreyCount));
        return new(facilities, retained, reservations, lights, diagnostics);

        MegastationBayFacility Build(
            MegastationBayHabitationRegion region,
            int seed,
            bool forceFeature)
        {
            MegastationBayWallSurface wall = habitation.Walls.Single(candidate =>
                candidate.Identity == region.WallIdentity);
            MegastationBayFacilityArchetype archetype = forceFeature
                ? region.Size.Y >= 11f
                    ? MegastationBayFacilityArchetype.MultiStoreyGallery
                    : MegastationBayFacilityArchetype.OpenGallery
                : PickArchetype(seed);
            bool secondary = archetype is MegastationBayFacilityArchetype.RecessedFacility
                    or MegastationBayFacilityArchetype.DeepRecess
                    or MegastationBayFacilityArchetype.EmbeddedBlock
                && Unit(seed, "secondary") < .28f;
            return BuildFacility(wall, region, archetype, secondary, seed,
                occupancy, topology);
        }

        bool TryAcceptFacility(MegastationBayFacility facility)
        {
            MegastationBayWallSurface wall = habitation.Walls.Single(candidate =>
                candidate.Identity == facility.WallIdentity);
            MegastationBayHabitationReservation reservation = Reservation(facility, wall);
            if (!FitsAuthoritativeWallAndBay(facility, wall, reservation))
                return false;
            if (reservations.Any(existing => ReservationsOverlap(existing, reservation)))
                return false;
            if (landingBuildingsOverlap(facility, wall))
                return false;
            return true;
        }

        bool FitsAuthoritativeWallAndBay(
            MegastationBayFacility facility,
            MegastationBayWallSurface wall,
            MegastationBayHabitationReservation reservation)
        {
            if (occupancy is null || topology is null)
                return true;
            if (!MegastationBayHabitationPlanner.TryFindSupportingFace(
                    wall, reservation.Centre, reservation.Size, 0f,
                    occupancy.Grid, topology, out BoundaryFace? face)
                || face is null
                || facility.Cutout is { } cutout && cutout.SupportingFace != face.Key)
                return false;
            (int dx, int dy, int dz) = Direction.Offset(face.Direction);
            int voidX = face.Key.X + dx;
            int voidY = face.Key.Y + dy;
            int voidZ = face.Key.Z + dz;
            if (occupancy.VoidKind(voidX, voidY, voidZ)
                != MegacellVoidKind.InteriorFlightVolume)
                return false;
            GridAxis axis = Direction.PrimaryAxis(face.Direction);
            int voidIndex = axis switch
            {
                GridAxis.X => voidX,
                GridAxis.Y => voidY,
                _ => voidZ,
            };
            return reservation.MaximumProjection
                <= occupancy.Grid.GetCellSize(axis, voidIndex) - .1f;
        }

        void AddFacility(MegastationBayFacility facility, int seed)
        {
            MegastationBayWallSurface wall = habitation.Walls.Single(candidate =>
                candidate.Identity == facility.WallIdentity);
            facilities.Add(facility);
            retained.Add(facility.RegionIdentity);
            reservations.Add(Reservation(facility, wall));
            if (facility.Cutout is not null
                && Unit(seed, "recess-light-presence") < .96f)
                lights.AddRange(BuildRecessLights(facility, seed));
        }

        bool landingBuildingsOverlap(
            MegastationBayFacility facility,
            MegastationBayWallSurface wall)
        {
            Vector2 centre = new(
                Vector3.Dot(facility.EnvelopeCentre - wall.Centre, wall.Right),
                Vector3.Dot(facility.EnvelopeCentre - wall.Centre, wall.Up));
            Vector2 size = new(facility.EnvelopeSize.X + ReservationMargin * 2f,
                facility.EnvelopeSize.Y + ReservationMargin * 2f);
            return (landingDistrict?.ServiceBuildings ?? []).Any(building => BuildingOverlaps(
                wall, centre, size, building));
        }
    }

    internal static bool ReservationsOverlap(
        MegastationBayHabitationReservation a,
        MegastationBayHabitationReservation b)
        => a.WallIdentity == b.WallIdentity
            && RectangleOverlaps(a.Centre, a.Size, b.Centre, b.Size);

    private static bool RectangleOverlaps(
        Vector2 centreA,
        Vector2 sizeA,
        Vector2 centreB,
        Vector2 sizeB)
        => MathF.Abs(centreA.X - centreB.X) * 2f < sizeA.X + sizeB.X
            && MathF.Abs(centreA.Y - centreB.Y) * 2f < sizeA.Y + sizeB.Y;

    private static MegastationBayHabitationReservation Reservation(
        MegastationBayFacility facility,
        MegastationBayWallSurface wall)
    {
        Vector3 offset = facility.EnvelopeCentre - wall.Centre;
        Vector2 centre = new(Vector3.Dot(offset, wall.Right),
            Vector3.Dot(offset, wall.Up));
        float projection = Vector3.Dot(offset, wall.Normal);
        MegastationBayHabitationReservationKind kind = facility.Cutout is not null
            ? MegastationBayHabitationReservationKind.Cutout
            : facility.Archetype switch
            {
                MegastationBayFacilityArchetype.ProjectingGallery =>
                    MegastationBayHabitationReservationKind.Gallery,
                MegastationBayFacilityArchetype.EmbeddedBlock =>
                    MegastationBayHabitationReservationKind.EmbeddedFacility,
                _ => MegastationBayHabitationReservationKind.ServiceAperture,
            };
        return new($"{facility.Identity}/reservation", facility.WallIdentity,
            facility.RegionIdentity, kind, centre,
            new(facility.EnvelopeSize.X + ReservationMargin * 2f,
                facility.EnvelopeSize.Y + ReservationMargin * 2f),
            projection - facility.EnvelopeSize.Z * .5f,
            projection + facility.EnvelopeSize.Z * .5f);
    }

    private static bool BuildingOverlaps(
        MegastationBayWallSurface wall,
        Vector2 centre,
        Vector2 size,
        MegastationLandingServiceBuilding building)
    {
        Vector3 buildingRight = Vector3.Normalize(building.Frontage.Right);
        Vector3 buildingUp = Vector3.Normalize(building.Frontage.Up);
        Vector3 buildingForward = Vector3.Normalize(building.Frontage.Normal);
        float Radius(Vector3 axis) =>
            MathF.Abs(Vector3.Dot(buildingRight, axis)) * building.Size.X * .5f
            + MathF.Abs(Vector3.Dot(buildingUp, axis)) * building.Size.Y * .5f
            + MathF.Abs(Vector3.Dot(buildingForward, axis)) * building.Size.Z * .5f;
        if (MathF.Abs(Vector3.Dot(building.Centre - wall.Centre, wall.Normal))
            > Radius(wall.Normal) + 2f)
            return false;
        Vector2 buildingCentre = new(
            Vector3.Dot(building.Centre - wall.Centre, wall.Right),
            Vector3.Dot(building.Centre - wall.Centre, wall.Up));
        Vector2 buildingSize = new(Radius(wall.Right) * 2f, Radius(wall.Up) * 2f);
        return RectangleOverlaps(centre, size, buildingCentre,
            buildingSize + new Vector2(ReservationMargin * 2f));
    }

    private static IReadOnlyList<MegastationArtificialLight> BuildRecessLights(
        MegastationBayFacility facility,
        int seed)
    {
        MegastationBayWallCutout cutout = facility.Cutout!;
        int count = facility.Archetype == MegastationBayFacilityArchetype.MultiStoreyGallery
            ? Math.Clamp(facility.StoreyCount, 2, 5)
            : 1;
        float storeyHeight = cutout.Size.Y / Math.Max(1, facility.StoreyCount);
        var result = new List<MegastationArtificialLight>(count);
        for (int level = 0; level < count; level++)
        {
            int child = MegastationSeed.Derive(seed, $"recess-light:{level}");
            float colourRoll = Unit(child, "colour");
            Color colour = colourRoll < .48f ? new Color(255, 226, 185)
                : colourRoll < .86f ? new Color(235, 238, 232)
                : new Color(204, 225, 242);
            float intensity = .60f + Unit(child, "intensity") * .28f;
            float range = MathHelper.Clamp(
                MathF.Max(cutout.Size.X * .42f, storeyHeight * 2.4f), 16f, 46f);
            float y = facility.StoreyCount > 1
                ? -cutout.Size.Y * .5f + (level + .68f) * storeyHeight
                : cutout.Size.Y * .22f;
            float x = count > 1 && level % 2 == 0 ? -cutout.Size.X * .20f
                : count > 1 ? cutout.Size.X * .20f : 0f;
            Vector3 position = cutout.Centre + cutout.Right * x + cutout.Up * y
                - cutout.Normal * MathF.Max(.5f, cutout.Depth - .8f);
            result.Add(new($"{cutout.Identity}/architectural-light:v2/{level}", position,
                colour, intensity, range, cutout.Normal, -.10f));
        }
        return result;
    }

    private static MegastationBayFacilityArchetype PickArchetype(int seed)
    {
        float roll = Unit(seed, "primary-form");
        return roll < .08f ? MegastationBayFacilityArchetype.RecessedFacility
            : roll < .27f ? MegastationBayFacilityArchetype.DeepRecess
            : roll < .69f ? MegastationBayFacilityArchetype.OpenGallery
            : roll < .75f ? MegastationBayFacilityArchetype.MultiStoreyGallery
            : roll < .80f ? MegastationBayFacilityArchetype.ProjectingGallery
            : roll < .94f ? MegastationBayFacilityArchetype.EmbeddedBlock
            : MegastationBayFacilityArchetype.ServiceAperture;
    }

    private static bool IsCutoutArchetype(MegastationBayFacilityArchetype archetype)
        => archetype is MegastationBayFacilityArchetype.RecessedFacility
            or MegastationBayFacilityArchetype.DeepRecess
            or MegastationBayFacilityArchetype.OpenGallery
            or MegastationBayFacilityArchetype.MultiStoreyGallery;

    private static MegastationBayFacility BuildFacility(
        MegastationBayWallSurface wall,
        MegastationBayHabitationRegion region,
        MegastationBayFacilityArchetype archetype,
        bool secondary,
        int seed,
        StructuralOccupancy? occupancy,
        BoundaryTopology? topology)
    {
        var parts = new List<MegastationBayFacilityPart>();
        var windows = new List<MegastationBayHabitationWindow>();
        MegastationBayWallCutout? cutout = null;
        float maximumProjection = 0f;
        int storeyCount = archetype switch
        {
            MegastationBayFacilityArchetype.OpenGallery => 1,
            MegastationBayFacilityArchetype.MultiStoreyGallery => Math.Clamp(
                Math.Min(5, (int)(region.Size.Y / 3.25f)), 2, 5),
            _ => 0,
        };
        float frameMargin = 1.5f + Unit(seed, "frame-margin") * 1.5f;
        float width = region.Size.X + frameMargin * 2f;
        float height = region.Size.Y + frameMargin * 2f;
        float frameThickness = archetype == MegastationBayFacilityArchetype.EmbeddedBlock
            ? 2.0f + Unit(seed, "frame-thickness") * 1.2f
            : .85f + Unit(seed, "frame-thickness") * .45f;
        float depth = archetype == MegastationBayFacilityArchetype.EmbeddedBlock
            ? 2.8f + Unit(seed, "depth") * 1.8f
            : .65f + Unit(seed, "depth") * .55f;

        if (IsCutoutArchetype(archetype)
            && occupancy is not null && topology is not null)
        {
            cutout = BuildCutout(wall, region, archetype, storeyCount,
                seed, occupancy, topology);
            if (cutout is not null && Unit(seed, "cut-edge-frame") < .42f)
                AddPerimeter(MegastationBayFacilityPartRole.ApertureFrame,
                    new(Vector3.Dot(cutout.Centre - wall.Centre, wall.Right),
                        Vector3.Dot(cutout.Centre - wall.Centre, wall.Up)),
                    cutout.Size.X + .9f, cutout.Size.Y + .9f, .45f, .35f,
                    SystemMaterialFamilyId.HeavyIndustrialPlate,
                    "cut-edge/frame");
        }
        if (IsCutoutArchetype(archetype)
            && cutout is null && occupancy is not null && topology is not null)
        {
            // A production recess is a structural cut or it is not a recess. Keep the
            // region useful without reintroducing the rejected picture-frame fallback.
            archetype = MegastationBayFacilityArchetype.ProjectingGallery;
            secondary = false;
            storeyCount = 0;
        }
        if (IsCutoutArchetype(archetype)
            && cutout is null)
        {
            // Compatibility for plan-only fixtures without topology. Production always
            // supplies authoritative faces and converts this archetype into a real cut.
            AddPerimeter(MegastationBayFacilityPartRole.ApertureFrame,
                region.Centre, width, height, frameThickness, depth,
                SystemMaterialFamilyId.DullStructuralMetal);
            maximumProjection = MathF.Max(maximumProjection, depth);
        }
        else if (archetype == MegastationBayFacilityArchetype.EmbeddedBlock)
        {
            AddPerimeter(archetype == MegastationBayFacilityArchetype.EmbeddedBlock
                ? MegastationBayFacilityPartRole.StructuralFrame
                : MegastationBayFacilityPartRole.ApertureFrame,
                region.Centre, width, height, frameThickness, depth,
                archetype == MegastationBayFacilityArchetype.EmbeddedBlock
                    ? SystemMaterialFamilyId.HeavyIndustrialPlate
                    : SystemMaterialFamilyId.DullStructuralMetal);
            maximumProjection = MathF.Max(maximumProjection, depth);
        }

        bool gallery = archetype == MegastationBayFacilityArchetype.ProjectingGallery
            || secondary && cutout is null;
        if (cutout is not null)
            AddRecessArchitecture(cutout);
        if (gallery)
        {
            float galleryWidth = MathF.Max(12f, region.Size.X * (.72f
                + Unit(seed, "gallery-width") * .18f));
            float galleryDepth = 3.2f + Unit(seed, "gallery-depth") * 2.6f;
            float slabThickness = .28f + Unit(seed, "slab-thickness") * .16f;
            float y = region.Centre.Y - region.Size.Y * .5f - .55f;
            AddPart("gallery/slab", MegastationBayFacilityPartRole.GallerySlab,
                new(region.Centre.X, y - slabThickness * .5f, galleryDepth * .5f),
                new(galleryWidth, slabThickness, galleryDepth),
                SystemMaterialFamilyId.HeavyIndustrialPlate,
                MegastationBayFacilityColourRole.Secondary, true);
            AddGalleryDetails(region.Centre.X, y, galleryWidth, galleryDepth);
            maximumProjection = MathF.Max(maximumProjection, galleryDepth);
        }

        if (archetype == MegastationBayFacilityArchetype.ServiceAperture)
        {
            float apertureWidth = MathHelper.Clamp(region.Size.X * .32f, 6f, 11f);
            float apertureHeight = 3.6f + Unit(seed, "aperture-height") * 2.2f;
            float apertureY = region.Centre.Y - region.Size.Y * .5f - apertureHeight * .7f;
            float apertureX = region.Centre.X + region.Size.X *
                (Unit(seed, "aperture-side") < .5f ? -.18f : .18f);
            AddPart("aperture/backing", MegastationBayFacilityPartRole.ApertureBacking,
                new(apertureX, apertureY, .08f),
                new(apertureWidth, apertureHeight, .16f),
                SystemMaterialFamilyId.DullStructuralMetal,
                MegastationBayFacilityColourRole.Dark, false);
            AddPerimeter(MegastationBayFacilityPartRole.ApertureFrame,
                new(apertureX, apertureY), apertureWidth + 1.4f,
                apertureHeight + 1.4f, .7f, .75f,
                SystemMaterialFamilyId.HeavyIndustrialPlate,
                "aperture/frame");
            maximumProjection = MathF.Max(maximumProjection, .75f);
        }

        IEnumerable<(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)>
            bounds = parts.Select(part =>
            {
                Vector3 local = new(
                    Vector3.Dot(part.Centre - wall.Centre, wall.Right),
                    Vector3.Dot(part.Centre - wall.Centre, wall.Up),
                    Vector3.Dot(part.Centre - wall.Centre, wall.Normal));
                return (local.X - part.Size.X * .5f, local.X + part.Size.X * .5f,
                    local.Y - part.Size.Y * .5f, local.Y + part.Size.Y * .5f,
                    local.Z - part.Size.Z * .5f, local.Z + part.Size.Z * .5f);
            });
        if (cutout is not null)
        {
            float x = Vector3.Dot(cutout.Centre - wall.Centre, wall.Right);
            float y = Vector3.Dot(cutout.Centre - wall.Centre, wall.Up);
            bounds = bounds.Append((x - cutout.Size.X * .5f, x + cutout.Size.X * .5f,
                y - cutout.Size.Y * .5f, y + cutout.Size.Y * .5f,
                -cutout.Depth, 0f));
        }
        var boundsArray = bounds.ToArray();
        float minX = boundsArray.Min(item => item.MinX);
        float maxX = boundsArray.Max(item => item.MaxX);
        float minY = boundsArray.Min(item => item.MinY);
        float maxY = boundsArray.Max(item => item.MaxY);
        float minZ = boundsArray.Min(item => item.MinZ);
        float maxZ = boundsArray.Max(item => item.MaxZ);
        maximumProjection = maxZ;
        Vector3 envelopeCentre = SurfacePoint(
            (minX + maxX) * .5f, (minY + maxY) * .5f, (minZ + maxZ) * .5f);
        Vector3 envelopeSize = new(maxX - minX, maxY - minY, maxZ - minZ);
        return new(
            $"{region.Identity}/facility:v3", wall.Identity, region.Identity,
            archetype, wall.Right, wall.Up, wall.Normal,
            envelopeCentre, envelopeSize, maximumProjection, secondary, parts,
            cutout, windows, storeyCount);

        void AddRecessArchitecture(MegastationBayWallCutout opening)
        {
            float localX = Vector3.Dot(opening.Centre - wall.Centre, wall.Right);
            float localY = Vector3.Dot(opening.Centre - wall.Centre, wall.Up);
            if (archetype is MegastationBayFacilityArchetype.DeepRecess
                or MegastationBayFacilityArchetype.RecessedFacility)
            {
                PlanRearWindowRows(opening, localX, localY,
                    Math.Clamp((int)(opening.Size.Y / 3.2f), 1, 5));
                AddPart("recess/light-fixture", MegastationBayFacilityPartRole.LightFixture,
                    new(localX, localY + opening.Size.Y * .28f, -opening.Depth + .10f),
                    new(MathHelper.Clamp(opening.Size.X * .16f, 1.4f, 3.2f), .22f, .16f),
                    SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Accent, false);
                return;
            }

            int levels = Math.Max(1, storeyCount);
            float storeyHeight = opening.Size.Y / levels;
            for (int level = 0; level < levels; level++)
            {
                float floorY = localY - opening.Size.Y * .5f + level * storeyHeight;
                float width = opening.Size.X * (.78f
                    + Unit(seed, $"gallery-width:{level}") * .18f);
                float depth = MathF.Max(2.6f, opening.Depth - .22f);
                if (level > 0)
                {
                    const float slab = .28f;
                    AddPart($"recess/gallery:{level}/slab",
                        MegastationBayFacilityPartRole.GallerySlab,
                        new(localX, floorY - slab * .5f,
                            -opening.Depth + depth * .5f),
                        new(width, slab, depth),
                        SystemMaterialFamilyId.HeavyIndustrialPlate,
                        MegastationBayFacilityColourRole.Secondary, true);
                }
                AddGalleryRails($"recess/gallery:{level}", localX, floorY, width,
                    0f, depth, exposedEnds: width < opening.Size.X - 1.2f);
                if (Unit(seed, $"gallery-door:{level}") < .78f || level == 0)
                    AddPart($"recess/gallery:{level}/door",
                        MegastationBayFacilityPartRole.PersonnelDoor,
                        new(localX, floorY + 1.2f, -opening.Depth + .10f),
                        new(1.25f, 2.4f, .20f),
                        SystemMaterialFamilyId.CleanTechnicalAlloy,
                        MegastationBayFacilityColourRole.Dark, false);
                AddPart($"recess/gallery:{level}/light-fixture",
                    MegastationBayFacilityPartRole.LightFixture,
                    new(localX + (level % 2 == 0 ? -width * .22f : width * .22f),
                        floorY + MathF.Min(2.72f, storeyHeight - .35f),
                        -opening.Depth + .10f),
                    new(1.65f, .22f, .16f), SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Accent, false);
                PlanBalconyWindows(opening, level, localX, floorY, width);
            }
        }

        void PlanRearWindowRows(
            MegastationBayWallCutout opening,
            float localX,
            float localY,
            int rows)
        {
            int columns = Math.Clamp((int)(opening.Size.X / 3.2f), 5, 18);
            float spacingX = MathF.Min(3f,
                (opening.Size.X - 3.5f) / Math.Max(1, columns - 1));
            float spacingY = MathF.Min(3.25f,
                (opening.Size.Y - 3.5f) / Math.Max(1, rows));
            for (int row = 0; row < rows; row++)
            {
                int rowSeed = MegastationSeed.Derive(seed, $"recess-window-row:{row}");
                MegastationWindowState rowState = Unit(rowSeed, "state") < .18f
                    ? MegastationWindowState.Dim : MegastationWindowState.Lit;
                int gapStride = Unit(rowSeed, "rhythm") < .35f ? 5 : 7;
                int gapOffset = (int)(Unit(rowSeed, "offset") * gapStride);
                for (int column = 0; column < columns; column++)
                {
                    if ((column + gapOffset) % gapStride == 0)
                        continue;
                    bool dark = Unit(rowSeed, $"dark:{column}") < .10f;
                    MegastationWindowState state = dark
                        ? MegastationWindowState.Dark : rowState;
                    Color colour = state == MegastationWindowState.Dark
                        ? new Color(18, 20, 21)
                        : state == MegastationWindowState.Dim
                            ? Color.Lerp(region.DominantColour, Color.Black, .42f)
                            : region.DominantColour;
                    float x = localX + (column - (columns - 1) * .5f) * spacingX;
                    float y = localY + (row - (rows - 1) * .5f) * spacingY;
                    windows.Add(new(
                        $"{region.Identity}/recess/window:{row}:{column}",
                        wall.Identity, region.Identity,
                        MegastationBayWindowPattern.DoubleRow,
                        wall.Centre + wall.Right * x + wall.Up * y
                            + wall.Normal * (-opening.Depth + .045f),
                        wall.Normal, wall.Up, 1.35f, 1.15f, state, colour,
                        state == MegastationWindowState.Lit ? .58f
                            : state == MegastationWindowState.Dim ? .22f : .015f));
                }
            }
        }

        void PlanBalconyWindows(
            MegastationBayWallCutout opening,
            int level,
            float x,
            float floorY,
            float balconyWidth)
        {
            int columns = Math.Clamp((int)(balconyWidth / 4f), 4, 8);
            float spacing = MathF.Min(3.2f,
                (balconyWidth - 3f) / Math.Max(1, columns - 1));
            int levelSeed = MegastationSeed.Derive(seed, $"gallery-level:{level}");
            MegastationWindowState levelState = Unit(levelSeed, "state") < .20f
                ? MegastationWindowState.Dim : MegastationWindowState.Lit;
            for (int column = 0; column < columns; column++)
            {
                if (column == columns / 2)
                    continue;
                MegastationWindowState state = Unit(levelSeed, $"dark:{column}") < .12f
                    ? MegastationWindowState.Dark : levelState;
                Color colour = state == MegastationWindowState.Dark
                    ? new Color(18, 20, 21)
                    : state == MegastationWindowState.Dim
                        ? Color.Lerp(region.DominantColour, Color.Black, .42f)
                        : region.DominantColour;
                windows.Add(new(
                    $"{region.Identity}/recess/balcony:{level}/window:{column}",
                    wall.Identity, region.Identity,
                    MegastationBayWindowPattern.ObservationStrip,
                    wall.Centre
                        + wall.Right * (x + (column - (columns - 1) * .5f) * spacing)
                        + wall.Up * (floorY + 1.45f)
                        + wall.Normal * (-opening.Depth + .045f),
                    wall.Normal, wall.Up, 1.35f, 1.15f, state, colour,
                    state == MegastationWindowState.Lit ? .58f
                        : state == MegastationWindowState.Dim ? .22f : .015f));
            }
        }

        void AddPerimeter(
            MegastationBayFacilityPartRole role,
            Vector2 centre,
            float perimeterWidth,
            float perimeterHeight,
            float member,
            float projection,
            SystemMaterialFamilyId material,
            string prefix = "frame")
        {
            AddPart($"{prefix}/left", role,
                new(centre.X - perimeterWidth * .5f + member * .5f, centre.Y,
                    projection * .5f),
                new(member, perimeterHeight, projection), material,
                MegastationBayFacilityColourRole.Dominant, true);
            AddPart($"{prefix}/right", role,
                new(centre.X + perimeterWidth * .5f - member * .5f, centre.Y,
                    projection * .5f),
                new(member, perimeterHeight, projection), material,
                MegastationBayFacilityColourRole.Dominant, true);
            AddPart($"{prefix}/bottom", role,
                new(centre.X, centre.Y - perimeterHeight * .5f + member * .5f,
                    projection * .5f),
                new(perimeterWidth - member * 2f, member, projection), material,
                MegastationBayFacilityColourRole.Secondary, true);
            AddPart($"{prefix}/top", role,
                new(centre.X, centre.Y + perimeterHeight * .5f - member * .5f,
                    projection * .5f),
                new(perimeterWidth - member * 2f, member, projection), material,
                MegastationBayFacilityColourRole.Secondary, true);
        }

        void AddGalleryDetails(float x, float y, float galleryWidth, float galleryDepth)
        {
            AddGalleryRails("gallery", x, y, galleryWidth, galleryDepth,
                galleryDepth, exposedEnds: true);
            AddPart("gallery/door", MegastationBayFacilityPartRole.PersonnelDoor,
                new(x, y + 1.2f, .10f), new(1.25f, 2.4f, .20f),
                SystemMaterialFamilyId.CleanTechnicalAlloy,
                MegastationBayFacilityColourRole.Dark, false);
            PlanGalleryWindows(x, y, galleryWidth, .045f, "gallery");
        }

        void AddGalleryRails(
            string prefix,
            float x,
            float y,
            float galleryWidth,
            float outerZ,
            float galleryDepth,
            bool exposedEnds)
        {
            const float railHeight = MegastationLandingDistrictMeshBuilder.RailingHeight;
            const float rail = MegastationLandingPadAssemblyStandards.RailingMemberThickness;
            AddPart($"{prefix}/rail/front", MegastationBayFacilityPartRole.Railing,
                new(x, y + railHeight, outerZ - rail * .5f),
                new(galleryWidth, rail, rail), SystemMaterialFamilyId.CleanTechnicalAlloy,
                MegastationBayFacilityColourRole.Accent, false);
            int posts = Math.Max(3, (int)(galleryWidth / 8f) + 1);
            for (int index = 0; index < posts; index++)
            {
                float px = x - galleryWidth * .5f
                    + galleryWidth * index / (posts - 1f);
                AddPart($"{prefix}/post:{index}", MegastationBayFacilityPartRole.Railing,
                    new(px, y + railHeight * .5f, outerZ - rail * .5f),
                    new(rail, railHeight, rail), SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Accent, false);
            }
            if (!exposedEnds)
                return;
            foreach (int side in new[] { -1, 1 })
            {
                float edgeX = x + side * (galleryWidth * .5f - rail * .5f);
                AddPart($"{prefix}/rail/end:{side}", MegastationBayFacilityPartRole.Railing,
                    new(edgeX, y + railHeight, outerZ - galleryDepth * .5f),
                    new(rail, rail, galleryDepth), SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Accent, false);
                AddPart($"{prefix}/post/end:{side}", MegastationBayFacilityPartRole.Railing,
                    new(edgeX, y + railHeight * .5f, outerZ - galleryDepth),
                    new(rail, railHeight, rail), SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Accent, false);
            }
        }

        void PlanGalleryWindows(float x, float y, float galleryWidth, float z, string prefix)
        {
            int columns = Math.Clamp((int)(galleryWidth / 3.2f), 4, 14);
            float spacing = MathF.Min(3f, (galleryWidth - 3f) / Math.Max(1, columns - 1));
            for (int column = 0; column < columns; column++)
            {
                if (column == columns / 2)
                    continue;
                int child = MegastationSeed.Derive(seed, $"{prefix}-window:{column}");
                MegastationWindowState state = Unit(child, "state") < .22f
                    ? MegastationWindowState.Dark : MegastationWindowState.Lit;
                Color colour = state == MegastationWindowState.Dark
                    ? new Color(18, 20, 21) : region.DominantColour;
                windows.Add(new(
                    $"{region.Identity}/{prefix}/window:{column}",
                    wall.Identity, region.Identity, MegastationBayWindowPattern.ObservationStrip,
                    wall.Centre + wall.Right * (x + (column - (columns - 1) * .5f) * spacing)
                        + wall.Up * (y + 1.55f) + wall.Normal * z,
                    wall.Normal, wall.Up, 1.4f, 1.1f, state, colour,
                    state == MegastationWindowState.Lit ? .58f : .015f));
            }
        }

        void AddPart(
            string suffix,
            MegastationBayFacilityPartRole role,
            Vector3 localCentre,
            Vector3 size,
            SystemMaterialFamilyId material,
            MegastationBayFacilityColourRole colour,
            bool casts)
            => parts.Add(new(
                $"{region.Identity}/facility:v3/{suffix}", role,
                SurfacePoint(localCentre.X, localCentre.Y, localCentre.Z),
                size, material, colour, casts));

        Vector3 SurfacePoint(float x, float y, float z)
            => wall.Centre + wall.Right * x + wall.Up * y + wall.Normal * z;
    }

    private static MegastationBayWallCutout? BuildCutout(
        MegastationBayWallSurface wall,
        MegastationBayHabitationRegion region,
        MegastationBayFacilityArchetype archetype,
        int storeyCount,
        int seed,
        StructuralOccupancy occupancy,
        BoundaryTopology topology)
    {
        SliceGrid grid = occupancy.Grid;
        float desiredHeight = archetype switch
        {
            MegastationBayFacilityArchetype.OpenGallery => MathHelper.Clamp(
                region.Size.Y, 3.2f, 4.2f),
            MegastationBayFacilityArchetype.MultiStoreyGallery => MathHelper.Clamp(
                storeyCount * (3.15f + Unit(seed, "storey-height") * .45f), 6.3f, 19f),
            _ => MathF.Max(region.Size.Y, 7f),
        };
        Vector2 desiredSize = new(region.Size.X, desiredHeight);
        if (!MegastationBayHabitationPlanner.TryFindSupportingFace(
                wall, region.Centre, desiredSize, 2f, grid, topology,
                out BoundaryFace? selectedFace)
            || selectedFace is null)
            return null;
        BoundaryFaceKey key = selectedFace.Key;
        if (!occupancy.IsOccupied(key.X, key.Y, key.Z))
            return null;
        (int dx, int dy, int dz) = Direction.Offset(selectedFace.Direction);
        if (occupancy.VoidKind(key.X + dx, key.Y + dy, key.Z + dz)
            != MegacellVoidKind.InteriorFlightVolume)
            return null;

        GridAxis depthAxis = Direction.PrimaryAxis(selectedFace.Direction);
        int depthIndex = depthAxis switch
        {
            GridAxis.X => key.X,
            GridAxis.Y => key.Y,
            _ => key.Z,
        };
        float maximumDepth = grid.GetCellSize(depthAxis, depthIndex) - .5f;
        if (maximumDepth < 1f)
            return null;
        float requestedDepth = IsCutoutArchetype(archetype)
            ? 4.5f + Unit(seed, "recess-depth") * 7.5f
            : 1f + Unit(seed, "recess-depth") * 4f;
        float depth = MathF.Min(requestedDepth, maximumDepth);
        if (IsCutoutArchetype(archetype) && depth < 3f)
            return null;
        Vector3 centre = wall.Centre + wall.Right * region.Centre.X
            + wall.Up * region.Centre.Y;
        return new(
            $"{region.Identity}/cutout:v2", key, wall.Identity,
            centre, wall.Right, wall.Up, wall.Normal, desiredSize, depth);
    }

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static string Signature(
        IReadOnlyList<MegastationBayFacility> facilities,
        IReadOnlyList<MegastationBayHabitationReservation> reservations,
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        var text = new StringBuilder().Append(AlgorithmVersion);
        foreach (MegastationBayFacility facility in facilities)
        {
            text.Append('|').Append(facility.Identity).Append(':')
                .Append(facility.Archetype).Append(':')
                .Append(facility.StoreyCount).Append(':')
                .Append(facility.MaximumProjection.ToString("R", CultureInfo.InvariantCulture));
            foreach (MegastationBayFacilityPart part in facility.Parts)
                text.Append(':').Append(part.Identity).Append('@').Append(part.Centre)
                    .Append(':').Append(part.Size).Append(':').Append(part.CastsArtificialShadow);
            if (facility.Cutout is { } cutout)
                text.Append(":cut@").Append(cutout.SupportingFace).Append(':')
                    .Append(cutout.Centre).Append(':').Append(cutout.Size).Append(':')
                    .Append(cutout.Depth.ToString("R", CultureInfo.InvariantCulture));
            foreach (MegastationBayHabitationWindow window in facility.Windows ?? [])
                text.Append(":window@").Append(window.Identity).Append(':')
                    .Append(window.Centre).Append(':').Append(window.Width)
                    .Append(':').Append(window.Height).Append(':').Append(window.State);
        }
        foreach (MegastationBayHabitationReservation reservation in reservations)
            text.Append("|reservation:").Append(reservation.Identity).Append(':')
                .Append(reservation.Kind).Append(':').Append(reservation.Centre)
                .Append(':').Append(reservation.Size).Append(':')
                .Append(reservation.MinimumProjection.ToString("R", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(reservation.MaximumProjection.ToString("R", CultureInfo.InvariantCulture));
        foreach (MegastationArtificialLight light in lights)
            text.Append("|light:").Append(light.Identity).Append(':')
                .Append(light.Position).Append(':').Append(light.Colour.PackedValue)
                .Append(':').Append(light.Intensity.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(light.Range.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed record MegastationBayFacilityMeshResult(
    int FirstFace,
    int FaceCount,
    MegastationBayFacilityDiagnostics Diagnostics,
    IReadOnlyList<(int Face, float Illumination)> IlluminationFaces);

public static class MegastationBayFacilityMeshBuilder
{
    public static MegastationBayFacilityMeshResult Append(
        StationModuleMesh mesh,
        MegastationBayFacilityPlan plan,
        MegastationSystemMaterialAssignment? materials,
        CancellationToken cancellationToken = default)
    {
        int firstFace = mesh.FaceCount;
        int firstVertex = mesh.VertexCount;
        int firstIndex = mesh.IndexCount;
        int shadowParts = 0;
        var illumination = new List<(int Face, float Illumination)>();
        Color dominant = materials?.Palette.DominantTint ?? new Color(74, 78, 82);
        Color secondary = materials?.Palette.SecondaryTint ?? new Color(96, 101, 106);
        Color accent = materials?.Palette.AccentTint ?? new Color(132, 144, 150);

        foreach (MegastationBayFacility facility in plan.Facilities)
        foreach (MegastationBayFacilityPart part in facility.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mesh.CurrentDecorClass = part.CastsArtificialShadow
                ? DecorClass.MegastationInteriorMajor
                : DecorClass.MegastationInteriorMinor;
            mesh.CurrentMaterialFamily = part.MaterialFamily;
            mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
                part.MaterialFamily).TileSizeMeters;
            Color colour = part.ColourRole switch
            {
                MegastationBayFacilityColourRole.Dominant =>
                    Color.Lerp(dominant, secondary, .18f),
                MegastationBayFacilityColourRole.Secondary =>
                    Color.Lerp(secondary, dominant, .22f),
                MegastationBayFacilityColourRole.Accent =>
                    Color.Lerp(accent, Color.White, .06f),
                _ => Color.Lerp(dominant, Color.Black, .72f),
            };
            Matrix frame = new(
                facility.Right.X, facility.Right.Y, facility.Right.Z, 0f,
                facility.Up.X, facility.Up.Y, facility.Up.Z, 0f,
                facility.Normal.X, facility.Normal.Y, facility.Normal.Z, 0f,
                part.Centre.X, part.Centre.Y, part.Centre.Z, 1f);
            int partFirstFace = mesh.FaceCount;
            mesh.AddOrientedBox(frame, part.Size, colour);
            if (part.Role == MegastationBayFacilityPartRole.LightFixture)
                for (int face = partFirstFace; face < mesh.FaceCount; face++)
                    illumination.Add((face, .88f));
            if (part.CastsArtificialShadow)
                shadowParts++;
        }
        foreach (MegastationBayFacility facility in plan.Facilities)
        foreach (MegastationBayHabitationWindow window in facility.Windows ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
            mesh.CurrentMaterialFamily = SystemMaterialFamilyId.CleanTechnicalAlloy;
            mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
                SystemMaterialFamilyId.CleanTechnicalAlloy).TileSizeMeters;
            int face = mesh.FaceCount;
            MegastationWindowMeshBuilder.AppendWindow(
                mesh, window.Centre, window.Normal, window.Up,
                window.Width, window.Height, window.Colour);
            illumination.Add((face, window.Illumination));
        }

        int vertices = mesh.VertexCount - firstVertex;
        int triangles = (mesh.IndexCount - firstIndex) / 3;
        MegastationBayFacilityDiagnostics diagnostics = plan.Diagnostics with
        {
            MeshVertexCount = vertices,
            MeshTriangleCount = triangles,
            ShadowVertexCount = shadowParts * 24,
            ShadowTriangleCount = shadowParts * 12,
            MeshBytes = (long)vertices * 36L + (long)triangles * 3L * sizeof(int),
        };
        return new(firstFace, mesh.FaceCount - firstFace, diagnostics, illumination);
    }

    public static void ApplyLighting(
        StationModuleMesh mesh,
        MegastationBayFacilityMeshResult result,
        IReadOnlyList<MegastationArtificialLight> lights,
        MegastationArtificialOcclusion? occlusion)
    {
        for (int face = result.FirstFace; face < result.FirstFace + result.FaceCount; face++)
        {
            Vector3 normal = mesh.LocalFaceNormal(face);
            Vector3[] samples = mesh.GetFaceVertexPositions(face)
                .Select(position => MegastationArtificialLighting.Evaluate(
                    position, normal, lights, occlusion))
                .ToArray();
            mesh.SetFaceArtificialLight(face, samples);
        }
    }
}
