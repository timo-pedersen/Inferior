using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationInteriorTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private const string GrandFixture = "Zydaan:Zydaan I:Delta Anchorage";
    private const float LargestShipWidth = 36f;
    private const float LargestShipHeight = 20f;
    private const float LargestShipLength = 72f;
    private static readonly SystemMaterialAssignmentContext MaterialContext =
        SystemMaterialCpuLibraryGenerator.CreateAssignmentContext(
            GalaxyGenerator.SystemSeed(
                StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star).Seed);
    private static readonly Lazy<MegastationPrototypeCpuResult> NovaResult =
        new(() => MegastationPrototypeGenerator.GenerateCpu(
            Nova,
            systemMaterials: MaterialContext));
    private static readonly Lazy<MegastationPrototypeCpuResult> GrandResult =
        new(() => MegastationPrototypeGenerator.GenerateCpu(
            GrandFixture,
            systemMaterials: MaterialContext));

    [Fact]
    public void StandardEntranceFixtureRetainsAcceptedH1Output()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        Assert.Equal(MegastationEntranceType.Standard, result.InteriorPlan.EntranceType);
        Assert.Equal(
            "FFC928024C07BDA30BADE311F5FA86DDFA26EA35EA8253C98840FE02FE7AAC31",
            result.InteriorPlan.Diagnostics.Signature);
        Assert.InRange(result.InteriorPlan.PortalClearSize.X, 163.6f, 163.8f);
        Assert.InRange(result.InteriorPlan.PortalClearSize.Y, 125.6f, 125.8f);
    }

    [Fact]
    public void GrandEntranceUsesBayWidthAndLargeEnvelopeHeight()
    {
        MegastationPrototypeCpuResult result = GrandResult.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationInteriorDiagnostics diagnostics = interior.Diagnostics;
        Assert.Equal(MegastationEntranceType.Grand, interior.EntranceType);
        Assert.Equal(MegastationEntranceType.Grand, diagnostics.EntranceType);
        Assert.InRange(diagnostics.EntranceWidthFraction, .68f, .98f);
        Assert.Equal(interior.PortalClearSize.X / diagnostics.BayClearWidth,
            diagnostics.EntranceWidthFraction, 4);
        Assert.InRange(interior.PortalClearSize.Y, 40f, 46.01f);
        Assert.True(interior.PortalClearSize.Y > LargestShipWidth);
        Assert.Equal(interior.PortalClearSize.Y - LargestShipHeight,
            diagnostics.LargeUprightVerticalClearance, 3);
        Assert.Equal(interior.PortalClearSize.Y - LargestShipWidth,
            diagnostics.LargeRolledVerticalClearance, 3);

        float structuralWidth = Vector3.Dot(Abs(interior.PortalRight), interior.ThroatVolume.Size);
        float structuralHeight = Vector3.Dot(Abs(interior.PortalUp), interior.ThroatVolume.Size);
        float wallThickness = interior.ThroatWallThickness;
        Assert.Equal(structuralWidth - wallThickness * 2f, interior.PortalClearSize.X, 3);
        Assert.Equal(structuralHeight - wallThickness * 2f, interior.PortalClearSize.Y, 3);
        Console.WriteLine(
            $"H1f {GrandFixture}: clear={interior.PortalClearSize.X:F1}x{interior.PortalClearSize.Y:F1}m; "
            + $"bay={diagnostics.BayClearWidth:F1}m; widthFraction={diagnostics.EntranceWidthFraction:P1}; "
            + $"wall={wallThickness:F1}m; uprightClearance={diagnostics.LargeUprightVerticalClearance:F1}m; "
            + $"rolled90Clearance={diagnostics.LargeRolledVerticalClearance:F1}m; "
            + $"throat={diagnostics.ThroatLength:F1}m; "
            + $"mesh={diagnostics.PortalVisibleVertexCount}v/{diagnostics.PortalVisibleTriangleCount}t; "
            + $"caster={diagnostics.PortalCasterVertexCount}v/{diagnostics.PortalCasterTriangleCount}t; "
            + $"fixtures={diagnostics.ThroatFixtureElementCount}; glows={diagnostics.GuidanceGlowCount}");
    }

    [Fact]
    public void GrandEntranceReusesCrownLightsBeamsAndReservationArchitecture()
    {
        MegastationPrototypeCpuResult result = GrandResult.Value;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        Assert.Equal(4, presentation.ThroatCrownCount);
        Assert.Equal(4, presentation.ApproachBeams.Count);
        Assert.Equal(16, presentation.ApproachFixtureElementCount);
        Assert.True(presentation.ThroatFixtureCount >= 20);
        Assert.Contains(result.AttachmentPlan.EffectiveProtectedVolumes,
            volume => volume.Identity == "interior/entrance-precinct");
        Assert.All(presentation.ApproachBeams, beam =>
            Assert.True(Vector3.Dot(beam.Axis, result.InteriorPlan.OutwardNormal) > .9999f));
        PlacedModule module = MegastationPrototypeGenerator.CreateInteriorModule(result);
        Assert.Null(module.TextureInstance);
        Assert.Null(module.MaterialInstance);
    }

    [Fact]
    public void CompleteEntranceAssemblyIsContainedByStandardAndGrandPrecincts()
    {
        foreach (MegastationPrototypeCpuResult result in new[]
                 {
                     NovaResult.Value,
                     GrandResult.Value,
                 })
        {
            MegastationInteriorPlan interior = result.InteriorPlan;
            MegastationEntrancePrecinct precinct = interior.EntrancePrecinct;
            Assert.Equal(precinct, result.InteriorPresentationPlan.Precinct);
            Assert.True(precinct.ClearanceMargin > 0f);

            MegastationInteriorGuidanceElement[] crown = result.InteriorPresentationPlan.Elements
                .Where(element => element.Identity.StartsWith(
                    "entrance/crown/", StringComparison.Ordinal)
                    && element.Kind == MegastationInteriorGuidanceKind.ThroatTransition)
                .ToArray();
            Assert.Equal(4, crown.Length);
            Vector3[] crownCorners = crown.SelectMany(ElementCorners).ToArray();
            float crownWidth = AxisSpan(crownCorners, interior.PortalRight);
            float crownHeight = AxisSpan(crownCorners, interior.PortalUp);
            Assert.Equal(precinct.CrownOuterWidth, crownWidth, 3);
            Assert.Equal(precinct.CrownOuterHeight, crownHeight, 3);

            MegastationInteriorGuidanceElement[] assembly = result.InteriorPresentationPlan.Elements
                .Where(element => element.Identity.StartsWith(
                    "entrance/crown/", StringComparison.Ordinal)
                    || element.Identity.StartsWith(
                        "entrance/approach/fixture:", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(assembly);
            Assert.All(assembly, element =>
            {
                (Vector3 minimum, Vector3 maximum) = ElementBounds(element);
                AssertBoundsContain(
                    precinct.AssemblyMinimum,
                    precinct.AssemblyMaximum,
                    minimum,
                    maximum);
            });
            Assert.All(result.InteriorPresentationPlan.Markers.Where(marker =>
                    marker.Identity.StartsWith(
                        "entrance/approach/source:", StringComparison.Ordinal)),
                marker => Assert.True(PointInsideBounds(
                    marker.Position,
                    precinct.AssemblyMinimum,
                    precinct.AssemblyMaximum)));

            Vector3 protectedSize = precinct.Maximum - precinct.Minimum;
            Assert.Equal(
                precinct.CrownOuterWidth + precinct.ClearanceMargin * 2f,
                Vector3.Dot(Abs(interior.PortalRight), protectedSize),
                3);
            Assert.Equal(
                precinct.CrownOuterHeight + precinct.ClearanceMargin * 2f,
                Vector3.Dot(Abs(interior.PortalUp), protectedSize),
                3);
        }
    }

    [Fact]
    public void StructuralTruthAndExteriorCompositionRespectStandardAndGrandAssemblyClearance()
    {
        foreach (MegastationPrototypeCpuResult result in new[]
                 {
                     NovaResult.Value,
                     GrandResult.Value,
                 })
        {
            MegastationEntrancePrecinct precinct = result.InteriorPlan.EntrancePrecinct;
            SliceGrid grid = result.Grid;
            for (int x = 0; x < grid.XCount; x++)
            for (int y = 0; y < grid.YCount; y++)
            for (int z = 0; z < grid.ZCount; z++)
            {
                if (!result.RegularisedOccupancy.IsOccupied(x, y, z)) continue;
                Vector3 minimum = new(
                    grid.GetCellMinimum(GridAxis.X, x),
                    grid.GetCellMinimum(GridAxis.Y, y),
                    grid.GetCellMinimum(GridAxis.Z, z));
                Vector3 maximum = new(
                    grid.GetCellMaximum(GridAxis.X, x),
                    grid.GetCellMaximum(GridAxis.Y, y),
                    grid.GetCellMaximum(GridAxis.Z, z));
                Assert.False(precinct.Intersects(minimum, maximum));
            }
            Assert.True(result.InteriorPlan.Diagnostics.EntranceAssemblyRemovedCellCount > 0);
            AssertExteriorCompositionRespectsPrecinct(result);
        }
    }

    [Fact]
    public void GrandEntranceMorphologyAndProtectedVolumeAreDeterministic()
    {
        MegastationPrototypeCpuResult first = GrandResult.Value;
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(
            GrandFixture,
            systemMaterials: MaterialContext);
        Assert.Equal(first.InteriorPlan.EntranceType, second.InteriorPlan.EntranceType);
        Assert.Equal(first.InteriorPlan.PortalClearSize, second.InteriorPlan.PortalClearSize);
        Assert.Equal(first.InteriorPlan.ThroatVolume, second.InteriorPlan.ThroatVolume);
        Assert.Equal(first.InteriorPlan.ProtectedCells, second.InteriorPlan.ProtectedCells);
        Assert.Equal(first.InteriorPlan.Diagnostics.Signature,
            second.InteriorPlan.Diagnostics.Signature);
        Assert.True(ProtectedCellsAreConnected(first.InteriorPlan));
    }

    [Fact]
    public void H1AlwaysPublishesOneConnectedProtectedInteriorAndEntrance()
    {
        foreach (string identity in new[]
                 {
                     Nova,
                     "Gaanis:Gaanis II:Omega Beacon",
                     "Enloax:Enloax Vd:Deep Haven",
                 })
        {
            MegastationPrototypeCpuResult result = identity == Nova
                ? NovaResult.Value
                : MegastationPrototypeGenerator.GenerateCpu(identity);
            MegastationInteriorPlan plan = result.InteriorPlan;
            Assert.Equal(1, plan.Diagnostics.InteriorCount);
            Assert.Contains(plan.ProtectedCells, cell =>
                cell.Kind == MegacellVoidKind.InteriorFlightVolume);
            Assert.Contains(plan.ProtectedCells, cell =>
                cell.Kind == MegacellVoidKind.EntranceThroat);
            Assert.True(ProtectedCellsAreConnected(plan));
            Assert.Contains(plan.ProtectedCells, cell =>
                cell.Kind == MegacellVoidKind.EntranceThroat
                && IsGridBoundary(result.Grid, cell.Cell));
            Assert.All(plan.ProtectedCells, cell =>
            {
                Assert.False(result.RegularisedOccupancy.IsOccupied(
                    cell.Cell.X, cell.Cell.Y, cell.Cell.Z));
                Assert.Equal(cell.Kind, result.RegularisedOccupancy.VoidKind(
                    cell.Cell.X, cell.Cell.Y, cell.Cell.Z));
            });
            Assert.Equal(4, result.InteriorPresentationPlan.ApproachBeams.Count);
            Assert.Equal(2, result.InteriorPresentationPlan.ApproachBeams.Count(beam =>
                beam.Vertical == MegastationApproachBeamVertical.Upper
                && beam.Colour == MegastationInteriorPresentationPlanner.ApproachUpColour));
            Assert.Equal(2, result.InteriorPresentationPlan.ApproachBeams.Count(beam =>
                beam.Vertical == MegastationApproachBeamVertical.Lower
                && beam.Colour == MegastationInteriorPresentationPlanner.ApproachDownColour));
            Assert.All(result.InteriorPresentationPlan.ApproachBeams, beam =>
                Assert.True(Vector3.Dot(beam.Axis, plan.OutwardNormal) > .9999f));
            AssertLargeShipClearance(plan);
            Console.WriteLine(
                $"H1 {identity}: portal={plan.PortalClearSize.X:F1}x{plan.PortalClearSize.Y:F1}m; "
                + $"throat={plan.Diagnostics.ThroatLength:F1}m; flight={plan.MainFlightVolume.Size.X:F1}x"
                + $"{plan.MainFlightVolume.Size.Y:F1}x{plan.MainFlightVolume.Size.Z:F1}m; "
                + $"protected={plan.Diagnostics.ProtectedVoidCellCount}; removed={plan.Diagnostics.RemovedStructuralCellCount}; "
                + $"boundaryFaces={plan.Diagnostics.InteriorBoundaryFaceCount}; "
                + $"portal={plan.Diagnostics.PortalVisibleVertexCount}v/{plan.Diagnostics.PortalVisibleTriangleCount}t; "
                + $"caster={plan.Diagnostics.PortalCasterVertexCount}v/{plan.Diagnostics.PortalCasterTriangleCount}t; "
                + $"signature={plan.Diagnostics.Signature}");
        }
    }

    [Fact]
    public void GuaranteedClearanceComfortablyFitsLargestConfiguredShip()
    {
        MegastationInteriorPlan plan = NovaResult.Value.InteriorPlan;
        AssertLargeShipClearance(plan);
    }

    [Fact]
    public void InteriorCannotLeakDirectlyToExteriorExceptThroughThroat()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        StructuralOccupancy occupancy = result.RegularisedOccupancy;
        foreach (MegastationProtectedVoidCell protectedCell in result.InteriorPlan.ProtectedCells
                     .Where(cell => cell.Kind == MegacellVoidKind.InteriorFlightVolume))
        foreach ((int dx, int dy, int dz) in Neighbours)
        {
            int x = protectedCell.Cell.X + dx;
            int y = protectedCell.Cell.Y + dy;
            int z = protectedCell.Cell.Z + dz;
            Assert.True(occupancy.Grid.Contains(x, y, z));
            if (!occupancy.IsOccupied(x, y, z))
                Assert.NotEqual(MegacellVoidKind.None, occupancy.VoidKind(x, y, z));
        }
    }

    [Fact]
    public void BoundarySemanticsExcludeInteriorFromExteriorSurfacePlanners()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        Assert.Contains(result.BoundaryTopology.Faces, face =>
            face.SpaceKind == MegastationBoundarySpaceKind.ExteriorBoundary);
        Assert.Contains(result.BoundaryTopology.Faces, face =>
            face.SpaceKind == MegastationBoundarySpaceKind.EntranceThroatBoundary);
        Assert.Contains(result.BoundaryTopology.Faces, face =>
            face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary);
        Assert.All(result.SemanticZoning.Surfaces, surface =>
            Assert.Equal(
                MegastationBoundarySpaceKind.ExteriorBoundary,
                result.BoundaryTopology.FaceByKey[surface.Face].SpaceKind));
        Assert.All(result.PlanarRegions.SelectMany(region => region.Faces), face =>
            Assert.Equal(
                MegastationBoundarySpaceKind.ExteriorBoundary,
                result.BoundaryTopology.FaceByKey[face].SpaceKind));
    }

    [Fact]
    public void InteriorBoundaryWindingFacesTheEmptyFlightSpace()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        SliceGrid grid = result.Grid;
        foreach (BoundaryFace face in result.BoundaryTopology.Faces.Where(face =>
                     face.SpaceKind != MegastationBoundarySpaceKind.ExteriorBoundary))
        {
            Vector3 occupiedCentre = new(
                grid.GetCellCentre(GridAxis.X, face.Key.X),
                grid.GetCellCentre(GridAxis.Y, face.Key.Y),
                grid.GetCellCentre(GridAxis.Z, face.Key.Z));
            Vector3 faceCentre = face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex))
                .Aggregate(Vector3.Zero, (sum, point) => sum + point) * .25f;
            Assert.True(Vector3.Dot(
                faceCentre - occupiedCentre,
                BoundaryTopologyBuilder.Normal(face.Direction)) > 0f);
        }
    }

    [Fact]
    public void PortalUsesBorrowedMaterialRangesAndSelectiveCasterPolicy()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        PlacedModule structure = MegastationPrototypeGenerator.CreatePlacedModule(result);
        PlacedModule portal = MegastationPrototypeGenerator.CreateInteriorModule(result);
        Assert.NotEmpty(portal.DecorationMaterialRanges);
        Assert.Null(portal.TextureInstance);
        Assert.Null(portal.MaterialInstance);
        Assert.True(portal.HasNativeMegastationInterior);
        Assert.True(structure.UsesHullVertexIllumination);
        Assert.Contains(portal.Mesh!.DecorClassRanges, range =>
            range.decorClass == DecorClass.MegastationInteriorMajor && range.indexCount > 0);
        Assert.Contains(portal.Mesh.DecorClassRanges, range =>
            range.decorClass == DecorClass.MegastationInteriorMinor && range.indexCount > 0);
        Assert.True(StationDecorator.DecorCastingPolicy[DecorClass.MegastationInteriorMajor]);
        Assert.False(StationDecorator.DecorCastingPolicy[DecorClass.MegastationInteriorMinor]);
        int expectedStructuralCasterFaces = result.BoundaryTopology.Faces.Count(face =>
            face.SpaceKind != MegastationBoundarySpaceKind.InteriorBoundary);
        Assert.Equal(expectedStructuralCasterFaces, structure.HullShadowMesh!.FaceCount);
        Assert.Equal(
            192 + result.InteriorPresentationPlan.ThroatCasterCount * 24,
            result.InteriorPlan.Diagnostics.PortalCasterVertexCount);
        Assert.Equal(
            96 + result.InteriorPresentationPlan.ThroatCasterCount * 12,
            result.InteriorPlan.Diagnostics.PortalCasterTriangleCount);
    }

    [Fact]
    public void H1aChangesPresentationWithoutChangingAcceptedH1Structure()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        Assert.Equal(
            "FFC928024C07BDA30BADE311F5FA86DDFA26EA35EA8253C98840FE02FE7AAC31",
            result.InteriorPlan.Diagnostics.Signature);
        Assert.Equal(
            "BDF65C8AA6211665A4538F136F6F04C0D6ACE2F0B16166FD6E0BBB7954549C43",
            MegastationMassingSignatureBuilder.Compute(result).Body);
        Assert.InRange(result.InteriorPlan.PortalClearSize.X, 163.6f, 163.8f);
        Assert.InRange(result.InteriorPlan.PortalClearSize.Y, 125.6f, 125.8f);

        var (vertices, _) = result.Mesh.ToIntArrays();
        for (int faceIndex = 0; faceIndex < result.BoundaryTopology.Faces.Count; faceIndex++)
        {
            BoundaryFace face = result.BoundaryTopology.Faces[faceIndex];
            byte[] alpha = vertices.Skip(faceIndex * 4).Take(4).Select(vertex => vertex.Color.A).ToArray();
            if (face.SpaceKind == MegastationBoundarySpaceKind.ExteriorBoundary)
                Assert.All(alpha, value => Assert.Equal(0, value));
            else if (face.SpaceKind == MegastationBoundarySpaceKind.EntranceThroatBoundary)
                Assert.All(alpha, value => Assert.InRange(value, (byte)25, (byte)62));
            else
                Assert.All(alpha, value => Assert.InRange(value, (byte)117, byte.MaxValue));
        }
    }

    [Fact]
    public void GuidancePlanIsDeterministicSemanticAndClearOfFlightVolume()
    {
        MegastationPrototypeCpuResult first = NovaResult.Value;
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(
            Nova,
            systemMaterials: MaterialContext);
        MegastationInteriorPresentationPlan a = first.InteriorPresentationPlan;
        MegastationInteriorPresentationPlan b = second.InteriorPresentationPlan;
        Assert.Equal(a.PortalGuidanceSeed, b.PortalGuidanceSeed);
        Assert.Equal(a.ThroatGuidanceSeed, b.ThroatGuidanceSeed);
        Assert.Equal(a.InteriorLandmarkSeed, b.InteriorLandmarkSeed);
        Assert.Equal(a.ThroatLinerSeed, b.ThroatLinerSeed);
        Assert.Equal(a.ThroatRibsSeed, b.ThroatRibsSeed);
        Assert.Equal(a.ThroatMarkingsSeed, b.ThroatMarkingsSeed);
        Assert.Equal(a.ThroatFixturesSeed, b.ThroatFixturesSeed);
        Assert.Equal(a.ApproachGuidanceSeed, b.ApproachGuidanceSeed);
        Assert.Equal(a.Palette, b.Palette);
        Assert.Equal(a.Precinct, b.Precinct);
        Assert.Equal(a.Elements, b.Elements);
        Assert.Equal(a.Markers, b.Markers);
        Assert.Equal(a.ApproachBeams, b.ApproachBeams);
        Assert.Equal(0, a.PortalElementCount);
        Assert.True(a.ThroatElementCount >= 20);
        Assert.Equal(3, a.InteriorLandmarkCount);
        Assert.True(a.Markers.Count >= 27);
        Assert.True(a.ThroatLinerCount >= 8);
        Assert.Equal(0, a.ThroatRibCount);
        Assert.Equal(4, a.ThroatCrownCount);
        Assert.True(a.ThroatFixtureCount >= 20);
        Assert.Equal(16, a.ApproachFixtureElementCount);
        Assert.Equal(4, a.ApproachBeams.Count);
        Assert.True(a.ThroatMarkingCount > 0);

        MegastationInteriorPlan interior = first.InteriorPlan;
        Assert.DoesNotContain(a.Elements, element => element.Kind is
            MegastationInteriorGuidanceKind.PortalEdge
            or MegastationInteriorGuidanceKind.PortalCorner
            or MegastationInteriorGuidanceKind.PortalCrown
            or MegastationInteriorGuidanceKind.ThroatBeam
            or MegastationInteriorGuidanceKind.ThroatRib);
        float innerEnd = -interior.Diagnostics.ThroatLength;
        float outerEnd = a.Precinct.ProjectionLength;
        Assert.All(a.Elements.Where(element =>
            element.Kind == MegastationInteriorGuidanceKind.ThroatBand
            && element.Identity.StartsWith("throat/", StringComparison.Ordinal)), element =>
        {
            float axial = Vector3.Dot(
                element.Centre - interior.PortalCentre,
                interior.OutwardNormal);
            Assert.InRange(axial, innerEnd + .1f, outerEnd - .1f);
        });
        Assert.All(a.Elements, element => Assert.False(
            IntersectsOpenVolume(element, interior.MainFlightVolume, interior)));
        PlacedModule module = MegastationPrototypeGenerator.CreatePlacedModule(first);
        PlacedModule presentationModule = MegastationPrototypeGenerator.CreateInteriorModule(first);
#if DEBUG
        Assert.NotEmpty(presentationModule.NativeInteriorDebugLines!);
#else
        Assert.Null(presentationModule.NativeInteriorDebugLines);
#endif
        StationLightInfo[] guidanceLights = module.GlowLights
            .Skip(first.LightPlan.Lights.Count)
            .ToArray();
        Assert.Equal(a.Markers.Select(marker => marker.Position),
            guidanceLights.Select(light => light.WorldPosition));
        Assert.All(guidanceLights, light =>
            Assert.Equal(GlowType.MegastationEntranceGuidance, light.Type));
        StationLightInfo[] recessHalos = guidanceLights
            .Where(light => light.PresentationSizePixels == 90f)
            .ToArray();
        Assert.Equal(a.Markers.Count(marker => marker.Identity.StartsWith(
            "throat/recess:", StringComparison.Ordinal)), recessHalos.Length);
        Assert.NotEmpty(recessHalos);
        Assert.All(recessHalos, light =>
        {
            Assert.Equal(1f, light.BaseIntensity);
            Assert.Equal(90f, light.PresentationSizePixels);
            Assert.Equal(220f, light.PresentationFadeStartMeters);
            Assert.Equal(1_500f, light.PresentationFadeEndMeters);
        });
        Assert.All(a.Markers.Where(marker => marker.Identity.StartsWith(
            "entrance/approach/source:", StringComparison.Ordinal)), marker =>
        {
            Assert.Equal(24f, marker.GlowSizePixels);
            Assert.Equal(500f, marker.GlowFadeStartMeters);
            Assert.Equal(3_000f, marker.GlowFadeEndMeters);
        });
    }

    [Fact]
    public void ApproachBeamsUsePortalFrameUniversalColoursAndClearCrownMountedFixtures()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        MegastationApproachGuidanceBeam[] beams = presentation.ApproachBeams.ToArray();
        MegastationInteriorGuidanceMarker[] sourceMarkers = presentation.Markers
            .Where(marker => marker.Identity.StartsWith(
                "entrance/approach/source:",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, beams.Length);
        Assert.Equal(4, sourceMarkers.Length);
        Assert.Equal(2, sourceMarkers.Count(marker =>
            marker.Colour == MegastationInteriorPresentationPlanner.ApproachUpColour));
        Assert.Equal(2, sourceMarkers.Count(marker =>
            marker.Colour == MegastationInteriorPresentationPlanner.ApproachDownColour));
        Assert.Equal(2, beams.Count(beam =>
            beam.Vertical == MegastationApproachBeamVertical.Upper));
        Assert.Equal(2, beams.Count(beam =>
            beam.Vertical == MegastationApproachBeamVertical.Lower));
        Assert.Equal(new[] { -1, 1 }, beams.Select(beam => beam.HorizontalSign)
            .Distinct().Order().ToArray());
        Assert.All(beams, beam =>
        {
            Assert.True(Vector3.Dot(Vector3.Normalize(beam.Axis), interior.OutwardNormal) > .9999f);
            Assert.True(Vector3.Dot(Vector3.Normalize(beam.RadialUp), interior.PortalUp) > .9999f);
            Assert.True(Vector3.Dot(Vector3.Normalize(beam.RadialRight), interior.PortalRight) > .9999f);
            Assert.InRange(beam.Length, 1_400f, 1_600f);
            Assert.InRange(beam.HalfAngleDegrees, .7f, 1.2f);
            Assert.Equal(
                beam.Vertical == MegastationApproachBeamVertical.Upper
                    ? MegastationInteriorPresentationPlanner.ApproachUpColour
                    : MegastationInteriorPresentationPlanner.ApproachDownColour,
                beam.Colour);

            Vector3 fromMouth = beam.Source - presentation.Precinct.OuterMouthCentre;
            Assert.True(Vector3.Dot(fromMouth, interior.OutwardNormal) > 0f);
            Assert.True(MathF.Abs(Vector3.Dot(fromMouth, interior.PortalRight))
                > interior.PortalClearSize.X * .5f);
            float signedUp = Vector3.Dot(fromMouth, interior.PortalUp);
            Assert.True(MathF.Abs(signedUp) > interior.PortalClearSize.Y * .5f);
            Assert.Equal(beam.Vertical == MegastationApproachBeamVertical.Upper,
                signedUp > 0f);
        });

        MegastationInteriorGuidanceElement[] fixtureParts = presentation.Elements
            .Where(element => element.Kind == MegastationInteriorGuidanceKind.ApproachFixture)
            .ToArray();
        Assert.Equal(16, fixtureParts.Length);
        float wallThickness = TubeWallThickness(presentation);
        Vector3 extensionCentre = (interior.PortalCentre
            + presentation.Precinct.OuterMouthCentre) * .5f;
        Vector3 clearHalf = Abs(interior.PortalRight)
                * (interior.PortalClearSize.X * .5f - wallThickness)
            + Abs(interior.PortalUp)
                * (interior.PortalClearSize.Y * .5f - wallThickness)
            + Abs(interior.OutwardNormal)
                * (presentation.Precinct.ProjectionLength * .5f);
        Assert.All(fixtureParts, element => Assert.False(IntersectsOpenBounds(
            element,
            extensionCentre - clearHalf,
            extensionCentre + clearHalf)));

        Assert.Equal(4, result.InteriorPlan.Diagnostics.ApproachBeamCount);
        Assert.Equal(16, result.InteriorPlan.Diagnostics.ApproachFixtureElementCount);
        Assert.Equal(result.ApproachBeamVertices.Length,
            result.InteriorPlan.Diagnostics.ApproachBeamVertexCount);
        Assert.Equal(result.ApproachBeamVertices.Length / 3,
            result.InteriorPlan.Diagnostics.ApproachBeamTriangleCount);
        Assert.Equal(interior.PortalUp, result.InteriorPlan.Diagnostics.EntrancePortalUp);
        Assert.Equal(interior.PortalRight, result.InteriorPlan.Diagnostics.EntrancePortalRight);
    }

    [Fact]
    public void ApproachBeamMeshIsFiniteSoftFadedAndAddsNoTextureOwnership()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        VertexPositionColor[] vertices = result.ApproachBeamVertices;
        Assert.NotEmpty(vertices);
        Assert.Equal(0, vertices.Length % 3);
        Assert.All(vertices, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.True(float.IsFinite(vertex.Position.Y));
            Assert.True(float.IsFinite(vertex.Position.Z));
        });
        Assert.Contains(vertices, vertex => vertex.Color.A == 0);
        Assert.Contains(vertices, vertex => vertex.Color.A > 0);

        foreach (MegastationApproachGuidanceBeam beam in result.InteriorPresentationPlan.ApproachBeams)
        {
            VertexPositionColor[] colourVertices = vertices.Where(vertex =>
                    vertex.Color.R == beam.Colour.R
                    && vertex.Color.G == beam.Colour.G
                    && vertex.Color.B == beam.Colour.B)
                .ToArray();
            Assert.NotEmpty(colourVertices);
            float maximumAxial = colourVertices.Max(vertex => Vector3.Dot(
                vertex.Position - beam.Source,
                beam.Axis));
            Assert.Equal(beam.Length, maximumAxial, 2);
            Assert.Contains(colourVertices, vertex =>
                vertex.Color.A == 0
                && Vector3.Dot(vertex.Position - beam.Source, beam.Axis)
                    >= beam.Length - .01f);
        }

        PlacedModule module = MegastationPrototypeGenerator.CreateInteriorModule(result);
        Assert.Same(result.ApproachBeamVertices, module.NativeApproachBeamVertices);
        Assert.Null(module.TextureInstance);
        Assert.Null(module.MaterialInstance);
    }

    [Fact]
    public void ConstructedThroatUsesNormalMaterialsSelectiveShadowsAndLuminousMarkings()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        MegastationInteriorGuidanceElement[] constructed = presentation.Elements
            .Where(element => element.Kind is
                MegastationInteriorGuidanceKind.ThroatLiner
                or MegastationInteriorGuidanceKind.ThroatBeam
                or MegastationInteriorGuidanceKind.ThroatRib
                or MegastationInteriorGuidanceKind.ThroatTransition)
            .ToArray();
        MegastationInteriorGuidanceElement[] markings = presentation.Elements
            .Where(element => element.Kind == MegastationInteriorGuidanceKind.ThroatBand)
            .ToArray();

        Assert.NotEmpty(constructed);
        Assert.All(constructed, element =>
        {
            Assert.True(element.CastsShadow);
            Assert.Equal(0f, element.Illumination);
            Assert.True(Math.Max(element.Colour.R,
                Math.Max(element.Colour.G, element.Colour.B)) >= 76);
            Assert.Contains(element.MaterialFamily, new[]
            {
                SystemMaterialFamilyId.HeavyIndustrialPlate,
                SystemMaterialFamilyId.DullStructuralMetal,
                SystemMaterialFamilyId.CleanTechnicalAlloy,
            });
        });
        Assert.Contains(markings, element => element.Illumination >= .9f);
        Assert.All(markings, element => Assert.True(element.Illumination >= .9f));
        Assert.All(markings, element => Assert.False(element.CastsShadow));
        Vector3 throatSize = result.InteriorPlan.ThroatVolume.Size;
        Vector3 portalRight = Abs(result.InteriorPlan.PortalRight);
        float wallThickness = TubeWallThickness(presentation);
        float throatHalfWidth = (throatSize.X * portalRight.X
            + throatSize.Y * portalRight.Y
            + throatSize.Z * portalRight.Z) * .5f - wallThickness;
        float throatHalfHeight = (throatSize.X * MathF.Abs(result.InteriorPlan.PortalUp.X)
            + throatSize.Y * MathF.Abs(result.InteriorPlan.PortalUp.Y)
            + throatSize.Z * MathF.Abs(result.InteriorPlan.PortalUp.Z)) * .5f - wallThickness;
        Assert.All(markings.Where(element =>
            element.Identity.StartsWith("throat/fixture:", StringComparison.Ordinal)), element =>
        {
            bool side = element.Identity.EndsWith("/left", StringComparison.Ordinal)
                || element.Identity.EndsWith("/right", StringComparison.Ordinal);
            Vector3 axis = side ? result.InteriorPlan.PortalRight : result.InteriorPlan.PortalUp;
            float halfClear = side ? throatHalfWidth : throatHalfHeight;
            float memberSize = side ? element.Size.X : element.Size.Y;
            float centreFromAxis = MathF.Abs(Vector3.Dot(
                element.Centre - result.InteriorPlan.PortalCentre, axis));
            float tunnelFacingSurface = centreFromAxis - memberSize * .5f;
            Assert.InRange(tunnelFacingSurface - halfClear, 1f, 4f);
        });
        PlacedModule module = MegastationPrototypeGenerator.CreateInteriorModule(result);
        Assert.Null(module.TextureInstance);
        Assert.Null(module.MaterialInstance);
        Assert.True(module.UsesDecorationVertexIllumination);
        Assert.True(module.UsesCoplanarStructuralOverlay);
        Assert.True(SystemSpaceState.H1CoplanarOverlayClipDepthBias > 0f);
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Full));
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Medium));
        Assert.False(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Minimal));
        Assert.InRange(
            module.DecorationMaterialRanges.Count,
            1,
            Enum.GetValues<SystemMaterialFamilyId>().Length);
    }

    [Fact]
    public void ConstructedThroatIsAContinuousThickOpenTubeWithRecessedFixtures()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        float outerWidth = Vector3.Dot(Abs(interior.PortalRight), interior.ThroatVolume.Size);
        float outerHeight = Vector3.Dot(Abs(interior.PortalUp), interior.ThroatVolume.Size);
        float wallThickness = TubeWallThickness(presentation);
        float width = outerWidth - wallThickness * 2f;
        float height = outerHeight - wallThickness * 2f;
        float innerAxial = -interior.Diagnostics.ThroatLength;
        float outerAxial = presentation.Precinct.ProjectionLength;
        string[] sides = ["left", "right", "ceiling", "floor"];

        MegastationInteriorGuidanceElement[] tubeAndFixtures = presentation.Elements
            .Where(element => element.Identity.StartsWith("throat/tube/", StringComparison.Ordinal)
                || element.Identity.StartsWith("throat/fixture:", StringComparison.Ordinal))
            .ToArray();
        foreach (string side in sides)
        {
            MegastationInteriorGuidanceElement[] coverage = tubeAndFixtures
                .Where(element => element.Identity.EndsWith($"/{side}", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(coverage);
            for (float axial = innerAxial + .25f; axial < outerAxial; axial += .5f)
            {
                Assert.Contains(coverage, element =>
                {
                    float centre = Vector3.Dot(
                        element.Centre - interior.PortalCentre,
                        interior.OutwardNormal);
                    return axial >= centre - element.Size.Z * .5f - .001f
                        && axial <= centre + element.Size.Z * .5f + .001f;
                });
            }
        }

        MegastationInteriorGuidanceElement leftWall = presentation.Elements.First(element =>
            element.Identity.StartsWith("throat/tube/", StringComparison.Ordinal)
            && element.Identity.EndsWith("/left", StringComparison.Ordinal));
        MegastationInteriorGuidanceElement ceiling = presentation.Elements.First(element =>
            element.Identity.StartsWith("throat/tube/", StringComparison.Ordinal)
            && element.Identity.EndsWith("/ceiling", StringComparison.Ordinal));
        Assert.InRange(leftWall.Size.X, 10f, 16.01f);
        Assert.InRange(ceiling.Size.Y, 10f, 16.01f);
        Assert.InRange(MathF.Abs(ceiling.Size.X - outerWidth), 0f, .001f);
        float leftOuterExtent = MathF.Abs(Vector3.Dot(
            leftWall.Centre - interior.PortalCentre,
            interior.PortalRight)) + leftWall.Size.X * .5f;
        Assert.InRange(MathF.Abs(leftOuterExtent - outerWidth * .5f), 0f, .001f);
        Assert.InRange(MathF.Abs(width - (outerWidth - wallThickness * 2f)), 0f, .001f);
        Assert.InRange(MathF.Abs(height - (outerHeight - wallThickness * 2f)), 0f, .001f);
        Assert.True(width >= LargestShipWidth * 1.5f);
        Assert.True(height >= LargestShipHeight * 1.5f);

        MegastationInteriorGuidanceElement[] fixtures = presentation.Elements
            .Where(element => element.Identity.StartsWith(
                "throat/fixture:", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(fixtures);
        (Vector3 clearMinimum, Vector3 clearMaximum) = ConstructedThroatClearBounds(
            interior,
            wallThickness);
        Assert.All(fixtures, element => Assert.False(
            IntersectsOpenBounds(element, clearMinimum, clearMaximum)));
        Assert.All(fixtures.Where(element => element.Identity.EndsWith("/left", StringComparison.Ordinal)
            || element.Identity.EndsWith("/right", StringComparison.Ordinal)), element =>
                Assert.True(element.Size.Y < height));
        Assert.All(fixtures.Where(element => element.Identity.EndsWith("/ceiling", StringComparison.Ordinal)
            || element.Identity.EndsWith("/floor", StringComparison.Ordinal)), element =>
                Assert.True(element.Size.X < width));

        MegastationInteriorGuidanceElement[] crown = presentation.Elements
            .Where(element => element.Identity.StartsWith("entrance/crown/", StringComparison.Ordinal)
                && element.Kind == MegastationInteriorGuidanceKind.ThroatTransition)
            .ToArray();
        Assert.Equal(4, crown.Length);
        Assert.All(crown, element => Assert.True(element.Size.Z >= 28f));
        Assert.DoesNotContain(presentation.Elements, element =>
            element.Kind is MegastationInteriorGuidanceKind.ThroatBeam
                or MegastationInteriorGuidanceKind.ThroatRib);
    }

    [Fact]
    public void EveryThroatLightIsASealedBlindWellWithTintedSidesAndGatedHalo()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        float outerWidth = Vector3.Dot(Abs(interior.PortalRight), interior.ThroatVolume.Size);
        float outerHeight = Vector3.Dot(Abs(interior.PortalUp), interior.ThroatVolume.Size);
        MegastationInteriorGuidanceElement leftWall = presentation.Elements.First(element =>
            element.Identity.StartsWith("throat/tube/", StringComparison.Ordinal)
            && element.Identity.EndsWith("/left", StringComparison.Ordinal));
        float wallThickness = leftWall.Size.X;
        float width = outerWidth - wallThickness * 2f;
        float height = outerHeight - wallThickness * 2f;
        (Vector3 clearMinimum, Vector3 clearMaximum) = ConstructedThroatClearBounds(
            interior,
            wallThickness);
        MegastationInteriorGuidanceElement[] backplates = presentation.Elements
            .Where(element => element.Identity.StartsWith(
                "throat/fixture:", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(backplates);

        foreach (MegastationInteriorGuidanceElement backplate in backplates)
        {
            string suffix = backplate.Identity["throat/fixture:".Length..];
            string recessIdentity = $"throat/recess:{suffix}";
            MegastationInteriorGuidanceElement seal = Assert.Single(
                presentation.Elements,
                element => element.Identity == $"{recessIdentity}/seal");
            MegastationInteriorGuidanceElement[] wellSides = presentation.Elements
                .Where(element => element.Identity.StartsWith(
                    $"{recessIdentity}/well/", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(8, wellSides.Length);
            MegastationInteriorGuidanceElement[] outerBounce = wellSides
                .Where(side => side.Identity.EndsWith("/outer", StringComparison.Ordinal))
                .ToArray();
            MegastationInteriorGuidanceElement[] deepBounce = wellSides
                .Where(side => side.Identity.EndsWith("/deep", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(4, outerBounce.Length);
            Assert.Equal(4, deepBounce.Length);
            Assert.All(outerBounce, side =>
            {
                Assert.Equal(.14f, side.Illumination);
                Assert.False(side.CastsShadow);
            });
            Assert.All(deepBounce, side =>
            {
                Assert.Equal(.32f, side.Illumination);
                Assert.False(side.CastsShadow);
            });

            bool vertical = suffix.EndsWith("/left", StringComparison.Ordinal)
                || suffix.EndsWith("/right", StringComparison.Ordinal);
            Vector3 axis = vertical ? interior.PortalRight : interior.PortalUp;
            float halfClear = (vertical ? width : height) * .5f;
            float backplateRadialSize = vertical ? backplate.Size.X : backplate.Size.Y;
            float sealRadialSize = vertical ? seal.Size.X : seal.Size.Y;
            float transverseSpan = vertical ? backplate.Size.Y : backplate.Size.X;
            float backplateDistance = MathF.Abs(Vector3.Dot(
                backplate.Centre - interior.PortalCentre, axis));
            float sealDistance = MathF.Abs(Vector3.Dot(
                seal.Centre - interior.PortalCentre, axis));
            float recessDepth = backplateDistance - backplateRadialSize * .5f - halfClear;
            float remainingWall = sealRadialSize;
            float outerExtent = sealDistance + sealRadialSize * .5f;
            Assert.True(recessDepth > 1f);
            Assert.True(remainingWall > 1f);
            Assert.True(outerExtent <= halfClear + wallThickness + .001f);
            Assert.True(sealDistance - sealRadialSize * .5f
                >= backplateDistance + backplateRadialSize * .5f - .001f);
            Assert.False(IntersectsOpenBounds(backplate, clearMinimum, clearMaximum));
            Assert.False(IntersectsOpenBounds(seal, clearMinimum, clearMaximum));
            Assert.All(wellSides, side => Assert.False(
                IntersectsOpenBounds(side, clearMinimum, clearMaximum)));
            foreach (IGrouping<string, MegastationInteriorGuidanceElement> pair in wellSides
                         .GroupBy(side => side.Identity[..side.Identity.LastIndexOf('/')]))
            {
                MegastationInteriorGuidanceElement outer = Assert.Single(
                    pair,
                    side => side.Identity.EndsWith("/outer", StringComparison.Ordinal));
                MegastationInteriorGuidanceElement deep = Assert.Single(
                    pair,
                    side => side.Identity.EndsWith("/deep", StringComparison.Ordinal));
                float outerSize = vertical ? outer.Size.X : outer.Size.Y;
                float deepSize = vertical ? deep.Size.X : deep.Size.Y;
                float outerDistance = MathF.Abs(Vector3.Dot(
                    outer.Centre - interior.PortalCentre, axis));
                float deepDistance = MathF.Abs(Vector3.Dot(
                    deep.Centre - interior.PortalCentre, axis));
                Assert.InRange(MathF.Abs(outerSize + deepSize - recessDepth), 0f, .001f);
                Assert.InRange(MathF.Abs(
                    outerDistance + outerSize * .5f
                    - (deepDistance - deepSize * .5f)), 0f, .001f);
            }

            int expectedHaloCount = transverseSpan >= 80f ? 3 : transverseSpan >= 40f ? 2 : 1;
            MegastationInteriorGuidanceMarker[] halos = presentation.Markers
                .Where(marker => marker.Identity.StartsWith(
                    $"{recessIdentity}/halo:", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(expectedHaloCount, halos.Length);
            Assert.All(halos, halo =>
            {
                Assert.NotNull(halo.SurfaceNormal);
                Assert.Equal(1f, halo.Intensity);
                Assert.Equal(90f, halo.GlowSizePixels);
                Assert.Equal(220f, halo.GlowFadeStartMeters);
                Assert.Equal(1_500f, halo.GlowFadeEndMeters);
                Assert.InRange(Vector3.Distance(halo.Position, backplate.Centre), 1f,
                    transverseSpan * .5f);
            });
        }
    }

    [Fact]
    public void ConstructedThroatPreservesFlightClearanceAndTracksEntranceStructuralSignature()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        Assert.Equal(
            "FFC928024C07BDA30BADE311F5FA86DDFA26EA35EA8253C98840FE02FE7AAC31",
            result.InteriorPlan.Diagnostics.Signature);
        Assert.Equal(
            "BDF65C8AA6211665A4538F136F6F04C0D6ACE2F0B16166FD6E0BBB7954549C43",
            MegastationMassingSignatureBuilder.Compute(result).Body);
        Assert.All(result.InteriorPresentationPlan.Elements.Where(element => element.Kind is
            MegastationInteriorGuidanceKind.ThroatLiner
            or MegastationInteriorGuidanceKind.ThroatBeam
            or MegastationInteriorGuidanceKind.ThroatRib
            or MegastationInteriorGuidanceKind.ThroatTransition
            or MegastationInteriorGuidanceKind.ThroatMarking), element =>
            {
                float wallThickness = TubeWallThickness(result.InteriorPresentationPlan);
                (Vector3 minimum, Vector3 maximum) = ConstructedThroatClearBounds(
                    result.InteriorPlan,
                    wallThickness);
                Assert.False(IntersectsOpenBounds(element, minimum, maximum));
            });
    }

    [Fact]
    public void ProjectedEntranceUsesStableFractionOfLocalSkylineAndReservesItsApproach()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationEntrancePrecinct precinct = result.InteriorPresentationPlan.Precinct;
        float mouthProjection = Vector3.Dot(
            precinct.OuterMouthCentre, interior.OutwardNormal);
        float portalProjection = Vector3.Dot(interior.PortalCentre, interior.OutwardNormal);
        Assert.Equal(precinct.LocalObstructionProjection - portalProjection,
            precinct.LocalSkylineHeight, 3);
        Assert.InRange(precinct.ProjectionHeightFraction, .25f, .75f);
        Assert.Equal(MathF.Max(55f,
                precinct.LocalSkylineHeight * precinct.ProjectionHeightFraction),
            precinct.ProjectionLength, 3);
        Assert.Equal(portalProjection + precinct.ProjectionLength, mouthProjection, 3);
        Assert.True(precinct.ProjectionLength >= 55f);
        MegastationInteriorGuidanceElement[] extension = result.InteriorPresentationPlan.Elements
            .Where(element => element.Identity.StartsWith("entrance/", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(extension);
        Vector3 extensionCentre = (interior.PortalCentre + precinct.OuterMouthCentre) * .5f;
        float wallThickness = TubeWallThickness(result.InteriorPresentationPlan);
        Vector3 clearHalf = Abs(interior.PortalRight)
                * (interior.PortalClearSize.X * .5f - wallThickness)
            + Abs(interior.PortalUp)
                * (interior.PortalClearSize.Y * .5f - wallThickness)
            + Abs(interior.OutwardNormal) * (precinct.ProjectionLength * .5f);
        Vector3 clearMinimum = extensionCentre - clearHalf;
        Vector3 clearMaximum = extensionCentre + clearHalf;
        Assert.All(extension, element => Assert.False(
            IntersectsOpenBounds(element, clearMinimum, clearMaximum)));
        MegastationInteriorGuidanceElement[] crown = extension
            .Where(element => element.Identity.StartsWith(
                "entrance/crown/", StringComparison.Ordinal)
                && element.Kind == MegastationInteriorGuidanceKind.ThroatTransition)
            .ToArray();
        Assert.Equal(4, crown.Length);
        Assert.All(crown, element => Assert.False(
            IntersectsOpenBounds(element, clearMinimum, clearMaximum)));
        AssertExteriorCompositionRespectsPrecinct(result);
    }

    [Fact]
    public void GuidancePaletteIsCuratedAndTransverseGeometryIsAxisAligned()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationInteriorPresentationPlan presentation = result.InteriorPresentationPlan;
        string[] curated = ["amber", "cyan", "red-orange", "green", "violet", "blue-white", "magenta"];
        Assert.Contains(presentation.Palette.Identity, curated);
        HashSet<string> sampled = Enumerable.Range(1, 24)
            .Select(seed => MegastationInteriorPresentationPlanner.Plan(
                result.InteriorPlan with { Seed = seed }).Palette.Identity)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(sampled.Count >= 4);

        Assert.DoesNotContain(presentation.Elements, element =>
            element.Identity.Contains("painted", StringComparison.Ordinal)
            || element.Identity.Contains("diagonal", StringComparison.Ordinal));
        foreach (MegastationInteriorGuidanceElement element in presentation.Elements.Where(
                     element => element.Kind == MegastationInteriorGuidanceKind.ThroatBand))
        {
            Vector3 x = new(element.Frame.M11, element.Frame.M12, element.Frame.M13);
            Vector3 y = new(element.Frame.M21, element.Frame.M22, element.Frame.M23);
            Vector3 z = new(element.Frame.M31, element.Frame.M32, element.Frame.M33);
            Assert.True(MathF.Abs(Vector3.Dot(x, result.InteriorPlan.PortalRight)) > .999f);
            Assert.True(MathF.Abs(Vector3.Dot(y, result.InteriorPlan.PortalUp)) > .999f);
            Assert.True(MathF.Abs(Vector3.Dot(z, result.InteriorPlan.OutwardNormal)) > .999f);
        }
    }

    [Fact]
    public void PortalPresentationMeshHasFiniteNonDegenerateTriangles()
    {
        foreach (MegastationPrototypeCpuResult result in new[]
                 {
                     NovaResult.Value,
                     GrandResult.Value,
                 })
        {
            Assert.All(result.InteriorPresentationPlan.Elements, element =>
            {
                Vector3 x = new(element.Frame.M11, element.Frame.M12, element.Frame.M13);
                Vector3 y = new(element.Frame.M21, element.Frame.M22, element.Frame.M23);
                Vector3 z = new(element.Frame.M31, element.Frame.M32, element.Frame.M33);
                Assert.True(Vector3.Dot(Vector3.Cross(x, y), z) > .999f,
                    $"Mirrored presentation frame: {element.Identity}");
            });

            StationModuleMesh mesh = result.InteriorMesh;
            var (vertices, indices) = mesh.ToIntArrays();
            Assert.NotEmpty(vertices);
            Assert.Equal(0, indices.Length % 3);
            Assert.All(indices, index => Assert.InRange(index, 0, vertices.Length - 1));
            Assert.All(vertices, vertex =>
            {
                Assert.True(IsFinite(vertex.Position));
                Assert.True(IsFinite(vertex.Normal));
            });
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 a = vertices[indices[index]].Position;
                Vector3 b = vertices[indices[index + 1]].Position;
                Vector3 c = vertices[indices[index + 2]].Position;
                Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > 1e-6f);
            }
        }
    }

    [Fact]
    public void InteriorPlanAndSignatureAreDeterministic()
    {
        MegastationPrototypeCpuResult first = NovaResult.Value;
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(Nova);
        Assert.Equal(first.InteriorPlan.PortalDirection, second.InteriorPlan.PortalDirection);
        Assert.Equal(first.InteriorPlan.PortalCentre, second.InteriorPlan.PortalCentre);
        Assert.Equal(first.InteriorPlan.PortalClearSize, second.InteriorPlan.PortalClearSize);
        Assert.Equal(first.InteriorPlan.ProtectedCells, second.InteriorPlan.ProtectedCells);
        Assert.Equal(first.InteriorPlan.Diagnostics.Signature, second.InteriorPlan.Diagnostics.Signature);
    }

    private static readonly (int dx, int dy, int dz)[] Neighbours =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    private static void AssertLargeShipClearance(MegastationInteriorPlan plan)
    {
        Assert.True(plan.PortalClearSize.X >= LargestShipWidth * 1.5f);
        Assert.True(plan.PortalClearSize.Y >= LargestShipHeight * 1.5f);
        Assert.True(plan.Diagnostics.ThroatLength >= LargestShipLength * 1.5f,
            $"throat={plan.Diagnostics.ThroatLength:F1}m, required={LargestShipLength * 1.5f:F1}m");
        Vector3 clear = plan.MainFlightVolume.Size;
        Assert.True(clear.X >= LargestShipWidth * 3f || clear.Z >= LargestShipWidth * 3f);
        Assert.True(clear.Y >= LargestShipHeight * 3f);
        Assert.True(MathF.Max(clear.X, clear.Z) >= LargestShipLength * 3f);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3[] ElementCorners(MegastationInteriorGuidanceElement element)
    {
        Vector3 x = new(element.Frame.M11, element.Frame.M12, element.Frame.M13);
        Vector3 y = new(element.Frame.M21, element.Frame.M22, element.Frame.M23);
        Vector3 z = new(element.Frame.M31, element.Frame.M32, element.Frame.M33);
        Vector3 halfX = x * element.Size.X * .5f;
        Vector3 halfY = y * element.Size.Y * .5f;
        Vector3 halfZ = z * element.Size.Z * .5f;
        return
        [
            element.Centre - halfX - halfY - halfZ,
            element.Centre + halfX - halfY - halfZ,
            element.Centre - halfX + halfY - halfZ,
            element.Centre + halfX + halfY - halfZ,
            element.Centre - halfX - halfY + halfZ,
            element.Centre + halfX - halfY + halfZ,
            element.Centre - halfX + halfY + halfZ,
            element.Centre + halfX + halfY + halfZ,
        ];
    }

    private static (Vector3 Minimum, Vector3 Maximum) ElementBounds(
        MegastationInteriorGuidanceElement element)
    {
        Vector3[] corners = ElementCorners(element);
        return (
            new Vector3(
                corners.Min(point => point.X),
                corners.Min(point => point.Y),
                corners.Min(point => point.Z)),
            new Vector3(
                corners.Max(point => point.X),
                corners.Max(point => point.Y),
                corners.Max(point => point.Z)));
    }

    private static float AxisSpan(IEnumerable<Vector3> points, Vector3 axis)
    {
        float[] projections = points.Select(point => Vector3.Dot(point, axis)).ToArray();
        return projections.Max() - projections.Min();
    }

    private static void AssertBoundsContain(
        Vector3 outerMinimum,
        Vector3 outerMaximum,
        Vector3 innerMinimum,
        Vector3 innerMaximum)
    {
        const float tolerance = .01f;
        Assert.True(innerMinimum.X >= outerMinimum.X - tolerance);
        Assert.True(innerMinimum.Y >= outerMinimum.Y - tolerance);
        Assert.True(innerMinimum.Z >= outerMinimum.Z - tolerance);
        Assert.True(innerMaximum.X <= outerMaximum.X + tolerance);
        Assert.True(innerMaximum.Y <= outerMaximum.Y + tolerance);
        Assert.True(innerMaximum.Z <= outerMaximum.Z + tolerance);
    }

    private static bool PointInsideBounds(Vector3 point, Vector3 minimum, Vector3 maximum)
        => point.X >= minimum.X && point.X <= maximum.X
            && point.Y >= minimum.Y && point.Y <= maximum.Y
            && point.Z >= minimum.Z && point.Z <= maximum.Z;

    private static void AssertExteriorCompositionRespectsPrecinct(
        MegastationPrototypeCpuResult result)
    {
        MegastationEntrancePrecinct precinct = result.InteriorPlan.EntrancePrecinct;
        MegastationProtectedVolume protectedVolume = Assert.Single(
            result.AttachmentPlan.EffectiveProtectedVolumes,
            volume => volume.Identity == "interior/entrance-precinct");
        Assert.Equal(precinct.Minimum, protectedVolume.Minimum);
        Assert.Equal(precinct.Maximum, protectedVolume.Maximum);
        Assert.All(result.AttachmentPlan.Placements, placement => Assert.False(
            precinct.Intersects(placement.AabbMin, placement.AabbMax)));
        Assert.All(result.WindowPlan.Windows, window => Assert.False(
            precinct.Contains(window.Centre)));
        Assert.All(result.LightPlan.Lights, light => Assert.False(
            precinct.Contains(light.SurfacePosition)));
        Assert.All(result.InfrastructurePlan.Clusters, cluster => Assert.False(
            precinct.Intersects(cluster.AabbMin, cluster.AabbMax)));
        Assert.All(result.FabricPlan.Instances, instance => Assert.False(
            precinct.Intersects(instance.AabbMin, instance.AabbMax)));
        Assert.All(result.MegaGreeblePlan.Instances, instance => Assert.False(
            precinct.Contains(instance.SurfacePosition)));
        Assert.All(result.ServiceChannelPlan.Networks, network =>
        {
            Assert.All(network.Runs, run =>
            {
                Assert.False(precinct.Contains(ServicePoint(network, run.Start)));
                Assert.False(precinct.Contains(ServicePoint(network, run.End)));
            });
            Assert.All(network.Nodes, node => Assert.False(
                precinct.Contains(ServicePoint(network, node.Position))));
        });
    }

    private static float TubeWallThickness(MegastationInteriorPresentationPlan presentation)
        => presentation.Elements.First(element =>
            element.Identity.StartsWith("throat/tube/", StringComparison.Ordinal)
            && element.Identity.EndsWith("/left", StringComparison.Ordinal)).Size.X;

    private static (Vector3 Minimum, Vector3 Maximum) ConstructedThroatClearBounds(
        MegastationInteriorPlan interior,
        float wallThickness)
    {
        Vector3 centre = (interior.ThroatVolume.Minimum + interior.ThroatVolume.Maximum) * .5f;
        float width = Vector3.Dot(Abs(interior.PortalRight), interior.ThroatVolume.Size)
            - wallThickness * 2f;
        float height = Vector3.Dot(Abs(interior.PortalUp), interior.ThroatVolume.Size)
            - wallThickness * 2f;
        float length = Vector3.Dot(Abs(interior.OutwardNormal), interior.ThroatVolume.Size);
        Vector3 half = Abs(interior.PortalRight) * (width * .5f)
            + Abs(interior.PortalUp) * (height * .5f)
            + Abs(interior.OutwardNormal) * (length * .5f);
        return (centre - half, centre + half);
    }

    private static bool IntersectsOpenVolume(
        MegastationInteriorGuidanceElement element,
        MegastationInteriorVolume volume,
        MegastationInteriorPlan plan)
    {
        Vector3 x = new(element.Frame.M11, element.Frame.M12, element.Frame.M13);
        Vector3 y = new(element.Frame.M21, element.Frame.M22, element.Frame.M23);
        Vector3 z = new(element.Frame.M31, element.Frame.M32, element.Frame.M33);
        Vector3 half = Abs(x) * element.Size.X * .5f
            + Abs(y) * element.Size.Y * .5f
            + Abs(z) * element.Size.Z * .5f;
        Vector3 minimum = element.Centre - half;
        Vector3 maximum = element.Centre + half;
        const float tolerance = .01f;
        return minimum.X < volume.Maximum.X - tolerance && maximum.X > volume.Minimum.X + tolerance
            && minimum.Y < volume.Maximum.Y - tolerance && maximum.Y > volume.Minimum.Y + tolerance
            && minimum.Z < volume.Maximum.Z - tolerance && maximum.Z > volume.Minimum.Z + tolerance;
    }

    private static bool IntersectsOpenBounds(
        MegastationInteriorGuidanceElement element,
        Vector3 volumeMinimum,
        Vector3 volumeMaximum)
    {
        Vector3 x = new(element.Frame.M11, element.Frame.M12, element.Frame.M13);
        Vector3 y = new(element.Frame.M21, element.Frame.M22, element.Frame.M23);
        Vector3 z = new(element.Frame.M31, element.Frame.M32, element.Frame.M33);
        Vector3 half = Abs(x) * element.Size.X * .5f
            + Abs(y) * element.Size.Y * .5f
            + Abs(z) * element.Size.Z * .5f;
        Vector3 minimum = element.Centre - half;
        Vector3 maximum = element.Centre + half;
        const float tolerance = .01f;
        return minimum.X < volumeMaximum.X - tolerance && maximum.X > volumeMinimum.X + tolerance
            && minimum.Y < volumeMaximum.Y - tolerance && maximum.Y > volumeMinimum.Y + tolerance
            && minimum.Z < volumeMaximum.Z - tolerance && maximum.Z > volumeMinimum.Z + tolerance;
    }

    private static Vector3 Abs(Vector3 value)
        => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));

    private static Vector3 ServicePoint(
        MegastationServiceChannelNetwork network,
        Vector2 position)
        => network.Normal * network.PlaneCoordinateMetres
            + network.TangentU * position.X
            + network.TangentV * position.Y;

    private static bool ProtectedCellsAreConnected(MegastationInteriorPlan plan)
    {
        HashSet<MegacellCoord> remaining = plan.ProtectedCells.Select(cell => cell.Cell).ToHashSet();
        var queue = new Queue<MegacellCoord>();
        MegacellCoord first = remaining.First();
        remaining.Remove(first);
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            MegacellCoord cell = queue.Dequeue();
            foreach ((int dx, int dy, int dz) in Neighbours)
            {
                var next = new MegacellCoord(cell.X + dx, cell.Y + dy, cell.Z + dz);
                if (remaining.Remove(next)) queue.Enqueue(next);
            }
        }
        return remaining.Count == 0;
    }

    private static bool IsGridBoundary(SliceGrid grid, MegacellCoord cell)
        => cell.X == 0 || cell.X == grid.XCount - 1
            || cell.Y == 0 || cell.Y == grid.YCount - 1
            || cell.Z == 0 || cell.Z == grid.ZCount - 1;
}
