using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum BolonSurfaceFinish
{
    Mature,
    Polished,
    Brushed,
    Eroded,
}

public enum BolonAperturePattern
{
    FourNineFour,
    CompactFive,
    SparseChain,
    Band,
    CompactCluster,
    CornerFan,
    EdgeRun,
    SparseField,
    VentRow,
}

public enum BolonAperturePatternFamily
{
    Band,
    CompactCluster,
    CornerFan,
    EdgeRun,
    SparseField,
    Vent,
}

public enum BolonShellPenetrationType
{
    OpticalAperture,
    Vent,
}

public enum BolonVentScale
{
    None,
    One,
    Two,
    Three,
}

public enum BolonAperturePaletteFamily
{
    Ruby,
    Violet,
    SpectralGreen,
}

public enum BolonApertureIlluminationState
{
    Unlit,
    Dim,
    Luminous,
    Bright,
}

public sealed record BolonApertureVisualState(
    BolonApertureIlluminationState Illumination,
    float Brightness,
    float RecessDepthScale,
    Color PerimeterColour,
    Color MiddleColour,
    Color InnerColour,
    float SurfacePhase);

public sealed record BolonSurfaceHistoryRegion(
    string Identity,
    int VesselIndex,
    int RegionIndex,
    Vector3 CenterDirection,
    float AngularRadius,
    float Age,
    BolonSurfaceFinish Finish,
    Vector3 ProjectionU,
    Vector3 ProjectionV,
    Vector3 BoundaryAxisA,
    Vector3 BoundaryAxisB,
    float BoundaryFrequencyA,
    float BoundaryFrequencyB,
    float BoundaryPhaseA,
    float BoundaryPhaseB,
    float BoundaryIrregularity,
    float ErosionStrength);

public sealed record BolonVesselSurfaceHistory(
    string Identity,
    int VesselIndex,
    BolonSurfaceFinish BaselineFinish,
    float BaselineAge,
    Vector3 BaselineProjectionU,
    Vector3 BaselineProjectionV,
    IReadOnlyList<BolonSurfaceHistoryRegion> Regions);

public sealed record BolonApertureInstance(
    string Identity,
    Vector3 Centre,
    float Radius,
    BolonApertureVisualState VisualState,
    BolonShellPenetrationType PenetrationType = BolonShellPenetrationType.OpticalAperture,
    BolonVentScale VentScale = BolonVentScale.None,
    float GrilleRotationRadians = 0f,
    int GrilleRibCount = 0);

public sealed record BolonApertureGroup(
    string Identity,
    int VesselIndex,
    int HostFaceIndex,
    BolonAperturePattern Pattern,
    BolonAperturePatternFamily PatternFamily,
    string PatternVariant,
    Vector3 HostFaceCenter,
    Vector3 Centre,
    Vector3 Normal,
    Vector3 TangentU,
    Vector3 TangentV,
    float RotationRadians,
    float HostSafeRadius,
    float CollarOuterRadius,
    float CollarHeight,
    Color ApertureColour,
    float Intensity,
    IReadOnlyList<BolonApertureInstance> Apertures,
    BolonAperturePaletteFamily PaletteFamily = BolonAperturePaletteFamily.Ruby,
    int SelectedCorner = -1,
    int SelectedEdge = -1,
    bool SymmetricPattern = false,
    bool PreservedB2aGroup = false);

public sealed record BolonSurfacePresentationPlan(
    string StationIdentity,
    MegastationArchetype Archetype,
    IReadOnlyList<BolonVesselSurfaceHistory> VesselHistories,
    IReadOnlyList<BolonApertureGroup> ApertureGroups,
    int BlankEligibleHexFaceCount,
    string SurfaceHistorySignature,
    string ApertureSignature,
    string ApertureVisualSignature,
    string ApertureVocabularySignature);

public sealed record BolonSurfaceMeshBuildResult(
    StationModuleMesh HullMesh,
    StationModuleMesh ApertureGlassMesh,
    int SurfaceTriangleCount,
    int ApertureCollarTriangleCount,
    int ApertureGlassTriangleCount,
    int VentGrilleTriangleCount,
    int ReinforcementCollarTriangleCount,
    int IrisHatchTriangleCount,
    int ApparatusRosetteTriangleCount)
{
    public int AmbassadorTriangleCount { get; init; }
}

public static class BolonSurfacePresentationPlanner
{
    public const int AlgorithmVersion = 1;
    private const int StructuralAlgorithmVersion = 2;

    public static BolonSurfacePresentationPlan Plan(
        BolonMegastationPlan structuralPlan,
        CancellationToken cancellationToken = default)
    {
        int structuralRoot = MegastationSeed.Root(
            structuralPlan.StationIdentity, StructuralAlgorithmVersion);
        int historySeed = MegastationSeed.Derive(
            structuralRoot, "bolon-surface-history:v1");
        int apertureSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-apertures:v1");
        int apertureVisualSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-aperture-presentation:v1");
        int coverageSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-aperture-coverage:v2");
        int vocabularySeed = MegastationSeed.Derive(
            structuralRoot, "bolon-aperture-vocabulary:v1");
        int paletteSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-aperture-palettes:v1");
        int ventSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-vents:v1");
        var histories = new List<BolonVesselSurfaceHistory>(
            structuralPlan.Vessels.Count);
        foreach (BolonVesselPlan vessel in structuralPlan.Vessels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            histories.Add(PlanHistory(vessel, historySeed));
        }

        HashSet<(int Vessel, int Face)> attachedFaces = structuralPlan.Relationships
            .SelectMany(relationship => new[]
            {
                (relationship.A, relationship.FaceA),
                (relationship.B, relationship.FaceB),
            })
            .ToHashSet();
        var groups = new List<BolonApertureGroup>();
        int eligibleFaceCount = 0;
        foreach (BolonVesselPlan vessel in structuralPlan.Vessels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] eligible = BolonMegastationGenerator.AttachmentFaces
                .Where(face => face.SideCount == 6
                    && !attachedFaces.Contains((vessel.Index, face.Index)))
                .Select(face => face.Index)
                .ToArray();
            eligibleFaceCount += eligible.Length;
            var countRng = new Random(MegastationSeed.Derive(
                apertureSeed, $"vessel:{vessel.Index}:group-count"));
            double countRoll = countRng.NextDouble();
            int groupCount = countRoll < .14 ? 0
                : countRoll < .58 ? 1
                : countRoll < .90 ? 2
                : 3;
            if (vessel.ScaleClass == BolonVesselScaleClass.Anchor && groupCount == 0)
                groupCount = 1;
            groupCount = Math.Min(groupCount, eligible.Length);
            int[] selected = eligible
                .OrderBy(face => MegastationSeed.Derive(
                    apertureSeed, $"vessel:{vessel.Index}:face:{face}"))
                .Take(groupCount)
                .ToArray();
            for (int groupIndex = 0; groupIndex < selected.Length; groupIndex++)
            {
                bool forceSignaturePattern = groups.Count == 0;
                groups.Add(PlanApertureGroup(
                    structuralPlan,
                    vessel,
                    selected[groupIndex],
                    groupIndex,
                    apertureSeed,
                    apertureVisualSeed,
                    forceSignaturePattern));
            }

            int targetCount = CoverageTarget(vessel, coverageSeed);
            var vesselGroups = groups.Where(group => group.VesselIndex == vessel.Index)
                .ToList();
            int[] supplementalFaces = SelectDistributedFaces(
                vessel,
                eligible.Except(vesselGroups.Select(group => group.HostFaceIndex)).ToArray(),
                vesselGroups.Select(group => group.HostFaceIndex).ToArray(),
                Math.Max(0, targetCount - vesselGroups.Count),
                coverageSeed);
            foreach (int hostFaceIndex in supplementalFaces)
            {
                int groupIndex = vesselGroups.Count;
                BolonApertureGroup group = PlanVocabularyGroup(
                    structuralPlan,
                    vessel,
                    hostFaceIndex,
                    groupIndex,
                    vocabularySeed,
                    paletteSeed,
                    apertureVisualSeed,
                    ventSeed);
                groups.Add(group);
                vesselGroups.Add(group);
            }

            int[] remainingFaces = eligible
                .Except(vesselGroups.Select(group => group.HostFaceIndex))
                .ToArray();
            foreach (int hostFaceIndex in SelectVentFaces(
                         vessel, remainingFaces, ventSeed))
            {
                int groupIndex = vesselGroups.Count;
                BolonApertureGroup ventGroup = PlanVentGroup(
                    structuralPlan,
                    vessel,
                    hostFaceIndex,
                    groupIndex,
                    ventSeed,
                    apertureVisualSeed);
                groups.Add(ventGroup);
                vesselGroups.Add(ventGroup);
            }
        }

        string historySignature = HistorySignature(
            structuralPlan.StationIdentity, histories);
        string apertureSignature = ApertureSignature(
            structuralPlan.StationIdentity, groups);
        string apertureVisualSignature = ApertureVisualSignature(
            structuralPlan.StationIdentity, groups);
        string vocabularySignature = ApertureVocabularySignature(
            structuralPlan.StationIdentity, groups);
        return new(
            structuralPlan.StationIdentity,
            structuralPlan.Archetype,
            histories,
            groups,
            eligibleFaceCount - groups.Count,
            historySignature,
            apertureSignature,
            apertureVisualSignature,
            vocabularySignature);
    }

    // Compose the entrance reservation AFTER deterministic B2 planning. Do not refill
    // the removed host: doing so would change accepted groups on unrelated faces.
    public static BolonSurfacePresentationPlan ReserveAmbassadorFace(
        BolonSurfacePresentationPlan plan, BolonAmbassadorBayPlan bay)
    {
        BolonApertureGroup[] groups = plan.ApertureGroups
            .Where(g => !bay.ReservesFace(g.VesselIndex, g.HostFaceIndex)).ToArray();
        return plan with
        {
            ApertureGroups = groups,
            BlankEligibleHexFaceCount = plan.BlankEligibleHexFaceCount + plan.ApertureGroups.Count - groups.Length,
            ApertureSignature = ApertureSignature(plan.StationIdentity, groups),
            ApertureVisualSignature = ApertureVisualSignature(plan.StationIdentity, groups),
            ApertureVocabularySignature = ApertureVocabularySignature(plan.StationIdentity, groups),
        };
    }

    public static string ResolveRegionIdentity(
        BolonVesselSurfaceHistory history,
        Vector3 direction)
    {
        string identity = history.Identity;
        foreach (BolonSurfaceHistoryRegion region in history.Regions)
        {
            if (Contains(region, direction))
                identity = region.Identity;
        }
        return identity;
    }

    internal static bool Contains(BolonSurfaceHistoryRegion region, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        float irregularity = (
            MathF.Sin(Vector3.Dot(direction, region.BoundaryAxisA)
                * region.BoundaryFrequencyA + region.BoundaryPhaseA)
            + .55f * MathF.Sin(Vector3.Dot(direction, region.BoundaryAxisB)
                * region.BoundaryFrequencyB + region.BoundaryPhaseB))
            * region.BoundaryIrregularity;
        return Vector3.Dot(direction, region.CenterDirection)
            >= MathF.Cos(region.AngularRadius) + irregularity;
    }

    private static BolonVesselSurfaceHistory PlanHistory(
        BolonVesselPlan vessel,
        int historySeed)
    {
        int vesselSeed = MegastationSeed.Derive(historySeed, $"vessel:{vessel.Index}");
        var baselineRng = new Random(MegastationSeed.Derive(vesselSeed, "baseline"));
        double baselineRoll = baselineRng.NextDouble();
        BolonSurfaceFinish baseline = baselineRoll < .58
            ? BolonSurfaceFinish.Mature
            : baselineRoll < .78 ? BolonSurfaceFinish.Brushed
            : baselineRoll < .94 ? BolonSurfaceFinish.Polished
            : BolonSurfaceFinish.Eroded;
        (Vector3 baselineU, Vector3 baselineV) = RandomProjectionFrame(baselineRng);
        var countRng = new Random(MegastationSeed.Derive(vesselSeed, "region-count"));
        int regionCount = countRng.Next(2, 7);
        var regions = new List<BolonSurfaceHistoryRegion>(regionCount);
        for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
        {
            var rng = new Random(MegastationSeed.Derive(
                vesselSeed, $"region:{regionIndex}"));
            BolonSurfaceFinish finish = PickRegionFinish(vessel.Index, regionIndex, rng);
            (Vector3 projectionU, Vector3 projectionV) = RandomProjectionFrame(rng);
            regions.Add(new(
                $"vessel:{vessel.Index}/surface-region:{regionIndex}",
                vessel.Index,
                regionIndex,
                RandomUnitVector(rng),
                Lerp(.56f, 1.22f, rng.NextDouble()),
                AgeForFinish(finish, rng),
                finish,
                projectionU,
                projectionV,
                RandomUnitVector(rng),
                RandomUnitVector(rng),
                Lerp(1.4f, 3.2f, rng.NextDouble()),
                Lerp(2.0f, 4.4f, rng.NextDouble()),
                Lerp(0f, MathF.Tau, rng.NextDouble()),
                Lerp(0f, MathF.Tau, rng.NextDouble()),
                Lerp(.035f, .11f, rng.NextDouble()),
                finish == BolonSurfaceFinish.Eroded
                    ? Lerp(.32f, .90f, rng.NextDouble())
                    : 0f));
        }
        return new(
            $"vessel:{vessel.Index}/surface-history:v1",
            vessel.Index,
            baseline,
            AgeForFinish(baseline, baselineRng),
            baselineU,
            baselineV,
            regions);
    }

    private static float AgeForFinish(BolonSurfaceFinish finish, Random rng)
        => finish switch
        {
            BolonSurfaceFinish.Polished => Lerp(.05f, .34f, rng.NextDouble()),
            BolonSurfaceFinish.Brushed => Lerp(.16f, .55f, rng.NextDouble()),
            BolonSurfaceFinish.Eroded => Lerp(.72f, .99f, rng.NextDouble()),
            _ => Lerp(.43f, .88f, rng.NextDouble()),
        };

    private static BolonSurfaceFinish PickRegionFinish(
        int vesselIndex,
        int regionIndex,
        Random rng)
    {
        if (regionIndex == 0)
            return (vesselIndex & 3) switch
            {
                0 => BolonSurfaceFinish.Polished,
                1 => BolonSurfaceFinish.Brushed,
                2 => BolonSurfaceFinish.Eroded,
                _ => BolonSurfaceFinish.Mature,
            };
        double roll = rng.NextDouble();
        return roll < .28 ? BolonSurfaceFinish.Polished
            : roll < .59 ? BolonSurfaceFinish.Brushed
            : roll < .79 ? BolonSurfaceFinish.Eroded
            : BolonSurfaceFinish.Mature;
    }

    private static BolonApertureGroup PlanApertureGroup(
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        int hostFaceIndex,
        int groupIndex,
        int apertureSeed,
        int apertureVisualSeed,
        bool forceSignaturePattern)
    {
        var rng = new Random(MegastationSeed.Derive(
            apertureSeed, $"vessel:{vessel.Index}:group:{groupIndex}:face:{hostFaceIndex}"));
        BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(hostFaceIndex);
        double patternRoll = rng.NextDouble();
        BolonAperturePattern pattern = forceSignaturePattern || patternRoll < .56
            ? BolonAperturePattern.FourNineFour
            : patternRoll < .80 ? BolonAperturePattern.CompactFive
            : BolonAperturePattern.SparseChain;
        Vector2[] patternPoints = PatternPoints(pattern, rng);
        float spacing = pattern switch
        {
            BolonAperturePattern.FourNineFour => Lerp(14f, 22f, rng.NextDouble()),
            BolonAperturePattern.CompactFive => Lerp(15f, 24f, rng.NextDouble()),
            _ => Lerp(19f, 31f, rng.NextDouble()),
        };
        float apertureRadius = spacing * Lerp(.24f, .31f, rng.NextDouble());
        float outerRadius = apertureRadius * Lerp(1.48f, 1.68f, rng.NextDouble());
        float safeRadius = face.LocalInscribedRadius * vessel.Radius * .74f;
        float footprint = patternPoints.Max(point => point.Length()) * spacing + outerRadius;
        if (footprint > safeRadius * .88f)
        {
            float scale = safeRadius * .88f / footprint;
            spacing *= scale;
            apertureRadius *= scale;
            outerRadius *= scale;
            footprint *= scale;
        }

        float rotation = Lerp(0f, MathF.Tau, rng.NextDouble());
        float offsetLimit = MathF.Max(0f, safeRadius - footprint);
        float offsetMagnitude = offsetLimit * Lerp(0f, .58f, rng.NextDouble());
        float offsetAngle = Lerp(0f, MathF.Tau, rng.NextDouble());
        Vector2 offset = new(
            MathF.Cos(offsetAngle) * offsetMagnitude,
            MathF.Sin(offsetAngle) * offsetMagnitude);

        Vector3 normal = Vector3.Normalize(Vector3.Transform(
            face.LocalNormal, vessel.Orientation));
        IReadOnlyList<Vector3> faceVertices =
            BolonMegastationGenerator.GetAttachmentFaceVertices(hostFaceIndex);
        Vector3 localTangent = Vector3.Normalize(faceVertices[0] - face.LocalCenter);
        Vector3 tangentU = Vector3.Normalize(Vector3.Transform(
            localTangent, vessel.Orientation));
        Vector3 tangentV = Vector3.Normalize(Vector3.Cross(normal, tangentU));
        Vector3 faceCenter = vessel.Position + Vector3.Transform(
            face.LocalCenter * vessel.Radius, vessel.Orientation);
        Vector3 groupCenter = faceCenter + tangentU * offset.X + tangentV * offset.Y;
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector3 groupU = tangentU * cos + tangentV * sin;
        Vector3 groupV = -tangentU * sin + tangentV * cos;
        float intensity = Lerp(.34f, .62f, rng.NextDouble());
        Color apertureColour = plan.Archetype == MegastationArchetype.RedBolon
            ? new Color((int)Lerp(112f, 146f, rng.NextDouble()), 14, 10)
            : new Color((int)Lerp(92f, 132f, rng.NextDouble()), 5, 14);
        var apertures = patternPoints.Select((point, index) =>
        {
            string identity =
                $"vessel:{vessel.Index}/face:{hostFaceIndex}/group:{groupIndex}/aperture:{index}";
            return new BolonApertureInstance(
                identity,
                groupCenter + groupU * (point.X * spacing) + groupV * (point.Y * spacing),
                apertureRadius,
                PlanVisualState(
                    identity,
                    apertureColour,
                    BolonAperturePaletteFamily.Ruby,
                    apertureVisualSeed));
        }).ToArray();
        return new(
            $"vessel:{vessel.Index}/face:{hostFaceIndex}/aperture-group:{groupIndex}",
            vessel.Index,
            hostFaceIndex,
            pattern,
            PatternFamily(pattern),
            pattern.ToString(),
            faceCenter,
            groupCenter,
            normal,
            groupU,
            groupV,
            rotation,
            safeRadius,
            outerRadius,
            Lerp(2.2f, 4.2f, rng.NextDouble()),
            apertureColour,
            intensity,
            apertures,
            BolonAperturePaletteFamily.Ruby,
            PreservedB2aGroup: true);
    }

    private static int CoverageTarget(BolonVesselPlan vessel, int coverageSeed)
    {
        var rng = new Random(MegastationSeed.Derive(
            coverageSeed, $"vessel:{vessel.Index}:target"));
        return vessel.ScaleClass switch
        {
            BolonVesselScaleClass.Anchor => rng.Next(5, 7),
            BolonVesselScaleClass.Standard => rng.Next(4, 6),
            _ => rng.Next(3, 5),
        };
    }

    private static int[] SelectDistributedFaces(
        BolonVesselPlan vessel,
        IReadOnlyList<int> candidates,
        IReadOnlyList<int> existing,
        int count,
        int coverageSeed)
    {
        var available = candidates.ToList();
        var selected = existing.ToList();
        while (available.Count > 0 && selected.Count < existing.Count + count)
        {
            int face = available
                .OrderBy(candidate => selected.Count == 0
                    ? 0f
                    : selected.Max(other => Vector3.Dot(
                        BolonMegastationGenerator.GetAttachmentFace(candidate).LocalNormal,
                        BolonMegastationGenerator.GetAttachmentFace(other).LocalNormal)))
                .ThenBy(candidate => MegastationSeed.Derive(
                    coverageSeed, $"vessel:{vessel.Index}:distributed-face:{candidate}"))
                .First();
            selected.Add(face);
            available.Remove(face);
        }
        return selected.Skip(existing.Count).ToArray();
    }

    private static int[] SelectVentFaces(
        BolonVesselPlan vessel,
        IReadOnlyList<int> candidates,
        int ventSeed)
    {
        var rng = new Random(MegastationSeed.Derive(
            ventSeed, $"vessel:{vessel.Index}:standalone-count"));
        int count = rng.NextDouble() < .34 ? 0 : rng.NextDouble() < .78 ? 1 : 2;
        return candidates
            .OrderBy(face => MegastationSeed.Derive(
                ventSeed, $"vessel:{vessel.Index}:standalone-face:{face}"))
            .Take(count)
            .ToArray();
    }

    private static BolonApertureGroup PlanVocabularyGroup(
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        int hostFaceIndex,
        int groupIndex,
        int vocabularySeed,
        int paletteSeed,
        int apertureVisualSeed,
        int ventSeed)
    {
        string groupDomain = $"vessel:{vessel.Index}:group:{groupIndex}:face:{hostFaceIndex}";
        var familyRng = new Random(MegastationSeed.Derive(
            vocabularySeed, groupDomain + ":family"));
        double familyRoll = familyRng.NextDouble();
        BolonAperturePattern pattern = familyRoll < .28 ? BolonAperturePattern.Band
            : familyRoll < .54 ? BolonAperturePattern.CompactCluster
            : familyRoll < .76 ? BolonAperturePattern.CornerFan
            : familyRoll < .91 ? BolonAperturePattern.EdgeRun
            : BolonAperturePattern.SparseField;
        var rng = new Random(MegastationSeed.Derive(
            vocabularySeed, groupDomain + ":variant"));
        BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(hostFaceIndex);
        IReadOnlyList<Vector3> faceVertices =
            BolonMegastationGenerator.GetAttachmentFaceVertices(hostFaceIndex);
        int selectedCorner = -1;
        int selectedEdge = -1;
        bool symmetric = false;
        string variant;
        Vector2[] points;
        switch (pattern)
        {
            case BolonAperturePattern.Band:
                (points, variant, symmetric) = BandPoints(rng);
                break;
            case BolonAperturePattern.CompactCluster:
                (points, variant, symmetric) = CompactPoints(rng);
                break;
            case BolonAperturePattern.CornerFan:
                selectedCorner = rng.Next(6);
                (points, variant, symmetric) = CornerFanPoints(rng);
                break;
            case BolonAperturePattern.EdgeRun:
                selectedEdge = rng.Next(6);
                (points, variant, symmetric) = EdgeRunPoints(rng);
                break;
            default:
                (points, variant, symmetric) = SparseFieldPoints(rng);
                break;
        }

        float spacing = pattern switch
        {
            BolonAperturePattern.CompactCluster => Lerp(15f, 24f, rng.NextDouble()),
            BolonAperturePattern.SparseField => Lerp(22f, 31f, rng.NextDouble()),
            _ => Lerp(14f, 22f, rng.NextDouble()),
        };
        float apertureRadius = spacing * Lerp(.24f, .31f, rng.NextDouble());
        float outerRadius = apertureRadius * Lerp(1.48f, 1.68f, rng.NextDouble());
        float safeRadius = face.LocalInscribedRadius * vessel.Radius * .74f;
        float maximumPointRadius = points.Max(point => point.Length());
        float maximumSpacing = (safeRadius * .86f - outerRadius)
            / MathF.Max(.001f, maximumPointRadius);
        spacing = MathF.Min(spacing, maximumSpacing);
        if (spacing < 10f && pattern is BolonAperturePattern.Band
                or BolonAperturePattern.CornerFan or BolonAperturePattern.EdgeRun)
        {
            pattern = BolonAperturePattern.CompactCluster;
            (points, variant, symmetric) = CompactPoints(rng);
            selectedCorner = -1;
            selectedEdge = -1;
            maximumPointRadius = points.Max(point => point.Length());
            spacing = MathF.Min(Lerp(15f, 20f, rng.NextDouble()),
                (safeRadius * .86f - outerRadius) / maximumPointRadius);
        }

        Vector3 normal = Vector3.Normalize(Vector3.Transform(
            face.LocalNormal, vessel.Orientation));
        Vector3 localTangent = Vector3.Normalize(faceVertices[0] - face.LocalCenter);
        Vector3 tangentU = Vector3.Normalize(Vector3.Transform(
            localTangent, vessel.Orientation));
        Vector3 tangentV = Vector3.Normalize(Vector3.Cross(normal, tangentU));
        float rotation = Lerp(0f, MathF.Tau, rng.NextDouble());
        if (selectedCorner >= 0)
        {
            Vector3 cornerDirection = Vector3.Normalize(
                faceVertices[selectedCorner] - face.LocalCenter);
            rotation = MathF.Atan2(
                Vector3.Dot(cornerDirection, Vector3.Normalize(
                    Vector3.Cross(face.LocalNormal, localTangent))),
                Vector3.Dot(cornerDirection, localTangent));
            if (rotation < 0f)
                rotation += MathF.Tau;
        }
        else if (selectedEdge >= 0)
        {
            Vector3 edgeDirection = Vector3.Normalize(
                faceVertices[(selectedEdge + 1) % 6] - faceVertices[selectedEdge]);
            rotation = MathF.Atan2(
                Vector3.Dot(edgeDirection, Vector3.Normalize(
                    Vector3.Cross(face.LocalNormal, localTangent))),
                Vector3.Dot(edgeDirection, localTangent));
            if (rotation < 0f)
                rotation += MathF.Tau;
        }
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector3 groupU = tangentU * cos + tangentV * sin;
        Vector3 groupV = -tangentU * sin + tangentV * cos;
        Vector3 faceCenter = vessel.Position + Vector3.Transform(
            face.LocalCenter * vessel.Radius, vessel.Orientation);
        float footprint = maximumPointRadius * spacing + outerRadius;
        float offsetLimit = MathF.Max(0f, safeRadius - footprint);
        Vector2 offset;
        if (selectedCorner >= 0)
            offset = new(offsetLimit * Lerp(.35f, .68f, rng.NextDouble()), 0f);
        else if (selectedEdge >= 0)
            offset = new(0f, -offsetLimit * Lerp(.30f, .62f, rng.NextDouble()));
        else
        {
            float angle = Lerp(0f, MathF.Tau, rng.NextDouble());
            float magnitude = offsetLimit * Lerp(0f, .52f, rng.NextDouble());
            offset = new(MathF.Cos(angle) * magnitude, MathF.Sin(angle) * magnitude);
        }
        Vector3 groupCenter = faceCenter + groupU * offset.X + groupV * offset.Y;
        BolonAperturePaletteFamily palette = SelectPaletteFamily(
            vessel, groupIndex, paletteSeed);
        Color groupColour = PaletteColour(plan.Archetype, palette, paletteSeed, groupDomain);
        float intensity = Lerp(.34f, .62f, rng.NextDouble());
        float collarHeight = Lerp(2.2f, 4.2f, rng.NextDouble());
        var apertures = points.Select((point, index) =>
        {
            string identity =
                $"vessel:{vessel.Index}/face:{hostFaceIndex}/group:{groupIndex}/aperture:{index}";
            bool substituteVent = new Random(MegastationSeed.Derive(
                    ventSeed, identity + ":substitution")).NextDouble() < .045;
            var ventRng = new Random(MegastationSeed.Derive(
                ventSeed, identity + ":grille"));
            return new BolonApertureInstance(
                identity,
                groupCenter + groupU * (point.X * spacing) + groupV * (point.Y * spacing),
                apertureRadius,
                PlanVisualState(identity, groupColour, palette, apertureVisualSeed),
                substituteVent
                    ? BolonShellPenetrationType.Vent
                    : BolonShellPenetrationType.OpticalAperture,
                substituteVent ? BolonVentScale.One : BolonVentScale.None,
                Lerp(0f, MathF.Tau, ventRng.NextDouble()),
                substituteVent ? ventRng.Next(3, 6) : 0);
        }).ToArray();
        return new(
            $"vessel:{vessel.Index}/face:{hostFaceIndex}/aperture-group:{groupIndex}",
            vessel.Index,
            hostFaceIndex,
            pattern,
            PatternFamily(pattern),
            variant,
            faceCenter,
            groupCenter,
            normal,
            groupU,
            groupV,
            rotation,
            safeRadius,
            outerRadius,
            collarHeight,
            groupColour,
            intensity,
            apertures,
            palette,
            selectedCorner,
            selectedEdge,
            symmetric);
    }

    private static BolonApertureGroup PlanVentGroup(
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        int hostFaceIndex,
        int groupIndex,
        int ventSeed,
        int apertureVisualSeed)
    {
        string domain = $"vessel:{vessel.Index}:face:{hostFaceIndex}:vent-row:{groupIndex}";
        var rng = new Random(MegastationSeed.Derive(ventSeed, domain));
        BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(hostFaceIndex);
        IReadOnlyList<Vector3> vertices =
            BolonMegastationGenerator.GetAttachmentFaceVertices(hostFaceIndex);
        BolonVentScale scale = rng.NextDouble() < .74
            ? BolonVentScale.Two
            : BolonVentScale.Three;
        float baseRadius = Lerp(4.0f, 6.0f, rng.NextDouble());
        float radius = baseRadius * (scale == BolonVentScale.Three ? 2.65f : 1.82f);
        float outerRadius = radius * Lerp(1.42f, 1.57f, rng.NextDouble());
        float safeRadius = face.LocalInscribedRadius * vessel.Radius * .74f;
        int requestedCount = rng.Next(1, 6);
        float spacing = outerRadius * Lerp(2.25f, 2.70f, rng.NextDouble());
        int count = Math.Min(requestedCount,
            Math.Max(1, (int)MathF.Floor((safeRadius * 1.45f - outerRadius) / spacing) + 1));
        Vector3 normal = Vector3.Normalize(Vector3.Transform(
            face.LocalNormal, vessel.Orientation));
        Vector3 localTangent = Vector3.Normalize(vertices[0] - face.LocalCenter);
        Vector3 tangentU = Vector3.Normalize(Vector3.Transform(localTangent, vessel.Orientation));
        Vector3 tangentV = Vector3.Normalize(Vector3.Cross(normal, tangentU));
        float rotation = Lerp(0f, MathF.Tau, rng.NextDouble());
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector3 groupU = tangentU * cos + tangentV * sin;
        Vector3 groupV = -tangentU * sin + tangentV * cos;
        Vector3 faceCenter = vessel.Position + Vector3.Transform(
            face.LocalCenter * vessel.Radius, vessel.Orientation);
        Color colour = plan.Archetype == MegastationArchetype.RedBolon
            ? new Color(112, 17, 14)
            : new Color(100, 8, 16);
        var vents = Enumerable.Range(0, count).Select(index =>
        {
            string identity = domain + $"/vent:{index}";
            var ventRng = new Random(MegastationSeed.Derive(ventSeed, identity));
            float x = (index - (count - 1) * .5f) * spacing;
            return new BolonApertureInstance(
                identity,
                faceCenter + groupU * x,
                radius,
                PlanVisualState(
                    identity,
                    colour,
                    BolonAperturePaletteFamily.Ruby,
                    apertureVisualSeed),
                BolonShellPenetrationType.Vent,
                scale,
                Lerp(0f, MathF.Tau, ventRng.NextDouble()),
                ventRng.Next(4, 8));
        }).ToArray();
        return new(
            $"vessel:{vessel.Index}/face:{hostFaceIndex}/vent-group:{groupIndex}",
            vessel.Index,
            hostFaceIndex,
            BolonAperturePattern.VentRow,
            BolonAperturePatternFamily.Vent,
            $"{scale}x{count}",
            faceCenter,
            faceCenter,
            normal,
            groupU,
            groupV,
            rotation,
            safeRadius,
            outerRadius,
            Lerp(3.0f, 5.0f, rng.NextDouble()) * (scale == BolonVentScale.Three ? 1.25f : 1f),
            colour,
            0f,
            vents);
    }

    private static BolonAperturePatternFamily PatternFamily(BolonAperturePattern pattern)
        => pattern switch
        {
            BolonAperturePattern.FourNineFour or BolonAperturePattern.Band
                => BolonAperturePatternFamily.Band,
            BolonAperturePattern.CompactFive or BolonAperturePattern.CompactCluster
                => BolonAperturePatternFamily.CompactCluster,
            BolonAperturePattern.CornerFan => BolonAperturePatternFamily.CornerFan,
            BolonAperturePattern.EdgeRun => BolonAperturePatternFamily.EdgeRun,
            BolonAperturePattern.VentRow => BolonAperturePatternFamily.Vent,
            _ => BolonAperturePatternFamily.SparseField,
        };

    private static (Vector2[] Points, string Variant, bool Symmetric) BandPoints(Random rng)
    {
        (int outer, int center)[] variants = [(3, 7), (4, 9), (5, 11), (6, 13)];
        (int outer, int center) = variants[rng.Next(variants.Length)];
        int lower = outer;
        if (rng.NextDouble() < .18 && lower > 3)
            lower--;
        var points = new List<Vector2>();
        AddCenteredRow(points, outer, -1f);
        AddCenteredRow(points, center, 0f);
        AddCenteredRow(points, lower, 1f);
        return (points.ToArray(), $"{outer}-{center}-{lower}", outer == lower);
    }

    private static (Vector2[] Points, string Variant, bool Symmetric) CompactPoints(Random rng)
    {
        int variant = rng.Next(3);
        return variant switch
        {
            0 => ([Vector2.Zero, new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f)], "cross-5", true),
            1 => ([Vector2.Zero, new(-1f, -.45f), new(1f, .45f), new(-.45f, 1f), new(.45f, -1f), new(0f, 1.45f)], "offset-6", false),
            _ => ([Vector2.Zero, new(-1f, 0f), new(1f, 0f), new(-.5f, -.9f), new(.5f, -.9f), new(-.5f, .9f), new(.5f, .9f)], "hex-7", true),
        };
    }

    private static (Vector2[] Points, string Variant, bool Symmetric) CornerFanPoints(Random rng)
    {
        int rows = rng.Next(3, 5);
        int widest = rng.Next(4, 7);
        var points = new List<Vector2>();
        for (int row = 0; row < rows; row++)
        {
            int count = Math.Max(1, widest - row);
            float x = (rows - 1) * .5f - row;
            AddCenteredColumn(points, count, x);
        }
        bool asymmetric = rng.NextDouble() < .16 && points.Count > 7;
        if (asymmetric)
            points.RemoveAt(points.Count - 2);
        return (points.ToArray(), $"fan-{widest}x{rows}" + (asymmetric ? "-trim" : ""), !asymmetric);
    }

    private static (Vector2[] Points, string Variant, bool Symmetric) EdgeRunPoints(Random rng)
    {
        int outer = rng.Next(4, 8);
        int inner = rng.NextDouble() < .78 ? Math.Max(2, outer - rng.Next(2, 4)) : 0;
        var points = new List<Vector2>();
        AddCenteredRow(points, outer, 0f);
        if (inner > 0)
            AddCenteredRow(points, inner, -1f);
        return (points.ToArray(), $"edge-{outer}-{inner}", true);
    }

    private static (Vector2[] Points, string Variant, bool Symmetric) SparseFieldPoints(Random rng)
    {
        Vector2[][] variants =
        [
            [new(-1.7f, -.2f), new(-.4f, 1.1f), new(.55f, -.95f), new(1.65f, .45f)],
            [new(-1.8f, .7f), new(-.75f, -.85f), new(.45f, .3f), new(1.65f, -.55f), new(.95f, 1.25f)],
            [new(-1.5f, -1f), new(-.9f, .8f), new(.4f, -.35f), new(1.5f, .95f)],
        ];
        int variant = rng.Next(variants.Length);
        return (variants[variant], $"field-{variant}", false);
    }

    private static void AddCenteredRow(ICollection<Vector2> points, int count, float y)
    {
        for (int index = 0; index < count; index++)
            points.Add(new(index - (count - 1) * .5f, y));
    }

    private static void AddCenteredColumn(ICollection<Vector2> points, int count, float x)
    {
        for (int index = 0; index < count; index++)
            points.Add(new(x, index - (count - 1) * .5f));
    }

    private static BolonAperturePaletteFamily SelectPaletteFamily(
        BolonVesselPlan vessel,
        int groupIndex,
        int paletteSeed)
    {
        var ballRng = new Random(MegastationSeed.Derive(
            paletteSeed, $"vessel:{vessel.Index}:bias"));
        bool unusualBias = ballRng.NextDouble() < .24;
        var groupRng = new Random(MegastationSeed.Derive(
            paletteSeed, $"vessel:{vessel.Index}:group:{groupIndex}:palette"));
        double roll = groupRng.NextDouble();
        double violetThreshold = unusualBias ? .17 : .07;
        if (roll < .012)
            return BolonAperturePaletteFamily.SpectralGreen;
        return roll < violetThreshold
            ? BolonAperturePaletteFamily.Violet
            : BolonAperturePaletteFamily.Ruby;
    }

    private static Color PaletteColour(
        MegastationArchetype archetype,
        BolonAperturePaletteFamily family,
        int paletteSeed,
        string groupDomain)
    {
        var rng = new Random(MegastationSeed.Derive(
            paletteSeed, groupDomain + ":colour"));
        return family switch
        {
            BolonAperturePaletteFamily.Violet => new Color(
                (int)Lerp(88f, 126f, rng.NextDouble()),
                (int)Lerp(18f, 34f, rng.NextDouble()),
                (int)Lerp(86f, 132f, rng.NextDouble())),
            BolonAperturePaletteFamily.SpectralGreen => new Color(
                (int)Lerp(12f, 28f, rng.NextDouble()),
                (int)Lerp(80f, 116f, rng.NextDouble()),
                (int)Lerp(68f, 108f, rng.NextDouble())),
            _ => archetype == MegastationArchetype.RedBolon
                ? new Color((int)Lerp(112f, 146f, rng.NextDouble()), 14, 10)
                : new Color((int)Lerp(92f, 132f, rng.NextDouble()), 5, 14),
        };
    }

    private static BolonApertureVisualState PlanVisualState(
        string identity,
        Color groupColour,
        BolonAperturePaletteFamily paletteFamily,
        int visualSeed)
    {
        var rng = new Random(MegastationSeed.Derive(visualSeed, identity));
        double stateRoll = rng.NextDouble();
        BolonApertureIlluminationState state = stateRoll < .20
            ? BolonApertureIlluminationState.Unlit
            : stateRoll < .52 ? BolonApertureIlluminationState.Dim
            : stateRoll < .94 ? BolonApertureIlluminationState.Luminous
            : BolonApertureIlluminationState.Bright;
        float brightness = state switch
        {
            BolonApertureIlluminationState.Unlit => Lerp(.035f, .075f, rng.NextDouble()),
            BolonApertureIlluminationState.Dim => Lerp(.16f, .28f, rng.NextDouble()),
            BolonApertureIlluminationState.Bright => Lerp(.62f, .76f, rng.NextDouble()),
            _ => Lerp(.36f, .55f, rng.NextDouble()),
        };
        float hueShift = Lerp(-8f, 9f, rng.NextDouble());
        Color rich = ShiftApertureColour(groupColour, paletteFamily, hueShift);
        Color dark;
        Color middle;
        Color inner;
        if (paletteFamily == BolonAperturePaletteFamily.Ruby)
        {
            dark = new(
                Math.Clamp((int)(rich.R * brightness * .18f), 2, 28),
                Math.Clamp((int)(rich.G * brightness * .11f), 1, 12),
                Math.Clamp((int)(rich.B * brightness * .22f), 2, 20));
            middle = new(
                Math.Clamp((int)(rich.R * brightness * .72f), 4, 124),
                Math.Clamp((int)(rich.G * brightness * .48f), 1, 34),
                Math.Clamp((int)(rich.B * brightness * .78f), 3, 58));
            inner = new(
                Math.Clamp((int)(rich.R * brightness * .86f), 5, 142),
                Math.Clamp((int)(rich.G * brightness * .56f), 1, 40),
                Math.Clamp((int)(rich.B * brightness * .90f), 3, 66));
        }
        else
        {
            dark = ScalePalette(rich, brightness * .18f, .035f, 34);
            middle = ScalePalette(rich, brightness * .72f, .065f, 132);
            inner = ScalePalette(rich, brightness * .88f, .085f, 154);
        }
        return new(
            state,
            brightness,
            Lerp(.84f, 1.16f, rng.NextDouble()),
            dark,
            middle,
            inner,
            Lerp(0f, MathF.Tau, rng.NextDouble()));
    }

    private static Color ScalePalette(
        Color colour,
        float factor,
        float minimumFactor,
        int maximum)
        => new(
            Math.Clamp((int)MathF.Round(colour.R * MathF.Max(factor, minimumFactor)), 1, maximum),
            Math.Clamp((int)MathF.Round(colour.G * MathF.Max(factor, minimumFactor)), 1, maximum),
            Math.Clamp((int)MathF.Round(colour.B * MathF.Max(factor, minimumFactor)), 1, maximum));

    private static Color ShiftApertureColour(
        Color colour,
        BolonAperturePaletteFamily family,
        float shift)
        => family switch
        {
            BolonAperturePaletteFamily.Violet => new(
                Math.Clamp(colour.R + (int)shift, 66, 150),
                Math.Clamp(colour.G - (int)(shift * .10f), 10, 48),
                Math.Clamp(colour.B + (int)(shift * .42f), 65, 158)),
            BolonAperturePaletteFamily.SpectralGreen => new(
                Math.Clamp(colour.R + (int)(shift * .15f), 7, 42),
                Math.Clamp(colour.G + (int)shift, 62, 142),
                Math.Clamp(colour.B + (int)(shift * .32f), 54, 136)),
            _ => new(
                Math.Clamp(colour.R + (int)shift, 72, 156),
                Math.Clamp(colour.G - (int)(shift * .15f), 3, 28),
                Math.Clamp(colour.B + (int)(shift * .30f), 7, 38)),
        };

    private static Vector2[] PatternPoints(BolonAperturePattern pattern, Random rng)
        => pattern switch
        {
            BolonAperturePattern.FourNineFour =>
            [
                new(-1.5f, -1f), new(-.5f, -1f), new(.5f, -1f), new(1.5f, -1f),
                new(-4f, 0f), new(-3f, 0f), new(-2f, 0f), new(-1f, 0f),
                Vector2.Zero, new(1f, 0f), new(2f, 0f), new(3f, 0f), new(4f, 0f),
                new(-1.5f, 1f), new(-.5f, 1f), new(.5f, 1f), new(1.5f, 1f),
            ],
            BolonAperturePattern.CompactFive =>
            [
                Vector2.Zero,
                new(-1.15f, -.35f), new(1.15f, .35f),
                new(-.35f, 1.15f), new(.35f, -1.15f),
            ],
            _ => SparseChain(rng.Next(3, 6)),
        };

    private static Vector2[] SparseChain(int count)
        => Enumerable.Range(0, count)
            .Select(index => new Vector2(index - (count - 1) * .5f, 0f))
            .ToArray();

    private static (Vector3 U, Vector3 V) RandomProjectionFrame(Random rng)
    {
        Vector3 u = RandomUnitVector(rng);
        Vector3 helper = MathF.Abs(u.Y) < .9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = Vector3.Normalize(Vector3.Cross(u, helper));
        return (u, v);
    }

    private static Vector3 RandomUnitVector(Random rng)
    {
        float z = Lerp(-1f, 1f, rng.NextDouble());
        float angle = Lerp(0f, MathF.Tau, rng.NextDouble());
        float radial = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new(radial * MathF.Cos(angle), z, radial * MathF.Sin(angle));
    }

    private static string HistorySignature(
        string stationIdentity,
        IReadOnlyList<BolonVesselSurfaceHistory> histories)
    {
        var text = new StringBuilder("bolon-surface:v1|").Append(stationIdentity);
        foreach (BolonVesselSurfaceHistory history in histories)
        {
            text.Append('|').Append(history.VesselIndex).Append(':')
                .Append(history.BaselineFinish).Append(':')
                .Append(F(history.BaselineAge)).Append(':')
                .Append(V(history.BaselineProjectionU)).Append(':')
                .Append(V(history.BaselineProjectionV));
            foreach (BolonSurfaceHistoryRegion region in history.Regions)
                text.Append('/').Append(region.RegionIndex).Append(':')
                    .Append(region.Finish).Append(':').Append(F(region.Age)).Append(':')
                    .Append(V(region.CenterDirection)).Append(':')
                    .Append(F(region.AngularRadius)).Append(':')
                    .Append(V(region.ProjectionU)).Append(':')
                    .Append(V(region.ProjectionV)).Append(':')
                    .Append(V(region.BoundaryAxisA)).Append(':')
                    .Append(V(region.BoundaryAxisB)).Append(':')
                    .Append(F(region.BoundaryFrequencyA)).Append(':')
                    .Append(F(region.BoundaryFrequencyB)).Append(':')
                    .Append(F(region.BoundaryPhaseA)).Append(':')
                    .Append(F(region.BoundaryPhaseB)).Append(':')
                    .Append(F(region.BoundaryIrregularity)).Append(':')
                    .Append(F(region.ErosionStrength));
        }
        return Hash(text);
    }

    private static string ApertureSignature(
        string stationIdentity,
        IReadOnlyList<BolonApertureGroup> groups)
    {
        var text = new StringBuilder("bolon-apertures:v1|").Append(stationIdentity);
        foreach (BolonApertureGroup group in groups.Where(group => group.PreservedB2aGroup))
        {
            text.Append('|').Append(group.VesselIndex).Append('.')
                .Append(group.HostFaceIndex).Append(':').Append(group.Pattern).Append(':')
                .Append(V(group.Centre)).Append(':').Append(V(group.Normal)).Append(':')
                .Append(V(group.TangentU)).Append(':').Append(V(group.TangentV)).Append(':')
                .Append(F(group.RotationRadians)).Append(':')
                .Append(F(group.CollarOuterRadius)).Append(':')
                .Append(F(group.CollarHeight)).Append(':')
                .Append(F(group.Intensity)).Append(':')
                .Append(group.ApertureColour.PackedValue.ToString("X8", CultureInfo.InvariantCulture));
            foreach (BolonApertureInstance aperture in group.Apertures)
                text.Append('/').Append(V(aperture.Centre)).Append(',').Append(F(aperture.Radius));
        }
        return Hash(text);
    }

    private static string ApertureVisualSignature(
        string stationIdentity,
        IReadOnlyList<BolonApertureGroup> groups)
    {
        var text = new StringBuilder("bolon-aperture-presentation:v1|")
            .Append(stationIdentity);
        foreach (BolonApertureInstance aperture in groups.SelectMany(
                     group => group.Apertures))
        {
            BolonApertureVisualState state = aperture.VisualState;
            text.Append('|').Append(aperture.Identity).Append(':')
                .Append(aperture.PenetrationType).Append(':')
                .Append(aperture.VentScale).Append(':')
                .Append(F(aperture.GrilleRotationRadians)).Append(':')
                .Append(aperture.GrilleRibCount).Append(':')
                .Append(state.Illumination).Append(':')
                .Append(F(state.Brightness)).Append(':')
                .Append(F(state.RecessDepthScale)).Append(':')
                .Append(state.PerimeterColour.PackedValue.ToString("X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(state.MiddleColour.PackedValue.ToString("X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(state.InnerColour.PackedValue.ToString("X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(F(state.SurfacePhase));
        }
        return Hash(text);
    }

    private static string ApertureVocabularySignature(
        string stationIdentity,
        IReadOnlyList<BolonApertureGroup> groups)
    {
        var text = new StringBuilder("bolon-aperture-vocabulary:v1|")
            .Append(stationIdentity);
        foreach (BolonApertureGroup group in groups)
        {
            text.Append('|').Append(group.Identity).Append(':')
                .Append(group.PatternFamily).Append(':')
                .Append(group.PatternVariant).Append(':')
                .Append(group.PaletteFamily).Append(':')
                .Append(group.SelectedCorner).Append(':')
                .Append(group.SelectedEdge).Append(':')
                .Append(group.SymmetricPattern).Append(':')
                .Append(group.PreservedB2aGroup).Append(':')
                .Append(V(group.Centre)).Append(':')
                .Append(F(group.RotationRadians));
            foreach (BolonApertureInstance aperture in group.Apertures)
                text.Append('/').Append(aperture.Identity).Append(',')
                    .Append(V(aperture.Centre)).Append(',')
                    .Append(F(aperture.Radius)).Append(',')
                    .Append(aperture.PenetrationType).Append(',')
                    .Append(aperture.VentScale).Append(',')
                    .Append(F(aperture.GrilleRotationRadians)).Append(',')
                    .Append(aperture.GrilleRibCount);
        }
        return Hash(text);
    }

    private static string Hash(StringBuilder text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

    private static string F(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string V(Vector3 value)
        => $"{F(value.X)},{F(value.Y)},{F(value.Z)}";

    private static float Lerp(float minimum, float maximum, double amount)
        => minimum + (maximum - minimum) * (float)amount;
}

public static class BolonSurfaceMeshBuilder
{
    private sealed record ApertureCutout(Vector3 Normal, Vector3[] Boundary);

    private readonly record struct SurfaceSample(
        BolonSurfaceFinish Finish,
        float Age,
        float ErosionStrength,
        Vector3 ProjectionU,
        Vector3 ProjectionV,
        string Identity);

    public static BolonSurfaceMeshBuildResult Build(
        BolonMegastationPlan structuralPlan,
        BolonSurfacePresentationPlan surfacePlan,
        BolonPentagonalUtilityPlan utilityPlan,
        CancellationToken cancellationToken = default,
        BolonAmbassadorBayPlan? ambassadorBay = null)
    {
        var hull = new StationModuleMesh();
        var glass = new StationModuleMesh();
        var omittedFaces = structuralPlan.Relationships
            .Where(relationship => relationship.Mode == BolonVesselRelationshipMode.DirectFaceJoin)
            .SelectMany(relationship => new[]
            {
                (relationship.A, relationship.FaceA),
                (relationship.B, relationship.FaceB),
            })
            .ToHashSet();
        Dictionary<(int Vessel, int Face), ApertureCutout[]> cutouts =
            surfacePlan.ApertureGroups
                .GroupBy(group => (group.VesselIndex, group.HostFaceIndex))
                .ToDictionary(
                    group => group.Key,
                    group => CreateCutouts(
                        structuralPlan.Vessels[group.Key.VesselIndex], group));
        foreach (IGrouping<(int VesselIndex, int HostFaceIndex), BolonPentagonalUtilityFixture> group
                     in utilityPlan.Fixtures
                         .Where(fixture => fixture.Family
                             == BolonPentagonalUtilityFamily.FiveLeafIris)
                         .GroupBy(fixture => (fixture.VesselIndex, fixture.HostFaceIndex)))
        {
            ApertureCutout[] utilityCutouts = CreateUtilityCutouts(
                structuralPlan.Vessels[group.Key.VesselIndex], group);
            var key = (group.Key.VesselIndex, group.Key.HostFaceIndex);
            cutouts[key] = cutouts.GetValueOrDefault(key, [])
                .Concat(utilityCutouts)
                .ToArray();
        }
        if (ambassadorBay is { } bay)
        {
            BolonVesselPlan vessel = structuralPlan.Vessels[bay.VesselIndex];
            Quaternion inverse = Quaternion.Inverse(vessel.Orientation);
            cutouts[(bay.VesselIndex, bay.HostFaceIndex)] = [new ApertureCutout(
                Vector3.Transform(bay.Outward, inverse), bay.MouthCorners().Select(p =>
                    Vector3.Transform(p - vessel.Position, inverse)).ToArray())];
        }
        int surfaceTrianglesBefore = hull.IndexCount / 3;
        foreach (BolonVesselPlan vessel in structuralPlan.Vessels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BolonVesselSurfaceHistory history = surfacePlan.VesselHistories[vessel.Index];
            foreach (BolonAttachmentFace face in BolonMegastationGenerator.AttachmentFaces)
            {
                if (omittedFaces.Contains((vessel.Index, face.Index)))
                    continue;
                IReadOnlyList<Vector3> polygon =
                    BolonMegastationGenerator.GetAttachmentFaceVertices(face.Index);
                IReadOnlyList<ApertureCutout> faceCutouts = cutouts.GetValueOrDefault(
                    (vessel.Index, face.Index), []);
                for (int i = 1; i < polygon.Count - 1; i++)
                    EmitSurfacePatch(
                        hull,
                        structuralPlan,
                        vessel,
                        history,
                        polygon[0] * vessel.Radius,
                        polygon[i] * vessel.Radius,
                        polygon[i + 1] * vessel.Radius,
                        subdivisionsRemaining: 2,
                        cutouts: faceCutouts);
            }
        }
        int surfaceTriangles = hull.IndexCount / 3 - surfaceTrianglesBefore;

        foreach (BolonVesselRelationship relationship in structuralPlan.Relationships.Where(
                     relationship => relationship.Mode == BolonVesselRelationshipMode.ShortConnector))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BolonVesselPlan a = structuralPlan.Vessels[relationship.A];
            BolonVesselPlan b = structuralPlan.Vessels[relationship.B];
            Vector3 axis = Vector3.Normalize(b.Position - a.Position);
            Vector3 start = FaceWorldCenter(a, relationship.FaceA) - axis * 2f;
            Vector3 end = FaceWorldCenter(b, relationship.FaceB) + axis * 2f;
            hull.CurrentMaterialFamily = SystemMaterialFamilyId.AgedMetal;
            hull.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
                SystemMaterialFamilyId.AgedMetal).TileSizeMeters;
            hull.AddPrismPipe(
                start,
                end,
                relationship.ConnectorRadius,
                12,
                ConnectorColour(structuralPlan.Archetype));
        }

        int collarStart = hull.IndexCount / 3;
        int ventGrilleTriangles = 0;
        foreach (BolonApertureGroup group in surfacePlan.ApertureGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (BolonApertureInstance aperture in group.Apertures)
                ventGrilleTriangles += EmitAperture(
                    hull, glass, structuralPlan.Archetype, group, aperture);
        }
        int collarTriangles = hull.IndexCount / 3 - collarStart;
        int reinforcementTriangles = 0;
        int irisTriangles = 0;
        int rosetteTriangles = 0;
        foreach (BolonPentagonalUtilityFixture fixture in utilityPlan.Fixtures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = hull.IndexCount / 3;
            switch (fixture.Family)
            {
                case BolonPentagonalUtilityFamily.ReinforcementCollar:
                    EmitReinforcementCollar(hull, fixture);
                    reinforcementTriangles += hull.IndexCount / 3 - start;
                    break;
                case BolonPentagonalUtilityFamily.FiveLeafIris:
                    EmitFiveLeafIris(hull, fixture);
                    irisTriangles += hull.IndexCount / 3 - start;
                    break;
                case BolonPentagonalUtilityFamily.ApparatusRosette:
                    EmitApparatusRosette(hull, fixture);
                    rosetteTriangles += hull.IndexCount / 3 - start;
                    break;
            }
        }
        int ambassadorStart = hull.IndexCount / 3;
        if (ambassadorBay != null)
        {
            // Previously DynamicLit ignored hull alpha. Zero all accepted exterior
            // vertices before opting this combined mesh into the illumination floor.
            hull.ApplyIlluminationFlags();
            EmitAmbassadorChamfer(hull, structuralPlan, surfacePlan, ambassadorBay);
            BolonAmbassadorBayMeshBuilder.Emit(hull, ambassadorBay);
            MegastationApproachFixtures.Emit(hull, ambassadorBay.ApproachFixtures().SelectMany(f => f.Elements));
        }
        hull.BaseFaceCount = hull.FaceCount;
        glass.BaseFaceCount = glass.FaceCount;
        return new(
            hull,
            glass,
            surfaceTriangles,
            collarTriangles,
            glass.IndexCount / 3,
            ventGrilleTriangles,
            reinforcementTriangles,
            irisTriangles,
            rosetteTriangles) { AmbassadorTriangleCount = hull.IndexCount / 3 - ambassadorStart };
    }

    private static void EmitSurfacePatch(
        StationModuleMesh mesh,
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        BolonVesselSurfaceHistory history,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        int subdivisionsRemaining,
        IReadOnlyList<ApertureCutout> cutouts)
    {
        if (subdivisionsRemaining == 0)
        {
            EmitCutSurfaceTriangle(
                mesh, plan, vessel, history, a, b, c, cutouts);
            return;
        }
        Vector3 ab = (a + b) * .5f;
        Vector3 bc = (b + c) * .5f;
        Vector3 ca = (c + a) * .5f;
        int next = subdivisionsRemaining - 1;
        EmitSurfacePatch(mesh, plan, vessel, history, a, ab, ca, next, cutouts);
        EmitSurfacePatch(mesh, plan, vessel, history, ab, b, bc, next, cutouts);
        EmitSurfacePatch(mesh, plan, vessel, history, ca, bc, c, next, cutouts);
        EmitSurfacePatch(mesh, plan, vessel, history, ab, bc, ca, next, cutouts);
    }

    private static void EmitCutSurfaceTriangle(
        StationModuleMesh mesh,
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        BolonVesselSurfaceHistory history,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        IReadOnlyList<ApertureCutout> cutouts)
    {
        List<Vector3[]> pieces = [[a, b, c]];
        foreach (ApertureCutout cutout in cutouts)
        {
            var next = new List<Vector3[]>();
            foreach (Vector3[] piece in pieces)
                SubtractConvexHole(piece, cutout, next);
            pieces = next;
            if (pieces.Count == 0)
                return;
        }
        foreach (Vector3[] piece in pieces)
        {
            for (int i = 1; i < piece.Length - 1; i++)
            {
                if (Vector3.Cross(piece[i] - piece[0], piece[i + 1] - piece[0])
                        .LengthSquared() <= 1e-2f)
                    continue;
                EmitSurfaceTriangle(
                    mesh, plan, vessel, history, piece[0], piece[i], piece[i + 1]);
            }
        }
    }

    private static void SubtractConvexHole(
        Vector3[] polygon,
        ApertureCutout hole,
        ICollection<Vector3[]> outsidePieces)
    {
        Vector3[] remaining = polygon;
        for (int edgeIndex = 0; edgeIndex < hole.Boundary.Length; edgeIndex++)
        {
            Vector3 edgeStart = hole.Boundary[edgeIndex];
            Vector3 edgeEnd = hole.Boundary[(edgeIndex + 1) % hole.Boundary.Length];
            SplitByEdge(
                remaining,
                edgeStart,
                edgeEnd,
                hole.Normal,
                out Vector3[] inside,
                out Vector3[] outside);
            if (outside.Length >= 3)
                outsidePieces.Add(outside);
            remaining = inside;
            if (remaining.Length < 3)
                return;
        }
        // What remains is inside the opening and is deliberately discarded.
    }

    internal static void EmitAmbassadorBulkhead(StationModuleMesh mesh, Vector3[] octagon,
        Vector3[] opening, Vector3 normal, Color colour)
    {
        var pieces = new List<Vector3[]>();
        Vector3 openingNormal = Vector3.Normalize(Vector3.Cross(opening[1] - opening[0], opening[2] - opening[0]));
        SubtractConvexHole(octagon, new ApertureCutout(openingNormal, opening), pieces);
        foreach (Vector3[] piece in pieces)
        for (int i = 1; i < piece.Length - 1; i++)
            BolonAmbassadorBayMeshBuilder.Triangle(mesh, piece[0], piece[i], piece[i + 1], normal, colour);
    }

    private static void SplitByEdge(
        IReadOnlyList<Vector3> polygon,
        Vector3 edgeStart,
        Vector3 edgeEnd,
        Vector3 normal,
        out Vector3[] inside,
        out Vector3[] outside)
    {
        var insidePoints = new List<Vector3>(polygon.Count + 1);
        var outsidePoints = new List<Vector3>(polygon.Count + 1);
        Vector3 edge = edgeEnd - edgeStart;
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector3 current = polygon[index];
            Vector3 next = polygon[(index + 1) % polygon.Count];
            float currentDistance = Vector3.Dot(
                Vector3.Cross(edge, current - edgeStart), normal);
            float nextDistance = Vector3.Dot(
                Vector3.Cross(edge, next - edgeStart), normal);
            bool currentInside = currentDistance >= -1e-5f;
            bool nextInside = nextDistance >= -1e-5f;
            AddUnique(currentInside ? insidePoints : outsidePoints, current);
            if (currentInside == nextInside)
                continue;
            float denominator = currentDistance - nextDistance;
            if (MathF.Abs(denominator) <= 1e-8f)
                continue;
            Vector3 intersection = Vector3.Lerp(
                current, next, currentDistance / denominator);
            AddUnique(insidePoints, intersection);
            AddUnique(outsidePoints, intersection);
        }
        inside = CleanPolygon(insidePoints);
        outside = CleanPolygon(outsidePoints);
    }

    private static void AddUnique(ICollection<Vector3> points, Vector3 point)
    {
        if (points.Count == 0 || Vector3.DistanceSquared(points.Last(), point) > 1e-8f)
            points.Add(point);
    }

    private static Vector3[] CleanPolygon(List<Vector3> points)
    {
        if (points.Count > 1
            && Vector3.DistanceSquared(points[0], points[^1]) <= 1e-8f)
            points.RemoveAt(points.Count - 1);
        return points.ToArray();
    }

    private static void EmitSurfaceTriangle(
        StationModuleMesh mesh,
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        BolonVesselSurfaceHistory history,
        Vector3 localA,
        Vector3 localB,
        Vector3 localC,
        Vector3? materialSamplePoint = null,
        Vector3? expectedStationNormal = null)
    {
        Vector3 sampleDirection = Vector3.Normalize(materialSamplePoint ?? (localA + localB + localC) / 3f);
        SurfaceSample sample = Resolve(history, sampleDirection);
        SystemMaterialFamilyId family = MaterialFamily(sample.Finish);
        mesh.CurrentMaterialFamily = family;
        float tile = SystemMaterialRecipes.Get(family).TileSizeMeters;
        Color colour = SurfaceColour(
            plan.Archetype, vessel.Index, sample, plan.StationIdentity);
        if (expectedStationNormal.HasValue)
            colour.A = 0; // hull-matched B4a.1 reveal: no artificial illumination override
        Vector3 worldA = vessel.Position + Vector3.Transform(localA, vessel.Orientation);
        Vector3 worldB = vessel.Position + Vector3.Transform(localB, vessel.Orientation);
        Vector3 worldC = vessel.Position + Vector3.Transform(localC, vessel.Orientation);
        Vector2 uvA = Project(localA, sample, tile);
        Vector2 uvB = Project(localB, sample, tile);
        Vector2 uvC = Project(localC, sample, tile);
        if (Vector3.Dot(Vector3.Cross(worldB - worldA, worldC - worldA),
                expectedStationNormal ?? Vector3.Transform(sampleDirection, vessel.Orientation)) < 0f)
            mesh.AddTriangleWithUv(worldA, uvA, worldC, uvC, worldB, uvB, colour);
        else
            mesh.AddTriangleWithUv(worldA, uvA, worldB, uvB, worldC, uvC, colour);
    }

    internal static void EmitAmbassadorChamfer(StationModuleMesh mesh, BolonMegastationPlan structural,
        BolonSurfacePresentationPlan surfaces, BolonAmbassadorBayPlan bay)
    {
        var vessel = structural.Vessels[bay.VesselIndex];
        var face = BolonMegastationGenerator.GetAttachmentFace(bay.HostFaceIndex);
        Quaternion inverse = Quaternion.Inverse(vessel.Orientation);
        Vector3[] mouth = bay.MouthCorners();
        Vector3[] reveal = bay.Rectangle(bay.MouthWidth, bay.MouthHeight, bay.OuterRevealDepth);
        Vector3[] inner = bay.Rectangle(bay.ClearWidth, bay.ClearHeight, bay.ChamferDepth);
        Join(mouth, reveal);
        Join(reveal, inner);

        void Join(Vector3[] a, Vector3[] b)
        {
            for (int edge = 0; edge < 4; edge++)
            {
                int j = (edge + 1) % 4;
                int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a[edge], a[j]) / 16f));
                for (int segment = 0; segment < steps; segment++)
                {
                    float t0 = segment / (float)steps, t1 = (segment + 1f) / steps;
                    Vector3 p0 = Vector3.Lerp(a[edge], a[j], t0), p1 = Vector3.Lerp(a[edge], a[j], t1);
                    Vector3 p2 = Vector3.Lerp(b[edge], b[j], t1), p3 = Vector3.Lerp(b[edge], b[j], t0);
                    Vector3 q = bay.Coordinates((p0 + p1 + p2 + p3) / 4f);
                    Vector3 inward = -bay.Right * q.X - bay.Up * q.Y;
                    Emit(p0, p1, p2, inward); Emit(p0, p2, p3, inward);
                }
            }
        }
        void Emit(Vector3 a, Vector3 b, Vector3 c, Vector3 inward)
        {
            Vector3 la = Vector3.Transform(a - vessel.Position, inverse);
            Vector3 lb = Vector3.Transform(b - vessel.Position, inverse);
            Vector3 lc = Vector3.Transform(c - vessel.Position, inverse);
            Vector3 sample = (la + lb + lc) / 3f;
            // Sample the surrounding exterior pressure shell, not a contrasting
            // interior recipe. Reuse its exact finish, tint, history and physical UVs.
            sample -= face.LocalNormal * Vector3.Dot(sample - face.LocalCenter * vessel.Radius, face.LocalNormal);
            EmitSurfaceTriangle(mesh, structural, vessel, surfaces.VesselHistories[vessel.Index],
                la, lb, lc, sample, inward);
        }
    }

    private static SurfaceSample Resolve(
        BolonVesselSurfaceHistory history,
        Vector3 direction)
    {
        var result = new SurfaceSample(
            history.BaselineFinish,
            history.BaselineAge,
            0f,
            history.BaselineProjectionU,
            history.BaselineProjectionV,
            history.Identity);
        foreach (BolonSurfaceHistoryRegion region in history.Regions)
        {
            if (BolonSurfacePresentationPlanner.Contains(region, direction))
            {
                result = new(
                    region.Finish,
                    region.Age,
                    region.ErosionStrength,
                    region.ProjectionU,
                    region.ProjectionV,
                    region.Identity);
            }
        }
        return result;
    }

    private static Vector2 Project(Vector3 localPoint, SurfaceSample sample, float tile)
        => new(
            Vector3.Dot(localPoint, sample.ProjectionU) / tile,
            Vector3.Dot(localPoint, sample.ProjectionV) / tile);

    private static SystemMaterialFamilyId MaterialFamily(BolonSurfaceFinish finish)
        => finish switch
        {
            BolonSurfaceFinish.Polished => SystemMaterialFamilyId.PolishedMetal,
            BolonSurfaceFinish.Brushed => SystemMaterialFamilyId.BrushedMetal,
            BolonSurfaceFinish.Eroded => SystemMaterialFamilyId.ErodedMetal,
            _ => SystemMaterialFamilyId.AgedMetal,
        };

    private static Color SurfaceColour(
        MegastationArchetype archetype,
        int vesselIndex,
        SurfaceSample sample,
        string stationIdentity)
    {
        Color baseColour = archetype == MegastationArchetype.RedBolon
            ? new Color(211, 105, 47)
            : new Color(235, 181, 54);
        int seed = MegastationSeed.Derive(
            MegastationSeed.Root(stationIdentity, 2),
            $"surface-colour:{vesselIndex}:{sample.Identity}");
        var rng = new Random(seed);
        float delta = sample.Finish switch
        {
            BolonSurfaceFinish.Polished => Lerp(5f, 14f, rng.NextDouble()),
            BolonSurfaceFinish.Brushed => Lerp(-3f, 5f, rng.NextDouble()),
            BolonSurfaceFinish.Eroded => Lerp(-24f, -12f, rng.NextDouble())
                - sample.ErosionStrength * 5f,
            _ => Lerp(-13f, -4f, rng.NextDouble()),
        };
        delta -= sample.Age * 3f;
        return ProceduralMaterialCpuGenerator.ShiftLuminance(baseColour, delta);
    }

    private static int EmitAperture(
        StationModuleMesh hull,
        StationModuleMesh glass,
        MegastationArchetype archetype,
        BolonApertureGroup group,
        BolonApertureInstance aperture)
    {
        Vector3 n = group.Normal;
        Vector3 u = group.TangentU;
        Vector3 v = group.TangentV;
        BolonApertureVisualState visual = aperture.VisualState;
        float outerRadius = group.CollarOuterRadius;
        float rimInnerRadius = outerRadius * .86f;
        float opticalRadius = aperture.Radius * 1.06f;
        float throatRadius = opticalRadius * 1.10f;
        float depth = group.CollarHeight * visual.RecessDepthScale;
        Vector3 outerCenter = aperture.Centre + n * .035f;
        Vector3 rimInnerCenter = aperture.Centre - n * .20f;
        Vector3 throatCenter = aperture.Centre - n * (depth * .80f);
        Vector3 opticalCenter = aperture.Centre - n * depth;
        Vector3[] outerRing = Ring(outerCenter, u, v, outerRadius);
        Vector3[] rimInnerRing = Ring(rimInnerCenter, u, v, rimInnerRadius);
        Vector3[] throatRing = Ring(throatCenter, u, v, throatRadius);
        Vector3[] opticalRing = Ring(opticalCenter, u, v, opticalRadius);
        Color rim = archetype == MegastationArchetype.RedBolon
            ? new Color(119, 64, 39)
            : new Color(128, 91, 42);
        Color throat = archetype == MegastationArchetype.RedBolon
            ? new Color(25, 12, 13)
            : new Color(27, 16, 14);
        hull.CurrentMaterialFamily = SystemMaterialFamilyId.AgedMetal;
        hull.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
            SystemMaterialFamilyId.AgedMetal).TileSizeMeters;
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            AddQuadFacing(hull,
                outerRing[i], outerRing[next], rimInnerRing[next], rimInnerRing[i],
                n, rim);
        }
        hull.CurrentMaterialFamily = SystemMaterialFamilyId.ErodedMetal;
        hull.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
            SystemMaterialFamilyId.ErodedMetal).TileSizeMeters;
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            AddQuadFacing(hull,
                rimInnerRing[i], rimInnerRing[next], throatRing[next], throatRing[i],
                Vector3.Normalize(aperture.Centre - rimInnerRing[i]), throat);
            AddQuadFacing(hull,
                throatRing[i], throatRing[next], opticalRing[next], opticalRing[i],
                Vector3.Normalize(opticalCenter - opticalRing[i]), throat);
        }
        if (aperture.PenetrationType == BolonShellPenetrationType.Vent)
            return EmitVentInterior(
                hull, opticalCenter, u, v, n, opticalRadius, throat, aperture);
        EmitOpticalSurface(glass, opticalCenter, u, v, n, opticalRadius, visual);
        return 0;
    }

    private static void EmitReinforcementCollar(
        StationModuleMesh hull,
        BolonPentagonalUtilityFixture fixture)
    {
        SetMaterial(hull, fixture.MaterialFamily);
        Vector3 n = fixture.Normal;
        Vector3[] outer = PentagonRing(
            fixture.Centre + n * .035f,
            fixture.TangentU,
            fixture.TangentV,
            fixture.OuterRadius);
        Vector3[] shoulder = PentagonRing(
            fixture.Centre + n * fixture.ReliefHeight,
            fixture.TangentU,
            fixture.TangentV,
            fixture.OuterRadius * .82f);
        Vector3[] inner = PentagonRing(
            fixture.Centre + n * (fixture.ReliefHeight * .38f),
            fixture.TangentU,
            fixture.TangentV,
            fixture.InnerRadius);
        for (int index = 0; index < 5; index++)
        {
            int next = (index + 1) % 5;
            AddQuadFacing(hull,
                outer[index], outer[next], shoulder[next], shoulder[index],
                n, fixture.StructuralColour);
            AddQuadFacing(hull,
                shoulder[index], shoulder[next], inner[next], inner[index],
                n, fixture.SecondaryColour);
        }
        AddPolygonFacing(hull, inner, n, fixture.StructuralColour);

        float ribHalfAngle = .055f;
        for (int index = 0; index < 5; index++)
        {
            float angle = index * MathF.Tau / 5f;
            Vector3[] rib =
            [
                Polar(fixture, angle - ribHalfAngle, fixture.InnerRadius * .82f,
                    fixture.ReliefHeight * .58f),
                Polar(fixture, angle + ribHalfAngle, fixture.InnerRadius * .82f,
                    fixture.ReliefHeight * .58f),
                Polar(fixture, angle + ribHalfAngle * .62f, fixture.OuterRadius * .91f,
                    fixture.ReliefHeight * .58f),
                Polar(fixture, angle - ribHalfAngle * .62f, fixture.OuterRadius * .91f,
                    fixture.ReliefHeight * .58f),
            ];
            EmitExtrudedPolygon(
                hull, rib, n, fixture.ReliefHeight * .28f, fixture.SecondaryColour);
        }
    }

    private static void EmitFiveLeafIris(
        StationModuleMesh hull,
        BolonPentagonalUtilityFixture fixture)
    {
        Vector3 n = fixture.Normal;
        SetMaterial(hull, fixture.MaterialFamily);
        Vector3[] outer = PentagonRing(
            fixture.Centre + n * .035f,
            fixture.TangentU,
            fixture.TangentV,
            fixture.OuterRadius);
        Vector3[] lip = PentagonRing(
            fixture.Centre - n * .34f,
            fixture.TangentU,
            fixture.TangentV,
            fixture.OuterRadius * .88f);
        float doorRadius = fixture.InnerRadius;
        Vector3 doorCenter = fixture.Centre - n * fixture.RecessDepth;
        Vector3[] throat = PentagonRing(
            doorCenter,
            fixture.TangentU,
            fixture.TangentV,
            doorRadius);
        for (int index = 0; index < 5; index++)
        {
            int next = (index + 1) % 5;
            AddQuadFacing(hull,
                outer[index], outer[next], lip[next], lip[index],
                n, fixture.StructuralColour);
        }
        SetMaterial(hull, SystemMaterialFamilyId.ErodedMetal);
        Color throatColour = new(24, 15, 13);
        for (int index = 0; index < 5; index++)
        {
            int next = (index + 1) % 5;
            Vector3 inward = Vector3.Normalize(fixture.Centre - lip[index]);
            AddQuadFacing(hull,
                lip[index], lip[next], throat[next], throat[index],
                inward, throatColour);
        }
        Vector3 cavityCenter = doorCenter - n * .55f;
        Vector3[] cavity = PentagonRing(
            cavityCenter,
            fixture.TangentU,
            fixture.TangentV,
            doorRadius * .98f);
        AddPolygonFacing(hull, cavity, n, new Color(12, 8, 8));

        SetMaterial(hull, fixture.MaterialFamily);
        float leafThickness = MathF.Max(.32f, fixture.ReliefHeight * .72f);
        for (int leafIndex = 0; leafIndex < 5; leafIndex++)
        {
            float angle = leafIndex * MathF.Tau / 5f;
            float layer = leafIndex * .045f;
            Vector3 leafOrigin = doorCenter + n * layer;
            Vector3[] leaf =
            [
                Polar(leafOrigin, fixture, angle - MathF.PI / 5f, doorRadius * .96f),
                Polar(leafOrigin, fixture, angle + MathF.PI / 5f, doorRadius * .96f),
                Polar(leafOrigin, fixture, angle + .39f, doorRadius * .53f),
                Polar(leafOrigin, fixture, angle + .20f, doorRadius * .20f),
                Polar(leafOrigin, fixture, angle - .20f, doorRadius * .20f),
                Polar(leafOrigin, fixture, angle - .39f, doorRadius * .53f),
            ];
            Color leafColour = leafIndex % 2 == 0
                ? fixture.StructuralColour
                : fixture.SecondaryColour;
            EmitExtrudedPolygon(hull, leaf, n, -leafThickness, leafColour);
        }
        Vector3 lockCenter = doorCenter + n * .28f;
        EmitPentagonalPrism(
            hull,
            lockCenter,
            fixture.TangentU,
            fixture.TangentV,
            n,
            doorRadius * .18f,
            MathF.Max(.42f, fixture.ReliefHeight * .65f),
            fixture.SecondaryColour);
    }

    private static void EmitApparatusRosette(
        StationModuleMesh hull,
        BolonPentagonalUtilityFixture fixture)
    {
        SetMaterial(hull, fixture.MaterialFamily);
        Vector3 n = fixture.Normal;
        float baseHeight = fixture.ReliefHeight * .22f;
        EmitPentagonalPrism(
            hull,
            fixture.Centre + n * .03f,
            fixture.TangentU,
            fixture.TangentV,
            n,
            fixture.InnerRadius,
            baseHeight,
            fixture.StructuralColour);
        float bladeHalfAngle = .12f;
        for (int index = 0; index < 5; index++)
        {
            float angle = index * MathF.Tau / 5f;
            Vector3[] blade =
            [
                Polar(fixture, angle - bladeHalfAngle, fixture.InnerRadius * .72f,
                    baseHeight * .55f),
                Polar(fixture, angle + bladeHalfAngle, fixture.InnerRadius * .72f,
                    baseHeight * .55f),
                Polar(fixture, angle + bladeHalfAngle * .42f, fixture.OuterRadius * .90f,
                    baseHeight * .55f),
                Polar(fixture, angle - bladeHalfAngle * .42f, fixture.OuterRadius * .90f,
                    baseHeight * .55f),
            ];
            EmitExtrudedPolygon(
                hull,
                blade,
                n,
                fixture.ReliefHeight * .34f,
                fixture.SecondaryColour);
            Vector3 nodeCenter = Polar(
                fixture,
                angle,
                fixture.OuterRadius * .70f,
                baseHeight + fixture.ReliefHeight * .31f);
            EmitPentagonalPrism(
                hull,
                nodeCenter,
                fixture.TangentU,
                fixture.TangentV,
                n,
                fixture.OuterRadius * .075f,
                fixture.ReliefHeight * .22f,
                fixture.HasOpticalAccent
                    ? fixture.AccentColour
                    : fixture.StructuralColour);
        }
    }

    private static void SetMaterial(
        StationModuleMesh mesh,
        SystemMaterialFamilyId family)
    {
        mesh.CurrentMaterialFamily = family;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(family).TileSizeMeters;
    }

    private static Vector3 Polar(
        BolonPentagonalUtilityFixture fixture,
        float angle,
        float radius,
        float normalOffset)
        => fixture.Centre + fixture.Normal * normalOffset
            + fixture.TangentU * (MathF.Cos(angle) * radius)
            + fixture.TangentV * (MathF.Sin(angle) * radius);

    private static Vector3 Polar(
        Vector3 center,
        BolonPentagonalUtilityFixture fixture,
        float angle,
        float radius)
        => center + fixture.TangentU * (MathF.Cos(angle) * radius)
            + fixture.TangentV * (MathF.Sin(angle) * radius);

    private static Vector3[] PentagonRing(
        Vector3 center,
        Vector3 u,
        Vector3 v,
        float radius)
        => Enumerable.Range(0, 5)
            .Select(index =>
            {
                float angle = index * MathF.Tau / 5f;
                return center + u * (MathF.Cos(angle) * radius)
                    + v * (MathF.Sin(angle) * radius);
            })
            .ToArray();

    private static void EmitPentagonalPrism(
        StationModuleMesh mesh,
        Vector3 baseCenter,
        Vector3 u,
        Vector3 v,
        Vector3 normal,
        float radius,
        float height,
        Color colour)
        => EmitExtrudedPolygon(
            mesh,
            PentagonRing(baseCenter, u, v, radius),
            normal,
            height,
            colour);

    private static void EmitExtrudedPolygon(
        StationModuleMesh mesh,
        IReadOnlyList<Vector3> basePolygon,
        Vector3 normal,
        float height,
        Color colour)
    {
        Vector3[] top = basePolygon.Select(point => point + normal * height).ToArray();
        Vector3 topNormal = height >= 0f ? normal : -normal;
        AddPolygonFacing(mesh, basePolygon, -topNormal, colour);
        AddPolygonFacing(mesh, top, topNormal, colour);
        Vector3 centroid = basePolygon.Aggregate(Vector3.Zero, (sum, point) => sum + point)
            / basePolygon.Count;
        for (int index = 0; index < basePolygon.Count; index++)
        {
            int next = (index + 1) % basePolygon.Count;
            Vector3 expected = ExtrudedSideNormal(
                centroid, basePolygon[index], basePolygon[next], normal);
            AddQuadFacing(mesh,
                basePolygon[index], basePolygon[next], top[next], top[index],
                expected, colour);
        }
    }

    internal static Vector3 ExtrudedSideNormal(
        Vector3 polygonCentroid,
        Vector3 edgeStart,
        Vector3 edgeEnd,
        Vector3 extrusionNormal)
    {
        Vector3 outward = (edgeStart + edgeEnd) * .5f - polygonCentroid;
        outward -= extrusionNormal * Vector3.Dot(outward, extrusionNormal);
        if (outward.LengthSquared() <= 1e-8f)
            outward = Vector3.Cross(edgeEnd - edgeStart, extrusionNormal);
        return Vector3.Normalize(outward);
    }

    private static void AddPolygonFacing(
        StationModuleMesh mesh,
        IReadOnlyList<Vector3> polygon,
        Vector3 expectedNormal,
        Color colour)
    {
        for (int index = 1; index < polygon.Count - 1; index++)
            AddGradientTriangleFacing(
                mesh,
                polygon[0], colour,
                polygon[index], colour,
                polygon[index + 1], colour,
                expectedNormal);
    }

    private static int EmitVentInterior(
        StationModuleMesh hull,
        Vector3 center,
        Vector3 u,
        Vector3 v,
        Vector3 normal,
        float radius,
        Color throatColour,
        BolonApertureInstance vent)
    {
        int start = hull.IndexCount / 3;
        hull.CurrentMaterialFamily = SystemMaterialFamilyId.ErodedMetal;
        hull.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
            SystemMaterialFamilyId.ErodedMetal).TileSizeMeters;
        Vector3[] backRing = Ring(center, u, v, radius);
        Color cavity = Modulate(throatColour, .56f);
        for (int index = 0; index < 6; index++)
            AddGradientTriangleFacing(
                hull,
                center, cavity,
                backRing[index], cavity,
                backRing[(index + 1) % 6], cavity,
                normal);

        float cos = MathF.Cos(vent.GrilleRotationRadians);
        float sin = MathF.Sin(vent.GrilleRotationRadians);
        Vector3 grilleU = u * cos + v * sin;
        Vector3 grilleV = -u * sin + v * cos;
        int ribCount = Math.Clamp(vent.GrilleRibCount, 3, 7);
        float usableSpan = radius * 1.30f;
        float ribWidth = radius * (ribCount <= 4 ? .15f : .11f);
        float ribDepth = MathF.Max(.22f, radius * .055f);
        Color rib = Modulate(throatColour, 1.45f);
        for (int index = 0; index < ribCount; index++)
        {
            float across = ribCount == 1
                ? 0f
                : Lerp(-usableSpan * .5f, usableSpan * .5f,
                    index / (double)(ribCount - 1));
            float normalized = across / MathF.Max(.001f, radius);
            float length = radius * 1.55f
                * MathF.Sqrt(MathF.Max(.42f, 1f - normalized * normalized * .72f));
            AddOrientedBox(
                hull,
                center + normal * (ribDepth * .65f) + grilleV * across,
                grilleU,
                grilleV,
                normal,
                length,
                ribWidth,
                ribDepth,
                rib);
        }
        return hull.IndexCount / 3 - start;
    }

    private static void AddOrientedBox(
        StationModuleMesh mesh,
        Vector3 center,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        float sizeX,
        float sizeY,
        float sizeZ,
        Color colour)
    {
        Vector3 x = axisX * (sizeX * .5f);
        Vector3 y = axisY * (sizeY * .5f);
        Vector3 z = axisZ * (sizeZ * .5f);
        Vector3 p000 = center - x - y - z;
        Vector3 p100 = center + x - y - z;
        Vector3 p110 = center + x + y - z;
        Vector3 p010 = center - x + y - z;
        Vector3 p001 = center - x - y + z;
        Vector3 p101 = center + x - y + z;
        Vector3 p111 = center + x + y + z;
        Vector3 p011 = center - x + y + z;
        AddQuadFacing(mesh, p001, p101, p111, p011, axisZ, colour);
        AddQuadFacing(mesh, p100, p000, p010, p110, -axisZ, colour);
        AddQuadFacing(mesh, p101, p100, p110, p111, axisX, colour);
        AddQuadFacing(mesh, p000, p001, p011, p010, -axisX, colour);
        AddQuadFacing(mesh, p011, p111, p110, p010, axisY, colour);
        AddQuadFacing(mesh, p000, p100, p101, p001, -axisY, colour);
    }

    private static void EmitOpticalSurface(
        StationModuleMesh glass,
        Vector3 center,
        Vector3 u,
        Vector3 v,
        Vector3 normal,
        float radius,
        BolonApertureVisualState visual)
    {
        Vector3[] outer = Ring(center, u, v, radius);
        Vector3[] middle = Ring(center, u, v, radius * .66f);
        Vector3[] inner = Ring(center, u, v, radius * .24f);
        Color centerColour = Modulate(visual.InnerColour,
            .89f + .035f * MathF.Sin(visual.SurfacePhase * 1.7f));
        for (int index = 0; index < 6; index++)
        {
            int next = (index + 1) % 6;
            Color outerA = Modulate(visual.PerimeterColour,
                SectorFactor(index, visual.SurfacePhase, .055f));
            Color outerB = Modulate(visual.PerimeterColour,
                SectorFactor(next, visual.SurfacePhase, .055f));
            Color middleA = Modulate(visual.MiddleColour,
                SectorFactor(index, visual.SurfacePhase + .8f, .085f));
            Color middleB = Modulate(visual.MiddleColour,
                SectorFactor(next, visual.SurfacePhase + .8f, .085f));
            Color innerA = Modulate(visual.InnerColour,
                SectorFactor(index, visual.SurfacePhase + 1.7f, .065f));
            Color innerB = Modulate(visual.InnerColour,
                SectorFactor(next, visual.SurfacePhase + 1.7f, .065f));
            AddGradientQuadFacing(
                glass,
                outer[index], outerA,
                outer[next], outerB,
                middle[next], middleB,
                middle[index], middleA,
                normal);
            AddGradientQuadFacing(
                glass,
                middle[index], middleA,
                middle[next], middleB,
                inner[next], innerB,
                inner[index], innerA,
                normal);
            AddGradientTriangleFacing(
                glass,
                center, centerColour,
                inner[index], innerA,
                inner[next], innerB,
                normal);
        }
    }

    private static float SectorFactor(int index, float phase, float amplitude)
        => 1f + amplitude * MathF.Sin(phase + index * 1.73f)
            + amplitude * .35f * MathF.Sin(phase * .63f + index * 2.41f);

    private static Color Modulate(Color colour, float factor)
        => new(
            Math.Clamp((int)MathF.Round(colour.R * factor), 0, 255),
            Math.Clamp((int)MathF.Round(colour.G * factor), 0, 255),
            Math.Clamp((int)MathF.Round(colour.B * factor), 0, 255),
            colour.A);

    private static ApertureCutout[] CreateCutouts(
        BolonVesselPlan vessel,
        IEnumerable<BolonApertureGroup> groups)
    {
        Quaternion inverse = Quaternion.Inverse(vessel.Orientation);
        var result = new List<ApertureCutout>();
        foreach (BolonApertureGroup group in groups)
        {
            Vector3 localNormal = Vector3.Normalize(Vector3.Transform(group.Normal, inverse));
            Vector3 localU = Vector3.Normalize(Vector3.Transform(group.TangentU, inverse));
            Vector3 localV = Vector3.Normalize(Vector3.Transform(group.TangentV, inverse));
            foreach (BolonApertureInstance aperture in group.Apertures)
            {
                Vector3 localCenter = Vector3.Transform(
                    aperture.Centre - vessel.Position, inverse);
                result.Add(new(
                    localNormal,
                    Ring(localCenter, localU, localV, group.CollarOuterRadius)));
            }
        }
        return result.ToArray();
    }

    private static ApertureCutout[] CreateUtilityCutouts(
        BolonVesselPlan vessel,
        IEnumerable<BolonPentagonalUtilityFixture> fixtures)
    {
        Quaternion inverse = Quaternion.Inverse(vessel.Orientation);
        return fixtures.Select(fixture =>
        {
            Vector3 localCenter = Vector3.Transform(
                fixture.Centre - vessel.Position, inverse);
            Vector3 localNormal = Vector3.Normalize(Vector3.Transform(
                fixture.Normal, inverse));
            Vector3 localU = Vector3.Normalize(Vector3.Transform(
                fixture.TangentU, inverse));
            Vector3 localV = Vector3.Normalize(Vector3.Transform(
                fixture.TangentV, inverse));
            return new ApertureCutout(
                localNormal,
                PentagonRing(localCenter, localU, localV, fixture.OuterRadius));
        }).ToArray();
    }

    private static Vector3[] Ring(
        Vector3 center,
        Vector3 u,
        Vector3 v,
        float radius)
        => Enumerable.Range(0, 6)
            .Select(index =>
            {
                float angle = index * MathF.Tau / 6f;
                return center + u * (MathF.Cos(angle) * radius)
                    + v * (MathF.Sin(angle) * radius);
            })
            .ToArray();

    private static void AddQuadFacing(
        StationModuleMesh mesh,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 expectedNormal,
        Color colour)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-4f
            || Vector3.Cross(c - a, d - a).LengthSquared() <= 1e-4f)
            return;
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) < 0f)
            mesh.AddQuad(a, d, c, b, colour);
        else
            mesh.AddQuad(a, b, c, d, colour);
    }

    private static void AddGradientQuadFacing(
        StationModuleMesh mesh,
        Vector3 a, Color colourA,
        Vector3 b, Color colourB,
        Vector3 c, Color colourC,
        Vector3 d, Color colourD,
        Vector3 expectedNormal)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-4f
            || Vector3.Cross(c - a, d - a).LengthSquared() <= 1e-4f)
            return;
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) < 0f)
        {
            AddGradientTriangleFacing(
                mesh, a, colourA, d, colourD, c, colourC, expectedNormal);
            AddGradientTriangleFacing(
                mesh, a, colourA, c, colourC, b, colourB, expectedNormal);
            return;
        }
        AddGradientTriangleFacing(
            mesh, a, colourA, b, colourB, c, colourC, expectedNormal);
        AddGradientTriangleFacing(
            mesh, a, colourA, c, colourC, d, colourD, expectedNormal);
    }

    private static void AddGradientTriangleFacing(
        StationModuleMesh mesh,
        Vector3 a, Color colourA,
        Vector3 b, Color colourB,
        Vector3 c, Color colourC,
        Vector3 expectedNormal)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-4f)
            return;
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) < 0f)
            mesh.AddTriangleGradient(a, colourA, c, colourC, b, colourB);
        else
            mesh.AddTriangleGradient(a, colourA, b, colourB, c, colourC);
    }

    private static Vector3 FaceWorldCenter(BolonVesselPlan vessel, int faceIndex)
    {
        BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(faceIndex);
        return vessel.Position + Vector3.Transform(
            face.LocalCenter * vessel.Radius, vessel.Orientation);
    }

    private static Color ConnectorColour(MegastationArchetype archetype)
        => archetype == MegastationArchetype.RedBolon
            ? new Color(132, 52, 28)
            : new Color(142, 96, 34);

    private static float Lerp(float minimum, float maximum, double amount)
        => minimum + (maximum - minimum) * (float)amount;
}
