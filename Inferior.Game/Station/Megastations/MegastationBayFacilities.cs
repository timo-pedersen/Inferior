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
}

public enum MegastationBayFacilityPartRole
{
    StructuralFrame,
    GallerySlab,
    ApertureFrame,
    ApertureBacking,
    PersonnelDoor,
    Railing,
}

public enum MegastationBayFacilityColourRole
{
    Dominant,
    Secondary,
    Accent,
    Dark,
}

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
    IReadOnlyList<MegastationBayHabitationWindow>? Windows = null);

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
    string Signature);

public sealed record MegastationBayFacilityPlan(
    IReadOnlyList<MegastationBayFacility> Facilities,
    MegastationBayFacilityDiagnostics Diagnostics);

public static class MegastationBayFacilityPlanner
{
    public const int AlgorithmVersion = 1;

    public static MegastationBayFacilityPlan Plan(
        MegastationInteriorPlan interior,
        MegastationBayHabitationPlan habitation,
        StructuralOccupancy? occupancy = null,
        BoundaryTopology? topology = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var facilities = new List<MegastationBayFacility>();
        int rootSeed = MegastationSeed.Derive(interior.Seed, "bay-wall-facilities:v1");
        foreach (MegastationBayHabitationRegion region in habitation.Regions)
        {
            int seed = MegastationSeed.Derive(rootSeed, region.Identity);
            if (Unit(seed, "presence") >= .60f)
                continue;
            facilities.Add(Build(region, seed, forceRecess: facilities.Count == 0));
        }
        if (facilities.Count == 0 && habitation.Regions.Count > 0)
        {
            MegastationBayHabitationRegion selected = habitation.Regions
                .OrderBy(region => Unit(MegastationSeed.Derive(rootSeed, region.Identity),
                    "presence"))
                .First();
            facilities.Add(Build(selected, MegastationSeed.Derive(rootSeed, selected.Identity),
                forceRecess: true));
        }
        if (topology is not null && occupancy is not null
            && facilities.All(facility => facility.Cutout is null))
        {
            foreach (MegastationBayHabitationRegion region in habitation.Regions
                         .OrderBy(item => Unit(MegastationSeed.Derive(rootSeed, item.Identity),
                             "cutout-priority")))
            {
                int regionSeed = MegastationSeed.Derive(rootSeed, region.Identity);
                MegastationBayFacility candidate = Build(region, regionSeed,
                    forceRecess: true);
                if (candidate.Cutout is null)
                    continue;
                int existing = facilities.FindIndex(facility =>
                    facility.RegionIdentity == region.Identity);
                if (existing >= 0)
                    facilities[existing] = candidate;
                else
                    facilities.Add(candidate);
                break;
            }
        }
        if (topology is not null && occupancy is not null)
        {
            var supportingFaces = new HashSet<BoundaryFaceKey>();
            for (int index = 0; index < facilities.Count; index++)
            {
                MegastationBayWallCutout? cutout = facilities[index].Cutout;
                if (cutout is null || supportingFaces.Add(cutout.SupportingFace))
                    continue;
                MegastationBayHabitationRegion region = habitation.Regions.Single(item =>
                    item.Identity == facilities[index].RegionIdentity);
                MegastationBayWallSurface wall = habitation.Walls.Single(item =>
                    item.Identity == region.WallIdentity);
                int regionSeed = MegastationSeed.Derive(rootSeed, region.Identity);
                facilities[index] = BuildFacility(wall, region,
                    MegastationBayFacilityArchetype.ProjectingGallery,
                    secondary: false, regionSeed, occupancy, topology);
            }
        }
        if (facilities.Count == habitation.Regions.Count && facilities.Count > 1)
        {
            int removable = facilities.FindLastIndex(facility => facility.Cutout is null);
            facilities.RemoveAt(removable >= 0 ? removable : facilities.Count - 1);
        }

        stopwatch.Stop();
        string signature = Signature(facilities);
        var diagnostics = new MegastationBayFacilityDiagnostics(
            AlgorithmVersion,
            habitation.Regions.Count,
            facilities.Count,
            habitation.Regions.Count - facilities.Count,
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.RecessedFacility),
            facilities.Count(item => item.Archetype ==
                MegastationBayFacilityArchetype.ProjectingGallery),
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
            signature);
        return new(facilities, diagnostics);

        MegastationBayFacility Build(
            MegastationBayHabitationRegion region,
            int seed,
            bool forceRecess)
        {
            MegastationBayWallSurface wall = habitation.Walls.Single(candidate =>
                candidate.Identity == region.WallIdentity);
            MegastationBayFacilityArchetype archetype = forceRecess
                ? MegastationBayFacilityArchetype.RecessedFacility
                : PickArchetype(seed);
            bool secondary = archetype is MegastationBayFacilityArchetype.RecessedFacility
                    or MegastationBayFacilityArchetype.EmbeddedBlock
                && Unit(seed, "secondary") < .28f;
            return BuildFacility(wall, region, archetype, secondary, seed,
                occupancy, topology);
        }
    }

    private static MegastationBayFacilityArchetype PickArchetype(int seed)
    {
        float roll = Unit(seed, "primary-form");
        return roll < .36f ? MegastationBayFacilityArchetype.RecessedFacility
            : roll < .67f ? MegastationBayFacilityArchetype.ProjectingGallery
            : roll < .91f ? MegastationBayFacilityArchetype.EmbeddedBlock
            : MegastationBayFacilityArchetype.ServiceAperture;
    }

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
        float frameMargin = 1.5f + Unit(seed, "frame-margin") * 1.5f;
        float width = region.Size.X + frameMargin * 2f;
        float height = region.Size.Y + frameMargin * 2f;
        float frameThickness = archetype == MegastationBayFacilityArchetype.EmbeddedBlock
            ? 2.0f + Unit(seed, "frame-thickness") * 1.2f
            : .85f + Unit(seed, "frame-thickness") * .45f;
        float depth = archetype == MegastationBayFacilityArchetype.EmbeddedBlock
            ? 2.8f + Unit(seed, "depth") * 1.8f
            : .65f + Unit(seed, "depth") * .55f;

        if (archetype == MegastationBayFacilityArchetype.RecessedFacility
            && occupancy is not null && topology is not null)
        {
            cutout = BuildCutout(wall, region, seed, occupancy.Grid, topology);
            if (cutout is not null && Unit(seed, "cut-edge-frame") < .42f)
                AddPerimeter(MegastationBayFacilityPartRole.ApertureFrame,
                    new(Vector3.Dot(cutout.Centre - wall.Centre, wall.Right),
                        Vector3.Dot(cutout.Centre - wall.Centre, wall.Up)),
                    cutout.Size.X + .9f, cutout.Size.Y + .9f, .45f, .35f,
                    SystemMaterialFamilyId.HeavyIndustrialPlate,
                    "cut-edge/frame");
        }
        if (archetype == MegastationBayFacilityArchetype.RecessedFacility
            && cutout is null && occupancy is not null && topology is not null)
        {
            // A production recess is a structural cut or it is not a recess. Keep the
            // region useful without reintroducing the rejected picture-frame fallback.
            archetype = MegastationBayFacilityArchetype.ProjectingGallery;
            secondary = false;
        }
        if (archetype == MegastationBayFacilityArchetype.RecessedFacility
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
            || secondary;
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
            $"{region.Identity}/facility:v1", wall.Identity, region.Identity,
            archetype, wall.Right, wall.Up, wall.Normal,
            envelopeCentre, envelopeSize, maximumProjection, secondary, parts,
            cutout, windows);

        void AddRecessArchitecture(MegastationBayWallCutout opening)
        {
            float localX = Vector3.Dot(opening.Centre - wall.Centre, wall.Right);
            float localY = Vector3.Dot(opening.Centre - wall.Centre, wall.Up);
            int balconyCount = opening.Size.Y >= 14f
                ? 1 + (Unit(seed, "balcony-count") > .72f ? 1 : 0)
                : 0;
            float[] balconyLevels = Enumerable.Range(0, balconyCount)
                .Select(level => localY - opening.Size.Y * .5f
                    + (level + 1f) * opening.Size.Y / (balconyCount + 1f))
                .ToArray();
            int rows = 1 + (int)(Unit(seed, "recess-window-rows") * 5f);
            rows = Math.Clamp(rows, 1, 5);
            int columns = Math.Clamp((int)(opening.Size.X / 3.4f), 4, 14);
            float spacingX = MathF.Min(3.2f, (opening.Size.X - 4f) / Math.Max(1, columns - 1));
            float spacingY = MathF.Min(3.4f, (opening.Size.Y - 4.5f) / Math.Max(1, rows));
            float windowWidth = 1.35f;
            float windowHeight = 1.15f;
            float facadeZ = -opening.Depth + .045f;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int child = MegastationSeed.Derive(seed, $"recess-window:{row}:{column}");
                if (Unit(child, "gap") < .08f)
                    continue;
                MegastationWindowState state = Unit(child, "state") < .18f
                    ? MegastationWindowState.Dark
                    : Unit(child, "state") < .36f
                        ? MegastationWindowState.Dim
                        : MegastationWindowState.Lit;
                Color colour = state == MegastationWindowState.Dark
                    ? new Color(18, 20, 21)
                    : state == MegastationWindowState.Dim
                        ? Color.Lerp(region.DominantColour, Color.Black, .42f)
                        : region.DominantColour;
                float illumination = state == MegastationWindowState.Lit ? .58f
                    : state == MegastationWindowState.Dim ? .22f : .015f;
                float x = localX + (column - (columns - 1) * .5f) * spacingX;
                float y = localY + (row - (rows - 1) * .5f) * spacingY;
                if (balconyLevels.Any(level =>
                        MathF.Abs(y - (level + 1.45f)) < 1f))
                    continue;
                Vector3 centre = wall.Centre + wall.Right * x + wall.Up * y
                    + wall.Normal * facadeZ;
                windows.Add(new(
                    $"{region.Identity}/recess/window:{row}:{column}",
                    wall.Identity, region.Identity, MegastationBayWindowPattern.DoubleRow,
                    centre, wall.Normal, wall.Up, windowWidth, windowHeight,
                    state, colour, illumination));
            }

            for (int level = 0; level < balconyCount; level++)
            {
                float y = balconyLevels[level];
                float width = opening.Size.X * (.68f + Unit(seed, $"balcony-width:{level}") * .26f);
                float depth = MathF.Max(1.2f, opening.Depth - .45f);
                float slab = .22f;
                AddPart($"recess/balcony:{level}/slab",
                    MegastationBayFacilityPartRole.GallerySlab,
                    new(localX, y - slab * .5f, -opening.Depth + depth * .5f),
                    new(width, slab, depth), SystemMaterialFamilyId.HeavyIndustrialPlate,
                    MegastationBayFacilityColourRole.Secondary, true);
                AddGalleryRails($"recess/balcony:{level}", localX, y, width,
                    -opening.Depth + depth, depth,
                    exposedEnds: width < opening.Size.X - 1.2f);
                AddPart($"recess/balcony:{level}/door",
                    MegastationBayFacilityPartRole.PersonnelDoor,
                    new(localX, y + 1.2f, -opening.Depth + .10f),
                    new(1.25f, 2.4f, .20f), SystemMaterialFamilyId.CleanTechnicalAlloy,
                    MegastationBayFacilityColourRole.Dark, false);
                PlanBalconyWindows(opening, level, localX, y, width);
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
            for (int column = 0; column < columns; column++)
            {
                if (column == columns / 2)
                    continue;
                int child = MegastationSeed.Derive(seed,
                    $"balcony-window:{level}:{column}");
                MegastationWindowState state = Unit(child, "state") < .18f
                    ? MegastationWindowState.Dark : MegastationWindowState.Lit;
                Color colour = state == MegastationWindowState.Dark
                    ? new Color(18, 20, 21) : region.DominantColour;
                windows.Add(new(
                    $"{region.Identity}/recess/balcony:{level}/window:{column}",
                    wall.Identity, region.Identity,
                    MegastationBayWindowPattern.ObservationStrip,
                    wall.Centre
                        + wall.Right * (x + (column - (columns - 1) * .5f) * spacing)
                        + wall.Up * (floorY + 1.45f)
                        + wall.Normal * (-opening.Depth + .045f),
                    wall.Normal, wall.Up, 1.35f, 1.15f, state, colour,
                    state == MegastationWindowState.Lit ? .58f : .015f));
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
                $"{region.Identity}/facility:v1/{suffix}", role,
                SurfacePoint(localCentre.X, localCentre.Y, localCentre.Z),
                size, material, colour, casts));

        Vector3 SurfacePoint(float x, float y, float z)
            => wall.Centre + wall.Right * x + wall.Up * y + wall.Normal * z;
    }

    private static MegastationBayWallCutout? BuildCutout(
        MegastationBayWallSurface wall,
        MegastationBayHabitationRegion region,
        int seed,
        SliceGrid grid,
        BoundaryTopology topology)
    {
        var candidates = topology.Faces
            .Where(face => face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction), wall.Normal) > .9999f)
            .Select(face =>
            {
                Vector3[] points = face.Vertices
                    .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray();
                float plane = MathF.Abs(Vector3.Dot(points[0] - wall.Centre, wall.Normal));
                float minX = points.Min(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float maxX = points.Max(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float minY = points.Min(point => Vector3.Dot(point - wall.Centre, wall.Up));
                float maxY = points.Max(point => Vector3.Dot(point - wall.Centre, wall.Up));
                bool contains = region.Centre.X >= minX && region.Centre.X <= maxX
                    && region.Centre.Y >= minY && region.Centre.Y <= maxY;
                return (Face: face, Plane: plane, MinX: minX, MaxX: maxX,
                    MinY: minY, MaxY: maxY, Contains: contains);
            })
            .Where(item => item.Plane < .01f && item.Contains)
            .OrderByDescending(item =>
                (item.MaxX - item.MinX) * (item.MaxY - item.MinY))
            .ToArray();
        if (candidates.Length == 0)
            return null;

        var selected = candidates[0];
        float availableWidth = selected.MaxX - selected.MinX - 4f;
        float availableHeight = selected.MaxY - selected.MinY - 4f;
        if (availableWidth < 10f || availableHeight < 8f)
            return null;
        float width = MathF.Min(region.Size.X, availableWidth);
        float height = MathF.Min(MathF.Max(region.Size.Y, 10f), availableHeight);
        float x = MathHelper.Clamp(region.Centre.X,
            selected.MinX + 2f + width * .5f,
            selected.MaxX - 2f - width * .5f);
        float y = MathHelper.Clamp(region.Centre.Y,
            selected.MinY + 2f + height * .5f,
            selected.MaxY - 2f - height * .5f);
        float depth = 1f + Unit(seed, "recess-depth") * 4f;
        Vector3 centre = wall.Centre + wall.Right * x + wall.Up * y;
        return new(
            $"{region.Identity}/cutout:v1", selected.Face.Key, wall.Identity,
            centre, wall.Right, wall.Up, wall.Normal, new(width, height), depth);
    }

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static string Signature(IReadOnlyList<MegastationBayFacility> facilities)
    {
        var text = new StringBuilder().Append(AlgorithmVersion);
        foreach (MegastationBayFacility facility in facilities)
        {
            text.Append('|').Append(facility.Identity).Append(':')
                .Append(facility.Archetype).Append(':')
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
            mesh.AddOrientedBox(frame, part.Size, colour);
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
