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
    MegastationMegaShelfPlan MegaShelfPlan,
    MegastationBayStructuralTrussPlan BayStructuralTrussPlan,
    MegastationBayUtilityPlan BayUtilityPlan,
    MegastationBaySecondaryUtilityPlan BaySecondaryUtilityPlan,
    MegastationArtificialLightingPlan ArtificialLightingPlan,
    MegastationLandingDistrictPlan LandingDistrictPlan,
    MegastationBayHabitationPlan BayHabitationPlan,
    MegastationBayFacilityPlan BayFacilityPlan,
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

internal sealed record RectilinearMegastationMacroData(
    string PersistenceId,
    int RootSeed,
    MegastationPrototypeSettings Settings,
    SliceGrid Grid,
    StructuralOccupancy Occupancy,
    StructuralOccupancy MacroOccupancy,
    MegastationInteriorPlan InteriorPlan,
    IReadOnlyList<SurfacePatch> Patches,
    MegastationUrbanStyle Style,
    IReadOnlyList<UrbanGrowthResult> Faces,
    IReadOnlyList<EdgeRegionPlan> Edges,
    IReadOnlyList<CornerRegionPlan> Corners,
    MegastationConnectivityReport Connectivity,
    long RawMassingMilliseconds,
    long EntranceCarveMilliseconds,
    double GenerationMilliseconds);

internal sealed record MegastationPrototypeMacroCpuResult(
    RectilinearMegastationMacroData StructuralData,
    BoundaryTopology BoundaryTopology,
    StationModuleMesh Mesh,
    long BoundaryTopologyBuildMilliseconds,
    long MeshBuildMilliseconds,
    double GenerationMilliseconds);

public static class MegastationPrototypeGenerator
{
    internal static MegastationPrototypeMacroCpuResult GenerateMacroCpu(
        string persistenceId,
        MegastationPrototypeSettings? settings = null,
        CancellationToken cancellationToken = default,
        SystemMaterialAssignmentContext? systemMaterials = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        var stopwatch = Stopwatch.StartNew();
        RectilinearMegastationMacroData structural = PrepareMacroData(
            persistenceId,
            settings,
            cancellationToken);
        var topologyStopwatch = Stopwatch.StartNew();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            structural.MacroOccupancy,
            settings);
        topologyStopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = new StationModuleMesh();
        var meshStopwatch = Stopwatch.StartNew();
        MegastationSystemMaterialAssignment? materialAssignment = systemMaterials is { } context
            ? MegastationSystemMaterialAssignment.Create(context, persistenceId)
            : null;
        MegastationPrototypeMeshBuilder.BuildMacro(
            structural.MacroOccupancy,
            topology,
            mesh,
            materialAssignment);
        meshStopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();
        return new(
            structural,
            topology,
            mesh,
            topologyStopwatch.ElapsedMilliseconds,
            meshStopwatch.ElapsedMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds);
    }

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
        RectilinearMegastationMacroData macro = PrepareMacroData(
            persistenceId,
            settings,
            cancellationToken);
        int rootSeed = macro.RootSeed;
        SliceGrid grid = macro.Grid;
        StructuralOccupancy occupancy = macro.Occupancy;
        IReadOnlyList<SurfacePatch> patches = macro.Patches;
        MegastationUrbanStyle style = macro.Style;
        IReadOnlyList<CornerRegionPlan> corners = macro.Corners;
        IReadOnlyList<EdgeRegionPlan> edges = macro.Edges;
        IReadOnlyList<UrbanGrowthResult> faceResults = macro.Faces;
        MegastationConnectivityReport validation = macro.Connectivity;
        MegastationInteriorPlan interiorPlan = macro.InteriorPlan;
        var regularised = settings.EnableTopologyRegularisation
            ? TopologyRegulariser.Regularise(macro.MacroOccupancy, settings)
            : BuildDisabledRegularisationResult(
                macro.MacroOccupancy,
                settings,
                MegastationConnectivity.Validate(macro.MacroOccupancy));
        var topologyStopwatch = Stopwatch.StartNew();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            regularised.Occupancy,
            settings);
        topologyStopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        MegastationMegaShelfPlan megaShelfPlan = MegastationMegaShelfPlanner.Plan(
            interiorPlan,
            regularised.Occupancy,
            topology,
            settings.MegaShelfDevelopment,
            cancellationToken);
        interiorPlan = interiorPlan with
        {
            AddedStructuralSolids = megaShelfPlan.StructuralSolids,
            AdditionalLandingSurfaces = megaShelfPlan.LandingSurfaces,
            AddedStructuralMarkers = megaShelfPlan.Markers,
            AddedStructuralTrusses = megaShelfPlan.Trusses,
            Diagnostics = interiorPlan.Diagnostics with
            {
                MegaShelfAlgorithmVersion = megaShelfPlan.AlgorithmVersion,
                MegaShelfCandidateCount = megaShelfPlan.Diagnostics.CandidateCount,
                MegaShelfCount = megaShelfPlan.Diagnostics.AcceptedCount,
                MegaShelfSupportRejectCount = megaShelfPlan.Diagnostics.SupportRejectCount,
                MegaShelfStructuralRejectCount = megaShelfPlan.Diagnostics.StructuralRejectCount,
                MegaShelfArrivalExclusionRejectCount =
                    megaShelfPlan.Diagnostics.ArrivalExclusionRejectCount,
                MegaShelfOverlapRejectCount = megaShelfPlan.Diagnostics.ShelfOverlapRejectCount,
                MegaShelfVerticalSeparationRejectCount =
                    megaShelfPlan.Diagnostics.VerticalSeparationRejectCount,
                MegaShelfOperatingClearanceRejectCount =
                    megaShelfPlan.Diagnostics.OperatingClearanceRejectCount,
                MegaShelfLandingDowngradeCount =
                    megaShelfPlan.Diagnostics.LandingDowngradeCount,
                MegaShelfFullSpanFlightClearanceRejectCount =
                    megaShelfPlan.Diagnostics.FullSpanFlightClearanceRejectCount,
                MegaShelfCompositionRejectCount =
                    megaShelfPlan.Diagnostics.CompositionRejectCount,
                MegaShelfCantileverCandidateCount =
                    megaShelfPlan.Diagnostics.CantileverCandidateCount,
                MegaShelfCornerCandidateCount =
                    megaShelfPlan.Diagnostics.CornerCandidateCount,
                MegaShelfFullSpanCandidateCount =
                    megaShelfPlan.Diagnostics.FullSpanCandidateCount,
                MegaShelfCornerViableCandidateCount =
                    megaShelfPlan.Diagnostics.CornerViableCandidateCount,
                MegaShelfFullSpanViableCandidateCount =
                    megaShelfPlan.Diagnostics.FullSpanViableCandidateCount,
                MegaShelfCantileverAcceptedCount =
                    megaShelfPlan.Diagnostics.CantileverAcceptedCount,
                MegaShelfCornerAcceptedCount =
                    megaShelfPlan.Diagnostics.CornerAcceptedCount,
                MegaShelfFullSpanAcceptedCount =
                    megaShelfPlan.Diagnostics.FullSpanAcceptedCount,
                MegaShelfLowCandidateCount = megaShelfPlan.Diagnostics.LowCandidateCount,
                MegaShelfMidCandidateCount = megaShelfPlan.Diagnostics.MidCandidateCount,
                MegaShelfHighCandidateCount = megaShelfPlan.Diagnostics.HighCandidateCount,
                MegaShelfLowAcceptedCount = megaShelfPlan.Diagnostics.LowAcceptedCount,
                MegaShelfMidAcceptedCount = megaShelfPlan.Diagnostics.MidAcceptedCount,
                MegaShelfHighAcceptedCount = megaShelfPlan.Diagnostics.HighAcceptedCount,
                MegaShelfMinimumWidth = megaShelfPlan.Diagnostics.MinimumWidth,
                MegaShelfMaximumWidth = megaShelfPlan.Diagnostics.MaximumWidth,
                MegaShelfMinimumProjection = megaShelfPlan.Diagnostics.MinimumProjection,
                MegaShelfMaximumProjection = megaShelfPlan.Diagnostics.MaximumProjection,
                MegaShelfMinimumThickness = megaShelfPlan.Diagnostics.MinimumThickness,
                MegaShelfMaximumThickness = megaShelfPlan.Diagnostics.MaximumThickness,
                MegaShelfVisibleTriangleCount = megaShelfPlan.Diagnostics.VisibleTriangleCount,
                MegaShelfCasterTriangleCount = megaShelfPlan.Diagnostics.CasterTriangleCount,
                MegaShelfMarkerCount = megaShelfPlan.Diagnostics.MarkerCount,
                MegaShelfCornerMarkerCount = megaShelfPlan.Diagnostics.CornerMarkerCount,
                MegaShelfLowerEdgeMarkerCount = megaShelfPlan.Diagnostics.LowerEdgeMarkerCount,
                MegaShelfTrussCount = megaShelfPlan.Diagnostics.TrussCount,
                MegaShelfBoxTrussCount = megaShelfPlan.Diagnostics.BoxTrussCount,
                MegaShelfTriangularTrussCount =
                    megaShelfPlan.Diagnostics.TriangularTrussCount,
                MegaShelfTrussVisibleTriangleCount =
                    megaShelfPlan.Diagnostics.TrussVisibleTriangleCount,
                MegaShelfTrussCasterTriangleCount =
                    megaShelfPlan.Diagnostics.TrussCasterTriangleCount,
                MegaShelfMacroLayout = megaShelfPlan.Diagnostics.MacroLayout,
                MegaShelfBookcaseCount = megaShelfPlan.Diagnostics.BookcaseCount,
                MegaShelfBookcaseShelfCount = megaShelfPlan.Diagnostics.BookcaseShelfCount,
                MegaShelfBookcaseInsufficientHeightRejectCount =
                    megaShelfPlan.Diagnostics.BookcaseInsufficientHeightRejectCount,
                MegaShelfBookcaseArrivalRejectCount =
                    megaShelfPlan.Diagnostics.BookcaseArrivalRejectCount,
                MegaShelfBookcaseWallContinuityRejectCount =
                    megaShelfPlan.Diagnostics.BookcaseWallContinuityRejectCount,
                MegaShelfBookcaseStructuralRejectCount =
                    megaShelfPlan.Diagnostics.BookcaseStructuralRejectCount,
                MegaShelfBookcaseSymmetryRejectCount =
                    megaShelfPlan.Diagnostics.BookcaseSymmetryRejectCount,
                MegaShelfMacroSummary = megaShelfPlan.Diagnostics.MacroSummary,
                MegaShelfSummary = megaShelfPlan.Diagnostics.Summary,
                MegaShelfSignature = megaShelfPlan.Diagnostics.Signature,
            },
        };
        MegastationSemanticZoningResult semanticZoning = MegastationSemanticZoningBuilder.Build(
            rootSeed,
            regularised.Occupancy,
            topology,
            faceResults);
        MegastationSystemMaterialAssignment? materialAssignment = systemMaterials is { } materialContext
            ? MegastationSystemMaterialAssignment.Create(materialContext, persistenceId)
            : null;
        MegastationLandingDistrictPlan landingDistrict =
            MegastationLandingDistrictPlanner.Plan(
                interiorPlan,
                regularised.Occupancy,
                megaShelfPlan,
                settings.MegaShelfDevelopment.ForceLandingSiteOnShelf);
        MegastationBayWallCompositionPlan bayWallComposition =
            MegastationBayWallCompositionPlanner.Plan(
                interiorPlan, landingDistrict, regularised.Occupancy, topology);
        MegastationBayHabitationPlan bayHabitation = bayWallComposition.Habitation;
        MegastationBayFacilityPlan bayFacilities = bayWallComposition.Facilities;
        MegastationBayStructuralTrussPlan bayStructuralTrusses =
            MegastationBayStructuralTrussPlanner.Plan(
                interiorPlan, regularised.Occupancy, topology, landingDistrict,
                bayFacilities, megaShelfPlan);
        MegastationBayStructuralTrussDiagnostics bayTrussDiagnostics =
            bayStructuralTrusses.Diagnostics;
        interiorPlan = interiorPlan with
        {
            BayStructuralTrusses = bayStructuralTrusses,
            Diagnostics = interiorPlan.Diagnostics with
            {
                BayStructuralTrussAlgorithmVersion = bayTrussDiagnostics.AlgorithmVersion,
                BayStructuralTrussWallFieldCount = bayTrussDiagnostics.WallFieldCount,
                BayStructuralTrussCeilingFieldCount = bayTrussDiagnostics.CeilingFieldCount,
                BayStructuralTrussWallCount = bayTrussDiagnostics.WallTrussCount,
                BayStructuralTrussCeilingCount = bayTrussDiagnostics.CeilingTrussCount,
                BayStructuralTrussHardConflictRejectCount =
                    bayTrussDiagnostics.HardConflictRejectCount,
                BayStructuralTrussOperationalRejectCount =
                    bayTrussDiagnostics.OperationalClearanceRejectCount,
                BayStructuralTrussSupportRejectCount = bayTrussDiagnostics.SupportRejectCount,
                BayStructuralTrussSoftWindowOverlapCount =
                    bayTrussDiagnostics.SoftWindowOverlapCount,
                BayStructuralTrussVisibleTriangleCount =
                    bayTrussDiagnostics.VisibleTriangleCount,
                BayStructuralTrussCasterTriangleCount =
                    bayTrussDiagnostics.CasterTriangleCount,
                BayStructuralTrussPlanningMilliseconds =
                    bayTrussDiagnostics.PlanningMilliseconds,
                BayStructuralTrussSummary = bayTrussDiagnostics.Summary,
                BayStructuralTrussSignature = bayTrussDiagnostics.Signature,
            },
        };
        MegastationBayUtilityPlan bayUtilities = MegastationBayUtilityPlanner.Plan(
            interiorPlan, regularised.Occupancy, topology, landingDistrict,
            bayFacilities, bayStructuralTrusses);
        interiorPlan = interiorPlan with { BayUtilities = bayUtilities };
        MegastationBaySecondaryUtilityPlan baySecondaryUtilities =
            MegastationBaySecondaryUtilityPlanner.Plan(
                interiorPlan, landingDistrict, bayFacilities, bayStructuralTrusses,
                bayUtilities);
        interiorPlan = interiorPlan with { BaySecondaryUtilities = baySecondaryUtilities };
        MegastationShelfLightingPlan shelfLighting =
            MegastationShelfLightingPlanner.Plan(interiorPlan, megaShelfPlan);
        MegastationArtificialLightingPlan artificialLighting =
            MegastationArtificialLighting.WithAdditionalLights(
                MegastationArtificialLighting.WithAdditionalLights(
                    MegastationArtificialLighting.WithAdditionalLights(
                        MegastationArtificialLighting.Plan(interiorPlan),
                        landingDistrict.ArtificialLights),
                    bayFacilities.ArtificialLights),
                shelfLighting.ArtificialLights);
        interiorPlan = interiorPlan with
        {
            ShelfLighting = shelfLighting,
            Diagnostics = interiorPlan.Diagnostics with
            {
                ArtificialLightAlgorithmVersion = artificialLighting.AlgorithmVersion,
                ArtificialLightSourceCount = artificialLighting.Lights.Count,
                ArtificialLightMinimumRange = artificialLighting.Lights.Min(light => light.Range),
                ArtificialLightMaximumRange = artificialLighting.Lights.Max(light => light.Range),
                ArtificialIndirectStrength = MegastationArtificialLighting.IndirectStrength,
                ArtificialIndirectRangeScale = MegastationArtificialLighting.IndirectRangeScale,
                ArtificialLightSignature = artificialLighting.Signature,
                MegaShelfObstacleBeaconCount = shelfLighting.Beacons.Count,
                MegaShelfFloodFixtureCount = shelfLighting.FloodFixtures.Count,
                MegaShelfStaticWorkLightCount = shelfLighting.ArtificialLights.Count,
                MegaShelfLightingSignature = shelfLighting.Signature,
            },
        };
        MegastationInteriorPresentationPlan interiorPresentation =
            MegastationInteriorPresentationPlanner.Plan(
                interiorPlan,
                materialAssignment);
        MegastationArtificialOcclusion artificialOcclusion =
            MegastationArtificialOcclusion.Build(
                regularised.Occupancy, landingDistrict, interiorPresentation, bayFacilities,
                megaShelfPlan.StructuralSolids);
        MegastationInteriorMeshBuildResult interiorMesh = MegastationInteriorMeshBuilder.Build(
            interiorPlan,
            materialAssignment,
            interiorPresentation,
            landingDistrict,
            bayHabitation,
            bayFacilities,
            artificialLighting,
            artificialOcclusion,
            cancellationToken);
        if (interiorMesh.LandingDistrictDiagnostics is { } landingDiagnostics)
            landingDistrict = landingDistrict with { Diagnostics = landingDiagnostics };
        if (interiorMesh.BayHabitationDiagnostics is { } habitationDiagnostics)
            bayHabitation = bayHabitation with { Diagnostics = habitationDiagnostics };
        if (interiorMesh.BayFacilityDiagnostics is { } facilityDiagnostics)
            bayFacilities = bayFacilities with { Diagnostics = facilityDiagnostics };
        if (interiorMesh.BayUtilityDiagnostics is { } utilityDiagnostics)
            bayUtilities = bayUtilities with { Diagnostics = utilityDiagnostics };
        if (interiorMesh.BaySecondaryUtilityDiagnostics is { } secondaryUtilityDiagnostics)
            baySecondaryUtilities = baySecondaryUtilities with
                { Diagnostics = secondaryUtilityDiagnostics };
        VertexPositionColor[] approachBeamVertices =
            MegastationApproachBeamMeshBuilder.Build(interiorPresentation);
        int throatBoundaryFaces = topology.Faces.Count(face =>
            face.SpaceKind == MegastationBoundarySpaceKind.EntranceThroatBoundary);
        int interiorBoundaryFaces = topology.Faces.Count(face =>
            face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary);
        int cutoutFaceExpansion = bayFacilities.Diagnostics.CutoutCount * 8;
        interiorPlan = interiorPlan with
        {
            Diagnostics = interiorMesh.Diagnostics with
            {
                ThroatBoundaryFaceCount = throatBoundaryFaces,
                InteriorBoundaryFaceCount = interiorBoundaryFaces,
                InteriorStructuralVertexCount =
                    (throatBoundaryFaces + interiorBoundaryFaces + cutoutFaceExpansion) * 4,
                InteriorStructuralTriangleCount =
                    (throatBoundaryFaces + interiorBoundaryFaces + cutoutFaceExpansion) * 2,
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
            artificialLighting: artificialLighting,
            artificialOcclusion: artificialOcclusion,
            bayFacilities: bayFacilities);
        // Every face in this mesh is authoritative structural mass. Reuse the exact
        // emitted geometry so exterior, throat, bay shell, structural steps, and real
        // cutout replacements agree between visible and stellar-caster transforms.
        // Upload preparation still creates the existing single hull-caster GPU buffer.
        StationModuleMesh structuralShadowMesh = mesh;
        MegastationArtificialOcclusionDiagnostics artificialOcclusionDiagnostics =
            artificialOcclusion.Diagnostics(artificialLighting.Lights.Count);
        interiorPlan = interiorPlan with
        {
            Diagnostics = interiorPlan.Diagnostics with
            {
                ArtificialOccluderCount = artificialOcclusionDiagnostics.OccluderCount,
                ArtificialLightReceiverSampleCount = artificialOcclusionDiagnostics.ReceiverSampleCount,
                ArtificialLightVisibilityTestCount = artificialOcclusionDiagnostics.VisibilityTestCount,
                ArtificialLightBlockedVisibilityTestCount = artificialOcclusionDiagnostics.BlockedVisibilityTestCount,
                ArtificialLightBakeMilliseconds = artificialOcclusionDiagnostics.BakeMilliseconds,
            },
        };
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
            megaShelfPlan,
            bayStructuralTrusses,
            bayUtilities,
            baySecondaryUtilities,
            artificialLighting,
            landingDistrict,
            bayHabitation,
            bayFacilities,
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

    private static RectilinearMegastationMacroData PrepareMacroData(
        string persistenceId,
        MegastationPrototypeSettings settings,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        int rootSeed = MegastationSeed.Root(
            persistenceId,
            settings.SeedCompatibilityVersion);
        SliceGrid grid = SliceGrid.Create(
            settings,
            MegastationSeed.Derive(rootSeed, "slice-grid layout"));
        cancellationToken.ThrowIfCancellationRequested();

        StructuralOccupancy occupancy =
            new CuboidStructuralVolumeGenerator().Generate(grid);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        IReadOnlyList<SurfacePatch> patches = SurfacePatchFinder.FindPatches(occupancy);
        MegastationUrbanStyle style = MegastationUrbanStyle.Generate(rootSeed);
        IReadOnlyList<CornerRegionPlan> corners = CornerRegionGenerator.PlanCorners(
            grid, settings, style, rootSeed);
        CornerRegionGenerator.Apply(occupancy, corners);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<EdgeRegionPlan> edges = EdgeRegionGenerator.PlanEdges(
            grid, settings, style, corners, rootSeed);
        EdgeRegionGenerator.Apply(occupancy, edges);

        var faces = new List<UrbanGrowthResult>(6);
        foreach (SurfacePatch patch in patches.OrderBy(
                     patch => patch.Id,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MegastationPrototypeSettings faceSettings = MegastationFaceSettings.ForPatch(
                settings, style, grid, patch, rootSeed);
            int faceSeed = patch.Direction == settings.UrbanPatchNormal
                ? MegastationSeed.Derive(rootSeed, "district layout")
                : MegastationSeed.Derive(rootSeed, $"district layout:{patch.Id}");
            faces.Add(UrbanGrowth.Generate(
                occupancy, patch, faceSettings, faceSeed));
        }

        MegastationConnectivityReport validation =
            MegastationConnectivity.Validate(occupancy);
        stageStopwatch.Stop();
        long rawMassingMilliseconds = stageStopwatch.ElapsedMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();
        stageStopwatch.Restart();
        StructuralOccupancy hollowedOccupancy = occupancy.Clone();
        MegastationInteriorPlan interiorPlan = MegastationInteriorPlanner.PlanAndApply(
            hollowedOccupancy,
            rootSeed,
            cancellationToken);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(hollowedOccupancy);
        stageStopwatch.Stop();
        long entranceCarveMilliseconds = stageStopwatch.ElapsedMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        return new(
            persistenceId,
            rootSeed,
            settings,
            grid,
            occupancy,
            hollowedOccupancy,
            interiorPlan,
            patches,
            style,
            faces,
            edges,
            corners,
            validation,
            rawMassingMilliseconds,
            entranceCarveMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds);
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
            ReceivesMegastationInteriorHaze = true,
            GlassMesh = cpu.WindowGlassMesh,
            HullMaterialRanges = cpu.Mesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
        module.GlowLights.AddRange(cpu.LightPlan.Lights.Select(light => light.ToStationLightInfo()));
        module.GlowLights.AddRange(CreateInteriorGuidanceLights(cpu.InteriorPresentationPlan));
        if (cpu.InteriorPlan.ShelfLighting is { } shelfLighting)
        {
            module.GlowLights.AddRange(CreateShelfObstacleLights(shelfLighting));
            module.GlowLights.AddRange(CreateShelfFloodGlowLights(shelfLighting));
        }
        return module;
    }

    internal static PlacedModule CreateMacroPlacedModule(
        MegastationPrototypeMacroCpuResult cpu)
    {
        Vector3 bounds = new(
            cpu.StructuralData.Grid.Dimension(GridAxis.X),
            cpu.StructuralData.Grid.Dimension(GridAxis.Y),
            cpu.StructuralData.Grid.Dimension(GridAxis.Z));
        var definition = new StationModuleDefinition
        {
            Id = "megastation-macro-rectilinear",
            Category = "megastation-macro",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
        return new PlacedModule
        {
            Definition = definition,
            Transform = Matrix.Identity,
            Seed = cpu.StructuralData.RootSeed,
            ChamferDepth = 0f,
            AabbMin = bounds * -.5f,
            AabbMax = bounds * .5f,
            HullMesh = cpu.Mesh,
            HullShadowMesh = cpu.Mesh,
            HullMaterialRanges = cpu.Mesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
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
        var module = new PlacedModule
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
            ReceivesMegastationInteriorHaze = true,
            UsesDecorationVertexIllumination = true,
            UsesCoplanarStructuralOverlay = true,
            NativeInteriorDebugLines = MegastationInteriorDebug.BuildLines(
                cpu.InteriorPlan,
                cpu.BoundaryTopology,
                cpu.Grid),
            NativeApproachBeamVertices = cpu.ApproachBeamVertices,
            DecorationMaterialRanges = cpu.InteriorMesh.PrepareMaterialGroups()?.Ranges ?? [],
        };
        return module;
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

    internal static IEnumerable<StationLightInfo> CreateShelfObstacleLights(
        MegastationShelfLightingPlan lighting)
        => lighting.Beacons.Select(beacon => new StationLightInfo(
            // The depth-tested sprite must sit beyond the physical cap. Keeping it at
            // beacon.Position puts it inside the housing, so the fixture occludes its own glow.
            beacon.Position + beacon.Up * .40f,
            beacon.Colour,
            GlowType.WarningStrobe,
            .92f,
            beacon.Rate,
            beacon.Phase,
            LightPattern.Strobe)
        {
            PresentationSizeScale = 4f,
            PresentationMinimumSizePixels = 5f,
            PresentationMaximumSizePixels = 280f,
        });

    internal static IEnumerable<StationLightInfo> CreateShelfFloodGlowLights(
        MegastationShelfLightingPlan lighting)
        => lighting.FloodFixtures.Select(flood => new StationLightInfo(
            // Place the depth-tested glow just beyond the luminous face so the fixture
            // cannot occlude its own sprite. It remains naturally hidden by the shelf.
            flood.Centre + flood.Direction * .68f,
            Color.White,
            GlowType.ShelfWorkFlood,
            .78f,
            0f,
            0f,
            LightPattern.Continuous)
        {
            PresentationSizeScale = 4f,
            PresentationMinimumSizePixels = 24f,
            PresentationMaximumSizePixels = 640f,
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
