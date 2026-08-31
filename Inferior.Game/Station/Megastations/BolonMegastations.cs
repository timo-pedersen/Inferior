using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum BolonVesselRelationshipMode
{
    ShortConnector,
    DirectFaceJoin,
}

public enum BolonVesselScaleClass
{
    Anchor,
    Standard,
    Secondary,
}

public sealed record BolonAttachmentFace(
    int Index,
    int SideCount,
    Vector3 LocalCenter,
    Vector3 LocalNormal,
    float LocalInscribedRadius);

public sealed record BolonVesselPlan(
    string Identity,
    int Index,
    Vector3 Position,
    Quaternion Orientation,
    float Radius,
    BolonVesselScaleClass ScaleClass,
    int ParentIndex);

public sealed record BolonVesselRelationship(
    string Identity,
    int A,
    int FaceA,
    int B,
    int FaceB,
    BolonVesselRelationshipMode Mode,
    float ConnectorRadius,
    float ConnectorLength);

public sealed record BolonMegastationPlan(
    string StationIdentity,
    MegastationArchetype Archetype,
    IReadOnlyList<BolonVesselPlan> Vessels,
    IReadOnlyList<BolonVesselRelationship> Relationships,
    Vector3 Minimum,
    Vector3 Maximum,
    string StructuralSignature);

public sealed record BolonMegastationDiagnostics(
    string StationIdentity,
    MegastationArchetype Archetype,
    int VesselCount,
    int AnchorVesselCount,
    int StandardVesselCount,
    int SecondaryVesselCount,
    int RelationshipCount,
    int ConnectorRelationshipCount,
    int DirectJoinRelationshipCount,
    int MaximumGraphDegree,
    float MinimumVesselRadius,
    float MaximumVesselRadius,
    Vector3 OverallDimensions,
    int VertexCount,
    int TriangleCount,
    long MeshBytes,
    int SurfaceTriangleCount,
    int ApertureStructureTriangleCount,
    int SurfaceHistoryRegionCount,
    int MatureRegionCount,
    int PolishedRegionCount,
    int BrushedRegionCount,
    int ErodedRegionCount,
    int ApertureGroupCount,
    int ApertureCount,
    int FourNineFourGroupCount,
    int CompactGroupCount,
    int SparseChainGroupCount,
    int BandGroupCount,
    int CornerFanGroupCount,
    int EdgeRunGroupCount,
    int SparseFieldGroupCount,
    int VentGroupCount,
    int OneXVentCount,
    int TwoXVentCount,
    int ThreeXVentCount,
    int RubyGroupCount,
    int VioletGroupCount,
    int RareOtherGroupCount,
    int BlankEligibleHexFaceCount,
    int UnlitApertureCount,
    int DimApertureCount,
    int LuminousApertureCount,
    int BrightApertureCount,
    int ApertureGlassVertexCount,
    int ApertureGlassTriangleCount,
    long ApertureGlassBytes,
    int VentGrilleTriangleCount,
    double PlanningMilliseconds,
    double MeshBuildMilliseconds,
    string StructuralSignature,
    string SurfaceHistorySignature,
    string ApertureSignature,
    string ApertureVisualSignature,
    string ApertureVocabularySignature);

public sealed record BolonMegastationCpuResult(
    BolonMegastationPlan Plan,
    BolonSurfacePresentationPlan SurfacePlan,
    StationModuleMesh Mesh,
    StationModuleMesh ApertureGlassMesh,
    BolonMegastationDiagnostics Diagnostics);

/// <summary>
/// B1 plans a low-degree molecular graph whose edges own actual C60 attachment
/// faces. Semantic vessels and relationships remain available independently of
/// the single combined render/shadow mesh.
/// </summary>
public static class BolonMegastationGenerator
{
    public const double ConservativeEnvelopeRadiusMeters = 3_500.0;
    private const int AlgorithmVersion = 2;
    private const float MaximumCentreRadius = 2_350f;
    private const float UnrelatedVesselClearance = 24f;
    private static readonly Vector3[] IcosahedronVertices = BuildIcosahedronVertices();
    private static readonly int[][] IcosahedronFaces =
    [
        [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11],
        [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
        [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9],
        [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
    ];
    private static readonly C60FaceGeometry[] C60Faces = BuildC60Faces();

    private enum GrowthStyle
    {
        Branched,
        HeavyCoreWithExtension,
        SparseBridge,
    }

    private sealed record C60FaceGeometry(
        BolonAttachmentFace Face,
        Vector3 ReferenceTangent,
        Vector3[] Vertices);

    private sealed record PlacementCandidate(
        int Parent,
        int ParentFace,
        int ChildFace,
        BolonVesselRelationshipMode Mode,
        Vector3 Position,
        Quaternion Orientation,
        float Radius,
        float ConnectorRadius,
        float ConnectorLength);

    public static IReadOnlyList<BolonAttachmentFace> AttachmentFaces { get; }
        = C60Faces.Select(face => face.Face).ToArray();

    public static BolonAttachmentFace GetAttachmentFace(int index)
        => C60Faces[index].Face;

    public static IReadOnlyList<Vector3> GetAttachmentFaceVertices(int index)
        => C60Faces[index].Vertices.ToArray();

    public static BolonMegastationCpuResult GenerateCpu(
        string stationIdentity,
        MegastationArchetype archetype,
        CancellationToken cancellationToken = default)
    {
        if (archetype == MegastationArchetype.Standard)
            throw new ArgumentException("Bolon generation requires a Bolon archetype.", nameof(archetype));

        var planning = System.Diagnostics.Stopwatch.StartNew();
        BolonMegastationPlan plan = Plan(stationIdentity, archetype, cancellationToken);
        BolonSurfacePresentationPlan surfacePlan = BolonSurfacePresentationPlanner.Plan(
            plan, cancellationToken);
        planning.Stop();
        var meshBuild = System.Diagnostics.Stopwatch.StartNew();
        BolonSurfaceMeshBuildResult meshes = BolonSurfaceMeshBuilder.Build(
            plan, surfacePlan, cancellationToken);
        meshBuild.Stop();
        StationModuleMesh mesh = meshes.HullMesh;
        StationModuleMesh glass = meshes.ApertureGlassMesh;
        int[] degrees = GraphDegrees(plan);
        BolonSurfaceHistoryRegion[] regions = surfacePlan.VesselHistories
            .SelectMany(history => history.Regions)
            .ToArray();
        BolonApertureInstance[] penetrations = surfacePlan.ApertureGroups
            .SelectMany(group => group.Apertures)
            .ToArray();
        BolonApertureInstance[] apertures = penetrations
            .Where(aperture => aperture.PenetrationType
                == BolonShellPenetrationType.OpticalAperture)
            .ToArray();
        var diagnostics = new BolonMegastationDiagnostics(
            stationIdentity,
            archetype,
            plan.Vessels.Count,
            plan.Vessels.Count(v => v.ScaleClass == BolonVesselScaleClass.Anchor),
            plan.Vessels.Count(v => v.ScaleClass == BolonVesselScaleClass.Standard),
            plan.Vessels.Count(v => v.ScaleClass == BolonVesselScaleClass.Secondary),
            plan.Relationships.Count,
            plan.Relationships.Count(r => r.Mode == BolonVesselRelationshipMode.ShortConnector),
            plan.Relationships.Count(r => r.Mode == BolonVesselRelationshipMode.DirectFaceJoin),
            degrees.Max(),
            plan.Vessels.Min(v => v.Radius),
            plan.Vessels.Max(v => v.Radius),
            plan.Maximum - plan.Minimum,
            mesh.VertexCount,
            mesh.IndexCount / 3,
            (long)mesh.VertexCount * VertexPositionNormalColorTexture.VertexDeclaration.VertexStride
                + (long)mesh.IndexCount * sizeof(int),
            meshes.SurfaceTriangleCount,
            meshes.ApertureCollarTriangleCount,
            regions.Length,
            regions.Count(region => region.Finish == BolonSurfaceFinish.Mature),
            regions.Count(region => region.Finish == BolonSurfaceFinish.Polished),
            regions.Count(region => region.Finish == BolonSurfaceFinish.Brushed),
            regions.Count(region => region.Finish == BolonSurfaceFinish.Eroded),
            surfacePlan.ApertureGroups.Count,
            apertures.Length,
            surfacePlan.ApertureGroups.Count(group => group.Pattern == BolonAperturePattern.FourNineFour),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.CompactCluster),
            surfacePlan.ApertureGroups.Count(group => group.Pattern == BolonAperturePattern.SparseChain),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.Band),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.CornerFan),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.EdgeRun),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.SparseField),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily == BolonAperturePatternFamily.Vent),
            penetrations.Count(aperture => aperture.VentScale == BolonVentScale.One),
            penetrations.Count(aperture => aperture.VentScale == BolonVentScale.Two),
            penetrations.Count(aperture => aperture.VentScale == BolonVentScale.Three),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily != BolonAperturePatternFamily.Vent
                && group.PaletteFamily == BolonAperturePaletteFamily.Ruby),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily != BolonAperturePatternFamily.Vent
                && group.PaletteFamily == BolonAperturePaletteFamily.Violet),
            surfacePlan.ApertureGroups.Count(group => group.PatternFamily != BolonAperturePatternFamily.Vent
                && group.PaletteFamily == BolonAperturePaletteFamily.SpectralGreen),
            surfacePlan.BlankEligibleHexFaceCount,
            apertures.Count(aperture => aperture.VisualState.Illumination
                == BolonApertureIlluminationState.Unlit),
            apertures.Count(aperture => aperture.VisualState.Illumination
                == BolonApertureIlluminationState.Dim),
            apertures.Count(aperture => aperture.VisualState.Illumination
                == BolonApertureIlluminationState.Luminous),
            apertures.Count(aperture => aperture.VisualState.Illumination
                == BolonApertureIlluminationState.Bright),
            glass.VertexCount,
            glass.IndexCount / 3,
            (long)glass.VertexCount * VertexPositionNormalColorTexture.VertexDeclaration.VertexStride
                + (long)glass.IndexCount * sizeof(int),
            meshes.VentGrilleTriangleCount,
            planning.Elapsed.TotalMilliseconds,
            meshBuild.Elapsed.TotalMilliseconds,
            plan.StructuralSignature,
            surfacePlan.SurfaceHistorySignature,
            surfacePlan.ApertureSignature,
            surfacePlan.ApertureVisualSignature,
            surfacePlan.ApertureVocabularySignature);
        return new(plan, surfacePlan, mesh, glass, diagnostics);
    }

    public static PlacedModule CreatePlacedModule(BolonMegastationCpuResult cpu)
    {
        Vector3 dimensions = cpu.Plan.Maximum - cpu.Plan.Minimum;
        var definition = new StationModuleDefinition
        {
            Id = cpu.Plan.Archetype == MegastationArchetype.RedBolon
                ? "megastation-bolon-red-b2"
                : "megastation-bolon-b2",
            Category = "megastation-bolon",
            BoundingBox = dimensions,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = MegastationSeed.Root(cpu.Plan.StationIdentity, AlgorithmVersion),
            ChamferDepth = 0f,
            AabbMin = cpu.Plan.Minimum - new Vector3(5f),
            AabbMax = cpu.Plan.Maximum + new Vector3(5f),
            HullMesh = cpu.Mesh,
            HullShadowMesh = cpu.Mesh,
            GlassMesh = cpu.ApertureGlassMesh.VertexCount > 0
                ? cpu.ApertureGlassMesh
                : null,
            HullMaterialRanges = cpu.Mesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
    }

    public static BolonMegastationPlan Plan(
        string stationIdentity,
        MegastationArchetype archetype,
        CancellationToken cancellationToken = default)
    {
        if (archetype == MegastationArchetype.Standard)
            throw new ArgumentException("Bolon planning requires a Bolon archetype.", nameof(archetype));

        int root = MegastationSeed.Root(stationIdentity, AlgorithmVersion);
        int macroSeed = MegastationSeed.Derive(root, "bolon-macro-graph:v2");
        int hierarchySeed = MegastationSeed.Derive(root, "bolon-vessel-hierarchy:v2");
        int growthSeed = MegastationSeed.Derive(root, "bolon-graph-growth:v2");
        int faceSeed = MegastationSeed.Derive(root, "bolon-attachment-faces:v2");
        int connectionSeed = MegastationSeed.Derive(root, "bolon-connection-modes:v2");
        int orientationSeed = MegastationSeed.Derive(root, "bolon-vessel-orientation:v2");

        var countRng = new Random(MegastationSeed.Derive(macroSeed, "vessel-count"));
        int vesselCount = archetype == MegastationArchetype.RedBolon
            ? countRng.Next(6, 11)
            : countRng.Next(8, 13);
        GrowthStyle growthStyle = (GrowthStyle)new Random(
            MegastationSeed.Derive(macroSeed, "growth-style")).Next(0, 3);
        BolonVesselScaleClass[] scaleClasses = PlanScaleClasses(
            vesselCount, hierarchySeed);
        HashSet<int> directJoinChildren = PlanDirectJoinChildren(
            scaleClasses, connectionSeed);
        float[] plannedRadii = PlanRadii(scaleClasses, hierarchySeed);

        var vessels = new List<BolonVesselPlan>(vesselCount);
        var relationships = new List<BolonVesselRelationship>(vesselCount - 1);
        var degrees = new List<int>(vesselCount);
        var usedFaces = new List<HashSet<int>>(vesselCount);

        var rootOrientationRng = new Random(MegastationSeed.Derive(
            orientationSeed, "vessel:0"));
        vessels.Add(new(
            "vessel:0",
            0,
            Vector3.Zero,
            RandomOrientation(rootOrientationRng),
            plannedRadii[0],
            scaleClasses[0],
            -1));
        degrees.Add(0);
        usedFaces.Add([]);

        for (int index = 1; index < vesselCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool wantsDirectJoin = directJoinChildren.Contains(index);
            PlacementCandidate? placement = null;
            for (int attempt = 0; attempt < 192 && placement == null; attempt++)
            {
                var attemptRng = new Random(MegastationSeed.Derive(
                    growthSeed, $"vessel:{index}:attempt:{attempt}"));
                int parent = SelectParent(
                    vessels,
                    degrees,
                    scaleClasses[index],
                    wantsDirectJoin,
                    growthStyle,
                    index,
                    attemptRng);
                if (parent < 0)
                    continue;
                Vector3 desiredDirection = DesiredGrowthDirection(
                    vessels, parent, growthStyle, index, attemptRng);
                int parentFace = SelectParentFace(
                    vessels[parent], usedFaces[parent], desiredDirection, attemptRng);
                if (parentFace < 0)
                    continue;
                BolonVesselRelationshipMode mode = wantsDirectJoin
                    ? BolonVesselRelationshipMode.DirectFaceJoin
                    : BolonVesselRelationshipMode.ShortConnector;
                int childFace = SelectChildFace(
                    parentFace,
                    mode,
                    new Random(MegastationSeed.Derive(
                        faceSeed, $"vessel:{index}:attempt:{attempt}")));
                float radius = mode == BolonVesselRelationshipMode.DirectFaceJoin
                    ? vessels[parent].Radius
                    : plannedRadii[index];
                placement = TryPlace(
                    vessels,
                    parent,
                    parentFace,
                    childFace,
                    mode,
                    radius,
                    index,
                    orientationSeed,
                    attemptRng,
                    enforceEnvelope: true);
            }

            placement ??= FindFallbackPlacement(
                vessels,
                degrees,
                usedFaces,
                scaleClasses[index],
                plannedRadii[index],
                index,
                orientationSeed);
            if (placement == null)
                throw new InvalidOperationException(
                    $"Unable to place Bolon vessel {index} without an unrelated hull intersection.");

            vessels.Add(new(
                $"vessel:{index}",
                index,
                placement.Position,
                placement.Orientation,
                placement.Radius,
                scaleClasses[index],
                placement.Parent));
            degrees.Add(1);
            usedFaces.Add([placement.ChildFace]);
            degrees[placement.Parent]++;
            usedFaces[placement.Parent].Add(placement.ParentFace);
            relationships.Add(new(
                $"relationship:{placement.Parent}:{placement.ParentFace}:{index}:{placement.ChildFace}",
                placement.Parent,
                placement.ParentFace,
                index,
                placement.ChildFace,
                placement.Mode,
                placement.ConnectorRadius,
                placement.ConnectorLength));
        }

        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        foreach (BolonVesselPlan vessel in vessels)
        {
            Vector3 extent = new(vessel.Radius);
            minimum = Vector3.Min(minimum, vessel.Position - extent);
            maximum = Vector3.Max(maximum, vessel.Position + extent);
        }
        string signature = Signature(stationIdentity, archetype, vessels, relationships);
        return new(stationIdentity, archetype, vessels, relationships, minimum, maximum, signature);
    }

    private static BolonVesselScaleClass[] PlanScaleClasses(int count, int hierarchySeed)
    {
        var result = Enumerable.Repeat(BolonVesselScaleClass.Standard, count).ToArray();
        result[0] = BolonVesselScaleClass.Anchor;
        var rng = new Random(MegastationSeed.Derive(hierarchySeed, "class-counts"));
        int anchorCount = 1 + rng.Next(count >= 10 ? 3 : 2);
        int secondaryCount = 1 + rng.Next(count >= 9 ? 3 : 2);
        // Keep the first two post-root vessels standard so B1 always has an
        // early same-scale pair available for a clean direct face join.
        int[] candidates = Enumerable.Range(3, Math.Max(0, count - 4))
            .OrderBy(index => MegastationSeed.Derive(hierarchySeed, $"class-order:{index}"))
            .ToArray();
        foreach (int index in candidates.Take(anchorCount - 1))
            result[index] = BolonVesselScaleClass.Anchor;
        foreach (int index in candidates.Skip(anchorCount - 1).Take(secondaryCount))
            result[index] = BolonVesselScaleClass.Secondary;
        return result;
    }

    private static float[] PlanRadii(
        IReadOnlyList<BolonVesselScaleClass> scaleClasses,
        int hierarchySeed)
    {
        var stationRng = new Random(MegastationSeed.Derive(hierarchySeed, "standard-radius"));
        float standardRadius = Lerp(245f, 285f, stationRng.NextDouble());
        var result = new float[scaleClasses.Count];
        for (int index = 0; index < result.Length; index++)
        {
            var rng = new Random(MegastationSeed.Derive(hierarchySeed, $"radius:{index}"));
            result[index] = scaleClasses[index] switch
            {
                BolonVesselScaleClass.Anchor => standardRadius
                    * Lerp(1.25f, 1.48f, rng.NextDouble()),
                BolonVesselScaleClass.Secondary => standardRadius
                    * Lerp(.72f, .86f, rng.NextDouble()),
                _ => standardRadius * Lerp(.94f, 1.07f, rng.NextDouble()),
            };
        }
        return result;
    }

    private static HashSet<int> PlanDirectJoinChildren(
        IReadOnlyList<BolonVesselScaleClass> classes,
        int connectionSeed)
    {
        int target = classes.Count >= 10 ? 2 : 1;
        int[] candidates = classes
            .Select((scaleClass, index) => (scaleClass, index))
            .Where(item => item.index >= 1
                && Enumerable.Range(0, item.index).Any(
                    previous => classes[previous] == item.scaleClass))
            .OrderBy(item => item.index)
            .Select(item => item.index)
            .ToArray();
        var result = new HashSet<int>();
        if (candidates.Length > 0)
            result.Add(candidates[0]);
        if (target > 1 && candidates.Length > 1)
        {
            int second = candidates.Skip(1).Take(2)
                .OrderBy(index => MegastationSeed.Derive(
                    connectionSeed, $"second-direct:{index}"))
                .First();
            result.Add(second);
        }
        return result;
    }

    private static int SelectParent(
        IReadOnlyList<BolonVesselPlan> vessels,
        IReadOnlyList<int> degrees,
        BolonVesselScaleClass childClass,
        bool directJoin,
        GrowthStyle style,
        int childIndex,
        Random rng)
    {
        int[] eligible = Enumerable.Range(0, vessels.Count)
            .Where(index => degrees[index] < MaximumDegree(vessels[index].ScaleClass))
            .Where(index => !directJoin || vessels[index].ScaleClass == childClass)
            .ToArray();
        if (eligible.Length == 0)
            return -1;

        if (style == GrowthStyle.HeavyCoreWithExtension && childIndex < vessels.Count / 2 + 2)
        {
            int[] anchors = eligible.Where(index =>
                vessels[index].ScaleClass == BolonVesselScaleClass.Anchor).ToArray();
            if (anchors.Length > 0 && rng.NextDouble() < .68)
                return anchors[rng.Next(anchors.Length)];
        }
        if ((style == GrowthStyle.SparseBridge || childIndex >= 5) && rng.NextDouble() < .72)
        {
            int recentStart = Math.Max(0, eligible.Length - 4);
            return eligible[rng.Next(recentStart, eligible.Length)];
        }
        return eligible[rng.Next(eligible.Length)];
    }

    private static int MaximumDegree(BolonVesselScaleClass scaleClass)
        => scaleClass == BolonVesselScaleClass.Anchor ? 4
            : scaleClass == BolonVesselScaleClass.Standard ? 3
            : 2;

    private static Vector3 DesiredGrowthDirection(
        IReadOnlyList<BolonVesselPlan> vessels,
        int parent,
        GrowthStyle style,
        int childIndex,
        Random rng)
    {
        BolonVesselPlan vessel = vessels[parent];
        Vector3 random = RandomUnitVector(rng);
        Vector3 outward = vessel.Position.LengthSquared() > 1f
            ? Vector3.Normalize(vessel.Position)
            : RandomUnitVector(rng);
        Vector3 incoming = outward;
        if (vessel.ParentIndex >= 0)
            incoming = Vector3.Normalize(vessel.Position - vessels[vessel.ParentIndex].Position);

        Vector3 result = style switch
        {
            GrowthStyle.SparseBridge => incoming * .82f + outward * .28f + random * .35f,
            GrowthStyle.HeavyCoreWithExtension when childIndex >= 5
                => incoming * .72f + outward * .42f + random * .35f,
            GrowthStyle.HeavyCoreWithExtension => outward * .48f + random * .75f,
            _ => incoming * .34f + outward * .46f + random * .72f,
        };
        return result.LengthSquared() > .0001f ? Vector3.Normalize(result) : random;
    }

    private static int SelectParentFace(
        BolonVesselPlan parent,
        IReadOnlySet<int> usedFaces,
        Vector3 desiredDirection,
        Random rng)
    {
        var ranked = C60Faces
            .Where(face => !usedFaces.Contains(face.Face.Index))
            .Select(face => new
            {
                face.Face.Index,
                Score = Vector3.Dot(
                    Vector3.Transform(face.Face.LocalNormal, parent.Orientation),
                    desiredDirection) + (float)rng.NextDouble() * .035f,
            })
            .OrderByDescending(item => item.Score)
            .Take(4)
            .ToArray();
        if (ranked.Length == 0)
            return -1;
        return ranked[rng.Next(Math.Min(2, ranked.Length))].Index;
    }

    private static int SelectChildFace(
        int parentFace,
        BolonVesselRelationshipMode mode,
        Random rng)
    {
        int requiredSides = C60Faces[parentFace].Face.SideCount;
        C60FaceGeometry[] candidates = mode == BolonVesselRelationshipMode.DirectFaceJoin
            ? C60Faces.Where(face => face.Face.SideCount == requiredSides).ToArray()
            : (rng.NextDouble() < .72
                ? C60Faces.Where(face => face.Face.SideCount == 6).ToArray()
                : C60Faces);
        return candidates[rng.Next(candidates.Length)].Face.Index;
    }

    private static PlacementCandidate? TryPlace(
        IReadOnlyList<BolonVesselPlan> vessels,
        int parentIndex,
        int parentFaceIndex,
        int childFaceIndex,
        BolonVesselRelationshipMode mode,
        float radius,
        int childIndex,
        int orientationSeed,
        Random rng,
        bool enforceEnvelope)
    {
        BolonVesselPlan parent = vessels[parentIndex];
        C60FaceGeometry parentFace = C60Faces[parentFaceIndex];
        C60FaceGeometry childFace = C60Faces[childFaceIndex];
        Vector3 axis = FaceWorldNormal(parent, parentFaceIndex);
        Vector3 parentCentre = FaceWorldCenter(parent, parentFaceIndex);
        Vector3 desiredTangent;
        if (mode == BolonVesselRelationshipMode.DirectFaceJoin)
        {
            desiredTangent = Vector3.Normalize(Vector3.Transform(
                parentFace.ReferenceTangent, parent.Orientation));
        }
        else
        {
            var orientationRng = new Random(MegastationSeed.Derive(
                orientationSeed, $"vessel:{childIndex}:roll:{parentIndex}:{parentFaceIndex}"));
            desiredTangent = PerpendicularWithRoll(axis, orientationRng);
        }
        Quaternion orientation = AlignFace(
            childFace,
            -axis,
            desiredTangent);
        Vector3 childFaceOffset = Vector3.Transform(
            childFace.Face.LocalCenter * radius, orientation);
        float gap = mode == BolonVesselRelationshipMode.ShortConnector
            ? Lerp(48f, 105f, rng.NextDouble())
            : 0f;
        Vector3 position = parentCentre + axis * gap - childFaceOffset;
        if (enforceEnvelope && position.Length() + radius > MaximumCentreRadius)
            return null;
        if (!HasUnrelatedClearance(vessels, parentIndex, position, radius))
            return null;

        float connectorRadius = 0f;
        if (mode == BolonVesselRelationshipMode.ShortConnector)
        {
            float parentLimit = parentFace.Face.LocalInscribedRadius * parent.Radius;
            float childLimit = childFace.Face.LocalInscribedRadius * radius;
            connectorRadius = MathF.Min(parentLimit, childLimit)
                * Lerp(.62f, .76f, rng.NextDouble());
        }
        return new(
            parentIndex,
            parentFaceIndex,
            childFaceIndex,
            mode,
            position,
            orientation,
            radius,
            connectorRadius,
            gap);
    }

    private static PlacementCandidate? FindFallbackPlacement(
        IReadOnlyList<BolonVesselPlan> vessels,
        IReadOnlyList<int> degrees,
        IReadOnlyList<HashSet<int>> usedFaces,
        BolonVesselScaleClass childClass,
        float radius,
        int childIndex,
        int orientationSeed)
    {
        foreach (int parent in Enumerable.Range(0, vessels.Count)
                     .Where(index => degrees[index] < MaximumDegree(vessels[index].ScaleClass))
                     .OrderByDescending(index => vessels[index].Position.LengthSquared()))
        {
            foreach (C60FaceGeometry face in C60Faces
                         .Where(face => !usedFaces[parent].Contains(face.Face.Index))
                         .OrderByDescending(face => Vector3.Dot(
                             FaceWorldNormal(vessels[parent], face.Face.Index),
                             vessels[parent].Position.LengthSquared() > 1f
                                 ? Vector3.Normalize(vessels[parent].Position)
                                 : Vector3.UnitX)))
            {
                var rng = new Random(MegastationSeed.Derive(
                    orientationSeed, $"fallback:{childIndex}:{parent}:{face.Face.Index}"));
                int childFace = SelectChildFace(
                    face.Face.Index,
                    BolonVesselRelationshipMode.ShortConnector,
                    rng);
                PlacementCandidate? candidate = TryPlace(
                    vessels,
                    parent,
                    face.Face.Index,
                    childFace,
                    BolonVesselRelationshipMode.ShortConnector,
                    radius,
                    childIndex,
                    orientationSeed,
                    rng,
                    enforceEnvelope: false);
                if (candidate != null)
                    return candidate;
            }
        }
        return null;
    }

    private static bool HasUnrelatedClearance(
        IReadOnlyList<BolonVesselPlan> vessels,
        int parent,
        Vector3 position,
        float radius)
    {
        for (int i = 0; i < vessels.Count; i++)
        {
            if (i == parent)
                continue;
            float minimum = radius + vessels[i].Radius + UnrelatedVesselClearance;
            if (Vector3.DistanceSquared(position, vessels[i].Position) < minimum * minimum)
                return false;
        }
        return true;
    }

    private static Quaternion AlignFace(
        C60FaceGeometry face,
        Vector3 desiredNormal,
        Vector3 desiredTangent)
    {
        Vector3 localNormal = face.Face.LocalNormal;
        Vector3 localTangent = face.ReferenceTangent;
        Vector3 localBitangent = Vector3.Normalize(Vector3.Cross(localNormal, localTangent));
        desiredNormal = Vector3.Normalize(desiredNormal);
        desiredTangent = Vector3.Normalize(
            desiredTangent - desiredNormal * Vector3.Dot(desiredTangent, desiredNormal));
        Vector3 desiredBitangent = Vector3.Normalize(Vector3.Cross(
            desiredNormal, desiredTangent));
        Matrix localFrame = Basis(localTangent, localBitangent, localNormal);
        Matrix desiredFrame = Basis(desiredTangent, desiredBitangent, desiredNormal);
        Matrix mapping = Matrix.Invert(localFrame) * desiredFrame;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(mapping));
    }

    private static Matrix Basis(Vector3 x, Vector3 y, Vector3 z)
        => new(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);

    private static Vector3 PerpendicularWithRoll(Vector3 axis, Random rng)
    {
        Vector3 reference = MathF.Abs(axis.Y) < .9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(reference, axis));
        return Vector3.Transform(
            tangent,
            Quaternion.CreateFromAxisAngle(axis, Lerp(0f, MathF.Tau, rng.NextDouble())));
    }

    private static Vector3 FaceWorldCenter(BolonVesselPlan vessel, int faceIndex)
        => vessel.Position + Vector3.Transform(
            C60Faces[faceIndex].Face.LocalCenter * vessel.Radius,
            vessel.Orientation);

    private static Vector3 FaceWorldNormal(BolonVesselPlan vessel, int faceIndex)
        => Vector3.Normalize(Vector3.Transform(
            C60Faces[faceIndex].Face.LocalNormal,
            vessel.Orientation));

    private static int[] GraphDegrees(BolonMegastationPlan plan)
    {
        var degrees = new int[plan.Vessels.Count];
        foreach (BolonVesselRelationship relationship in plan.Relationships)
        {
            degrees[relationship.A]++;
            degrees[relationship.B]++;
        }
        return degrees;
    }

    private static Quaternion RandomOrientation(Random rng)
        => Quaternion.CreateFromAxisAngle(RandomUnitVector(rng),
            Lerp(0f, MathF.Tau, rng.NextDouble()));

    private static Vector3 RandomUnitVector(Random rng)
    {
        float z = Lerp(-1f, 1f, rng.NextDouble());
        float angle = Lerp(0f, MathF.Tau, rng.NextDouble());
        float radial = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new(radial * MathF.Cos(angle), z, radial * MathF.Sin(angle));
    }

    private static C60FaceGeometry[] BuildC60Faces()
    {
        var result = new List<C60FaceGeometry>(32);
        Vector3 Near(int from, int to)
            => (IcosahedronVertices[from] * 2f + IcosahedronVertices[to]) / 3f;

        foreach (int[] face in IcosahedronFaces)
        {
            int a = face[0], b = face[1], c = face[2];
            AddFace(result,
            [
                Near(a, b), Near(b, a), Near(b, c),
                Near(c, b), Near(c, a), Near(a, c),
            ]);
        }
        for (int vertex = 0; vertex < IcosahedronVertices.Length; vertex++)
        {
            int[] neighbours = IcosahedronFaces
                .Where(face => face.Contains(vertex))
                .SelectMany(face => face)
                .Where(index => index != vertex)
                .Distinct()
                .ToArray();
            Vector3 normal = IcosahedronVertices[vertex];
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(
                MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX,
                normal));
            Vector3 bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));
            AddFace(result, neighbours
                .Select(neighbour => Near(vertex, neighbour))
                .OrderBy(point => MathF.Atan2(
                    Vector3.Dot(point, bitangent),
                    Vector3.Dot(point, tangent)))
                .ToArray());
        }
        return result.ToArray();
    }

    private static void AddFace(List<C60FaceGeometry> result, Vector3[] polygon)
    {
        Vector3 center = polygon.Aggregate(Vector3.Zero, (sum, point) => sum + point)
            / polygon.Length;
        Vector3 normal = Vector3.Normalize(Vector3.Cross(
            polygon[1] - polygon[0], polygon[2] - polygon[0]));
        if (Vector3.Dot(normal, center) < 0f)
        {
            Array.Reverse(polygon);
            normal = -normal;
        }
        Vector3 tangent = Vector3.Normalize(polygon[0] - center);
        float inscribedRadius = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Length];
            float distance = MathF.Abs(Vector3.Dot(
                Vector3.Cross(b - a, center - a), normal)) / (b - a).Length();
            inscribedRadius = MathF.Min(inscribedRadius, distance);
        }
        var face = new BolonAttachmentFace(
            result.Count,
            polygon.Length,
            center,
            normal,
            inscribedRadius);
        result.Add(new(face, tangent, polygon));
    }

    private static Vector3[] BuildIcosahedronVertices()
    {
        float phi = (1f + MathF.Sqrt(5f)) * .5f;
        Vector3[] vertices =
        [
            new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
            new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
            new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
        ];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = Vector3.Normalize(vertices[i]);
        float truncatedRadius = ((vertices[0] * 2f + vertices[11]) / 3f).Length();
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] /= truncatedRadius;
        return vertices;
    }

    private static string Signature(
        string stationIdentity,
        MegastationArchetype archetype,
        IReadOnlyList<BolonVesselPlan> vessels,
        IReadOnlyList<BolonVesselRelationship> relationships)
    {
        var text = new StringBuilder()
            .Append("bolon:v").Append(AlgorithmVersion).Append('|')
            .Append(stationIdentity).Append('|').Append(archetype);
        foreach (BolonVesselPlan vessel in vessels)
            text.Append('|').Append(vessel.Index).Append(':')
                .Append(vessel.ScaleClass).Append(':')
                .Append(vessel.ParentIndex).Append(':')
                .Append(vessel.Position.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vessel.Position.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vessel.Position.Z.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(vessel.Orientation.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vessel.Orientation.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vessel.Orientation.Z.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vessel.Orientation.W.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(vessel.Radius.ToString("R", CultureInfo.InvariantCulture));
        foreach (BolonVesselRelationship relationship in relationships)
            text.Append('|').Append(relationship.A).Append('.').Append(relationship.FaceA)
                .Append('>').Append(relationship.B).Append('.').Append(relationship.FaceB)
                .Append(':').Append(relationship.Mode).Append(':')
                .Append(relationship.ConnectorRadius.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(relationship.ConnectorLength.ToString(
                    "R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static float Lerp(float minimum, float maximum, double amount)
        => minimum + (maximum - minimum) * (float)amount;
}
