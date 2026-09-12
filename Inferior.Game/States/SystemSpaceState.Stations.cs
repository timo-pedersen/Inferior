using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Diagnostics;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

internal readonly record struct StationGlowDepthDecision(
    bool IsFrontFacing,
    float Facing,
    float AppliedBiasMeters,
    Vector3 BiasedCameraRelativePosition);

public sealed partial class SystemSpaceState
{
    private static void PublishMegastationPrototypeDiagnostics(
        MegastationPrototypeDiagnostics d,
        MegastationPrototypeSelectionMode mode)
    {
        DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
            $"Megastation H1 [{mode}] id={d.StationPersistenceId}; build={d.BuildIdentifier}; v={d.GeneratorVersion}; seedCompat={d.SeedCompatibilityVersion}; interior={d.InteriorAlgorithmVersion}; topoReg={d.TopologyRegularisationAlgorithmVersion}; boundary={d.BoundaryTopologyAlgorithmVersion}; chamfer={d.StructuralChamferAlgorithmVersion}; path={d.MeshPath}; " +
            $"seed={d.RootSeed}; slices={d.XSliceCount}x{d.YSliceCount}x{d.ZSliceCount}; " +
            $"cells={d.GridCellCount}; structural={d.StructuralOccupiedCellCount}; rawUrban={d.UrbanOccupiedCellCount}; regularised={d.RegularisedOccupiedCellCount}; repairs+={d.TopologyRepairAddedCellCount}; " +
            $"faces={d.UrbanizedFaceCount}; faceCells={d.FaceRegionOccupiedCellCount}; edgeCells={d.EdgeRegionOccupiedCellCount}; cornerCells={d.CornerRegionOccupiedCellCount}; " +
            $"districts={d.DistrictCount}; maxDepth={d.MaximumUrbanDepth}; rawComponents={d.ConnectedComponentsBeforeValidation}; regComponents={d.RegularisedConnectedComponents}; " +
            $"edgeCritical={d.EdgeCriticalConfigurationsBeforeRegularisation}->{d.EdgeCriticalConfigurationsAfterRegularisation}; vertexCritical={d.VertexCriticalConfigurationsBeforeRegularisation}->{d.VertexCriticalConfigurationsAfterRegularisation}; " +
            $"sealed={d.HasSealedCavity}->{d.RegularisedHasSealedCavity}; boundaryFaces={d.BoundaryFaceCount}; edges flat/convex/concave/invalid={d.FlatContinuationEdgeCount}/{d.ConvexExteriorEdgeCount}/{d.ConcaveExteriorEdgeCount}/{d.InvalidDiagonalEdgeCount}; " +
            $"vertices simple/straight/concave/complex/nonmanifold={d.SimpleConvexVertexCount}/{d.StraightConvexContinuationVertexCount}/{d.SimpleConcaveVertexCount}/{d.ComplexVertexCount}/{d.NonManifoldVertexCount}; " +
            $"eligible={d.EligibleChamferSegmentCount}; suppressed={d.SuppressedConvexSegmentCount}; runs={d.ChamferRunCount}/{d.SuppressedChamferRunCount}; renderedBevels={d.BevelQuadCount}; renderedCaps={d.CornerCapCount}; chamferSemantic taperOnly/nearZero/missingRetract={d.ChamferSemanticValidation.TaperOnlyRenderedRunCount}/{d.ChamferSemanticValidation.NearZeroAreaRenderedRunCount}/{d.ChamferSemanticValidation.MissingFaceRetractionRunCount}; quads={d.ExposedQuadCount}; " +
            $"tris={d.TriangleCount}; verts={d.VertexCount}; pages={d.MeshPageCount}; topoMs={d.BoundaryTopologyBuildMilliseconds}; meshMs={d.BoundaryMeshBuildMilliseconds}; genMs={d.GenerationMilliseconds}",
            SystemMessagePriority.NB));
    }

    private static void PublishBolonMegastationDiagnostics(BolonMegastationDiagnostics d)
    {
        if (d.AmbassadorBay is { } bay)
        {
            string report = $"[BolonAmbassadorBay] station={d.StationIdentity}; vessel={bay.VesselIndex}; face={bay.HostFaceIndex}; axis={bay.CornerAxis}; " +
                $"mouth={bay.MouthWidth:F1}x{bay.MouthHeight:F1}m; clear={bay.ClearWidth:F1}x{bay.ClearHeight:F1}m; " +
                $"chamfer={bay.VisibleChamferDepth:F1}m; outerReveal={bay.OuterRevealDepth:F1}m; throat={bay.ThroatLength:F1}m; bay={bay.BayWidth:F1}x{bay.BayHeight:F1}x{bay.BayLength:F1}m; " +
                $"rearPort={bay.RearPortWidth:F1}x{bay.RearPortHeight:F1}m; rearStub={bay.RearPortCorridorLength:F1}m; H1eBeams=4; " +
                $"centre={bay.MouthCenter}; outward={bay.Outward}; down={bay.Down}; approachClearance={bay.ApproachClearance:F1}m; " +
                $"triangles={d.AmbassadorTriangleCount}; collision=deferred; signature={bay.Signature}";
            System.Diagnostics.Debug.WriteLine(report);
            DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(report, SystemMessagePriority.NB));
        }
        DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
            $"[BolonMegastation] id={d.StationIdentity}; type={d.Archetype}; " +
            $"vessels={d.VesselCount} (anchor:{d.AnchorVesselCount},standard:{d.StandardVesselCount},secondary:{d.SecondaryVesselCount}); " +
            $"radius={d.MinimumVesselRadius:F1}-{d.MaximumVesselRadius:F1}m; " +
            $"graph=edges:{d.RelationshipCount},maxDegree:{d.MaximumGraphDegree},connector:{d.ConnectorRelationshipCount},direct:{d.DirectJoinRelationshipCount}; " +
            $"dimensions={d.OverallDimensions.X:F1}x{d.OverallDimensions.Y:F1}x{d.OverallDimensions.Z:F1}m; " +
            $"mesh={d.VertexCount}v/{d.TriangleCount}t/{d.MeshBytes}B (surface:{d.SurfaceTriangleCount},apertureStructure:{d.ApertureStructureTriangleCount}); " +
            $"surfaceHistory=regions:{d.SurfaceHistoryRegionCount},mature:{d.MatureRegionCount},polished:{d.PolishedRegionCount},brushed:{d.BrushedRegionCount},eroded:{d.ErodedRegionCount}; " +
            $"apertures=groups:{d.ApertureGroupCount},optical:{d.ApertureCount},band:{d.BandGroupCount} (4-9-4:{d.FourNineFourGroupCount}),compact:{d.CompactGroupCount},corner:{d.CornerFanGroupCount},edge:{d.EdgeRunGroupCount},sparse:{d.SparseFieldGroupCount},blankHex:{d.BlankEligibleHexFaceCount}; " +
            $"vents=groups:{d.VentGroupCount},1x:{d.OneXVentCount},2x:{d.TwoXVentCount},3x:{d.ThreeXVentCount},grilleTriangles:{d.VentGrilleTriangleCount}; " +
            $"palettes=ruby:{d.RubyGroupCount},violet:{d.VioletGroupCount},other:{d.RareOtherGroupCount}; " +
            $"pentagons=bare:{d.BarePentagonCount},collars:{d.ReinforcementCollarCount},iris:{d.IrisHatchCount},rosettes:{d.ApparatusRosetteCount}; " +
            $"pentagonTriangles=collar:{d.ReinforcementCollarTriangleCount},iris:{d.IrisHatchTriangleCount},rosette:{d.ApparatusRosetteTriangleCount}; " +
            $"apertureState=unlit:{d.UnlitApertureCount},dim:{d.DimApertureCount},luminous:{d.LuminousApertureCount},bright:{d.BrightApertureCount}; " +
            $"apertureGlass={d.ApertureGlassVertexCount}v/{d.ApertureGlassTriangleCount}t/{d.ApertureGlassBytes}B; " +
            $"planningMs={d.PlanningMilliseconds:F1}; meshMs={d.MeshBuildMilliseconds:F1}; " +
            $"structuralSignature={d.StructuralSignature}; surfaceSignature={d.SurfaceHistorySignature}; apertureSignature={d.ApertureSignature}; apertureVisualSignature={d.ApertureVisualSignature}; vocabularySignature={d.ApertureVocabularySignature}; pentagonalUtilitySignature={d.PentagonalUtilitySignature}",
            SystemMessagePriority.NB));
    }

    private static void PublishMegastationInteriorDiagnostics(
        string stationIdentity,
        MegastationInteriorDiagnostics d)
    {
        PublishStationResidencyMessage(
            $"[MegastationInterior] station={stationIdentity}; version={d.AlgorithmVersion}; " +
            $"count={d.InteriorCount}; entranceType={d.EntranceType}; portal={d.PortalDirection}; " +
            $"clear={d.PortalClearWidth:F1}x{d.PortalClearHeight:F1}m; " +
            $"bayWidth={d.BayClearWidth:F1}m; widthFraction={d.EntranceWidthFraction:P0}; " +
            $"largeClearance=upright:{d.LargeUprightVerticalClearance:F1}m," +
            $"rolled90:{d.LargeRolledVerticalClearance:F1}m; " +
            $"crown={d.CrownOuterWidth:F1}x{d.CrownOuterHeight:F1}m; " +
            $"entranceMargin={d.EntranceClearanceMargin:F1}m; " +
            $"assemblyClearedCells={d.EntranceAssemblyRemovedCellCount}; " +
            $"throatLength={d.ThroatLength:F1}m; " +
            $"flightClear={d.MainFlightClearSize.X:F1}x{d.MainFlightClearSize.Y:F1}x{d.MainFlightClearSize.Z:F1}m; " +
            $"protectedCells={d.ProtectedVoidCellCount}; removedCells={d.RemovedStructuralCellCount}; " +
            $"boundaryFaces=throat:{d.ThroatBoundaryFaceCount},interior:{d.InteriorBoundaryFaceCount}; " +
            $"structural={d.InteriorStructuralVertexCount}v/{d.InteriorStructuralTriangleCount}t; " +
            $"portal={d.PortalVisibleVertexCount}v/{d.PortalVisibleTriangleCount}t; " +
            $"portalCaster={d.PortalCasterVertexCount}v/{d.PortalCasterTriangleCount}t; " +
            $"guidance=portal:{d.PortalGuidanceElementCount},throat:{d.ThroatGuidanceElementCount}," +
            $"landmarks:{d.InteriorLandmarkElementCount},glows:{d.GuidanceGlowCount}; " +
            $"guidanceGeometry={d.GuidanceVisibleVertexCount}v/{d.GuidanceVisibleTriangleCount}t; " +
            $"constructedThroat=walls:{d.ThroatTubeWallElementCount},crown:{d.ThroatCrownElementCount}," +
            $"fixtures:{d.ThroatFixtureElementCount},ribs:{d.ThroatRibElementCount}," +
            $"casters:{d.ThroatCasterElementCount}; " +
            $"projectedEntrance={d.EntranceProjectionLength:F1}m; " +
            $"localSkyline={d.EntranceLocalSkylineHeight:F1}m; " +
            $"skylineFraction={d.EntranceProjectionHeightFraction:P0}; " +
            $"approachBeams={d.ApproachBeamCount}; fixtureParts={d.ApproachFixtureElementCount}; " +
            $"beamLength={d.ApproachBeamLength:F1}m; " +
            $"beamHalfAngle={d.ApproachBeamHalfAngleDegrees:F2}deg; " +
            $"beamGeometry={d.ApproachBeamVertexCount}v/{d.ApproachBeamTriangleCount}t; " +
            $"portalUp={d.EntrancePortalUp}; portalRight={d.EntrancePortalRight}; " +
            $"palette={d.EntrancePaletteIdentity}; precinctReservations={d.EntrancePrecinctReservationCount}; " +
            $"artificial=v{d.ArtificialLightAlgorithmVersion},sources:{d.ArtificialLightSourceCount}," +
            $"range:{d.ArtificialLightMinimumRange:F1}-{d.ArtificialLightMaximumRange:F1}m," +
            $"indirect:{d.ArtificialIndirectStrength:P0}@{d.ArtificialIndirectRangeScale:F2}x," +
            $"signature:{d.ArtificialLightSignature}; " +
            $"artificialOcclusion=occluders:{d.ArtificialOccluderCount}," +
            $"receivers:{d.ArtificialLightReceiverSampleCount}," +
            $"tests:{d.ArtificialLightVisibilityTestCount}," +
            $"blocked:{d.ArtificialLightBlockedVisibilityTestCount}," +
            $"bakeMs:{d.ArtificialLightBakeMilliseconds:F1}; " +
            $"megaShelves=v{d.MegaShelfAlgorithmVersion},accepted:{d.MegaShelfCount}/" +
            $"{d.MegaShelfCandidateCount}candidates," +
            $"rejects:{d.MegaShelfSupportRejectCount}support/" +
            $"{d.MegaShelfStructuralRejectCount}structure/" +
            $"{d.MegaShelfArrivalExclusionRejectCount}arrival/" +
            $"{d.MegaShelfOverlapRejectCount}overlap/" +
            $"{d.MegaShelfVerticalSeparationRejectCount}separation/" +
            $"{d.MegaShelfOperatingClearanceRejectCount}operating-clearance/" +
            $"{d.MegaShelfFullSpanFlightClearanceRejectCount}span-flight/" +
            $"{d.MegaShelfCompositionRejectCount}composition," +
            $"downgraded:{d.MegaShelfLandingDowngradeCount}," +
            $"families:{d.MegaShelfCantileverAcceptedCount}/{d.MegaShelfCantileverCandidateCount}cantilever/" +
            $"{d.MegaShelfCornerAcceptedCount}/{d.MegaShelfCornerCandidateCount}corner/" +
            $"{d.MegaShelfFullSpanAcceptedCount}/{d.MegaShelfFullSpanCandidateCount}full-span," +
            $"viable:{d.MegaShelfCornerViableCandidateCount}corner/" +
            $"{d.MegaShelfFullSpanViableCandidateCount}full-span," +
            $"bands:{d.MegaShelfLowAcceptedCount}/{d.MegaShelfLowCandidateCount}low/" +
            $"{d.MegaShelfMidAcceptedCount}/{d.MegaShelfMidCandidateCount}mid/" +
            $"{d.MegaShelfHighAcceptedCount}/{d.MegaShelfHighCandidateCount}high," +
            $"width:{d.MegaShelfMinimumWidth:F1}-{d.MegaShelfMaximumWidth:F1}m," +
            $"projection:{d.MegaShelfMinimumProjection:F1}-{d.MegaShelfMaximumProjection:F1}m," +
            $"thickness:{d.MegaShelfMinimumThickness:F1}-{d.MegaShelfMaximumThickness:F1}m," +
            $"geometry:{d.MegaShelfVisibleTriangleCount}t/" +
            $"{d.MegaShelfCasterTriangleCount}casterT," +
            $"markers:{d.MegaShelfMarkerCount}/" +
            $"{d.MegaShelfCornerMarkerCount}corner/" +
            $"{d.MegaShelfLowerEdgeMarkerCount}lower," +
            $"trusses:{d.MegaShelfTrussCount}" +
            $"({d.MegaShelfBoxTrussCount}box/" +
            $"{d.MegaShelfTriangularTrussCount}tri)," +
            $"trussGeometry:{d.MegaShelfTrussVisibleTriangleCount}t/" +
            $"{d.MegaShelfTrussCasterTriangleCount}casterT," +
            $"macro:{d.MegaShelfMacroLayout}," +
            $"bookcases:{d.MegaShelfBookcaseCount}/{d.MegaShelfBookcaseShelfCount}shelves," +
            $"macroRejects:{d.MegaShelfBookcaseInsufficientHeightRejectCount}height/" +
            $"{d.MegaShelfBookcaseArrivalRejectCount}arrival/" +
            $"{d.MegaShelfBookcaseWallContinuityRejectCount}wall/" +
            $"{d.MegaShelfBookcaseStructuralRejectCount}structure/" +
            $"{d.MegaShelfBookcaseSymmetryRejectCount}symmetry," +
            $"macroPlan:{d.MegaShelfMacroSummary}," +
            $"plans:{d.MegaShelfSummary},signature:{d.MegaShelfSignature}; " +
            $"shelfLighting=beacons:{d.MegaShelfObstacleBeaconCount}," +
            $"floodFixtures:{d.MegaShelfFloodFixtureCount}," +
            $"staticSources:{d.MegaShelfStaticWorkLightCount}," +
            $"signature:{d.MegaShelfLightingSignature}; " +
            $"bayStructuralTrusses=v{d.BayStructuralTrussAlgorithmVersion}," +
            $"fields:{d.BayStructuralTrussWallFieldCount}wall/" +
            $"{d.BayStructuralTrussCeilingFieldCount}ceiling," +
            $"runs:{d.BayStructuralTrussWallCount}wall/" +
            $"{d.BayStructuralTrussCeilingCount}ceiling," +
            $"rejects:{d.BayStructuralTrussHardConflictRejectCount}hard/" +
            $"{d.BayStructuralTrussOperationalRejectCount}operational/" +
            $"{d.BayStructuralTrussSupportRejectCount}support," +
            $"softWindowOverlaps:{d.BayStructuralTrussSoftWindowOverlapCount}," +
            $"geometry:{d.BayStructuralTrussVisibleTriangleCount}t/" +
            $"{d.BayStructuralTrussCasterTriangleCount}casterT," +
            $"plan:{d.BayStructuralTrussPlanningMilliseconds}ms," +
            $"layout:{d.BayStructuralTrussSummary}," +
            $"signature:{d.BayStructuralTrussSignature}; " +
            $"bayUtilities=v{d.BayUtilityAlgorithmVersion}," +
            $"walls:{d.BayUtilityActiveWallCount},networks:{d.BayUtilityNetworkCount}," +
            $"runs:{d.BayUtilityTrunkCount}trunk/{d.BayUtilityBranchCount}branch," +
            $"nodes:{d.BayUtilityJunctionBoxCount}junction/{d.BayUtilityServiceBoxCount}service," +
            $"cables:{d.BayUtilityCableCount},clamps:{d.BayUtilityClampCount}," +
            $"length:{d.BayUtilityTotalRunLength:F1}m," +
            $"trunks:{d.BayUtilityMinimumTrunkLength:F1}/" +
            $"{d.BayUtilityMedianTrunkLength:F1}/{d.BayUtilityMaximumTrunkLength:F1}m," +
            $"conflicts:{d.BayUtilityHardConflictRejectCount}hard/" +
            $"{d.BayUtilityWindowConflictScore}windowScore," +
            $"geometry:{d.BayUtilityVisibleVertexCount}v/{d.BayUtilityVisibleTriangleCount}t/" +
            $"{d.BayUtilityMajorCasterTriangleCount}casterT," +
            $"plan:{d.BayUtilityPlanningMilliseconds}ms," +
            $"layout:{d.BayUtilityWallSummary},signature:{d.BayUtilitySignature}; " +
            $"baySecondaryUtilities=v{d.BaySecondaryUtilityAlgorithmVersion}," +
            $"walls:{d.BaySecondaryUtilityActiveWallCount}," +
            $"installations:{d.BaySecondaryUtilityInstallationCount}," +
            $"pipes:{d.BaySecondaryUtilityPipeCount}/" +
            $"{d.BaySecondaryUtilityTotalPipeLength:F1}m," +
            $"supports:{d.BaySecondaryUtilityPipeSupportCount}," +
            $"vents:{d.BaySecondaryUtilityLouverBankCount}louver/" +
            $"{d.BaySecondaryUtilityProjectingDuctCount}duct," +
            $"access:{d.BaySecondaryUtilityHatchCount}hatch/" +
            $"{d.BaySecondaryUtilityLadderCount}ladder," +
            $"conflicts:{d.BaySecondaryUtilityHardConflictRejectCount}hard/" +
            $"{d.BaySecondaryUtilityWindowConflictScore}windowScore," +
            $"geometry:{d.BaySecondaryUtilityVisibleTriangleCount}t/" +
            $"{d.BaySecondaryUtilityMajorCasterTriangleCount}casterT," +
            $"plan:{d.BaySecondaryUtilityPlanningMilliseconds}ms," +
            $"layout:{d.BaySecondaryUtilityWallSummary}," +
            $"signature:{d.BaySecondaryUtilitySignature}; " +
            $"landingDistrict=sites:{d.LandingDistrictSiteCount}" +
            $"({d.LandingDistrictSmallSiteCount}S/{d.LandingDistrictMediumSiteCount}M/" +
            $"{d.LandingDistrictLargeSiteCount}L),pads:{d.LandingDistrictPadCount}," +
            $"standard:{d.LandingDistrictStandardPadCount},large:{d.LandingDistrictLargePadCount}," +
            $"capacityDeficit:{d.LandingDistrictPadCapacityDeficit}/" +
            $"{d.LandingDistrictLargePadCapacityDeficit}L," +
            $"services:{d.LandingDistrictServiceBuildingCount},lights:{d.LandingDistrictLightCount}," +
            $"loadingAreas:{d.LandingDistrictLoadingAreaCount},containers:{d.LandingDistrictContainerCount}," +
            $"keepClear:{d.LandingDistrictKeepClearZoneCount}," +
            $"mesh:{d.LandingDistrictVisibleVertexCount}v/{d.LandingDistrictVisibleTriangleCount}t," +
            $"caster:{d.LandingDistrictShadowVertexCount}v/{d.LandingDistrictShadowTriangleCount}t," +
            $"apronReceivers:{d.LandingDistrictApronReceiverVertexCount}v/" +
            $"{d.LandingDistrictApronReceiverTriangleCount}t@" +
            $"{d.LandingDistrictApronReceiverMaximumSpacing:F2}m," +
            $"structuralRejects:{d.LandingDistrictStructuralEnvelopeRejectCount} envelope/" +
            $"{d.LandingDistrictStructuralComponentRejectCount} component," +
            $"structuralRejections:{d.LandingDistrictStructuralRejectionSummary}," +
            $"landingSurfaces:{d.LandingDistrictLandingSurfaceCount}," +
            $"shelfSites:{d.LandingDistrictShelfSiteCount}," +
            $"shelfUtilization:{d.LandingDistrictOccupiedShelfCount}/" +
            $"{d.LandingDistrictLandingCapableShelfCount}," +
            $"shelfLayouts:{d.LandingDistrictFreestandingShelfSiteCount}free/" +
            $"{d.LandingDistrictWallIntegratedShelfSiteCount}integrated," +
            $"deliberatelyEmpty:{d.LandingDistrictDeliberatelyEmptyShelfCount}," +
            $"shelfPlan:{d.LandingDistrictShelfUtilizationSummary}," +
            $"surfacePlans:{d.LandingDistrictLandingSurfaceSummary}," +
            $"sitePlans:{d.LandingDistrictSiteSummary}," +
            $"signature:{d.LandingDistrictSignature}; " +
            $"bayHabitation=v{d.BayHabitationAlgorithmVersion}," +
            $"walls:{d.BayHabitationActiveWallCount}active/{d.BayHabitationBlankWallCount}blank," +
            $"regions:{d.BayHabitationRegionCount},groups:{d.BayHabitationWindowGroupCount}," +
            $"windows:{d.BayHabitationWindowCount}" +
            $"({d.BayHabitationLitWindowCount}lit/{d.BayHabitationDimWindowCount}dim/" +
            $"{d.BayHabitationDarkWindowCount}dark)," +
            $"mesh:{d.BayHabitationMeshVertexCount}v/{d.BayHabitationMeshTriangleCount}t," +
            $"walls:{d.BayHabitationWallSummary},signature:{d.BayHabitationSignature}; " +
            $"bayFacilities=v{d.BayFacilityAlgorithmVersion}," +
            $"regions:{d.BayFacilityEnhancedRegionCount}enhanced/{d.BayFacilityPlainRegionCount}plain," +
            $"archetypes:{d.BayFacilityRecessedCount}recessed/{d.BayFacilityGalleryCount}gallery/" +
            $"{d.BayFacilityEmbeddedCount}embedded/{d.BayFacilityServiceApertureCount}aperture," +
            $"L3c:{d.BayFacilityDeepRecessCount}deep/{d.BayFacilityOpenGalleryCount}open/" +
            $"{d.BayFacilityMultiStoreyGalleryCount}multi/{d.BayFacilityGalleryStoreyCount}storeys," +
            $"secondary:{d.BayFacilitySecondaryFormCount},parts:{d.BayFacilityStructuralPartCount}," +
            $"railings:{d.BayFacilityRailingPartCount},occluders:{d.BayFacilityOccluderPartCount}," +
            $"cutouts:{d.BayFacilityCutoutCount},facilityWindows:{d.BayFacilityWindowCount}," +
            $"balconies:{d.BayFacilityBalconyCount}," +
            $"reservations:{d.BayFacilityReservationCount}," +
            $"rejects:{d.BayFacilityReservationRejectCount}reservation/" +
            $"{d.BayFacilityCutoutValidationRejectCount}cutout," +
            $"recessLights:{d.BayFacilityArtificialLightCount}," +
            $"plan:{d.BayFacilityPlanningMilliseconds}ms," +
            $"projection:{d.BayFacilityMaximumProjection:F1}m," +
            $"mesh:{d.BayFacilityMeshVertexCount}v/{d.BayFacilityMeshTriangleCount}t," +
            $"shadow:{d.BayFacilityShadowVertexCount}v/{d.BayFacilityShadowTriangleCount}t," +
            $"signature:{d.BayFacilitySignature}; " +
            $"planningMs={d.PlanningMilliseconds}; meshMs={d.MeshBuildMilliseconds}; " +
            $"signature={d.Signature}",
            SystemMessagePriority.NB);
    }

    private static void PublishMegastationSemanticZoningDiagnostics(
        string stationIdentity,
        MegastationSemanticZoningDiagnostics diagnostics)
    {
        string roles = string.Join("; ", Enum.GetValues<MegastationZoneRole>().Select(role =>
        {
            float percentage = diagnostics.TotalSurfaceArea <= 0f
                ? 0f
                : diagnostics.AreaByRole.GetValueOrDefault(role) / diagnostics.TotalSurfaceArea * 100f;
            return $"{role}={percentage:F1}%";
        }));
        DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
            $"[MegastationZoning] station={stationIdentity}; zones={diagnostics.ZoneCount}; " +
            $"surfaceFaces={diagnostics.SurfaceFaceCount}; surfaceArea={diagnostics.TotalSurfaceArea:F1}m2; " +
            $"zoningMs={diagnostics.ZoningMilliseconds}; {roles}; " +
            $"anchors core/faceDistricts/edges/corners={diagnostics.CoreAnchorCount}/" +
            $"{diagnostics.FaceDistrictAnchorCount}/{diagnostics.EdgeAnchorCount}/{diagnostics.CornerAnchorCount}; " +
            $"repairFragments={diagnostics.RepairFragmentCount}; fragmentsMerged={diagnostics.FragmentsMerged}",
            SystemMessagePriority.NB));
    }

    private static void PublishMegastationWindowDiagnostics(
        string stationIdentity,
        MegastationWindowDiagnostics diagnostics)
    {
        string message = $"[MegastationWindows] station={stationIdentity}; " +
            $"habitationZones={diagnostics.HabitationZoneCount}; " +
            $"eligibleRegions={diagnostics.EligibleRegionCount}; activeRegions={diagnostics.ActiveRegionCount}; " +
            $"darkRegions={diagnostics.DarkRegionCount}; blocks={diagnostics.BlockCount}; " +
            $"windows={diagnostics.WindowCount}; lit={diagnostics.LitWindowCount}; " +
            $"dim={diagnostics.DimWindowCount}; dark={diagnostics.DarkWindowCount}; " +
            $"absentCandidates={diagnostics.AbsentCandidateCount}; " +
            $"eligibleArea={diagnostics.EligibleHabitationWallArea:F1}m2; " +
            $"activeArea={diagnostics.ActiveWindowArea:F1}m2; " +
            $"meshVertices={diagnostics.MeshVertexCount}; meshTriangles={diagnostics.MeshTriangleCount}; " +
            $"meshBytes={diagnostics.MeshBytes}; planningMs={diagnostics.PlanningMilliseconds}; " +
            $"meshBuildMs={diagnostics.MeshBuildMilliseconds}";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    private static void PublishMegastationLightingDiagnostics(
        string stationIdentity,
        MegastationLightingDiagnostics diagnostics)
    {
        string message = $"[MegastationLighting] station={stationIdentity}; " +
            $"industrial=zones:{diagnostics.IndustrialZoneCount},activeRegions:{diagnostics.IndustrialActiveRegionCount}," +
            $"clusters:{diagnostics.IndustrialClusterCount},lights:{diagnostics.IndustrialLightCount}," +
            $"eligibleAreaM2:{diagnostics.IndustrialEligibleArea:F0},lightsPer1000M2:" +
            $"{LightsPer1000SquareMetres(diagnostics.IndustrialLightCount, diagnostics.IndustrialEligibleArea):F3}; " +
            $"logistics=zones:{diagnostics.LogisticsZoneCount},activeRegions:{diagnostics.LogisticsActiveRegionCount}," +
            $"clusters:{diagnostics.LogisticsClusterCount},lights:{diagnostics.LogisticsLightCount}," +
            $"eligibleAreaM2:{diagnostics.LogisticsEligibleArea:F0},lightsPer1000M2:" +
            $"{LightsPer1000SquareMetres(diagnostics.LogisticsLightCount, diagnostics.LogisticsEligibleArea):F3}; " +
            $"utilities=zones:{diagnostics.UtilitiesZoneCount},activeRegions:{diagnostics.UtilitiesActiveRegionCount}," +
            $"clusters:{diagnostics.UtilitiesClusterCount},lights:{diagnostics.UtilitiesLightCount}," +
            $"eligibleAreaM2:{diagnostics.UtilitiesEligibleArea:F0},lightsPer1000M2:" +
            $"{LightsPer1000SquareMetres(diagnostics.UtilitiesLightCount, diagnostics.UtilitiesEligibleArea):F3}; " +
            $"strategic=zones:{diagnostics.StrategicZoneCount},activeRegions:{diagnostics.StrategicActiveRegionCount}," +
            $"clusters:{diagnostics.StrategicClusterCount},lights:{diagnostics.StrategicLightCount}," +
            $"eligibleAreaM2:{diagnostics.StrategicEligibleArea:F0},lightsPer1000M2:" +
            $"{LightsPer1000SquareMetres(diagnostics.StrategicLightCount, diagnostics.StrategicEligibleArea):F3}; " +
            $"totalClusters={diagnostics.ClusterCount}; totalLights=" +
            $"{diagnostics.IndustrialLightCount + diagnostics.LogisticsLightCount + diagnostics.UtilitiesLightCount + diagnostics.StrategicLightCount}; " +
            $"steady={diagnostics.SteadyLightCount}; " +
            $"animated={diagnostics.AnimatedLightCount}; planningMs={diagnostics.PlanningMilliseconds}";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    private static float LightsPer1000SquareMetres(int lightCount, float area)
        => area <= 0f ? 0f : lightCount * 1000f / area;

    private static void PublishMegastationAttachmentDiagnostics(
        string stationIdentity,
        MegastationAttachmentDiagnostics diagnostics)
    {
        string families = diagnostics.ModuleFamilyCounts.Count == 0
            ? "none"
            : string.Join(',', diagnostics.ModuleFamilyCounts.Select(pair =>
                $"{pair.Key}:{pair.Value}"));
        string message = $"[MegastationAttachments] station={stationIdentity}; " +
            $"candidates={diagnostics.CandidateSurfaceCount}; selected={diagnostics.SelectedCandidateCount}; " +
            $"placed={diagnostics.PlacedModuleCount}; " +
            $"rejected=support:{diagnostics.RejectedSupportCount},clearance:{diagnostics.RejectedClearanceCount},semantic:{diagnostics.RejectedSemanticCount}; " +
            $"roles=habitation:{diagnostics.HabitationCount},industrial:{diagnostics.IndustrialCount}," +
            $"logistics:{diagnostics.LogisticsCount},utilities:{diagnostics.UtilitiesCount},strategic:{diagnostics.StrategicCount}; " +
            $"families={families}; suppressed=windows:{diagnostics.SuppressedWindowCount},lights:{diagnostics.SuppressedLightCount}; " +
            $"planningMs={diagnostics.PlanningMilliseconds}; clearanceMs={diagnostics.ClearanceMilliseconds}";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    private static void PublishMegastationInfrastructureDiagnostics(
        string stationIdentity,
        MegastationInfrastructureDiagnostics diagnostics,
        double visibleUploadMilliseconds,
        double shadowUploadMilliseconds,
        int ownedTextures,
        int gpuBuffers,
        long uploadedResourceGpuBytes,
        StationShadowGpuParticipation shadowParticipation)
    {
        string roles = string.Join(',', Enum.GetValues<MegastationZoneRole>()
            .Where(role => role != MegastationZoneRole.Structural)
            .Select(role =>
            {
                MegastationInfrastructureRoleDiagnostics d = diagnostics.ByRole[role];
                return $"{role}:clusters:{d.ClusterCount},housings:{d.HousingCount}," +
                    $"vents:{d.VentCount},tanks:{d.TankCount}";
            }));
        string casterFamilies = string.Join(',', diagnostics.ShadowByFamily.Select(family =>
            $"{family.Family}:policy:{family.Policy},instances:{family.ShadowCastingInstanceCount}/" +
            $"{family.VisibleInstanceCount},visibleTriangles:{family.VisibleTriangleCount}," +
            $"caster:{family.CasterVertexCount}v/{family.CasterTriangleCount}t"));
        string message = $"[MegastationInfrastructure] station={stationIdentity}; " +
            $"candidateArea={diagnostics.CandidateArea:F0}; activeArea={diagnostics.ActiveArea:F0}; " +
            $"regions={diagnostics.CandidateRegionCount}; cells={diagnostics.CandidateCellCount}; " +
            $"clusters={diagnostics.ClusterCount}; primitives={diagnostics.PrimitiveCount}; " +
            $"composition=independent:{diagnostics.IndependentPlacementCount}," +
            $"edge:{diagnostics.ChannelEdgePlacementCount},junction:{diagnostics.ChannelNodePlacementCount}," +
            $"endpoint:{diagnostics.ChannelEndpointPlacementCount}," +
            $"rejectedChannelAware:{diagnostics.RejectedChannelAwareAttemptCount}; " +
            $"housings={diagnostics.HousingCount}; vents={diagnostics.VentCount}; tanks={diagnostics.TankCount}; " +
            $"roles={roles}; rejects=exactMask:{diagnostics.ExactMaskRejectCount}," +
            $"g1:{diagnostics.G1RejectCount},windows:{diagnostics.WindowRejectCount}," +
            $"lights:{diagnostics.LightRejectCount},spacing:{diagnostics.SpacingRejectCount}," +
            $"topology:{diagnostics.TopologyUnsuitableCount},density:{diagnostics.RoleDensityRejectCount}," +
            $"stationCap:{diagnostics.StationCapRejectCount},zoneCap:{diagnostics.ZoneCapRejectCount}; " +
            $"visibleVertices={diagnostics.VisibleVertexCount}; visibleTriangles={diagnostics.VisibleTriangleCount}; " +
            $"visibleBytes={diagnostics.VisibleMeshBytes}; shadowVertices={diagnostics.ShadowVertexCount}; " +
            $"shadowTriangles={diagnostics.ShadowTriangleCount}; shadowBytes={diagnostics.ShadowMeshBytes}; " +
            $"shadowFamilies={casterFamilies}; " +
            $"planningMs={diagnostics.PlanningMilliseconds}; meshBuildMs={diagnostics.MeshBuildMilliseconds}; " +
            $"visibleUploadMs={visibleUploadMilliseconds:F1}; shadowUploadMs={shadowUploadMilliseconds:F1}; " +
            $"gpuShadow=uploaded:{shadowParticipation.GpuCasterUploaded}," +
            $"traversed:{shadowParticipation.ModuleInShadowTraversal},fitBounds:{shadowParticipation.FitBoundsUploaded}," +
            $"{shadowParticipation.GpuCasterVertices}v/{shadowParticipation.GpuCasterTriangles}t; " +
            $"ownedTextures={ownedTextures}; gpuBuffers={gpuBuffers}; " +
            $"uploadedResourceGpuBytes={uploadedResourceGpuBytes}";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    private static void PublishMegastationMegaGreebleDiagnostics(
        string stationIdentity,
        MegastationMegaGreebleDiagnostics diagnostics,
        double visibleUploadMilliseconds,
        double shadowUploadMilliseconds,
        int ownedTextures,
        int gpuBuffers,
        long uploadedResourceGpuBytes,
        StationShadowGpuParticipation shadowParticipation)
    {
        string families = string.Join(',', diagnostics.ByFamily.Select(pair =>
            $"{pair.Key}:regions:{pair.Value.EligibleRegionCount},area:{pair.Value.EligibleArea:F0}," +
            $"candidates:{pair.Value.CandidateCount},accepted:{pair.Value.AcceptedCount}," +
            $"rejects:mask:{pair.Value.ExactMaskRejectCount}/g1:{pair.Value.G1RejectCount}/" +
            $"windows:{pair.Value.WindowRejectCount}/lights:{pair.Value.LightRejectCount}/" +
            $"g2:{pair.Value.G2RejectCount}/other:{pair.Value.MegaGreebleRejectCount}/" +
            $"suitability:{pair.Value.SuitabilityRejectCount}/clearance:{pair.Value.OutwardClearanceRejectCount}/" +
            $"density:{pair.Value.DensityRejectCount}/cap:{pair.Value.CapRejectCount}"));
        string casterFamilies = string.Join(',', diagnostics.ShadowByFamily.Select(family =>
            $"{family.Family}:policy:{family.Policy},instances:{family.ShadowCastingInstanceCount}/" +
            $"{family.VisibleInstanceCount},visibleTriangles:{family.VisibleTriangleCount}," +
            $"caster:{family.CasterVertexCount}v/{family.CasterTriangleCount}t"));
        string message = $"[MegastationMegaGreeble] station={stationIdentity}; families={families}; " +
            $"solar=surface:{diagnostics.SolarSurfaceArrayCount},radial:{diagnostics.SolarRadialWingCount}," +
            $"single:{diagnostics.SolarSingleWingCount},double:{diagnostics.SolarDoubleWingCount}," +
            $"broad:{diagnostics.SolarBroadCollectorCount},field:{diagnostics.SolarSmallFieldCount}," +
            $"length:{diagnostics.SolarMinimumLength:F1}/{diagnostics.SolarMedianLength:F1}/{diagnostics.SolarMaximumLength:F1}m; " +
            $"radialHeight:{diagnostics.RadialWingMinimumHeight:F1}/{diagnostics.RadialWingMedianHeight:F1}/{diagnostics.RadialWingMaximumHeight:F1}m; " +
            $"radialWidth:{diagnostics.RadialWingMinimumWidth:F1}/{diagnostics.RadialWingMedianWidth:F1}/{diagnostics.RadialWingMaximumWidth:F1}m; " +
            $"radialFolds:radial:{diagnostics.RadialFoldOrientationCount}," +
            $"transverse:{diagnostics.TransverseFoldOrientationCount}; " +
            $"dish=supported:{diagnostics.SupportedDishCount},surface:{diagnostics.SurfaceMountedDishCount}," +
            $"diameter:{diagnostics.DishMinimumDiameter:F1}/{diagnostics.DishMedianDiameter:F1}/{diagnostics.DishMaximumDiameter:F1}m; " +
            $"visible={diagnostics.VisibleVertexCount}v/{diagnostics.VisibleTriangleCount}t/{diagnostics.VisibleMeshBytes}B; " +
            $"shadow={diagnostics.ShadowVertexCount}v/{diagnostics.ShadowTriangleCount}t/{diagnostics.ShadowMeshBytes}B; " +
            $"shadowFamilies={casterFamilies}; " +
            $"planningMs={diagnostics.PlanningMilliseconds}; meshBuildMs={diagnostics.MeshBuildMilliseconds}; " +
            $"uploadMs=visible:{visibleUploadMilliseconds:F1},shadow:{shadowUploadMilliseconds:F1}; " +
            $"gpuShadow=uploaded:{shadowParticipation.GpuCasterUploaded}," +
            $"traversed:{shadowParticipation.ModuleInShadowTraversal},fitBounds:{shadowParticipation.FitBoundsUploaded}," +
            $"{shadowParticipation.GpuCasterVertices}v/{shadowParticipation.GpuCasterTriangles}t; " +
            $"ownedTextureDelta={diagnostics.OwnedTextureDelta}; gpuBufferDelta={diagnostics.GpuBufferDelta}; " +
            $"packageOwnedTextures={ownedTextures}; packageGpuBuffers={gpuBuffers}; uploadedResourceGpuBytes={uploadedResourceGpuBytes}; " +
            $"signature={diagnostics.PlanSignature}; largest={diagnostics.LargestInstanceIdentity}:" +
            $"{diagnostics.LargestInstanceWidth:F1}x{diagnostics.LargestInstanceLength:F1}x{diagnostics.LargestInstanceProtrusion:F1}m";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    private static void PublishMegastationFabricDiagnostics(
        string stationIdentity, MegastationFabricDiagnostics d,
        double visibleUploadMilliseconds, double shadowUploadMilliseconds,
        int ownedTextures, int gpuBuffers, long uploadedResourceGpuBytes,
        StationShadowGpuParticipation shadow)
    {
        string archetypes = string.Join(',', d.ByArchetype.Select(x => $"{x.Key}:{x.Value}"));
        string roles = string.Join(',', d.ByRole.Select(x => $"{x.Key}:{x.Value}"));
        string patterns = string.Join(',', d.ByPattern.Select(x => $"{x.Key}:{x.Value}"));
        string dense = string.Join('|', d.DensestRegions.Select(x =>
            $"{x.Role}:{x.Direction}:{x.StructureCount}@{x.Centre.X:F0},{x.Centre.Y:F0},{x.Centre.Z:F0}"));
        PublishStationResidencyMessage(
            $"[MegastationFabric] station={stationIdentity}; area={d.EligibleArea:F0}; " +
            $"regions={d.EligibleRegionCount}; candidates={d.CandidateCount}; accepted={d.AcceptedCount}; " +
            $"composition=independent:{d.IndependentStructureCount},row:{d.ChannelRowStructureCount}," +
            $"cluster:{d.ChannelClusterStructureCount},junction:{d.ChannelNodeStructureCount}," +
            $"endpoint:{d.ChannelEndpointStructureCount}," +
            $"rejectedChannelAware:{d.RejectedChannelAwareAttemptCount}; " +
            $"archetypes={archetypes}; roles={roles}; patterns={patterns}; " +
            $"sizeWxLxH={d.MinimumWidth:F1}/{d.MedianWidth:F1}/{d.MaximumWidth:F1}x" +
            $"{d.MinimumLength:F1}/{d.MedianLength:F1}/{d.MaximumLength:F1}x" +
            $"{d.MinimumHeight:F1}/{d.MedianHeight:F1}/{d.MaximumHeight:F1}m; " +
            $"rejects=mask:{d.ExactMaskRejectCount},g1:{d.G1RejectCount},windows:{d.WindowRejectCount}," +
            $"lights:{d.LightRejectCount},g2:{d.G2RejectCount},mega:{d.MegaGreebleRejectCount}," +
            $"self:{d.SelfRejectCount},density:{d.DensityRejectCount},structure:{d.StructuralCollisionRejectCount}; " +
            $"visible={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
            $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
            $"timing=plan:{d.PlanningMilliseconds}ms,mesh:{d.MeshBuildMilliseconds}ms," +
            $"visibleUpload:{visibleUploadMilliseconds:F1}ms,shadowUpload:{shadowUploadMilliseconds:F1}ms; " +
            $"gpuShadow=uploaded:{shadow.GpuCasterUploaded},traversed:{shadow.ModuleInShadowTraversal}," +
            $"fitBounds:{shadow.FitBoundsUploaded},{shadow.GpuCasterVertices}v/{shadow.GpuCasterTriangles}t; " +
            $"ownedTextureDelta={d.OwnedTextureDelta}; gpuBufferDelta={d.GpuBufferDelta}; " +
            $"packageOwnedTextures={ownedTextures}; packageGpuBuffers={gpuBuffers}; " +
            $"uploadedResourceGpuBytes={uploadedResourceGpuBytes}; dense={dense}; signature={d.PlanSignature}",
            SystemMessagePriority.NB);
    }

    private static void PublishMegastationServiceChannelDiagnostics(
        string stationIdentity, MegastationServiceChannelDiagnostics d,
        double visibleUploadMilliseconds, double shadowUploadMilliseconds,
        int ownedTextures, int gpuBuffers, long uploadedResourceGpuBytes,
        StationShadowGpuParticipation shadow)
    {
        string roles = string.Join(',', d.ByRole.Select(pair => $"{pair.Key}:{pair.Value}"));
        PublishStationResidencyMessage(
            $"[MegastationServiceChannels] station={stationIdentity}; area={d.EligibleArea:F0}; " +
            $"regions={d.EligibleRegionCount}; candidates={d.CandidateSurfaceCount}; networks={d.NetworkSurfaceCount}; " +
            $"primary={d.PrimaryTrunkCount}; secondary={d.SecondaryBranchCount}; segments={d.RunSegmentCount}; " +
            $"nodes=turn:{d.TurnCount},t:{d.TJunctionCount},four:{d.FourWayJunctionCount},dead:{d.DeadEndCount}; " +
            $"coveredNodes=t:{d.CoveredTJunctionCount},minorT:{d.UncoveredTJunctionCount},four:{d.CoveredFourWayJunctionCount}; " +
            $"length={d.TotalChannelLength:F0}m; primaryLength={d.MinimumPrimaryLength:F1}/{d.MedianPrimaryLength:F1}/{d.MaximumPrimaryLength:F1}m; " +
            $"bridges={d.BridgeCount}; roles={roles}; " +
            $"utilization=surfaces:{d.ChannelBearingSurfaceCount}," +
            $"runsG2:{d.RunsWithAdjacentG2Count},runsFabric:{d.RunsWithAdjacentFabricCount}," +
            $"junctions:{d.JunctionsWithDevelopmentCount},endpoints:{d.EndpointsWithDevelopmentCount}; " +
            $"rejects=mask:{d.ExactMaskRejectCount},g1:{d.G1RejectCount},windows:{d.WindowRejectCount}," +
            $"lights:{d.LightRejectCount},g2:{d.G2RejectCount},mega:{d.MegaGreebleRejectCount}," +
            $"fabric:{d.FabricRejectCount},parallel:{d.ParallelClearanceRejectCount}," +
            $"density:{d.DensityRejectCount},cap:{d.CapRejectCount}; " +
            $"visible={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
            $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
            $"coveredNodeGeometry={d.CoveredNodeVisibleVertexCount}v/{d.CoveredNodeVisibleTriangleCount}t," +
            $"caster:{d.CoveredNodeShadowVertexCount}v/{d.CoveredNodeShadowTriangleCount}t; " +
            $"materials={d.MaterialRangeCount}; timing=plan:{d.PlanningMilliseconds}ms," +
            $"mesh:{d.MeshBuildMilliseconds}ms,visibleUpload:{visibleUploadMilliseconds:F1}ms," +
            $"shadowUpload:{shadowUploadMilliseconds:F1}ms; " +
            $"gpuShadow=uploaded:{shadow.GpuCasterUploaded},traversed:{shadow.ModuleInShadowTraversal}," +
            $"fitBounds:{shadow.FitBoundsUploaded},{shadow.GpuCasterVertices}v/{shadow.GpuCasterTriangles}t; " +
            $"ownedTextureDelta={d.OwnedTextureDelta}; gpuBufferDelta={d.GpuBufferDelta}; " +
            $"packageOwnedTextures={ownedTextures}; packageGpuBuffers={gpuBuffers}; " +
            $"uploadedResourceGpuBytes={uploadedResourceGpuBytes}; signature={d.PlanSignature}",
            SystemMessagePriority.NB);
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Station drawing ───────────────────────────────────────────────────────

    // Brief S2c-2: derivative bump strength, locked after Timo's in-engine A/B — Whisper
    // (0.3) confirmed as the right amount ("looking swell now... the others are too
    // strong"); Off/Subtle/Default/Strong presets and the J-key runtime cycle are removed
    // now that the value is settled, not deferred as future tuning.
    private const float StationBumpStrength = 0.3f;
    // The 10m diagnostic established that sprite scale, not bias magnitude, was the
    // decisive visibility failure. Both 2m and 0.5m clipped correctly at shallow
    // angles, but 2m was visually preferred and is retained. This changes only
    // submitted sprite depth, never the
    // planned or projected light position; rear-facing lights are rejected before bias.
    internal const float MegastationGlowCameraDepthBiasMeters = 2f;
    // Nova's original 6px floor was imperceptible; the 72px diagnostic established
    // that the presentation path was sound. Twenty-five pixels is the requested
    // production-scale follow-up, retaining each light's normal colour and intensity.
    internal const float MegastationGlowSizePixels = 25f;
    private int _megastationGlowFrameVisibleCount;
    private int _megastationGlowFrameSubmittedCount;
    private double _megastationGlowFrameProjectionAndSubmissionMilliseconds;
    private double _nextMegastationGlowFrameDiagnosticSeconds;

    private static float StationPhysicalRadius(Galaxy.Station s) => s.Size switch
    {
        Galaxy.StationSize.Small  =>  250f,
        Galaxy.StationSize.Medium =>  800f,
        Galaxy.StationSize.Large  => 2500f,
        _                         =>  250f,
    };

    private IEnumerable<(Galaxy.Station station, DVec3 universePosition)> ResidentStationEntries()
    {
        if (TryGetResidentStation(out _, out Galaxy.Station station, out DVec3 position))
            yield return (station, position);
    }

    private InteriorHazeParameters ResolveMegastationInteriorHaze(
        Galaxy.Station station,
        DVec3 universePosition,
        bool? enabledOverride = null)
    {
        if (ResidentStationVisual?.MegastationInterior is not { } interior)
            return InteriorHazeParameters.Disabled;

        DVec3 cameraOffset = _camera.UniversePosition - universePosition;
        var orientation = station.GetOrientation(_gameTimeSeconds);
        var stationRotation = new Quaternion(
            orientation.X, orientation.Y, orientation.Z, orientation.W);
        Vector3 cameraStationLocal = Vector3.Transform(
            cameraOffset.ToVector3(),
            Quaternion.Inverse(stationRotation));
        return MegastationInteriorHaze.Resolve(
            cameraStationLocal,
            interior,
            enabledOverride
                ?? (_megastationInteriorHazeStrength != MegastationInteriorHazeStrength.Off),
            MegastationInteriorHaze.MaximumBlendFor(_megastationInteriorHazeStrength));
    }

    private float ProbeMegastationInteriorHazeActivation()
    {
        if (!TryGetResidentStation(out _, out Galaxy.Station station, out DVec3 position))
            return 0f;
        return ResolveMegastationInteriorHaze(station, position, enabledOverride: true).Activation;
    }

    private bool ResidentVisualIntersectsDepthTier(DetailLevel level)
    {
        if (!TryGetResidentStation(
            out StationVisualPackage visual,
            out _,
            out DVec3 stationPosition))
            return false;

        double centre = (stationPosition - _camera.UniversePosition).Length;
        double nearest = Math.Max(centre - visual.RenderBoundsRadiusMeters, 0.0);
        double farthest = centre + visual.RenderBoundsRadiusMeters;
        return level switch
        {
            DetailLevel.Full => nearest <= NearTierFar && farthest >= NearTierNear,
            DetailLevel.Medium => nearest <= MidTierFar && farthest >= MidTierNear,
            _ => farthest >= MidTierFar,
        };
    }

    private void DrawStations(DetailLevel level)
    {
        if (_stationPositions.Count == 0 || _meshRenderer == null) return;
        if (!ResidentVisualIntersectsDepthTier(level)) return;

        float rs = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        Matrix proj = _effect.Projection;
        var    sunCol = new Color(SceneLighting.SunColour);
        var (specStrength, specShininess) = SpecularParamsFor(_specularPreset);
        SystemMaterialLibrary? systemMaterials = SystemMaterials;

        // Hull pass — real-time LitSurface.fx DynamicLit (ambient + saturate(N.L)) with
        // procedural texture; MaterialColor left White (matches the old
        // BasicEffect.DiffuseColor = Vector3.One) so all tint comes from the texture.
        // Brief S1: station hulls share DynamicLit with ships/containers/the calibration
        // cube, so they pick up the same specular term (station decoration below stays
        // BakedColorLit* and untouched until S2 — a deliberate scope call, not a gap).
        // Brief U1: this loop needs no module-kind branch at all — _hullMeshes holds an
        // entry for every module, box or MeshFactory alike (see the OnEnter rebuild in
        // SystemSpaceState.cs), so a docking-bay's hull draws through the exact same path
        // as a hab-block's, material map (gloss/bump) included.
        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;
            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;
            InteriorHazeParameters stationInteriorHaze =
                ResolveMegastationInteriorHaze(station, universePos);

            bool useShadow = _stationShadowContext != null
                && ReferenceEquals(_stationShadowContext.Station, station)
                && _stationShadowMap != null;

            Matrix stationRot;
            if (useShadow)
                stationRot = _stationShadowContext!.StationRotation;
            else
            {
                var sysQ   = station.GetOrientation(_gameTimeSeconds);
                var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
                stationRot = Matrix.CreateFromQuaternion(stRotQ);
            }

            foreach (var mod in modules)
            {
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                bool usesSystemMaterials = mod.HullMaterialRanges.Count > 0
                    && systemMaterials != null;
                if (mod.TextureInstance == null && !usesSystemMaterials) continue;

                // mod.Transform used directly, not decomposed-then-rebuilt: the shadow
                // caster (RenderStationShadowMap) and the receiver's ModuleToStationLocal
                // parameter both use mod.Transform raw, so this world matrix must be built
                // from that exact same matrix too, not a numerically-reconstructed
                // approximation of it — otherwise the on-screen vertex position and the
                // position the shadow system evaluates for that vertex quietly disagree by
                // a mm-scale amount (compounded by Decompose's own precision), which eats
                // into the shadow-correction budget independently of anything the
                // receiver-plane correction can fix. See SystemSpaceState.Shadows.cs.
                Matrix world = mod.Transform * Matrix.CreateScale(rs) * stationRot
                             * Matrix.CreateTranslation(renderPos);
                InteriorHazeParameters interiorHaze =
                    mod.ReceivesMegastationInteriorHaze
                        ? stationInteriorHaze
                        : InteriorHazeParameters.Disabled;

                if (_megastationZoningDebug
                    && ResidentStationVisual.MegastationSemanticZoning is { } zoning
                    && mod.Definition.Category == "megastation-prototype")
                {
                    IReadOnlyDictionary<MegastationZoneRole, IndexBuffer> debugBuffers =
                        ResidentStationVisual.EnsureSemanticDebugIndexBuffers(_gd);
                    foreach (MegastationSemanticIndexGroup group in zoning.DebugIndexGroups)
                    {
                        if (!debugBuffers.TryGetValue(group.Role, out IndexBuffer? debugIndices))
                            continue;
                        _meshRenderer.DrawDebugFlatColorRange(
                            hull.vb,
                            debugIndices,
                            0,
                            debugIndices.IndexCount,
                            world,
                            view,
                            proj,
                            MegastationZoneDebugColor(group.Role));
                    }
                    int semanticIndexCount = ResidentStationVisual.MegastationDiagnostics?.BoundaryFaceCount * 6
                        ?? zoning.Diagnostics.SurfaceFaceCount * 6;
                    if (semanticIndexCount < hull.ib.IndexCount)
                    {
                        _meshRenderer.DrawDebugFlatColorRange(
                            hull.vb,
                            hull.ib,
                            semanticIndexCount,
                            hull.ib.IndexCount - semanticIndexCount,
                            world,
                            view,
                            proj,
                            MegastationZoneDebugColor(MegastationZoneRole.Structural));
                    }
                    continue;
                }

                if (usesSystemMaterials)
                {
                    foreach (SystemMaterialDrawRange range in mod.HullMaterialRanges)
                    {
                        SystemMaterialResource material = systemMaterials!.Get(range.FamilyId);
                        SystemMaterialRecipe recipe = material.Recipe;
                        if (useShadow)
                        {
                            var ctx = _stationShadowContext!;
                            float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                            _meshRenderer.DrawDynamicLitShadowedRange(
                                hull.vb, hull.ib, range.StartIndex, range.IndexCount,
                                world, view, proj, Color.White,
                                SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                                recipe.SpecularStrength, recipe.SpecularShininess,
                                material.Albedo, _stationShadowMap!, mod.Transform,
                                ctx.StationLocalToLightView, ctx.MinXY, ctx.InvSize,
                                ctx.Near, ctx.DepthSpan,
                                new Vector2(1f / _stationShadowMapResolution,
                                    1f / _stationShadowMapResolution),
                                StationShadowCorrectionLimit, shadowBiasDepth,
                                _stationShadowBinaryView, _stationShadowDeltaView,
                                ShadowKernelRadiusFor(_shadowKernelMode),
                                material.MaterialMap, recipe.BumpStrength,
                                vertexIlluminationScale: mod.UsesHullVertexIllumination ? 1f : 0f,
                                interiorHaze: interiorHaze);
                        }
                        else
                        {
                            _meshRenderer.DrawDynamicLitRange(
                                hull.vb, hull.ib, range.StartIndex, range.IndexCount,
                                world, view, proj, Color.White,
                                SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                                recipe.SpecularStrength, recipe.SpecularShininess,
                                material.Albedo, material.MaterialMap,
                                recipe.BumpStrength,
                                vertexIlluminationScale: mod.UsesHullVertexIllumination ? 1f : 0f,
                                interiorHaze: interiorHaze,
                                moduleToStationLocal: mod.Transform);
                        }
                    }
                }
                else if (useShadow)
                {
                    var ctx = _stationShadowContext!;
                    // Normalized depth units — StationShadowBiasMetres is expressed in
                    // metres; LitSurface.fx's ShadowBiasDepth compares in the same
                    // normalized space as receiverDepth/storedDepth.
                    float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                    _meshRenderer.DrawDynamicLitShadowed(hull.vb, hull.ib, world, view, proj,
                        Color.White, SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                        specStrength, specShininess,
                        mod.TextureInstance!, _stationShadowMap!, mod.Transform,
                        ctx.StationLocalToLightView, ctx.MinXY, ctx.InvSize, ctx.Near,
                        ctx.DepthSpan,
                        new Vector2(1f / _stationShadowMapResolution, 1f / _stationShadowMapResolution),
                        StationShadowCorrectionLimit, shadowBiasDepth,
                        _stationShadowBinaryView, _stationShadowDeltaView,
                        ShadowKernelRadiusFor(_shadowKernelMode),
                        mod.MaterialInstance, StationBumpStrength,
                        vertexIlluminationScale: mod.UsesHullVertexIllumination ? 1f : 0f,
                        interiorHaze: interiorHaze);
                }
                else
                {
                    _meshRenderer.DrawDynamicLit(hull.vb, hull.ib, world, view, proj,
                        Color.White, SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                        specStrength, specShininess,
                        mod.TextureInstance, mod.MaterialInstance, StationBumpStrength,
                        vertexIlluminationScale: mod.UsesHullVertexIllumination ? 1f : 0f,
                        interiorHaze: interiorHaze,
                        moduleToStationLocal: mod.Transform);
                }
            }
        }

        // Decoration pass — vertex colour is albedo x AO (+ self-illumination floor S in
        // alpha); the sun term is computed here every frame from the real world normal
        // (LitSurface.fx BakedColorLit technique), so a rotating station is lit correctly.
        // Full uses the wear/ambient-occlusion-graded mesh; Medium/Minimal use the flat
        // (ungraded) variant built before that pass ran — same generator, fewer steps,
        // same principle already established for containers and station decoration.
        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;

            bool useShadow = _stationShadowContext != null
                && ReferenceEquals(_stationShadowContext.Station, station)
                && _stationShadowMap != null;
            InteriorHazeParameters stationInteriorHaze =
                ResolveMegastationInteriorHaze(station, universePos);

            Matrix stationRot;
            if (useShadow)
                stationRot = _stationShadowContext!.StationRotation;
            else
            {
                var sysQ   = station.GetOrientation(_gameTimeSeconds);
                var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
                stationRot = Matrix.CreateFromQuaternion(stRotQ);
            }

            foreach (var mod in modules)
            {
                // Depth tier and geometric LOD are separate concerns. The current pass
                // API carries them in one DetailLevel value, but G2's metre-scale native
                // infrastructure must remain visible in the 5m–57km mid depth pass.
                // Reuse its one full mesh there; do not allocate/upload a flattened copy.
                var decoMeshesForModule = UsesFullDecorationMeshInPass(mod, level)
                    ? _decoMeshes
                    : _decoMeshesFlat;
                if (!decoMeshesForModule.TryGetValue(mod, out var deco)) continue;

                // See the hull pass above — mod.Transform used directly, matching the
                // caster and ModuleToStationLocal exactly, not decomposed-then-rebuilt.
                Matrix world = mod.Transform * Matrix.CreateScale(rs) * stationRot
                             * Matrix.CreateTranslation(renderPos);
                InteriorHazeParameters interiorHaze =
                    mod.ReceivesMegastationInteriorHaze
                        ? stationInteriorHaze
                        : InteriorHazeParameters.Disabled;

                // StationTextureRegistry.Get(SurfaceTexture) fallback removed (Brief S2b-1,
                // Report S2a §5): the upload step assigns TextureInstance to
                // every module, so this branch was provably dead. Kept a defensive null
                // fallback (not a crash) in case a future module kind ever skips
                // texture upload — White reads as a flat unlit panel, not a missing-texture
                // artifact.
                Texture2D tex = mod.TextureInstance ?? StationTextureRegistry.White;

                if (mod.DecorationMaterialRanges.Count > 0 && systemMaterials != null)
                {
                    foreach (SystemMaterialDrawRange range in mod.DecorationMaterialRanges)
                    {
                        SystemMaterialResource material = systemMaterials.Get(range.FamilyId);
                        SystemMaterialRecipe recipe = material.Recipe;
                        if (useShadow)
                        {
                            var ctx = _stationShadowContext!;
                            float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                            _meshRenderer.DrawDynamicLitShadowedRange(
                                deco.vb, deco.ib, range.StartIndex, range.IndexCount,
                                world, view, proj, Color.White,
                                SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                                recipe.SpecularStrength, recipe.SpecularShininess,
                                material.Albedo, _stationShadowMap!, mod.Transform,
                                ctx.StationLocalToLightView, ctx.MinXY, ctx.InvSize,
                                ctx.Near, ctx.DepthSpan,
                                new Vector2(1f / _stationShadowMapResolution,
                                    1f / _stationShadowMapResolution),
                                StationShadowCorrectionLimit, shadowBiasDepth,
                                _stationShadowBinaryView, _stationShadowDeltaView,
                                ShadowKernelRadiusFor(_shadowKernelMode),
                                material.MaterialMap, recipe.BumpStrength,
                                vertexIlluminationScale: mod.UsesDecorationVertexIllumination ? 1f : 0f,
                                presentationDepthBias: mod.UsesCoplanarStructuralOverlay
                                    ? H1CoplanarOverlayClipDepthBias : 0f,
                                interiorHaze: interiorHaze);
                        }
                        else
                        {
                            _meshRenderer.DrawDynamicLitRange(
                                deco.vb, deco.ib, range.StartIndex, range.IndexCount,
                                world, view, proj, Color.White,
                                SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                                recipe.SpecularStrength, recipe.SpecularShininess,
                                material.Albedo, material.MaterialMap,
                                recipe.BumpStrength,
                                vertexIlluminationScale: mod.UsesDecorationVertexIllumination ? 1f : 0f,
                                presentationDepthBias: mod.UsesCoplanarStructuralOverlay
                                    ? H1CoplanarOverlayClipDepthBias : 0f,
                                interiorHaze: interiorHaze,
                                moduleToStationLocal: mod.Transform);
                        }
                    }
                    continue;
                }

                // Brief U1: mod.Mesh is decoration only, for every module kind — a
                // MeshFactory module's hull now lives in its own separate mesh (drawn in
                // the hull pass above, alongside box modules), so this is always a single,
                // unconditional decoration draw, exactly as it was before Brief F1's
                // now-deleted hull/decoration index-range split.
                if (useShadow)
                {
                    var ctx = _stationShadowContext!;
                    float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                    _meshRenderer.DrawBakedColorLitShadowed(deco.vb, deco.ib, world, view, proj,
                        SceneLighting.SunDirection, sunCol, SceneLighting.Ambient, tex,
                        StationBrightnessTuning.DecorationBrightnessMultiplier,
                        _stationShadowMap!, mod.Transform, ctx.StationLocalToLightView,
                        ctx.MinXY, ctx.InvSize, ctx.Near, ctx.DepthSpan,
                        new Vector2(1f / _stationShadowMapResolution, 1f / _stationShadowMapResolution),
                        StationShadowCorrectionLimit, shadowBiasDepth,
                        _stationShadowBinaryView, _stationShadowDeltaView,
                        ShadowKernelRadiusFor(_shadowKernelMode));
                }
                else
                {
                    _meshRenderer.DrawBakedColorLit(deco.vb, deco.ib, world, view, proj,
                        SceneLighting.SunDirection, sunCol, SceneLighting.Ambient, tex,
                        StationBrightnessTuning.DecorationBrightnessMultiplier);
                }
            }
        }

        // Glass pass — windows, portholes; unlit, unchanged (Docs/station-lighting-pipeline-spec.md
        // D5: glass is a separate mesh, explicitly out of scope for this migration). Still
        // BasicEffect; explicit state set here since the deco pass above no longer touches _effect.
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = true;
        _effect.Texture            = StationTextureRegistry.White;

        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _gd.SetVertexBuffer(glass.vb);
                _gd.Indices = glass.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: glass.triCount);
                }
            }
        }

        if (_megastationInfrastructureDebug || _megastationInteriorDebug)
            DrawMegastationInfrastructureDebugLines();

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;

        // Brief D-Z2 Measurement 1: zone-type debug overlay (Ctrl+F4). Opaque, unlit,
        // depth-tested against the real hull/decoration drawn above, so it reads as paint
        // on the surface rather than a floating layer. Reuses the same per-module world
        // transform (mod.Transform * scale * stationRot * translate) as every other pass in
        // this method — the overlay sits exactly where the retained zone data says it does.
        if (_showZoneTypeDebug)
        {
            _effect.LightingEnabled    = false;
            _effect.VertexColorEnabled = true;
            _effect.TextureEnabled     = false;

            foreach (var (station, universePos) in _stationPositions)
            {
                Vector3 renderPos = _camera.ToRenderSpace(universePos);
                if (renderPos.Length() > 30_000f) continue;
                // Only one station's geometry is resident at a time under the visual
                // residency system — the debug overlay can only draw for that one.
                if (ResidentStationVisual is not { } residentForZoneDebug
                    || residentForZoneDebug.Descriptor.Station != station) continue;
                var modules = residentForZoneDebug.Modules;

                var sysQ   = station.GetOrientation(_gameTimeSeconds);
                var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
                Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

                foreach (var mod in modules)
                {
                    var built = BuildZoneDebugQuads(mod);
                    if (built == null) continue;
                    var (verts, indices) = built.Value;

                    _effect.World = mod.Transform * Matrix.CreateScale(rs) * stationRot
                                  * Matrix.CreateTranslation(renderPos);

                    foreach (var pass in _effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _gd.DrawUserIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            verts, 0, verts.Length,
                            indices, 0, indices.Length / 3);
                    }
                }
            }

            _effect.TextureEnabled     = false;
            _effect.VertexColorEnabled = false;
            _effect.LightingEnabled    = true;
        }

        // MeshRenderer's Draw() leaves rasterizer/depth state set for its own techniques;
        // restore what the rest of this frame's 3D passes expect (matches DrawContainers'
        // and ShipMeshRenderer.Draw's post-draw restore).
        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    private void DrawMegastationApproachBeams(DetailLevel level)
    {
        if (ResidentStationVisual == null || !ResidentVisualIntersectsDepthTier(level))
            return;

        float renderScale = (float)Camera3D.RenderScale;
        _effect.TextureEnabled = false;
        _effect.LightingEnabled = false;
        _effect.VertexColorEnabled = true;
        _effect.DiffuseColor = Vector3.One;
        _effect.Alpha = 1f;
        _gd.BlendState = BlendState.Additive;
        _gd.DepthStencilState = DepthStencilState.DepthRead;
        _gd.RasterizerState = RasterizerState.CullNone;

        foreach ((Galaxy.Station station, DVec3 universePosition) in ResidentStationEntries())
        {
            Vector3 renderPosition = _camera.ToRenderSpace(universePosition);
            Quaternion orientation = station.GetOrientation(_gameTimeSeconds);
            Matrix stationRotation = Matrix.CreateFromQuaternion(new Quaternion(
                orientation.X,
                orientation.Y,
                orientation.Z,
                orientation.W));
            _effect.World = Matrix.CreateScale(renderScale)
                * stationRotation
                * Matrix.CreateTranslation(renderPosition);

            foreach (PlacedModule module in ResidentStationVisual.Modules)
            {
                VertexPositionColor[]? vertices = module.NativeApproachBeamVertices;
                if (vertices is not { Length: >= 3 }) continue;
                foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawUserPrimitives(
                        PrimitiveType.TriangleList,
                        vertices,
                        0,
                        vertices.Length / 3);
                }
            }
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled = true;
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    internal const float H1CoplanarOverlayClipDepthBias = .00002f;

    internal static bool UsesFullDecorationMeshInPass(PlacedModule module, DetailLevel level)
        => level == DetailLevel.Full
            || (level == DetailLevel.Medium && (module.HasNativeMegastationInfrastructure
                || module.HasNativeMegastationMegaGreeble
                || module.HasNativeMegastationFabric
                || module.HasNativeMegastationServiceChannels
                || module.HasNativeMegastationInterior));

    private void DrawMegastationInfrastructureDebugLines()
    {
        float renderScale = (float)Camera3D.RenderScale;
        _effect.TextureEnabled = false;
        _effect.LightingEnabled = false;
        _effect.VertexColorEnabled = true;
        _gd.DepthStencilState = DepthStencilState.Default;

        foreach (var (station, universePosition) in ResidentStationEntries())
        {
            Vector3 renderPosition = _camera.ToRenderSpace(universePosition);
            Quaternion orientation = station.GetOrientation(_gameTimeSeconds);
            Matrix stationRotation = Matrix.CreateFromQuaternion(new Quaternion(
                orientation.X, orientation.Y, orientation.Z, orientation.W));
            _effect.World = Matrix.CreateScale(renderScale) * stationRotation
                * Matrix.CreateTranslation(renderPosition);
            foreach (PlacedModule module in ResidentStationVisual!.Modules)
            {
                if (_megastationInfrastructureDebug)
                {
                    VertexPositionColor[]? detailLines = module.NativeInfrastructureDebugLines
                        ?? module.NativeMegaGreebleDebugLines
                        ?? module.NativeFabricDebugLines
                        ?? module.NativeServiceChannelDebugLines;
                    DrawLines(detailLines);
                }
                if (_megastationInteriorDebug)
                    DrawLines(module.NativeInteriorDebugLines);
            }
        }

        void DrawLines(VertexPositionColor[]? lines)
        {
            if (lines is not { Length: > 1 }) return;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserPrimitives(PrimitiveType.LineList, lines, 0, lines.Length / 2);
            }
        }
    }

    private static Color MegastationZoneDebugColor(MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Structural => new Color(78, 88, 98),
        MegastationZoneRole.Habitation => new Color(55, 170, 225),
        MegastationZoneRole.Industrial => new Color(220, 125, 42),
        MegastationZoneRole.Logistics => new Color(215, 195, 62),
        MegastationZoneRole.Utilities => new Color(130, 78, 180),
        MegastationZoneRole.Strategic => new Color(220, 55, 75),
        _ => Color.White,
    };

    private void DrawStationOrbitRings()
    {
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        var ringColor = new Color(20, 30, 50, 120);

        foreach (var (station, _) in _stationPositions)
        {
            // Station orbit ring is centred on its parent body's render pos
            DVec3 parentEcliptic = station.OrbitParent != null
                ? station.OrbitParent.GetPosition(_gameTimeSeconds, DVec3.Zero)
                : DVec3.Zero;
            DVec3   parentUniverse = EclipticToGalaxy(parentEcliptic);
            Vector3 parentRender   = _camera.ToRenderSpace(parentUniverse);

            float ringR = (float)(station.OrbitalRadius * Camera3D.RenderScale);
            if (ringR < 0.0001f || ringR > 5_000f) continue;

            _effect.World = Matrix.CreateScale(ringR)
                          * _eclipticRotation
                          * Matrix.CreateTranslation(parentRender);
            _ringPrimitive.Draw(_gd, _effect, ringColor);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Station dot icons — 3×3 pixel screen-space marker, visible up to 1 million km.
    // Drawn on top of all 3D geometry so stations are always locatable.
    private void DrawStationDots(SpriteBatch sb)
    {
        const float MaxDistRU = 1.0f;   // 1 million km → 1.0 render unit

        var viewProj = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
        int w = _gd.Viewport.Width;
        int h = _gd.Viewport.Height;

        foreach (var (station, universePos) in _stationPositions)
        {
            if (ResidentStationVisual?.Descriptor.UseMegastationPrototype == true
                && ReferenceEquals(ResidentStationVisual.Descriptor.Station, station))
                continue;
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > MaxDistRU) continue;

            Vector4 clip = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
            if (clip.W <= 0f) continue;

            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;
            if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

            sb.Draw(_pixel, new Rectangle((int)sx - 1, (int)sy - 1, 3, 3), new Color(160, 190, 210, 220));
        }
    }

    // Draws additive screen-space glow sprites over all station nav lights and warning
    // strobes. Called once per render pass (see DrawFarPassContent/DrawMidPassContent/
    // DrawNearPassContent), filtered to that pass's own real-metre distance range —
    // required because each pass clears and rebuilds its own depth buffer, so a light's
    // glow can only be correctly depth-tested against the SAME pass that drew its host
    // geometry; testing it against a later pass's buffer would compare it against
    // "cleared to far" everywhere that pass didn't itself draw anything, i.e. almost
    // everywhere for lights outside that pass's own range, defeating the depth test.
    // Must run after DrawStations() in the same pass so the additive blend brightens
    // visible geometry and depth-tests against it correctly.
    private void DrawStationGlows(SpriteBatch sb, float nearBoundReal, float farBoundReal)
    {
        if (ResidentStationVisual == null) return;
        long timingStart = Stopwatch.GetTimestamp();

        // Active pass's projection (_effect.Projection), not camera.ProjectionMatrix —
        // that's only a representative mid-tier projection now that rendering uses three
        // independent per-pass projections. Same fix as ShipMeshRenderer/DrawContainers.
        Matrix   viewProj  = _effect.View * _effect.Projection;
        Viewport viewport  = _gd.Viewport;
        Vector2  texCentre = new(_navGlowTex.Width * 0.5f, _navGlowTex.Height * 0.5f);

        // DepthRead so these sprites are occluded by hull geometry in front of them —
        // read-only depth test (DepthBufferEnable=true, DepthBufferWriteEnable=false),
        // since they're a 2D overlay, not real geometry that should write new depth.
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.DepthRead);
        foreach (var (station, universePos) in ResidentStationEntries())
        {
            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;
            Vector3 stationRel = (universePos - _camera.UniversePosition).ToVector3(); // metres

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);

            foreach (var mod in modules)
            {
                foreach (var light in mod.GlowLights)
                {
                    Vector3 stationLocalRotated = Vector3.Transform(light.WorldPosition, stRotQ);
                    Vector3 relPos   = stationRel + stationLocalRotated;
                    float   distance = relPos.Length();
                    if (distance < 0.1f) continue;
                    if (distance < nearBoundReal || distance >= farBoundReal) continue;

                    StationGlowDepthDecision depthDecision = light.SurfaceNormal is { } surfaceNormal
                        ? ResolveStationGlowDepth(
                            relPos,
                            Vector3.Transform(surfaceNormal, stRotQ))
                        : new StationGlowDepthDecision(true, 1f, 0f, relPos);
                    if (!depthDecision.IsFrontFacing) continue;

                    Vector2? screen = TargetingSystem.ProjectToScreen(relPos, viewProj, viewport);
                    if (screen == null) continue;

                    float intensity = ComputeGlowIntensity(light)
                        * ResolveStationGlowDistanceFade(light, distance);
                    float baseSize = light.Type switch
                    {
                        StationGen.GlowType.NavigationLight => 1200f,
                        StationGen.GlowType.WarningStrobe   => 700f,
                        StationGen.GlowType.AviationWarning => 800f,
                        StationGen.GlowType.AmbientMarker   => 400f,
                        StationGen.GlowType.DockGuidance    => 600f,   // AmbientMarker x1.5, per Timo's ask
                        StationGen.GlowType.MegastationEntranceGuidance => 18_000f,
                        // Roughly twice the angular width of the 1.34 m shelf-flood lens.
                        StationGen.GlowType.ShelfWorkFlood => 2_400f,
                        _                                   => 400f,
                    };
                    float size = light.PresentationSizePixels
                        ?? (light.SurfaceNormal != null
                            ? MegastationGlowSizePixels
                            : MathHelper.Clamp(
                                baseSize / distance * light.PresentationSizeScale,
                                light.PresentationMinimumSizePixels ?? 6f,
                                light.PresentationMaximumSizePixels ?? 140f));
                    float scale = size / _navGlowTex.Width;

                    if (intensity < 0.01f) continue;
                    _megastationGlowFrameVisibleCount++;

                    // Real depth for this pass's depth test. Without this every sprite
                    // draws at layerDepth 0 (nearest possible depth value), which would
                    // always pass DepthRead regardless of what's actually in front of it —
                    // the state change alone (above) isn't sufficient without this.
                    Vector3 depthRenderPos = depthDecision.BiasedCameraRelativePosition
                        * (float)Camera3D.RenderScale;
                    Vector4 depthClip = Vector4.Transform(
                        new Vector4(depthRenderPos, 1f),
                        viewProj);
                    float layerDepth = MathHelper.Clamp(depthClip.Z / depthClip.W, 0f, 1f);

                    sb.Draw(_navGlowTex, screen.Value, null,
                            light.Colour * intensity, 0f, texCentre, scale,
                            SpriteEffects.None, layerDepth);
                    _megastationGlowFrameSubmittedCount++;
                }
            }
        }
        sb.End();
        _megastationGlowFrameProjectionAndSubmissionMilliseconds +=
            Stopwatch.GetElapsedTime(timingStart).TotalMilliseconds;
    }

    private void BeginMegastationGlowFrameDiagnostics()
    {
        _megastationGlowFrameVisibleCount = 0;
        _megastationGlowFrameSubmittedCount = 0;
        _megastationGlowFrameProjectionAndSubmissionMilliseconds = 0.0;
    }

    private void CompleteMegastationGlowFrameDiagnostics()
    {
        if (ResidentStationVisual?.MegastationLightingDiagnostics is not { } diagnostics
            || _gameTimeSeconds < _nextMegastationGlowFrameDiagnosticSeconds)
            return;

        _nextMegastationGlowFrameDiagnosticSeconds = _gameTimeSeconds + 5.0;
        int planned = diagnostics.IndustrialLightCount
            + diagnostics.LogisticsLightCount
            + diagnostics.UtilitiesLightCount
            + diagnostics.StrategicLightCount;
        string message = $"[MegastationLightingFrame] station={ResidentStationVisual.Descriptor.Identity}; " +
            $"plannedLightCount={planned}; visibleLightCount={_megastationGlowFrameVisibleCount}; " +
            $"submittedLightCount={_megastationGlowFrameSubmittedCount}; " +
            $"animatedLightCount={diagnostics.AnimatedLightCount}; " +
            $"depthBiasMeters={MegastationGlowCameraDepthBiasMeters:F1}; " +
            $"surfaceGlowSizePixels={MegastationGlowSizePixels:F0}; rearFaceGate=true; " +
            $"lightProjectionAndSubmissionMs=" +
            $"{_megastationGlowFrameProjectionAndSubmissionMilliseconds:F3}";
        Console.WriteLine(message);
        Debug.WriteLine(message);
    }

    internal static StationGlowDepthDecision ResolveStationGlowDepth(
        Vector3 cameraRelativePosition,
        Vector3 worldSurfaceNormal)
    {
        float distanceSquared = cameraRelativePosition.LengthSquared();
        float normalLengthSquared = worldSurfaceNormal.LengthSquared();
        if (distanceSquared < 0.0001f || normalLengthSquared < 0.0001f)
            return new StationGlowDepthDecision(false, 0f, 0f, cameraRelativePosition);

        Vector3 fromCamera = Vector3.Normalize(cameraRelativePosition);
        Vector3 normal = Vector3.Normalize(worldSurfaceNormal);
        float facing = Vector3.Dot(normal, -fromCamera);
        if (facing <= 0f)
            return new StationGlowDepthDecision(false, facing, 0f, cameraRelativePosition);

        Vector3 biasedPosition = cameraRelativePosition
            - fromCamera * MegastationGlowCameraDepthBiasMeters;
        return new StationGlowDepthDecision(
            true,
            facing,
            MegastationGlowCameraDepthBiasMeters,
            biasedPosition);
    }

    internal static float ResolveStationGlowDistanceFade(
        StationLightInfo light,
        float cameraDistanceMeters)
    {
        if (light.PresentationFadeStartMeters is not { } start
            || light.PresentationFadeEndMeters is not { } end)
            return 1f;
        if (cameraDistanceMeters <= start) return 1f;
        if (cameraDistanceMeters >= end) return 0f;
        float range = end - start;
        if (range <= 0f) return 0f;
        float t = MathHelper.Clamp((cameraDistanceMeters - start) / range, 0f, 1f);
        float smooth = t * t * (3f - 2f * t);
        return 1f - smooth;
    }

    private static float ComputeGlowIntensity(StationLightInfo light)
    {
        if (light.Rate <= 0f) return light.BaseIntensity;
        float t = (float)((GameClock.SimTime * light.Rate + light.Phase) % 1.0);
        return EvaluateGlowPattern(light.Pattern, t) * light.BaseIntensity;
    }

    internal static float EvaluateGlowPattern(LightPattern pattern, float t)
    {
        t -= MathF.Floor(t);
        return pattern switch
        {
            LightPattern.Strobe    => t < 0.18f ? 1f : 0f,
            LightPattern.SlowPulse => MathF.Sin(t * MathF.Tau) * 0.5f + 0.5f,
            LightPattern.Heartbeat => t < 0.10f ? 1f
                                    : t < 0.22f ? 0f
                                    : t < 0.32f ? .65f
                                    : 0f,
            _ => 1f,
        };
    }
}
