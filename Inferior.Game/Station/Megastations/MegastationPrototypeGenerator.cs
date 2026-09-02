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
    int InteriorAlgorithmVersion,
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
    MegastationInteriorPlan InteriorPlan,
    MegastationArtificialLightingPlan ArtificialLightingPlan,
    MegastationLandingDistrictPlan LandingDistrictPlan,
    MegastationInteriorPresentationPlan InteriorPresentationPlan,
    TopologyRegularisationReport TopologyRegularisation,
    BoundaryTopology BoundaryTopology,
    MegastationSemanticZoningResult SemanticZoning,
    IReadOnlyList<MegastationPlanarRegion> PlanarRegions,
    MegastationAttachmentPlan AttachmentPlan,
    IReadOnlyList<SurfacePatch> Patches,
    MegastationUrbanStyle Style,
    IReadOnlyList<UrbanGrowthResult> Faces,
    IReadOnlyList<EdgeRegionPlan> Edges,
    IReadOnlyList<CornerRegionPlan> Corners,
    StationModuleMesh Mesh,
    StationModuleMesh StructuralShadowMesh,
    StationModuleMesh InteriorMesh,
    VertexPositionColor[] ApproachBeamVertices,
    MegastationWindowPlan WindowPlan,
    StationModuleMesh WindowGlassMesh,
    MegastationLightPlan LightPlan,
    MegastationInfrastructurePlan InfrastructurePlan,
    StationModuleMesh InfrastructureMesh,
    MegastationMegaGreeblePlan MegaGreeblePlan,
    StationModuleMesh MegaGreebleMesh,
    MegastationFabricPlan FabricPlan,
    StationModuleMesh FabricMesh,
    MegastationServiceChannelPlan ServiceChannelPlan,
    StationModuleMesh ServiceChannelMesh,
    MegastationSystemMaterialAssignment? MaterialAssignment,
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
        PlacedModule interior = CreateInteriorModule(cpu);
        PlacedModule? megaGreeble = CreateMegaGreebleModule(cpu);

        var albedo = MakeFlat(gd, Color.White);
        var material = MakeFlat(gd, new Color(128, 255, 0, 0));
        module.TextureInstance = albedo;
        module.MaterialInstance = material;
        interior.TextureInstance = albedo;
        interior.MaterialInstance = material;
        if (megaGreeble != null)
        {
            megaGreeble.TextureInstance = albedo;
            megaGreeble.MaterialInstance = material;
        }
        return new MegastationPrototypeResult(
            megaGreeble == null ? [module, interior] : [module, interior, megaGreeble],
            [albedo, material], cpu.Diagnostics);
    }

    public static MegastationPrototypeCpuResult GenerateCpu(
        string persistenceId,
        MegastationPrototypeSettings? settings = null,
        Stopwatch? stopwatch = null,
        CancellationToken cancellationToken = default,
        SystemMaterialAssignmentContext? systemMaterials = null)
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
        StructuralOccupancy hollowedOccupancy = occupancy.Clone();
        MegastationInteriorPlan interiorPlan = MegastationInteriorPlanner.PlanAndApply(
            hollowedOccupancy,
            rootSeed,
            cancellationToken);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(hollowedOccupancy);
        var regularised = settings.EnableTopologyRegularisation
            ? TopologyRegulariser.Regularise(hollowedOccupancy, settings)
            : BuildDisabledRegularisationResult(
                hollowedOccupancy,
                settings,
                MegastationConnectivity.Validate(hollowedOccupancy));
        var topologyStopwatch = Stopwatch.StartNew();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(regularised.Occupancy, settings);
        topologyStopwatch.Stop();
        MegastationSemanticZoningResult semanticZoning = MegastationSemanticZoningBuilder.Build(
            rootSeed,
            regularised.Occupancy,
            topology,
            faceResults);
        MegastationSystemMaterialAssignment? materialAssignment = systemMaterials is { } materialContext
            ? MegastationSystemMaterialAssignment.Create(materialContext, persistenceId)
            : null;
        MegastationLandingDistrictPlan landingDistrict =
            MegastationLandingDistrictPlanner.Plan(interiorPlan);
        MegastationArtificialLightingPlan artificialLighting =
            MegastationArtificialLighting.WithAdditionalLights(
                MegastationArtificialLighting.Plan(interiorPlan),
                landingDistrict.ArtificialLights);
        interiorPlan = interiorPlan with
        {
            Diagnostics = interiorPlan.Diagnostics with
            {
                ArtificialLightAlgorithmVersion = artificialLighting.AlgorithmVersion,
                ArtificialLightSourceCount = artificialLighting.Lights.Count,
                ArtificialLightMinimumRange = artificialLighting.Lights.Min(light => light.Range),
                ArtificialLightMaximumRange = artificialLighting.Lights.Max(light => light.Range),
                ArtificialIndirectStrength = MegastationArtificialLighting.IndirectStrength,
                ArtificialIndirectRangeScale = MegastationArtificialLighting.IndirectRangeScale,
                ArtificialLightSignature = artificialLighting.Signature,
            },
        };
        MegastationInteriorPresentationPlan interiorPresentation =
            MegastationInteriorPresentationPlanner.Plan(
                interiorPlan,
                materialAssignment);
        MegastationInteriorMeshBuildResult interiorMesh = MegastationInteriorMeshBuilder.Build(
            interiorPlan,
            materialAssignment,
            interiorPresentation,
            landingDistrict,
            artificialLighting,
            cancellationToken);
        if (interiorMesh.LandingDistrictDiagnostics is { } landingDiagnostics)
            landingDistrict = landingDistrict with { Diagnostics = landingDiagnostics };
        VertexPositionColor[] approachBeamVertices =
            MegastationApproachBeamMeshBuilder.Build(interiorPresentation);
        StationModuleMesh structuralShadowMesh = MegastationInteriorMeshBuilder.BuildStructuralCaster(
            regularised.Occupancy,
            topology);
        int throatBoundaryFaces = topology.Faces.Count(face =>
            face.SpaceKind == MegastationBoundarySpaceKind.EntranceThroatBoundary);
        int interiorBoundaryFaces = topology.Faces.Count(face =>
            face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary);
        interiorPlan = interiorPlan with
        {
            Diagnostics = interiorMesh.Diagnostics with
            {
                ThroatBoundaryFaceCount = throatBoundaryFaces,
                InteriorBoundaryFaceCount = interiorBoundaryFaces,
                InteriorStructuralVertexCount = (throatBoundaryFaces + interiorBoundaryFaces) * 4,
                InteriorStructuralTriangleCount = (throatBoundaryFaces + interiorBoundaryFaces) * 2,
            },
        };
        MegastationPlanarRegion[] planarRegions = MegastationPlanarRegionExtractor.Extract(
            grid,
            topology,
            semanticZoning,
            cancellationToken);
        MegastationAttachmentPlan attachmentPlan = MegastationAttachmentPlanner.Plan(
            grid,
            regularised.Occupancy,
            planarRegions,
            cancellationToken);
        attachmentPlan = MegastationAttachmentPlanner.ApplyEntrancePriority(
            attachmentPlan,
            planarRegions,
            interiorPresentation.Precinct);
        interiorPlan = interiorPlan with
        {
            Diagnostics = interiorPlan.Diagnostics with
            {
                EntrancePrecinctReservationCount = attachmentPlan.Reservations.Count(
                    reservation => reservation.PlacementIdentity.StartsWith(
                        "interior/entrance-precinct/", StringComparison.Ordinal)),
            },
        };
        MegastationWindowPlan windowPlan = MegastationWindowPlanner.Plan(
            grid,
            topology,
            semanticZoning,
            cancellationToken);
        windowPlan = MegastationAttachmentPlanner.SuppressWindows(
            windowPlan,
            attachmentPlan.Reservations,
            out int suppressedWindows);
        MegastationWindowMeshBuildResult windowMesh = MegastationWindowMeshBuilder.Build(
            windowPlan,
            cancellationToken);
        windowPlan = windowPlan with { Diagnostics = windowMesh.Diagnostics };
        MegastationLightPlan lightPlan = MegastationLightingPlanner.Plan(
            grid,
            topology,
            semanticZoning,
            cancellationToken);
        lightPlan = MegastationAttachmentPlanner.SuppressLights(
            lightPlan,
            attachmentPlan.Reservations,
            out int suppressedLights);
        attachmentPlan = MegastationAttachmentPlanner.WithSuppressionCounts(
            attachmentPlan,
            suppressedWindows,
            suppressedLights);
        MegastationInfrastructurePlan baselineInfrastructurePlan = MegastationInfrastructurePlanner.Plan(
            planarRegions,
            attachmentPlan,
            windowPlan,
            lightPlan,
            cancellationToken);
        MegastationMegaGreeblePlan megaGreeblePlan = MegastationMegaGreeblePlanner.Plan(
            planarRegions, attachmentPlan, windowPlan, lightPlan, baselineInfrastructurePlan,
            regularised.Occupancy, style,
            cancellationToken);
        MegastationMegaGreebleMeshBuildResult megaGreebleMesh =
            MegastationMegaGreebleMeshBuilder.Build(megaGreeblePlan, cancellationToken);
        megaGreeblePlan = megaGreeblePlan with { Diagnostics = megaGreebleMesh.Diagnostics };
        MegastationFabricPlan baselineFabricPlan = MegastationFabricPlanner.Plan(
            planarRegions, attachmentPlan, windowPlan, lightPlan, baselineInfrastructurePlan,
            megaGreeblePlan, regularised.Occupancy, cancellationToken);
        MegastationServiceChannelPlan serviceChannelPlan =
            MegastationServiceChannelPlanner.Plan(planarRegions, attachmentPlan, windowPlan,
                lightPlan, baselineInfrastructurePlan, megaGreeblePlan, baselineFabricPlan,
                cancellationToken);
        MegastationInfrastructurePlan infrastructurePlan = MegastationInfrastructurePlanner.Plan(
            planarRegions, attachmentPlan, windowPlan, lightPlan, serviceChannelPlan,
            megaGreeblePlan, cancellationToken);
        MegastationInfrastructureMeshBuildResult infrastructureMesh =
            MegastationInfrastructureMeshBuilder.Build(infrastructurePlan, cancellationToken);
        infrastructurePlan = infrastructurePlan with
        {
            Diagnostics = infrastructureMesh.Diagnostics,
        };
        MegastationFabricPlan fabricPlan = MegastationFabricPlanner.Plan(
            planarRegions, attachmentPlan, windowPlan, lightPlan, infrastructurePlan,
            megaGreeblePlan, regularised.Occupancy, serviceChannelPlan, cancellationToken);
        MegastationFabricMeshBuildResult fabricMesh =
            MegastationFabricMeshBuilder.Build(fabricPlan, materialAssignment, cancellationToken);
        fabricPlan = fabricPlan with { Diagnostics = fabricMesh.Diagnostics };
        HashSet<string> developedFeatures = infrastructurePlan.Clusters
            .Where(cluster => cluster.ChannelFeatureIdentity is not null)
            .Select(cluster => cluster.ChannelFeatureIdentity!)
            .Concat(fabricPlan.Instances
                .Where(instance => instance.ChannelFeatureIdentity is not null)
                .Select(instance => instance.ChannelFeatureIdentity!))
            .ToHashSet(StringComparer.Ordinal);
        serviceChannelPlan = serviceChannelPlan with
        {
            Diagnostics = serviceChannelPlan.Diagnostics with
            {
                ChannelBearingSurfaceCount = serviceChannelPlan.Networks.Count,
                RunsWithAdjacentG2Count = infrastructurePlan.Clusters
                    .Where(cluster => cluster.ChannelAssociation ==
                        MegastationChannelAssociationKind.ChannelEdge)
                    .Select(cluster => cluster.ChannelFeatureIdentity)
                    .Where(identity => identity is not null).Distinct(StringComparer.Ordinal).Count(),
                RunsWithAdjacentFabricCount = fabricPlan.Instances
                    .Where(instance => instance.ChannelAssociation ==
                        MegastationChannelAssociationKind.ChannelEdge)
                    .Select(instance => instance.ChannelFeatureIdentity)
                    .Where(identity => identity is not null).Distinct(StringComparer.Ordinal).Count(),
                JunctionsWithDevelopmentCount = serviceChannelPlan.Nodes.Count(node =>
                    (node.Kind is MegastationServiceChannelNodeKind.TJunction
                        or MegastationServiceChannelNodeKind.FourWay)
                    && developedFeatures.Contains(node.Identity)),
                EndpointsWithDevelopmentCount = serviceChannelPlan.Nodes.Count(node =>
                    node.Endpoint.HasValue && developedFeatures.Contains(node.Identity)),
            },
        };
        MegastationServiceChannelMeshBuildResult serviceChannelMesh =
            MegastationServiceChannelMeshBuilder.Build(
                serviceChannelPlan, materialAssignment, cancellationToken);
        serviceChannelPlan = serviceChannelPlan with
        {
            Diagnostics = serviceChannelMesh.Diagnostics,
        };
        var mesh = new StationModuleMesh();
        var meshStats = MegastationPrototypeMeshBuilder.Build(
            regularised.Occupancy,
            topology,
            mesh,
            settings: settings,
            topologyBuildMilliseconds: topologyStopwatch.ElapsedMilliseconds,
            semanticZoning: semanticZoning,
            materialAssignment: materialAssignment,
            interiorPlan: interiorPlan,
            artificialLighting: artificialLighting);
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        int districtCount = faceResults.Sum(f => f.Districts.Count);
        int maxDepth = faceResults.Count == 0 ? 0 : faceResults.Max(f => f.MaximumDepth);

        var diag = new MegastationPrototypeDiagnostics(
            persistenceId,
            BuildIdentifier(),
            settings.GeneratorVersion,
            settings.SeedCompatibilityVersion,
            settings.InteriorAlgorithmVersion,
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
            interiorPlan,
            artificialLighting,
            landingDistrict,
            interiorPresentation,
            regularised.Report,
            topology,
            semanticZoning,
            planarRegions,
            attachmentPlan,
            patches,
            style,
            faceResults,
            edges,
            corners,
            mesh,
            structuralShadowMesh,
            interiorMesh.Mesh,
            approachBeamVertices,
            windowPlan,
            windowMesh.Mesh,
            lightPlan,
            infrastructurePlan,
            infrastructureMesh.Mesh,
            megaGreeblePlan,
            megaGreebleMesh.Mesh,
            fabricPlan,
            fabricMesh.Mesh,
            serviceChannelPlan,
            serviceChannelMesh.Mesh,
            materialAssignment,
            meshStats,
            diag);
    }

    public static PlacedModule CreatePlacedModule(MegastationPrototypeCpuResult cpu)
    {
#if DEBUG
        VertexPositionColor[]? infrastructureDebugLines =
            MegastationInfrastructureDebug.BuildLines(cpu.InfrastructurePlan);
#else
        VertexPositionColor[]? infrastructureDebugLines = null;
#endif
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
        var module = new PlacedModule
        {
            Definition = def,
            Transform = Matrix.Identity,
            Seed = cpu.Diagnostics.RootSeed,
            ChamferDepth = 0f,
            AabbMin = bounds * -0.5f,
            AabbMax = bounds * 0.5f,
            Mesh = cpu.InfrastructureMesh,
            HasNativeMegastationInfrastructure = true,
            NativeInfrastructureDebugLines = infrastructureDebugLines,
            HullMesh = cpu.Mesh,
            HullShadowMesh = cpu.StructuralShadowMesh,
            UsesHullVertexIllumination = true,
            GlassMesh = cpu.WindowGlassMesh,
            HullMaterialRanges = cpu.Mesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
        module.GlowLights.AddRange(cpu.LightPlan.Lights.Select(light => light.ToStationLightInfo()));
        module.GlowLights.AddRange(CreateInteriorGuidanceLights(cpu.InteriorPresentationPlan));
        return module;
    }

    public static PlacedModule CreateInteriorModule(MegastationPrototypeCpuResult cpu)
    {
        Vector3 bounds = new(
            cpu.Grid.Dimension(GridAxis.X),
            cpu.Grid.Dimension(GridAxis.Y),
            cpu.Grid.Dimension(GridAxis.Z));
        var definition = new StationModuleDefinition
        {
            Id = "megastation-interior-h1",
            Category = "megastation-interior",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = MegastationSeed.Derive(cpu.Diagnostics.RootSeed, "interior presentation"),
            ChamferDepth = 0f,
            AabbMin = bounds * -.5f,
            AabbMax = bounds * .5f,
            Mesh = cpu.InteriorMesh,
            IsHullLessPresentationLayer = true,
            HasNativeMegastationInterior = true,
            UsesDecorationVertexIllumination = true,
            UsesCoplanarStructuralOverlay = true,
            NativeInteriorDebugLines = MegastationInteriorDebug.BuildLines(
                cpu.InteriorPlan,
                cpu.BoundaryTopology,
                cpu.Grid),
            NativeApproachBeamVertices = cpu.ApproachBeamVertices,
            DecorationMaterialRanges = cpu.InteriorMesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
    }

    private static IEnumerable<StationLightInfo> CreateInteriorGuidanceLights(
        MegastationInteriorPresentationPlan presentation)
        => presentation.Markers.Select(marker => new StationLightInfo(
            marker.Position,
            marker.Colour,
            GlowType.MegastationEntranceGuidance,
            marker.Intensity,
            0f,
            0f,
            LightPattern.Continuous)
        {
            SurfaceNormal = marker.SurfaceNormal,
            PresentationSizePixels = marker.GlowSizePixels,
            PresentationFadeStartMeters = marker.GlowFadeStartMeters,
            PresentationFadeEndMeters = marker.GlowFadeEndMeters,
        });

    public static PlacedModule? CreateMegaGreebleModule(MegastationPrototypeCpuResult cpu)
    {
        if (cpu.MegaGreebleMesh.IsEmpty)
            return null;
        Vector3 bounds = new(cpu.Grid.Dimension(GridAxis.X), cpu.Grid.Dimension(GridAxis.Y),
            cpu.Grid.Dimension(GridAxis.Z));
        var definition = new StationModuleDefinition
        {
            Id = "megastation-mega-greeble",
            Category = "megastation-mega-greeble",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
#if DEBUG
        VertexPositionColor[]? debugLines =
            MegastationMegaGreebleDebug.BuildLines(cpu.MegaGreeblePlan);
#else
        VertexPositionColor[]? debugLines = null;
#endif
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = MegastationSeed.Derive(cpu.Diagnostics.RootSeed, "mega-greeble:v1"),
            ChamferDepth = 0f,
            AabbMin = bounds * -0.5f,
            AabbMax = bounds * 0.5f,
            Mesh = cpu.MegaGreebleMesh,
            HasNativeMegastationMegaGreeble = true,
            IsHullLessPresentationLayer = true,
            NativeMegaGreebleDebugLines = debugLines,
        };
    }

    public static PlacedModule? CreateFabricModule(MegastationPrototypeCpuResult cpu)
    {
        if (cpu.FabricMesh.IsEmpty)
            return null;
        Vector3 bounds = new(cpu.Grid.Dimension(GridAxis.X), cpu.Grid.Dimension(GridAxis.Y),
            cpu.Grid.Dimension(GridAxis.Z));
        var definition = new StationModuleDefinition
        {
            Id = "megastation-fabric-structures",
            Category = "megastation-fabric-structures",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
#if DEBUG
        VertexPositionColor[]? debugLines = MegastationFabricDebug.BuildLines(cpu.FabricPlan);
#else
        VertexPositionColor[]? debugLines = null;
#endif
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = MegastationSeed.Derive(cpu.Diagnostics.RootSeed, "fabric-structures:v1"),
            ChamferDepth = 0f,
            AabbMin = bounds * -0.5f,
            AabbMax = bounds * 0.5f,
            Mesh = cpu.FabricMesh,
            HasNativeMegastationFabric = true,
            IsHullLessPresentationLayer = true,
            NativeFabricDebugLines = debugLines,
            DecorationMaterialRanges = cpu.FabricMesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
    }

    public static PlacedModule? CreateServiceChannelModule(MegastationPrototypeCpuResult cpu)
    {
        if (cpu.ServiceChannelMesh.IsEmpty)
            return null;
        Vector3 bounds = new(cpu.Grid.Dimension(GridAxis.X), cpu.Grid.Dimension(GridAxis.Y),
            cpu.Grid.Dimension(GridAxis.Z));
        var definition = new StationModuleDefinition
        {
            Id = "megastation-service-channels-sc2",
            Category = "megastation-service-channels",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
#if DEBUG
        VertexPositionColor[]? debugLines =
            MegastationServiceChannelDebug.BuildLines(cpu.ServiceChannelPlan);
#else
        VertexPositionColor[]? debugLines = null;
#endif
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = MegastationSeed.Derive(cpu.Diagnostics.RootSeed, "service-channels:sc2"),
            ChamferDepth = 0f,
            AabbMin = bounds * -.5f,
            AabbMax = bounds * .5f,
            Mesh = cpu.ServiceChannelMesh,
            HasNativeMegastationServiceChannels = true,
            IsHullLessPresentationLayer = true,
            NativeServiceChannelDebugLines = debugLines,
            DecorationMaterialRanges =
                cpu.ServiceChannelMesh.PrepareMaterialGroups()?.Ranges ?? [],
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
