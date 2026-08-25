using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationMegaGreebleFamily { SolarArray, ParabolicAntenna }
public enum MegastationSolarArchetype { SingleWing, DoubleWing, BroadCollector, SmallField }
public enum MegastationSolarForm { SurfaceArray, RadialSolarWing }
public enum MegastationSolarFoldOrientation { Radial, Transverse }
public enum MegastationDishArchetype { Supported, SurfaceMounted }
public enum MegastationMegaGreebleCasterFamily
{
    SurfaceArray,
    RadialSolarWing,
    SupportedDish,
    SurfaceMountedDish,
}

public interface IMegastationMegaGreebleParameters;

public sealed record MegastationSolarParameters(
    MegastationSolarForm Form, MegastationSolarArchetype Archetype,
    float Length, float Width, float SupportHeight,
    float MountAngleRadians, int SegmentCount, int PylonCount, bool OuterFrame,
    float AzimuthRadians, float RadialWingHeight, int AccordionFoldCount,
    MegastationSolarFoldOrientation FoldOrientation,
    float AccordionFoldDepth, float FrameThickness, float RootWidth,
    bool HasCentralSpine, bool PairedWing)
    : IMegastationMegaGreebleParameters
{
    public float RadialPivotHeight => MathF.Max(.45f, FrameThickness * 1.35f);
    public float RadialTotalProtrusion => SupportHeight + RadialPivotHeight + RadialWingHeight;
}

public sealed record MegastationDishParameters(
    MegastationDishArchetype Archetype, float Diameter, float Depth, float PedestalHeight,
    float TiltRadians, int RimSegments, int RadialRibs, int RingCount)
    : IMegastationMegaGreebleParameters;

public sealed record MegastationMegaGreebleInstance(
    string Identity, int Seed, MegastationMegaGreebleFamily Family,
    string SurfaceStableId, string ZoneId, MegastationZoneRole ZoneRole,
    Vector3 SurfacePosition, Vector3 Normal, Vector3 TangentU, Vector3 TangentV,
    float MinU, float MaxU, float MinV, float MaxV, float Protrusion,
    IMegastationMegaGreebleParameters Parameters, Color PrimaryColour,
    Color SecondaryColour, Color AccentColour, bool CastsShadow);

public sealed record MegastationMegaGreebleFamilyDiagnostics(
    int EligibleRegionCount, float EligibleArea, int CandidateCount, int AcceptedCount,
    int ExactMaskRejectCount, int G1RejectCount, int WindowRejectCount, int LightRejectCount,
    int G2RejectCount, int MegaGreebleRejectCount, int SuitabilityRejectCount,
    int OutwardClearanceRejectCount, int DensityRejectCount, int CapRejectCount);

public sealed record MegastationMegaGreebleDiagnostics(
    IReadOnlyDictionary<MegastationMegaGreebleFamily, MegastationMegaGreebleFamilyDiagnostics> ByFamily,
    int SolarSurfaceArrayCount, int SolarRadialWingCount,
    int SolarSingleWingCount, int SolarDoubleWingCount, int SolarBroadCollectorCount,
    int SolarSmallFieldCount, int SupportedDishCount, int SurfaceMountedDishCount,
    float SolarMinimumLength, float SolarMedianLength, float SolarMaximumLength,
    float RadialWingMinimumHeight, float RadialWingMedianHeight, float RadialWingMaximumHeight,
    float RadialWingMinimumWidth, float RadialWingMedianWidth, float RadialWingMaximumWidth,
    int RadialFoldOrientationCount, int TransverseFoldOrientationCount,
    float DishMinimumDiameter, float DishMedianDiameter, float DishMaximumDiameter,
    int VisibleVertexCount, int VisibleTriangleCount, long VisibleMeshBytes,
    int ShadowVertexCount, int ShadowTriangleCount, long ShadowMeshBytes,
    IReadOnlyList<MegastationShadowFamilyDiagnostics> ShadowByFamily,
    long PlanningMilliseconds, long MeshBuildMilliseconds,
    int OwnedTextureDelta, int GpuBufferDelta, string PlanSignature,
    string LargestInstanceIdentity, float LargestInstanceWidth, float LargestInstanceLength,
    float LargestInstanceProtrusion);

public sealed record MegastationMegaGreeblePlan(
    IReadOnlyList<MegastationMegaGreebleInstance> Instances,
    MegastationMegaGreebleDiagnostics Diagnostics);

public sealed record MegastationMegaGreebleMeshBuildResult(
    StationModuleMesh Mesh, MegastationMegaGreebleDiagnostics Diagnostics);

public static class MegastationMegaGreeblePlanner
{
    private const string AlgorithmKey = "mega-greeble:v1";
    private const int SolarCap = 40;
    private const int DishCap = 12;
    private const int TotalCap = 48;

    private sealed record Candidate(
        string Identity, int Seed, MegastationMegaGreebleFamily Family,
        MegastationPlanarRegion Region, float U, float V, float Priority,
        float MinU, float MaxU, float MinV, float MaxV, float Protrusion,
        IMegastationMegaGreebleParameters Parameters);

    private sealed class Counts
    {
        public int Regions, Candidates, Accepted, Exact, G1, Window, Light, G2, Other, Density, Cap;
        public int Suitability, Clearance;
        public float Area;
        public MegastationMegaGreebleFamilyDiagnostics Freeze() => new(
            Regions, Area, Candidates, Accepted, Exact, G1, Window, Light, G2, Other,
            Suitability, Clearance, Density, Cap);
    }

    public static MegastationMegaGreeblePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        CancellationToken cancellationToken = default)
        => Plan(regions, attachments, windows, lights, infrastructure, null, 1f, cancellationToken);

    public static MegastationMegaGreeblePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        MegastationUrbanStyle style,
        CancellationToken cancellationToken = default)
        => Plan(regions, attachments, windows, lights, infrastructure, null,
            Math.Clamp(style.OverallDensity, .85f, 1.20f), cancellationToken);

    public static MegastationMegaGreeblePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        StructuralOccupancy occupancy,
        MegastationUrbanStyle style,
        CancellationToken cancellationToken = default)
        => Plan(regions, attachments, windows, lights, infrastructure, occupancy,
            Math.Clamp(style.OverallDensity, .85f, 1.20f), cancellationToken);

    private static MegastationMegaGreeblePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        StructuralOccupancy? occupancy,
        float stationStyleDensity,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = Enum.GetValues<MegastationMegaGreebleFamily>()
            .ToDictionary(family => family, _ => new Counts());
        var candidates = new List<Candidate>();

        foreach (MegastationMegaGreebleFamily family in Enum.GetValues<MegastationMegaGreebleFamily>())
        foreach (MegastationPlanarRegion region in regions.OrderBy(r => r.StableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            float suitability = Suitability(family, region);
            if (suitability <= 0f) continue;
            Counts c = counts[family];
            c.Regions++;
            c.Area += region.PhysicalArea;
            float cell = family == MegastationMegaGreebleFamily.SolarArray ? 190f : 280f;
            int firstU = (int)MathF.Floor(region.MinU / cell);
            int lastU = (int)MathF.Ceiling(region.MaxU / cell) - 1;
            int firstV = (int)MathF.Floor(region.MinV / cell);
            int lastV = (int)MathF.Ceiling(region.MaxV / cell) - 1;
            int familySeed = MegastationSeed.Derive(
                MegastationSeed.Derive(region.ZoneSeed, AlgorithmKey), family.ToString());
            int surfaceSeed = MegastationSeed.Derive(familySeed, region.StableId);
            for (int cv = firstV; cv <= lastV; cv++)
            for (int cu = firstU; cu <= lastU; cu++)
            {
                int cellSeed = MegastationSeed.Derive(surfaceSeed, $"cell:{cu}:{cv}");
                MegastationSolarForm?[] forms = family == MegastationMegaGreebleFamily.SolarArray
                    ? [MegastationSolarForm.SurfaceArray, MegastationSolarForm.RadialSolarWing]
                    : [null];
                foreach (MegastationSolarForm? form in forms)
                {
                    c.Candidates++;
                    int seed = form.HasValue
                        ? MegastationSeed.Derive(cellSeed, $"form:{form.Value}")
                        : cellSeed;
                    float u = (cu + 0.5f) * cell + Signed(seed, "u") * cell * 0.25f;
                    float v = (cv + 0.5f) * cell + Signed(seed, "v") * cell * 0.25f;
                    IMegastationMegaGreebleParameters parameters = Parameters(family, seed, form);
                    float candidateSuitability = CandidateSuitability(
                        family, region, parameters, suitability);
                    if (candidateSuitability <= 0f)
                    {
                        c.Suitability++;
                        continue;
                    }
                    (float width, float length, float protrusion) = Envelope(parameters);
                    float minU = u - width * 0.5f;
                    float maxU = u + width * 0.5f;
                    float minV = v - length * 0.5f;
                    float maxV = v + length * 0.5f;
                    if (!MegastationPlanarRegionExtractor.ContainsFootprint(region,
                            minU, maxU, minV, maxV, 2f))
                    {
                        c.Exact++;
                        continue;
                    }
                    string formIdentity = form.HasValue ? $"/form:{form.Value}" : "";
                    string id = $"{region.StableId}/{AlgorithmKey}/{family}{formIdentity}/cell:{cu}:{cv}";
                    var candidate = new Candidate(id, seed, family, region, u, v,
                        candidateSuitability + Sample(seed, "priority") * 0.2f,
                        minU, maxU, minV, maxV, protrusion, parameters);
                    if (occupancy != null && !HasOutwardClearance(candidate, occupancy))
                    {
                        c.Clearance++;
                        continue;
                    }
                    // Radial candidates are already much rarer after their strict open-site
                    // gate, so their per-opportunity rate is higher without making them
                    // universal station-wide.
                    float formWeight = form == MegastationSolarForm.RadialSolarWing ? 1.50f : .58f;
                    float density = Density(family, region.ZoneRole) * candidateSuitability
                        * stationStyleDensity * (form.HasValue ? formWeight : 1f);
                    if (Sample(seed, "selected") >= density)
                    {
                        c.Density++;
                        continue;
                    }
                    candidates.Add(candidate);
                }
            }
        }

        var accepted = new List<MegastationMegaGreebleInstance>();
        foreach (Candidate candidate in candidates
                     .OrderByDescending(c => c.Priority)
                     .ThenBy(c => c.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Counts c = counts[candidate.Family];
            int familyCount = accepted.Count(i => i.Family == candidate.Family);
            int familyCap = candidate.Family == MegastationMegaGreebleFamily.SolarArray ? SolarCap : DishCap;
            if (accepted.Count >= TotalCap || familyCount >= familyCap) { c.Cap++; continue; }
            if (OverlapsG1(candidate, attachments)) { c.G1++; continue; }
            if (OverlapsWindow(candidate, windows.Windows)) { c.Window++; continue; }
            if (OverlapsLight(candidate, lights.Lights)) { c.Light++; continue; }
            if (OverlapsG2(candidate, infrastructure.Clusters)) { c.G2++; continue; }
            if (accepted.Any(instance => Overlaps(candidate, instance, 12f))) { c.Other++; continue; }

            Vector3 position = candidate.Region.OutwardNormal * candidate.Region.PlaneCoordinateMetres
                + candidate.Region.TangentU * candidate.U + candidate.Region.TangentV * candidate.V;
            (Color primary, Color secondary, Color accent) = Palette(candidate.Family, candidate.Seed);
            accepted.Add(new(candidate.Identity, candidate.Seed, candidate.Family,
                candidate.Region.StableId, candidate.Region.ZoneId, candidate.Region.ZoneRole,
                position, candidate.Region.OutwardNormal, candidate.Region.TangentU,
                candidate.Region.TangentV, candidate.MinU, candidate.MaxU, candidate.MinV,
                candidate.MaxV, candidate.Protrusion, candidate.Parameters,
                primary, secondary, accent, true));
            c.Accepted++;
        }

        MegastationMegaGreebleInstance[] ordered = accepted
            .OrderBy(instance => instance.Identity, StringComparer.Ordinal).ToArray();
        stopwatch.Stop();
        MegastationMegaGreebleDiagnostics diagnostics = Diagnostics(
            ordered, counts.ToDictionary(pair => pair.Key, pair => pair.Value.Freeze()),
            stopwatch.ElapsedMilliseconds);
        return new(ordered, diagnostics);
    }

    // Family registry/dispatch boundary: adding a family requires one explicit suitability,
    // density and parameter policy here, plus one emitter branch below.
    internal static float Suitability(MegastationMegaGreebleFamily family, MegastationPlanarRegion region)
    {
        float role = family switch
        {
            MegastationMegaGreebleFamily.SolarArray => region.ZoneRole switch
            {
                MegastationZoneRole.Utilities => 1f,
                MegastationZoneRole.Logistics => 0.85f,
                MegastationZoneRole.Industrial => 0.55f,
                MegastationZoneRole.Strategic => 0.12f,
                _ => 0f,
            },
            MegastationMegaGreebleFamily.ParabolicAntenna => region.ZoneRole switch
            {
                MegastationZoneRole.Strategic => 1f,
                MegastationZoneRole.Utilities => 0.72f,
                MegastationZoneRole.Industrial => 0.12f,
                _ => 0f,
            },
            _ => 0f,
        };
        if (role == 0f) return 0f;
        float open = Math.Clamp(region.Exposure * 0.45f + region.Prominence * 0.28f
            + region.Extremity * 0.22f - region.Concavity * 0.35f
            - region.RelativeDepth * 0.25f + 0.30f, 0f, 1f);
        float area = Math.Clamp(region.PhysicalArea / 25_000f, 0.15f, 1f);
        return Math.Clamp(role * (0.22f + open * 0.58f + area * 0.20f), 0f, 1f);
    }

    internal static float CandidateSuitability(
        MegastationMegaGreebleFamily family,
        MegastationPlanarRegion region,
        IMegastationMegaGreebleParameters parameters,
        float familySuitability)
    {
        if (family != MegastationMegaGreebleFamily.SolarArray
            || parameters is not MegastationSolarParameters solar)
            return familySuitability;

        // Both solar forms need open sky. The upright radial wing has a much larger
        // outward volume, so it deliberately demands a crown/terrace-like site.
        if (solar.Form == MegastationSolarForm.RadialSolarWing)
        {
            if (region.Exposure < 0.30f || region.Concavity > 0.32f
                || region.RelativeDepth > 0.58f)
                return 0f;
            float open = region.Exposure * 0.46f + region.Prominence * 0.30f
                + region.Extremity * 0.24f - region.Concavity * 0.32f
                - region.RelativeDepth * 0.24f;
            return Math.Clamp(familySuitability * (0.55f + open), 0f, 1f);
        }

        // Preserve some mildly recessed terraces, but reject obvious ravine floors and
        // enclosed canyon walls where the accepted flat array looked functionally buried.
        if (region.Exposure < 0.18f || region.RelativeDepth > 0.70f
            || (region.Concavity > 0.62f && region.RelativeDepth > 0.30f))
            return 0f;
        float surfaceOpen = region.Exposure * 0.32f + region.Prominence * 0.22f
            + region.Extremity * 0.16f - region.Concavity * 0.30f
            - region.RelativeDepth * 0.20f;
        return Math.Clamp(familySuitability * (0.58f + surfaceOpen), 0f, 1f);
    }

    private static float Density(MegastationMegaGreebleFamily family, MegastationZoneRole role)
        => family switch
        {
            MegastationMegaGreebleFamily.SolarArray => role switch
            {
                MegastationZoneRole.Utilities => 0.26f,
                MegastationZoneRole.Logistics => 0.20f,
                MegastationZoneRole.Industrial => 0.12f,
                _ => 0.03f,
            },
            _ => role switch
            {
                MegastationZoneRole.Strategic => 0.48f,
                MegastationZoneRole.Utilities => 0.22f,
                _ => 0.06f,
            },
        };

    private static IMegastationMegaGreebleParameters Parameters(
        MegastationMegaGreebleFamily family, int seed,
        MegastationSolarForm? requestedSolarForm = null)
    {
        if (family == MegastationMegaGreebleFamily.SolarArray)
        {
            bool radial = requestedSolarForm == MegastationSolarForm.RadialSolarWing;
            float roll = Sample(seed, "archetype");
            MegastationSolarArchetype archetype = roll < 0.18f ? MegastationSolarArchetype.SingleWing
                : roll < 0.70f ? MegastationSolarArchetype.DoubleWing
                : roll < 0.90f ? MegastationSolarArchetype.BroadCollector
                : MegastationSolarArchetype.SmallField;
            float length = archetype switch
            {
                MegastationSolarArchetype.BroadCollector => Lerp(24f, 48f, Sample(seed, "length")),
                MegastationSolarArchetype.SmallField => Lerp(42f, 72f, Sample(seed, "length")),
                _ => Lerp(30f, 70f, Sample(seed, "length")),
            };
            float width = archetype == MegastationSolarArchetype.BroadCollector
                ? Lerp(12f, 20f, Sample(seed, "width"))
                : Lerp(6f, 14f, Sample(seed, "width"));
            float supportHeight = Lerp(2.8f, 8f, Sample(seed, "support"));
            return new MegastationSolarParameters(
                radial ? MegastationSolarForm.RadialSolarWing : MegastationSolarForm.SurfaceArray,
                archetype, length, width,
                supportHeight,
                MathHelper.ToRadians(Lerp(-7f, 7f, Sample(seed, "angle"))),
                6 + (int)(Sample(seed, "segments") * 7f),
                archetype == MegastationSolarArchetype.SmallField ? 4 : 2,
                Sample(seed, "outer-frame") < 0.62f,
                Sample(seed, "azimuth") * MathF.Tau,
                width * Lerp(3f, 5f, Sample(seed, "radial-height"))
                    * Lerp(2f, 4f, Sample(seed, "radial-height-amendment")),
                7 + (int)(Sample(seed, "fold-count") * 7f),
                Sample(seed, "fold-orientation") < .5f
                    ? MegastationSolarFoldOrientation.Radial
                    : MegastationSolarFoldOrientation.Transverse,
                Lerp(0.45f, 1.25f, Sample(seed, "fold-depth")),
                Lerp(0.28f, 0.62f, Sample(seed, "frame-thickness")),
                Lerp(3.2f, 6.2f, Sample(seed, "root-width")),
                Sample(seed, "central-spine") < 0.68f,
                Sample(seed, "paired-wing") < 0.28f);
        }

        // Use stable candidate identity bits for the major form split. Keeping this
        // independent of the selection sample avoids sparse dish populations becoming
        // accidentally mono-archetype through correlated threshold tails.
        bool surface = ((uint)seed & 3u) == 0u;
        float diameter = surface
            ? Lerp(32f, 86f, Sample(seed, "diameter"))
            : Lerp(20f, Sample(seed, "exceptional") < 0.08f ? 100f : 72f,
                Sample(seed, "diameter"));
        return new MegastationDishParameters(
            surface ? MegastationDishArchetype.SurfaceMounted : MegastationDishArchetype.Supported,
            diameter, diameter * Lerp(0.10f, 0.22f, Sample(seed, "depth")),
            surface ? 0f : Lerp(5f, 15f, Sample(seed, "pedestal")),
            surface ? 0f : MathHelper.ToRadians(Lerp(4f, 24f, Sample(seed, "tilt"))),
            16 + (int)(Sample(seed, "segments") * 9f),
            6 + (int)(Sample(seed, "ribs") * 7f), 3);
    }

    private static (float Width, float Length, float Protrusion) Envelope(
        IMegastationMegaGreebleParameters parameters) => parameters switch
    {
        MegastationSolarParameters solar when solar.Form == MegastationSolarForm.RadialSolarWing
            => (solar.RootWidth, MathF.Max(4.2f, solar.FrameThickness * 5f),
                solar.RadialTotalProtrusion),
        MegastationSolarParameters solar => solar.Archetype == MegastationSolarArchetype.SmallField
            ? (solar.Width * 2.3f, solar.Length, solar.SupportHeight + solar.Width * 0.25f)
            : (solar.Width, solar.Length, solar.SupportHeight + solar.Width * 0.25f),
        MegastationDishParameters dish => dish.Archetype == MegastationDishArchetype.Supported
            ? (dish.Diameter + MathF.Sin(dish.TiltRadians) *
                    (dish.Depth + dish.Diameter * 0.65f),
                dish.Diameter,
                dish.PedestalHeight + dish.Diameter * 0.65f)
            : (dish.Diameter, dish.Diameter, dish.Depth + dish.Diameter * 0.08f),
        _ => throw new ArgumentOutOfRangeException(nameof(parameters)),
    };

    private static bool OverlapsG1(Candidate c, MegastationAttachmentPlan plan)
    {
        if (plan.Reservations.Any(r => Coplanar(c, r.Normal, r.PlaneCoordinateMetres)
            && Rects(c.MinU, c.MaxU, c.MinV, c.MaxV, r.MinU - 3f, r.MaxU + 3f, r.MinV - 3f, r.MaxV + 3f)))
            return true;
        (Vector3 min, Vector3 max) = Bounds(c);
        return plan.Placements.Any(p => AabbIntersects(min,max,p.AabbMin,p.AabbMax));
    }

    private static bool OverlapsWindow(Candidate c, IReadOnlyList<MegastationWindowInstance> windows)
        => windows.Any(w =>
        {
            if (!Coplanar(c, w.Normal, Vector3.Dot(w.Centre, w.Normal))) return false;
            Vector3 right = Vector3.Normalize(Vector3.Cross(w.Up, w.Normal));
            Vector3[] points =
            [
                w.Centre - right*w.Width/2 - w.Up*w.Height/2, w.Centre + right*w.Width/2 - w.Up*w.Height/2,
                w.Centre + right*w.Width/2 + w.Up*w.Height/2, w.Centre - right*w.Width/2 + w.Up*w.Height/2,
            ];
            float minU = points.Min(p => Vector3.Dot(p, c.Region.TangentU));
            float maxU = points.Max(p => Vector3.Dot(p, c.Region.TangentU));
            float minV = points.Min(p => Vector3.Dot(p, c.Region.TangentV));
            float maxV = points.Max(p => Vector3.Dot(p, c.Region.TangentV));
            return Rects(c.MinU,c.MaxU,c.MinV,c.MaxV,minU-2,maxU+2,minV-2,maxV+2);
        });

    private static bool OverlapsLight(Candidate c, IReadOnlyList<MegastationLightInstance> lights)
        => lights.Any(light => Coplanar(c, light.Normal, Vector3.Dot(light.SurfacePosition, light.Normal))
            && PointIn(c, Vector3.Dot(light.SurfacePosition, c.Region.TangentU),
                Vector3.Dot(light.SurfacePosition, c.Region.TangentV), 4f));

    private static bool OverlapsG2(Candidate c, IReadOnlyList<MegastationInfrastructureCluster> clusters)
        => clusters.Any(cluster => Coplanar(c, cluster.Normal,
                Vector3.Dot(cluster.SurfacePosition, cluster.Normal))
            && Rects(c.MinU,c.MaxU,c.MinV,c.MaxV,
                cluster.MinU-2,cluster.MaxU+2,cluster.MinV-2,cluster.MaxV+2));

    private static bool Overlaps(Candidate c, MegastationMegaGreebleInstance other, float margin)
    {
        bool footprint = Vector3.Dot(c.Region.OutwardNormal, other.Normal) > 0.999f
            && MathF.Abs(c.Region.PlaneCoordinateMetres - Vector3.Dot(other.SurfacePosition, other.Normal)) < 0.2f
            && Rects(c.MinU,c.MaxU,c.MinV,c.MaxV,other.MinU-margin,other.MaxU+margin,other.MinV-margin,other.MaxV+margin);
        (Vector3 cMin, Vector3 cMax) = Bounds(c);
        (Vector3 oMin, Vector3 oMax) = Bounds(other);
        return footprint || AabbIntersects(cMin, cMax, oMin - new Vector3(margin), oMax + new Vector3(margin));
    }

    private static bool Coplanar(Candidate c, Vector3 normal, float plane)
        => Vector3.Dot(c.Region.OutwardNormal, normal) > 0.999f
            && MathF.Abs(c.Region.PlaneCoordinateMetres - plane) < 0.2f;
    private static bool PointIn(Candidate c, float u, float v, float margin)
        => u >= c.MinU-margin && u <= c.MaxU+margin && v >= c.MinV-margin && v <= c.MaxV+margin;
    private static bool Rects(float a0,float a1,float b0,float b1,float c0,float c1,float d0,float d1)
        => a0 < c1 && a1 > c0 && b0 < d1 && b1 > d0;
    private static (Vector3 Min,Vector3 Max) Bounds(Candidate c)
    {
        Vector3 position=c.Region.OutwardNormal*c.Region.PlaneCoordinateMetres
            +c.Region.TangentU*c.U+c.Region.TangentV*c.V;
        if (c.Parameters is MegastationSolarParameters
            { Form: MegastationSolarForm.RadialSolarWing } solar)
            return RadialBounds(position, c.Region.OutwardNormal, c.Region.TangentU,
                c.Region.TangentV, solar);
        Vector3[] points=
        [
            position+c.Region.TangentU*(c.MinU-c.U)+c.Region.TangentV*(c.MinV-c.V),
            position+c.Region.TangentU*(c.MaxU-c.U)+c.Region.TangentV*(c.MinV-c.V),
            position+c.Region.TangentU*(c.MaxU-c.U)+c.Region.TangentV*(c.MaxV-c.V),
            position+c.Region.TangentU*(c.MinU-c.U)+c.Region.TangentV*(c.MaxV-c.V),
        ];
        points=[..points,..points.Select(p=>p+c.Region.OutwardNormal*c.Protrusion)];
        return (new(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),
            new(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z)));
    }
    private static (Vector3 Min,Vector3 Max) Bounds(MegastationMegaGreebleInstance instance)
    {
        if (instance.Parameters is MegastationSolarParameters
            { Form: MegastationSolarForm.RadialSolarWing } solar)
            return RadialBounds(instance.SurfacePosition, instance.Normal, instance.TangentU,
                instance.TangentV, solar);
        Vector3 hu=instance.TangentU*(instance.MaxU-instance.MinU)*.5f;
        Vector3 hv=instance.TangentV*(instance.MaxV-instance.MinV)*.5f;
        Vector3[] points=
        [
            instance.SurfacePosition-hu-hv,instance.SurfacePosition+hu-hv,
            instance.SurfacePosition+hu+hv,instance.SurfacePosition-hu+hv,
        ];
        points=[..points,..points.Select(p=>p+instance.Normal*instance.Protrusion)];
        return (new(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),
            new(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z)));
    }
    private static (Vector3 Min,Vector3 Max) RadialBounds(
        Vector3 root, Vector3 normal, Vector3 tangentU, Vector3 tangentV,
        MegastationSolarParameters solar)
    {
        Vector3 horizontal=Vector3.Normalize(tangentU*MathF.Cos(solar.AzimuthRadians)
            +tangentV*MathF.Sin(solar.AzimuthRadians));
        Vector3 depth=Vector3.Normalize(Vector3.Cross(normal,horizontal));
        float halfWidth=solar.Length*.5f;
        float halfDepth=MathF.Max(2.1f,solar.AccordionFoldDepth+solar.FrameThickness);
        Vector3 bottom=root;
        Vector3 top=root+normal*solar.RadialTotalProtrusion;
        Vector3[] points=
        [
            bottom-horizontal*halfWidth-depth*halfDepth,bottom+horizontal*halfWidth-depth*halfDepth,
            bottom+horizontal*halfWidth+depth*halfDepth,bottom-horizontal*halfWidth+depth*halfDepth,
            top-horizontal*halfWidth-depth*halfDepth,top+horizontal*halfWidth-depth*halfDepth,
            top+horizontal*halfWidth+depth*halfDepth,top-horizontal*halfWidth+depth*halfDepth,
        ];
        return (new(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),
            new(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z)));
    }

    private static bool HasOutwardClearance(Candidate candidate, StructuralOccupancy occupancy)
    {
        if (candidate.Parameters is not MegastationSolarParameters
            { Form: MegastationSolarForm.RadialSolarWing } solar)
            return true;
        Vector3 root=candidate.Region.OutwardNormal*candidate.Region.PlaneCoordinateMetres
            +candidate.Region.TangentU*candidate.U+candidate.Region.TangentV*candidate.V;
        Vector3 horizontal=Vector3.Normalize(candidate.Region.TangentU*MathF.Cos(solar.AzimuthRadians)
            +candidate.Region.TangentV*MathF.Sin(solar.AzimuthRadians));
        Vector3 depth=Vector3.Normalize(Vector3.Cross(candidate.Region.OutwardNormal,horizontal));
        float fullHeight=solar.RadialTotalProtrusion;
        // Scale clearance sampling with the amended sail height. Fixed four-level sampling
        // was adequate at ~50 m but could step completely over intervening mass once a sail
        // reaches 130-200+ m. This remains a bounded occupancy lookup, not a spatial search.
        int heightSteps=Math.Max(4,(int)MathF.Ceiling(fullHeight/20f));
        for(int heightStep=1;heightStep<=heightSteps;heightStep++)
        foreach(float widthFactor in new[]{-1f,-.5f,0f,.5f,1f})
        foreach(float depthFactor in new[]{-1f,0f,1f})
        {
            float heightFactor=heightStep/(float)heightSteps;
            Vector3 point=root+candidate.Region.OutwardNormal*(fullHeight*heightFactor)
                +horizontal*(solar.Length*.5f*widthFactor)
                +depth*(MathF.Max(2.1f,solar.AccordionFoldDepth+solar.FrameThickness)*depthFactor);
            if (OccupiedAt(occupancy, point)) return false;
        }
        return true;
    }

    private static bool OccupiedAt(StructuralOccupancy occupancy, Vector3 point)
    {
        SliceGrid grid=occupancy.Grid;
        int x=CellAt(grid,GridAxis.X,point.X), y=CellAt(grid,GridAxis.Y,point.Y),
            z=CellAt(grid,GridAxis.Z,point.Z);
        return x>=0&&y>=0&&z>=0&&occupancy.IsOccupied(x,y,z);
    }

    private static int CellAt(SliceGrid grid,GridAxis axis,float coordinate)
    {
        for(int i=0;i<grid.Count(axis);i++)
            if(coordinate>=grid.GetCellMinimum(axis,i)&&coordinate<grid.GetCellMaximum(axis,i))
                return i;
        return -1;
    }
    private static bool AabbIntersects(Vector3 a0,Vector3 a1,Vector3 b0,Vector3 b1)
        =>a0.X<b1.X&&a1.X>b0.X&&a0.Y<b1.Y&&a1.Y>b0.Y&&a0.Z<b1.Z&&a1.Z>b0.Z;

    private static (Color, Color, Color) Palette(MegastationMegaGreebleFamily family, int seed)
    {
        int variant = (int)(Sample(seed, "palette") * 3f);
        return family == MegastationMegaGreebleFamily.SolarArray
            ? variant switch
            {
                0 => (new Color(34,46,63), new Color(128,133,126), new Color(176,118,48)),
                1 => (new Color(26,42,57), new Color(157,151,132), new Color(93,115,128)),
                _ => (new Color(42,37,52), new Color(108,117,120), new Color(151,92,48)),
            }
            : variant switch
            {
                0 => (new Color(151,148,133), new Color(75,78,76), new Color(184,126,55)),
                1 => (new Color(105,116,123), new Color(48,54,58), new Color(169,160,132)),
                _ => (new Color(128,109,80), new Color(57,57,54), new Color(187,176,143)),
            };
    }

    private static MegastationMegaGreebleDiagnostics Diagnostics(
        IReadOnlyList<MegastationMegaGreebleInstance> instances,
        IReadOnlyDictionary<MegastationMegaGreebleFamily, MegastationMegaGreebleFamilyDiagnostics> byFamily,
        long planningMs)
    {
        float[] solar = instances.Where(i => i.Parameters is MegastationSolarParameters)
            .Select(i => ((MegastationSolarParameters)i.Parameters).Length).Order().ToArray();
        float[] radialHeights = instances.Where(i => i.Parameters is MegastationSolarParameters
                { Form: MegastationSolarForm.RadialSolarWing })
            .Select(i => ((MegastationSolarParameters)i.Parameters).RadialWingHeight)
            .Order().ToArray();
        float[] radialWidths = instances.Where(i => i.Parameters is MegastationSolarParameters
                { Form: MegastationSolarForm.RadialSolarWing })
            .Select(i => ((MegastationSolarParameters)i.Parameters).Length)
            .Order().ToArray();
        float[] dish = instances.Where(i => i.Parameters is MegastationDishParameters)
            .Select(i => ((MegastationDishParameters)i.Parameters).Diameter).Order().ToArray();
        MegastationMegaGreebleInstance? largest = instances.OrderByDescending(i =>
            PhysicalDimensions(i).Width*PhysicalDimensions(i).Length).FirstOrDefault();
        (float Width,float Length,float Protrusion) largestDimensions = largest == null
            ? default : PhysicalDimensions(largest);
        return new(byFamily,
            instances.Count(i=>i.Parameters is MegastationSolarParameters { Form: MegastationSolarForm.SurfaceArray }),
            instances.Count(i=>i.Parameters is MegastationSolarParameters { Form: MegastationSolarForm.RadialSolarWing }),
            CountSolar(MegastationSolarArchetype.SingleWing), CountSolar(MegastationSolarArchetype.DoubleWing),
            CountSolar(MegastationSolarArchetype.BroadCollector), CountSolar(MegastationSolarArchetype.SmallField),
            CountDish(MegastationDishArchetype.Supported), CountDish(MegastationDishArchetype.SurfaceMounted),
            Min(solar), Median(solar), Max(solar),
            Min(radialHeights),Median(radialHeights),Max(radialHeights),
            Min(radialWidths),Median(radialWidths),Max(radialWidths),
            instances.Count(i=>i.Parameters is MegastationSolarParameters
                { Form: MegastationSolarForm.RadialSolarWing,
                  FoldOrientation: MegastationSolarFoldOrientation.Radial }),
            instances.Count(i=>i.Parameters is MegastationSolarParameters
                { Form: MegastationSolarForm.RadialSolarWing,
                  FoldOrientation: MegastationSolarFoldOrientation.Transverse }),
            Min(dish), Median(dish), Max(dish),
            0,0,0,0,0,0, [], planningMs,0,0,0, Signature(instances), largest?.Identity ?? "none",
            largestDimensions.Width,largestDimensions.Length,largestDimensions.Protrusion);

        int CountSolar(MegastationSolarArchetype a) => instances.Count(i =>
            i.Parameters is MegastationSolarParameters
                { Form: MegastationSolarForm.SurfaceArray } p && p.Archetype == a);
        int CountDish(MegastationDishArchetype a) => instances.Count(i => i.Parameters is MegastationDishParameters p && p.Archetype == a);
    }

    private static (float Width,float Length,float Protrusion) PhysicalDimensions(
        MegastationMegaGreebleInstance instance)=>instance.Parameters switch
    {
        MegastationSolarParameters { Form: MegastationSolarForm.RadialSolarWing } solar
            =>(solar.Length,MathF.Max(4.2f,solar.AccordionFoldDepth*2f),
                solar.RadialTotalProtrusion),
        MegastationSolarParameters solar=>(solar.Width,solar.Length,instance.Protrusion),
        MegastationDishParameters dish=>(dish.Diameter,dish.Diameter,instance.Protrusion),
        _=>(instance.MaxU-instance.MinU,instance.MaxV-instance.MinV,instance.Protrusion),
    };

    private static string Signature(IReadOnlyList<MegastationMegaGreebleInstance> instances)
    {
        var text = new StringBuilder();
        foreach (var i in instances.OrderBy(i => i.Identity, StringComparer.Ordinal))
            text.Append(i.Identity).Append('|').Append(i.Seed).Append('|').Append(i.Family).Append('|')
                .Append(i.MinU.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(i.MaxU.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(i.MinV.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(i.MaxV.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(i.Parameters).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static float Min(float[] values) => values.Length == 0 ? 0 : values[0];
    private static float Max(float[] values) => values.Length == 0 ? 0 : values[^1];
    private static float Median(float[] values) => values.Length == 0 ? 0 : values[values.Length/2];
    private static float Sample(int seed, string key) => unchecked((uint)MegastationSeed.Derive(seed,key))/(float)uint.MaxValue;
    private static float Signed(int seed, string key) => Sample(seed,key)*2f-1f;
    private static float Lerp(float a,float b,float t) => a+(b-a)*t;
}

public static class MegastationMegaGreebleMeshBuilder
{
    public static MegastationMegaGreebleMeshBuildResult Build(
        MegastationMegaGreeblePlan plan, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var mesh = new StationModuleMesh();
        var forms = Enum.GetValues<MegastationMegaGreebleCasterFamily>();
        var familyRanges = forms.ToDictionary(form => form,
            _ => new List<(int indexStart, int indexCount)>());
        var visibleTriangles = forms.ToDictionary(form => form, _ => 0);
        var casterInstances = forms.ToDictionary(form => form, _ => 0);
        foreach (MegastationMegaGreebleInstance instance in plan.Instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MegastationMegaGreebleCasterFamily form =
                MegastationMegaGreebleEmitters.CasterFamily(instance);
            int rangeStart = mesh.DecorClassRanges.Count;
            int indexStart = mesh.IndexCount;
            mesh.BreakDecorClassRange();
            MegastationMegaGreebleEmitters.Emit(instance, mesh);
            visibleTriangles[form] += (mesh.IndexCount - indexStart) / 3;
            var casterRanges = mesh.DecorClassRanges.Skip(rangeStart)
                .Where(range => range.decorClass == DecorClass.MegastationMegaGreebleMajor)
                .Select(range => (range.indexStart, range.indexCount))
                .ToArray();
            if (casterRanges.Length > 0)
            {
                casterInstances[form]++;
                familyRanges[form].AddRange(casterRanges);
            }
        }
        mesh.ApplyIlluminationFlags();
        StationMeshCpuData? shadow = mesh.PrepareIndexRanges(mesh.DecorClassRanges
            .Where(r => r.decorClass == DecorClass.MegastationMegaGreebleMajor)
            .Select(r => (r.indexStart,r.indexCount)).ToArray());
        stopwatch.Stop();
        int sv = shadow?.Vertices.Length ?? 0;
        int si = shadow?.Indices.Length ?? 0;
        MegastationShadowFamilyDiagnostics[] shadowByFamily = forms.Select(form =>
        {
            StationMeshCpuData? familyShadow = mesh.PrepareIndexRanges(familyRanges[form]);
            return new MegastationShadowFamilyDiagnostics(
                form.ToString(), MegastationMegaGreebleEmitters.ShadowPolicies[form],
                plan.Instances.Count(instance =>
                    MegastationMegaGreebleEmitters.CasterFamily(instance) == form),
                casterInstances[form], visibleTriangles[form],
                familyShadow?.Vertices.Length ?? 0,
                (familyShadow?.Indices.Length ?? 0) / 3);
        }).ToArray();
        var diagnostics = plan.Diagnostics with
        {
            VisibleVertexCount=mesh.VertexCount, VisibleTriangleCount=mesh.IndexCount/3,
            VisibleMeshBytes=Bytes(mesh.VertexCount,mesh.IndexCount),
            ShadowVertexCount=sv, ShadowTriangleCount=si/3, ShadowMeshBytes=Bytes(sv,si),
            ShadowByFamily=shadowByFamily,
            MeshBuildMilliseconds=stopwatch.ElapsedMilliseconds,
            OwnedTextureDelta=0, GpuBufferDelta=mesh.IsEmpty ? 0 : 4,
        };
        return new(mesh,diagnostics);
    }
    private static long Bytes(int v,int i)=>(long)v*36L+(long)i*4L;
}

internal static class MegastationMegaGreebleDebug
{
    public static VertexPositionColor[] BuildLines(MegastationMegaGreeblePlan plan)
    {
        var lines = new List<VertexPositionColor>();
        foreach (MegastationMegaGreebleInstance instance in plan.Instances)
        {
            Color colour = instance.Parameters switch
            {
                MegastationSolarParameters { Form: MegastationSolarForm.SurfaceArray } => Color.Yellow,
                MegastationSolarParameters { Form: MegastationSolarForm.RadialSolarWing } => Color.Cyan,
                _ => Color.Magenta,
            };
            float plane = Vector3.Dot(instance.SurfacePosition, instance.Normal) + .3f;
            Vector3 Point(float u,float v) => instance.Normal*plane+instance.TangentU*u+instance.TangentV*v;
            Vector3 p0=Point(instance.MinU,instance.MinV), p1=Point(instance.MaxU,instance.MinV);
            Vector3 p2=Point(instance.MaxU,instance.MaxV), p3=Point(instance.MinU,instance.MaxV);
            Add(p0,p1); Add(p1,p2); Add(p2,p3); Add(p3,p0);
            Vector3 centre=instance.SurfacePosition+instance.Normal*.3f;
            Add(centre,centre+instance.Normal*instance.Protrusion);
            void Add(Vector3 a,Vector3 b){lines.Add(new(a,colour));lines.Add(new(b,colour));}
        }
        return lines.ToArray();
    }
}

public static class MegastationMegaGreebleEmitters
{
    public static IReadOnlyDictionary<MegastationMegaGreebleCasterFamily, MegastationShadowPolicy>
        ShadowPolicies { get; } = Enum.GetValues<MegastationMegaGreebleCasterFamily>()
            .ToDictionary(form => form, _ => MegastationShadowPolicy.Simplified);

    public static MegastationMegaGreebleCasterFamily CasterFamily(
        MegastationMegaGreebleInstance instance) => instance.Parameters switch
    {
        MegastationSolarParameters { Form: MegastationSolarForm.SurfaceArray } =>
            MegastationMegaGreebleCasterFamily.SurfaceArray,
        MegastationSolarParameters { Form: MegastationSolarForm.RadialSolarWing } =>
            MegastationMegaGreebleCasterFamily.RadialSolarWing,
        MegastationDishParameters { Archetype: MegastationDishArchetype.Supported } =>
            MegastationMegaGreebleCasterFamily.SupportedDish,
        MegastationDishParameters { Archetype: MegastationDishArchetype.SurfaceMounted } =>
            MegastationMegaGreebleCasterFamily.SurfaceMountedDish,
        _ => throw new ArgumentOutOfRangeException(nameof(instance)),
    };

    public static void Emit(MegastationMegaGreebleInstance instance, StationModuleMesh mesh)
    {
        Frame frame = Frame.Create(instance);
        switch (instance.Parameters)
        {
            case MegastationSolarParameters solar: EmitSolar(frame, instance, solar, mesh); break;
            case MegastationDishParameters dish: EmitDish(frame, instance, dish, mesh); break;
            default: throw new ArgumentOutOfRangeException(nameof(instance));
        }
    }

    private static void EmitSolar(Frame f, MegastationMegaGreebleInstance i,
        MegastationSolarParameters p, StationModuleMesh mesh)
    {
        if(p.Form==MegastationSolarForm.RadialSolarWing)
            EmitRadialSolarWing(f,i,p,mesh);
        else
            EmitSurfaceSolar(f,i,p,mesh);
    }

    // Accepted SurfaceArray emitter. Keep this geometry stable; placement policy is
    // tightened independently in CandidateSuitability above.
    private static void EmitSurfaceSolar(Frame f, MegastationMegaGreebleInstance i,
        MegastationSolarParameters p, StationModuleMesh mesh)
    {
        Vector3 wingAxis = f.V;
        Vector3 widthAxis = f.U;
        Vector3 panelNormal = Vector3.Normalize(f.N*MathF.Cos(p.MountAngleRadians)+widthAxis*MathF.Sin(p.MountAngleRadians));
        widthAxis = Vector3.Normalize(Vector3.Cross(wingAxis,panelNormal));
        float fieldOffset = p.Archetype == MegastationSolarArchetype.SmallField ? p.Width*0.65f : 0f;
        int rows = p.Archetype == MegastationSolarArchetype.SmallField ? 3 : 1;
        for(int row=0;row<rows;row++)
        {
            Vector3 root=f.O+widthAxis*((row-(rows-1)*.5f)*fieldOffset);
            AddSolarInstallation(root, wingAxis, widthAxis, panelNormal, i, p, mesh);
        }
    }

    private static void EmitRadialSolarWing(Frame f,MegastationMegaGreebleInstance i,
        MegastationSolarParameters p,StationModuleMesh mesh)
    {
        Vector3 horizontal=Vector3.Normalize(f.U*MathF.Cos(p.AzimuthRadians)
            +f.V*MathF.Sin(p.AzimuthRadians));
        Vector3 front=Vector3.Normalize(Vector3.Cross(f.N,horizontal));
        float rootDepth=MathF.Max(3.8f,p.FrameThickness*5f);
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        Vector3 rootCentre=f.O+f.N*(p.SupportHeight*.5f);
        // The foundation is installed square to the station architecture. Seeded azimuth
        // begins above it at the turntable; only the collector assembly rotates.
        mesh.AddOrientedBox(FrameMatrix(f.U,f.V,f.N,rootCentre),
            new Vector3(p.RootWidth,rootDepth,p.SupportHeight),i.SecondaryColour);

        float turntableRadius=MathF.Max(1.05f,p.RootWidth*.28f);
        float turntableHeight=p.RadialPivotHeight;
        Vector3 turntableBottom=f.O+f.N*(p.SupportHeight-turntableHeight*.25f);
        Vector3 pivot=f.O+f.N*(p.SupportHeight+turntableHeight);
        mesh.AddPrismPipe(turntableBottom,pivot,turntableRadius,8,i.AccentColour,true,true);
        mesh.AddOrientedBox(FrameMatrix(horizontal,front,f.N,pivot),
            new Vector3(MathF.Max(p.RootWidth*.82f,turntableRadius*1.6f),
                MathF.Max(.75f,p.FrameThickness*2.1f),turntableHeight),i.SecondaryColour);

        float gap=p.PairedWing?MathF.Max(2.2f,p.RootWidth*.62f):0f;
        int wingCount=p.PairedWing?2:1;
        float eachWidth=p.PairedWing?(p.Length-gap)*.5f:p.Length;
        for(int wing=0;wing<wingCount;wing++)
        {
            float offset=p.PairedWing?(wing==0?-(gap+eachWidth)*.5f:(gap+eachWidth)*.5f):0f;
            Vector3 centre=pivot+horizontal*offset+f.N*(p.RadialWingHeight*.5f);
            EmitRadialCollector(centre,horizontal,f.N,front,eachWidth,p.RadialWingHeight,
                i,p,mesh);
        }

        // Root-to-frame mast is part of the simplified caster and makes the sail read
        // as mechanically connected rather than balanced on a point.
        Vector3 mastBottom=pivot;
        Vector3 mastTop=pivot+f.N*(p.RadialWingHeight*.5f);
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        mesh.AddPrismPipe(mastBottom,mastTop,MathF.Max(.32f,p.FrameThickness*.75f),6,
            i.SecondaryColour,true,true);
        Vector3 yokeCentre=pivot+f.N*(p.FrameThickness*.65f);
        mesh.AddOrientedBox(FrameMatrix(horizontal,front,f.N,yokeCentre),
            new Vector3(p.PairedWing?gap+p.FrameThickness*3f:p.RootWidth,
                MathF.Max(.6f,p.FrameThickness*1.8f),p.FrameThickness),i.SecondaryColour);
    }

    private static void EmitRadialCollector(Vector3 centre,Vector3 horizontal,Vector3 vertical,
        Vector3 front,float width,float height,MegastationMegaGreebleInstance i,
        MegastationSolarParameters p,StationModuleMesh mesh)
    {
        float frame=p.FrameThickness;
        float shell=.16f;
        Color back=Darken(i.PrimaryColour,.62f);

        // Flat physical back plate doubles as the deliberately simplified collector
        // shadow caster; accordion folds remain visible-only minor geometry.
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        Vector3 backCentre=centre-front*(p.AccordionFoldDepth*.5f+shell*.5f);
        mesh.AddOrientedBox(FrameMatrix(horizontal,vertical,front,backCentre),
            new Vector3(width-frame*2f,height-frame*2f,shell),back);

        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMinor;
        float innerWidth=width-frame*2f, innerHeight=height-frame*2f;
        bool radialFolds=p.FoldOrientation==MegastationSolarFoldOrientation.Radial;
        Vector3 variationAxis=radialFolds?horizontal:vertical;
        Vector3 ridgeAxis=radialFolds?vertical:horizontal;
        float variationSpan=radialFolds?innerWidth:innerHeight;
        float ridgeSpan=radialFolds?innerHeight:innerWidth;
        float segment=variationSpan/p.AccordionFoldCount;
        float start=-variationSpan*.5f;
        for(int fold=0;fold<p.AccordionFoldCount;fold++)
        {
            float x0=start+fold*segment, x1=x0+segment;
            float d0=(fold&1)==0?-p.AccordionFoldDepth*.5f:p.AccordionFoldDepth*.5f;
            float d1=(fold&1)==0?p.AccordionFoldDepth*.5f:-p.AccordionFoldDepth*.5f;
            Vector3 a=centre+variationAxis*x0-ridgeAxis*ridgeSpan*.5f+front*d0;
            Vector3 b=centre+variationAxis*x1-ridgeAxis*ridgeSpan*.5f+front*d1;
            Vector3 c=centre+variationAxis*x1+ridgeAxis*ridgeSpan*.5f+front*d1;
            Vector3 d=centre+variationAxis*x0+ridgeAxis*ridgeSpan*.5f+front*d0;
            AddQuadFacing(mesh,a,b,c,d,front,i.PrimaryColour);
            // End closures make either corrugation orientation physical at its exposed edge.
            Vector3 backA=a-front*(d0+p.AccordionFoldDepth*.5f+shell);
            Vector3 backB=b-front*(d1+p.AccordionFoldDepth*.5f+shell);
            AddQuadFacing(mesh,a,backA,backB,b,-ridgeAxis,i.SecondaryColour);
            Vector3 topA=d-front*(d0+p.AccordionFoldDepth*.5f+shell);
            Vector3 topB=c-front*(d1+p.AccordionFoldDepth*.5f+shell);
            AddQuadFacing(mesh,d,c,topB,topA,ridgeAxis,i.SecondaryColour);
        }

        // Rectangular perimeter only: no cross-bracing over the collector face.
        foreach(float side in new[]{-1f,1f})
        {
            Vector3 sideCentre=centre+horizontal*side*(width-frame)*.5f;
            mesh.AddOrientedBox(FrameMatrix(horizontal,vertical,front,sideCentre),
                new Vector3(frame,height,MathF.Max(frame,p.AccordionFoldDepth+shell)),i.SecondaryColour);
        }
        foreach(float side in new[]{-1f,1f})
        {
            Vector3 edgeCentre=centre+vertical*side*(height-frame)*.5f;
            mesh.AddOrientedBox(FrameMatrix(horizontal,vertical,front,edgeCentre),
                new Vector3(width,frame,MathF.Max(frame,p.AccordionFoldDepth+shell)),i.SecondaryColour);
        }
        if(p.HasCentralSpine)
            mesh.AddOrientedBox(FrameMatrix(horizontal,vertical,front,centre),
                new Vector3(frame*.82f,height-frame*2f,MathF.Max(frame,p.AccordionFoldDepth+shell)),i.AccentColour);
    }

    private static void AddSolarInstallation(Vector3 root,Vector3 axis,Vector3 widthAxis,Vector3 normal,
        MegastationMegaGreebleInstance i,MegastationSolarParameters p,StationModuleMesh mesh)
    {
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        Vector3 hub=root+normal*(p.SupportHeight*.5f);
        mesh.AddOrientedBox(FrameMatrix(widthAxis,axis,normal,hub),new Vector3(3.2f,4.2f,p.SupportHeight),i.SecondaryColour);
        Vector3 panelCentre=root+normal*p.SupportHeight;
        bool doubleWing=p.Archetype is MegastationSolarArchetype.DoubleWing or MegastationSolarArchetype.SmallField;
        float clear=doubleWing?3.2f:1.5f;
        int wings=doubleWing?2:1;
        for(int w=0;w<wings;w++)
        {
            float sign=doubleWing?(w==0?-1:1):1;
            float wingLength=doubleWing?(p.Length-clear*2)*.5f:p.Length-clear;
            float start=doubleWing?clear:clear*.5f;
            Vector3 centre=panelCentre+axis*sign*(start+wingLength*.5f);
            float segmentLength=wingLength/p.SegmentCount;
            for(int s=0;s<p.SegmentCount;s++)
            {
                Vector3 sc=centre+axis*sign*((s-(p.SegmentCount-1)*.5f)*segmentLength);
                mesh.AddOrientedBox(FrameMatrix(widthAxis,axis,normal,sc),
                    new Vector3(p.Width,segmentLength*.92f,.18f),i.PrimaryColour);
            }
            mesh.AddPrismPipe(panelCentre, panelCentre+axis*sign*(start+wingLength),.28f,6,i.SecondaryColour,true,true);
            mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMinor;
            for(int s=0;s<=p.SegmentCount;s++)
            {
                Vector3 rib=panelCentre+axis*sign*(start+s*segmentLength);
                mesh.AddOrientedBox(rib,widthAxis,p.Width+.4f,.16f,.16f,i.AccentColour);
            }
            if(p.OuterFrame)
                foreach(float side in new[]{-1f,1f})
                    mesh.AddPrismPipe(panelCentre+widthAxis*side*p.Width*.5f+axis*sign*start,
                        panelCentre+widthAxis*side*p.Width*.5f+axis*sign*(start+wingLength),.12f,4,i.SecondaryColour,true,true);
            mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        }
    }

    private static void EmitDish(Frame f,MegastationMegaGreebleInstance i,
        MegastationDishParameters p,StationModuleMesh mesh)
    {
        if(p.Archetype==MegastationDishArchetype.SurfaceMounted) EmitSurfaceDish(f,i,p,mesh);
        else EmitSupportedDish(f,i,p,mesh);
    }

    private static void EmitSupportedDish(Frame f,MegastationMegaGreebleInstance i,
        MegastationDishParameters p,StationModuleMesh mesh)
    {
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        Vector3 axis=Vector3.Normalize(f.N*MathF.Cos(p.TiltRadians)+f.U*MathF.Sin(p.TiltRadians));
        Vector3 right=Vector3.Normalize(Vector3.Cross(f.V,axis));
        Vector3 up=Vector3.Normalize(Vector3.Cross(axis,right));
        float r=p.Diameter*.5f;
        Vector3 pedestalTop=f.O+f.N*p.PedestalHeight;
        mesh.AddPrismPipe(f.O+f.N*.15f,pedestalTop,p.Diameter*.055f,10,i.SecondaryColour,true,true);
        Vector3 vertex=pedestalTop+axis*(r*.14f);
        DishShell shell=AddParaboloid(mesh,vertex,axis,right,up,r,p.Depth,p.RimSegments,3,
            i.PrimaryColour,i.SecondaryColour);
        // Coarse rear structure connects the pedestal to the reflector's physical shell.
        Vector3 rearHub=shell.BackTip-axis*(r*.035f);
        mesh.AddPrismPipe(pedestalTop,rearHub,MathF.Max(.34f,r*.030f),8,
            i.SecondaryColour,true,true);
        mesh.AddOrientedBox(rearHub,axis,MathF.Max(1.2f,r*.10f),
            MathF.Max(1.1f,r*.12f),MathF.Max(1.1f,r*.12f),i.SecondaryColour);
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMinor;
        Vector3[] rearSupportRing=shell.BackRings[Math.Min(2,shell.BackRings.Length-1)];
        int supportCount=4;
        for(int support=0;support<supportCount;support++)
        {
            int index=support*rearSupportRing.Length/supportCount;
            mesh.AddPrismPipe(rearHub,rearSupportRing[index],MathF.Max(.18f,r*.014f),5,
                i.SecondaryColour,true,true);
        }
        Vector3 receiver=shell.FrontTip+axis*(p.Depth+r*.42f);
        mesh.AddPrismPipe(shell.FrontTip,receiver,r*.025f,5,i.SecondaryColour,true,true);
        mesh.AddOrientedBox(receiver,axis,r*.12f,r*.10f,r*.10f,i.AccentColour);
        for(int s=0;s<3;s++)
        {
            Vector3 rim=shell.FrontRings[^1][(int)(s/3f*shell.FrontRings[^1].Length)];
            mesh.AddPrismPipe(rim,receiver,r*.012f,4,i.SecondaryColour,true,true);
        }
    }

    private static void EmitSurfaceDish(Frame f,MegastationMegaGreebleInstance i,
        MegastationDishParameters p,StationModuleMesh mesh)
    {
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMajor;
        float r=p.Diameter*.5f;
        float shellThickness=MathF.Max(.25f,r*.012f);
        Vector3 vertex=f.O+f.N*(shellThickness+.25f);
        DishShell shell=AddParaboloid(mesh,vertex,f.N,f.U,f.V,r,p.Depth,p.RimSegments,p.RingCount,
            i.PrimaryColour,i.SecondaryColour);
        Vector3 rimCentre=vertex+f.N*p.Depth;
        for(int s=0;s<p.RimSegments;s++)
        {
            float a0=s*MathF.Tau/p.RimSegments, a1=(s+1)*MathF.Tau/p.RimSegments;
            Vector3 a=rimCentre+f.U*MathF.Cos(a0)*r+f.V*MathF.Sin(a0)*r;
            Vector3 b=rimCentre+f.U*MathF.Cos(a1)*r+f.V*MathF.Sin(a1)*r;
            mesh.AddPrismPipe(a,b,MathF.Max(.5f,r*.035f),6,i.SecondaryColour,true,true);
        }
        Vector3 baseHub=f.O+f.N*.18f;
        mesh.AddPrismPipe(baseHub,shell.BackTip,MathF.Max(.35f,r*.020f),8,
            i.SecondaryColour,true,true);
        for(int support=0;support<4;support++)
        {
            int index=support*shell.BackRings[^1].Length/4;
            Vector3 rimBack=shell.BackRings[^1][index];
            Vector3 basePoint=f.O+f.U*Vector3.Dot(rimBack-f.O,f.U)*.92f
                +f.V*Vector3.Dot(rimBack-f.O,f.V)*.92f+f.N*.18f;
            mesh.AddPrismPipe(basePoint,rimBack,MathF.Max(.25f,r*.016f),6,
                i.SecondaryColour,true,true);
        }
        mesh.CurrentDecorClass=DecorClass.MegastationMegaGreebleMinor;
        Vector3 hub=vertex+f.N*(p.Depth*.35f);
        mesh.AddOrientedBox(hub,f.N,MathF.Max(1.5f,r*.10f),MathF.Max(1.5f,r*.12f),MathF.Max(1.5f,r*.12f),i.AccentColour);
        for(int rib=0;rib<p.RadialRibs;rib++)
        {
            float a=rib*MathF.Tau/p.RadialRibs;
            Vector3 edge=rimCentre+f.U*MathF.Cos(a)*r+f.V*MathF.Sin(a)*r;
            mesh.AddPrismPipe(hub,edge,MathF.Max(.12f,r*.010f),4,i.SecondaryColour,true,true);
        }
    }

    private sealed record DishShell(Vector3 FrontTip,Vector3 BackTip,
        Vector3[][] FrontRings,Vector3[][] BackRings);

    private static DishShell AddParaboloid(StationModuleMesh mesh,Vector3 vertex,Vector3 axis,
        Vector3 right,Vector3 up,float radius,float depth,int sides,int rings,
        Color surface,Color structure)
    {
        float shellThickness=MathF.Max(.25f,radius*.012f);
        Color rearSurface=Darken(surface,.68f);
        var frontRings=new Vector3[rings+1][];
        var backRings=new Vector3[rings+1][];
        frontRings[0]=[vertex];
        backRings[0]=[vertex-axis*shellThickness];
        for(int ring=1;ring<=rings;ring++)
        {
            float t=ring/(float)rings, rr=radius*t, z=depth*t*t;
            frontRings[ring]=Enumerable.Range(0,sides).Select(s=>
                vertex+axis*z+right*MathF.Cos(s*MathF.Tau/sides)*rr+up*MathF.Sin(s*MathF.Tau/sides)*rr).ToArray();
            backRings[ring]=frontRings[ring].Select(point=>point-axis*shellThickness).ToArray();
        }
        for(int s=0;s<sides;s++)
        {
            mesh.AddTriangle(vertex,frontRings[1][s],frontRings[1][(s+1)%sides],surface);
            mesh.AddTriangle(backRings[0][0],backRings[1][(s+1)%sides],backRings[1][s],rearSurface);
        }
        for(int ring=1;ring<rings;ring++) for(int s=0;s<sides;s++)
        {
            mesh.AddQuad(frontRings[ring][s],frontRings[ring+1][s],
                frontRings[ring+1][(s+1)%sides],frontRings[ring][(s+1)%sides],surface);
            mesh.AddQuad(backRings[ring][(s+1)%sides],backRings[ring+1][(s+1)%sides],
                backRings[ring+1][s],backRings[ring][s],rearSurface);
        }
        Vector3[] frontRim=frontRings[^1], backRim=backRings[^1];
        for(int s=0;s<sides;s++)
        {
            int next=(s+1)%sides;
            mesh.AddQuad(frontRim[s],frontRim[next],backRim[next],backRim[s],structure);
            mesh.AddPrismPipe(frontRim[s],frontRim[next],MathF.Max(.22f,radius*.018f),5,
                structure,true,true);
        }
        return new(vertex,backRings[0][0],frontRings,backRings);
    }

    private static void AddQuadFacing(StationModuleMesh mesh,Vector3 a,Vector3 b,
        Vector3 c,Vector3 d,Vector3 expectedNormal,Color colour)
    {
        if(Vector3.Dot(Vector3.Cross(b-a,c-a),expectedNormal)<0f)
            mesh.AddQuad(a,d,c,b,colour);
        else
            mesh.AddQuad(a,b,c,d,colour);
    }

    private static Color Darken(Color colour,float factor)=>new(
        (byte)Math.Clamp(colour.R*factor,0f,255f),
        (byte)Math.Clamp(colour.G*factor,0f,255f),
        (byte)Math.Clamp(colour.B*factor,0f,255f),colour.A);

    private readonly record struct Frame(Vector3 O,Vector3 N,Vector3 U,Vector3 V)
    {
        public static Frame Create(MegastationMegaGreebleInstance i)
        {
            Vector3 n=Vector3.Normalize(i.Normal),u=Vector3.Normalize(i.TangentU),v=Vector3.Normalize(i.TangentV);
            if(Vector3.Dot(Vector3.Cross(u,v),n)<0)v=-v;
            return new(i.SurfacePosition,n,u,v);
        }
    }
    private static Matrix FrameMatrix(Vector3 u,Vector3 v,Vector3 n,Vector3 c)=>new(
        u.X,u.Y,u.Z,0,v.X,v.Y,v.Z,0,n.X,n.Y,n.Z,0,c.X,c.Y,c.Z,1);
}
