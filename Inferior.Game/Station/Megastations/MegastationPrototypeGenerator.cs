using System.Diagnostics;
using System.Reflection;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationPrototypeDiagnostics(
    string StationPersistenceId,
    string BuildIdentifier,
    int GeneratorVersion,
    int SeedCompatibilityVersion,
    int TopologyRegularisationAlgorithmVersion,
    int BoundaryTopologyAlgorithmVersion,
    int StructuralChamferAlgorithmVersion,
    int PositiveYUrbanSeedVersion,
    int FaceUrbanAlgorithmVersion,
    int EdgeAlgorithmVersion,
    int CornerAlgorithmVersion,
    int RootSeed,
    int XSliceCount,
    int YSliceCount,
    int ZSliceCount,
    int GridCellCount,
    int StructuralOccupiedCellCount,
    int UrbanOccupiedCellCount,
    int RegularisedOccupiedCellCount,
    int TopologyRepairAddedCellCount,
    int TopologyRepairRemovedCellCount,
    int UrbanizedFaceCount,
    int FaceRegionOccupiedCellCount,
    int EdgeRegionOccupiedCellCount,
    int CornerRegionOccupiedCellCount,
    int DistrictCount,
    int MaximumUrbanDepth,
    IReadOnlyList<string> PerFaceSummary,
    IReadOnlyList<string> PerEdgeSummary,
    IReadOnlyList<string> PerCornerSummary,
    int ConnectedComponentsBeforeValidation,
    int RemovedDisconnectedCells,
    bool HasSealedCavity,
    int EdgeCriticalConfigurationsBeforeRegularisation,
    int EdgeCriticalConfigurationsAfterRegularisation,
    int VertexCriticalConfigurationsBeforeRegularisation,
    int VertexCriticalConfigurationsAfterRegularisation,
    int RegularisedConnectedComponents,
    bool RegularisedHasSealedCavity,
    IReadOnlyList<string> TopologyDefectOwnerSummary,
    int ExposedQuadCount,
    int TriangleCount,
    int VertexCount,
    int MeshPageCount,
    int BoundaryFaceCount,
    int CanonicalEdgeSegmentCount,
    int FlatContinuationEdgeCount,
    int ConvexExteriorEdgeCount,
    int ConcaveExteriorEdgeCount,
    int InvalidDiagonalEdgeCount,
    int SimpleConvexVertexCount,
    int StraightConvexContinuationVertexCount,
    int SimpleConcaveVertexCount,
    int ComplexVertexCount,
    int NonManifoldVertexCount,
    int EligibleChamferSegmentCount,
    int SuppressedConvexSegmentCount,
    int ChamferRunCount,
    int SuppressedChamferRunCount,
    int BevelQuadCount,
    int CornerCapCount,
    ChamferSemanticValidationReport ChamferSemanticValidation,
    IReadOnlyList<ChamferRunDiagnostics> ChamferRuns,
    MegastationMeshPath MeshPath,
    string BoundaryTopologySignature,
    BoundaryMeshValidationReport SharpBoundaryValidation,
    BoundaryMeshValidationReport ChamferedBoundaryValidation,
    long BoundaryTopologyBuildMilliseconds,
    long BoundaryMeshBuildMilliseconds,
    long GenerationMilliseconds);

public sealed record MegastationPrototypeResult(
    List<PlacedModule> Modules,
    IReadOnlyList<Texture2D> PanelTextures,
    MegastationPrototypeDiagnostics Diagnostics);

public sealed record MegastationPrototypeCpuResult(
    SliceGrid Grid,
    StructuralOccupancy Occupancy,
    StructuralOccupancy RegularisedOccupancy,
    TopologyRegularisationReport TopologyRegularisation,
    BoundaryTopology BoundaryTopology,
    MegastationSemanticZoningResult SemanticZoning,
    IReadOnlyList<SurfacePatch> Patches,
    MegastationUrbanStyle Style,
    IReadOnlyList<UrbanGrowthResult> Faces,
    IReadOnlyList<EdgeRegionPlan> Edges,
    IReadOnlyList<CornerRegionPlan> Corners,
    StationModuleMesh Mesh,
    MegastationMeshStats MeshStats,
    MegastationPrototypeDiagnostics Diagnostics);

public static class MegastationPrototypeGenerator
{
    public static MegastationPrototypeResult Generate(Galaxy.Station station, GraphicsDevice gd, MegastationPrototypeSettings? settings = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        var sw = Stopwatch.StartNew();
        string id = station.PersistenceId ?? station.Name;
        MegastationPrototypeCpuResult cpu = GenerateCpu(id, settings, sw);
        PlacedModule module = CreatePlacedModule(cpu);

        var albedo = MakeFlat(gd, Color.White);
        var material = MakeFlat(gd, new Color(128, 255, 0, 0));
        module.TextureInstance = albedo;
        module.MaterialInstance = material;
        return new MegastationPrototypeResult([module], [albedo, material], cpu.Diagnostics);
    }

    public static MegastationPrototypeCpuResult GenerateCpu(
        string persistenceId,
        MegastationPrototypeSettings? settings = null,
        Stopwatch? stopwatch = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= MegastationPrototypeSettings.Default;
        stopwatch ??= Stopwatch.StartNew();
        int rootSeed = MegastationSeed.Root(persistenceId, settings.SeedCompatibilityVersion);

        var grid = SliceGrid.Create(settings, MegastationSeed.Derive(rootSeed, "slice-grid layout"));
        cancellationToken.ThrowIfCancellationRequested();
        var occupancy = new CuboidStructuralVolumeGenerator().Generate(grid);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        var patches = SurfacePatchFinder.FindPatches(occupancy);
        var style = MegastationUrbanStyle.Generate(rootSeed);
        var corners = CornerRegionGenerator.PlanCorners(grid, settings, style, rootSeed);
        CornerRegionGenerator.Apply(occupancy, corners);
        cancellationToken.ThrowIfCancellationRequested();
        var edges = EdgeRegionGenerator.PlanEdges(grid, settings, style, corners, rootSeed);
        EdgeRegionGenerator.Apply(occupancy, edges);

        var faceResults = new List<UrbanGrowthResult>(6);
        foreach (var patch in patches.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var faceSettings = MegastationFaceSettings.ForPatch(settings, style, grid, patch, rootSeed);
            int faceSeed = patch.Direction == settings.UrbanPatchNormal
                ? MegastationSeed.Derive(rootSeed, "district layout")
                : MegastationSeed.Derive(rootSeed, $"district layout:{patch.Id}");
            faceResults.Add(UrbanGrowth.Generate(occupancy, patch, faceSettings, faceSeed));
        }

        var validation = MegastationConnectivity.Validate(occupancy);
        cancellationToken.ThrowIfCancellationRequested();
        var regularised = settings.EnableTopologyRegularisation
            ? TopologyRegulariser.Regularise(occupancy, settings)
            : BuildDisabledRegularisationResult(occupancy, settings, validation);
        var topologyStopwatch = Stopwatch.StartNew();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(regularised.Occupancy, settings);
        topologyStopwatch.Stop();
        MegastationSemanticZoningResult semanticZoning = MegastationSemanticZoningBuilder.Build(
            rootSeed,
            regularised.Occupancy,
            topology,
            faceResults);
        var mesh = new StationModuleMesh();
        var meshStats = MegastationPrototypeMeshBuilder.Build(
            regularised.Occupancy,
            topology,
            mesh,
            settings: settings,
            topologyBuildMilliseconds: topologyStopwatch.ElapsedMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        int districtCount = faceResults.Sum(f => f.Districts.Count);
        int maxDepth = faceResults.Count == 0 ? 0 : faceResults.Max(f => f.MaximumDepth);

        var diag = new MegastationPrototypeDiagnostics(
            persistenceId,
            BuildIdentifier(),
            settings.GeneratorVersion,
            settings.SeedCompatibilityVersion,
            settings.TopologyRegularisationAlgorithmVersion,
            settings.BoundaryTopologyAlgorithmVersion,
            settings.StructuralChamferAlgorithmVersion,
            settings.PositiveYUrbanSeedVersion,
            settings.FaceUrbanAlgorithmVersion,
            settings.EdgeAlgorithmVersion,
            settings.CornerAlgorithmVersion,
            rootSeed,
            grid.XCount,
            grid.YCount,
            grid.ZCount,
            grid.CellCount,
            occupancy.StructuralOccupiedCount,
            occupancy.UrbanOccupiedCount,
            regularised.Occupancy.TotalOccupiedCount,
            regularised.Report.RepairAddedCells,
            regularised.Report.RepairRemovedCells,
            faceResults.Count(f => f.Districts.Count > 0),
            occupancy.FaceRegionOccupiedCount,
            occupancy.EdgeRegionOccupiedCount,
            occupancy.CornerRegionOccupiedCount,
            districtCount,
            maxDepth,
            faceResults.Select(f => $"{RegionIdentity.Face(f.Patch.Direction)} districts={f.Districts.Count} maxDepth={f.MaximumDepth}").ToArray(),
            edges.Select(e => $"{e.Id} {e.ProfileSummary} start=({e.StartCornerDepthA},{e.StartCornerDepthB}) end=({e.EndCornerDepthA},{e.EndCornerDepthB})").ToArray(),
            corners.Select(c => $"{c.Id} {c.Summary} extents=({c.DepthA},{c.DepthB},{c.DepthC})").ToArray(),
            validation.ConnectedComponentsBeforeValidation,
            validation.RemovedDisconnectedCells,
            validation.HasSealedCavity,
            regularised.Report.EdgeCriticalBefore,
            regularised.Report.EdgeCriticalAfter,
            regularised.Report.VertexCriticalBefore,
            regularised.Report.VertexCriticalAfter,
            regularised.Report.ConnectedComponentsAfter,
            regularised.Report.SealedCavityAfter,
            regularised.Report.DefectOwnerSummary,
            meshStats.ExposedQuadCount,
            meshStats.TriangleCount,
            meshStats.VertexCount,
            meshStats.MeshPageCount,
            meshStats.BoundaryFaceCount,
            meshStats.CanonicalEdgeSegmentCount,
            meshStats.FlatContinuationCount,
            meshStats.ConvexExteriorCount,
            meshStats.ConcaveExteriorCount,
            meshStats.InvalidDiagonalCount,
            meshStats.SimpleConvexVertexCount,
            meshStats.StraightConvexContinuationVertexCount,
            meshStats.SimpleConcaveVertexCount,
            meshStats.ComplexVertexCount,
            meshStats.NonManifoldVertexCount,
            meshStats.EligibleChamferSegmentCount,
            meshStats.SuppressedConvexSegmentCount,
            meshStats.ChamferRunCount,
            meshStats.SuppressedChamferRunCount,
            meshStats.BevelQuadCount,
            meshStats.CornerCapCount,
            meshStats.ChamferSemanticValidation,
            meshStats.ChamferRuns,
            meshStats.MeshPath,
            meshStats.TopologySignature.Semantic,
            meshStats.SharpValidation,
            meshStats.ChamferedValidation,
            meshStats.TopologyBuildMilliseconds,
            meshStats.MeshBuildMilliseconds,
            stopwatch.ElapsedMilliseconds);

        return new MegastationPrototypeCpuResult(
            grid,
            occupancy,
            regularised.Occupancy,
            regularised.Report,
            topology,
            semanticZoning,
            patches,
            style,
            faceResults,
            edges,
            corners,
            mesh,
            meshStats,
            diag);
    }

    public static PlacedModule CreatePlacedModule(MegastationPrototypeCpuResult cpu)
    {
        Vector3 bounds = new(
            cpu.Grid.Dimension(GridAxis.X),
            cpu.Grid.Dimension(GridAxis.Y),
            cpu.Grid.Dimension(GridAxis.Z));
        var def = new StationModuleDefinition
        {
            Id = "megastation-prototype-b",
            Category = "megastation-prototype",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
        return new PlacedModule
        {
            Definition = def,
            Transform = Matrix.Identity,
            Seed = cpu.Diagnostics.RootSeed,
            ChamferDepth = 0f,
            AabbMin = bounds * -0.5f,
            AabbMax = bounds * 0.5f,
            HullMesh = cpu.Mesh,
        };
    }

    public static double EstimateConservativeEnvelopeRadius(
        string persistenceId,
        MegastationPrototypeSettings? settings = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        int rootSeed = MegastationSeed.Root(persistenceId, settings.SeedCompatibilityVersion);
        SliceGrid grid = SliceGrid.Create(
            settings,
            MegastationSeed.Derive(rootSeed, "slice-grid layout"));
        double x = grid.Dimension(GridAxis.X) * 0.5;
        double y = grid.Dimension(GridAxis.Y) * 0.5;
        double z = grid.Dimension(GridAxis.Z) * 0.5;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static Texture2D MakeFlat(GraphicsDevice gd, Color color)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData([color]);
        return tex;
    }

    private static string BuildIdentifier()
        => typeof(MegastationPrototypeGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(MegastationPrototypeGenerator).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static (StructuralOccupancy Occupancy, TopologyRegularisationReport Report) BuildDisabledRegularisationResult(
        StructuralOccupancy occupancy,
        MegastationPrototypeSettings settings,
        MegastationConnectivityReport connectivity)
    {
        var contacts = TopologyRegulariser.FindCriticalContacts(occupancy);
        var report = new TopologyRegularisationReport(
            settings.TopologyRegularisationAlgorithmVersion,
            0,
            occupancy.TotalOccupiedCount,
            occupancy.TotalOccupiedCount,
            0,
            0,
            contacts.Count(c => c.Kind == TopologyContactKind.EdgeDiagonal),
            contacts.Count(c => c.Kind == TopologyContactKind.EdgeDiagonal),
            contacts.Count(c => c.Kind == TopologyContactKind.VertexOnly),
            contacts.Count(c => c.Kind == TopologyContactKind.VertexOnly),
            connectivity.ConnectedComponentsBeforeValidation,
            connectivity.ConnectedComponentsBeforeValidation,
            connectivity.HasSealedCavity,
            connectivity.HasSealedCavity,
            [],
            contacts.Take(16).ToArray());
        return (occupancy.Clone(), report);
    }
}
