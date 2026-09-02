using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class BolonMegastationTests
{
    [Fact]
    public void ArchetypeSelectionIsDeterministicAndApproximatelyHalfQuarterQuarter()
    {
        const int count = 20_000;
        MegastationArchetype[] first = Enumerable.Range(0, count)
            .Select(index => MegastationArchetypeSelector.ForIdentity(
                $"B0 Distribution:System:Station {index:D5}"))
            .ToArray();
        MegastationArchetype[] second = Enumerable.Range(0, count)
            .Select(index => MegastationArchetypeSelector.ForIdentity(
                $"B0 Distribution:System:Station {index:D5}"))
            .ToArray();

        Assert.Equal(first, second);
        Assert.InRange(first.Count(type => type == MegastationArchetype.Standard),
            (int)(count * .47), (int)(count * .53));
        Assert.InRange(first.Count(type => type == MegastationArchetype.Bolon),
            (int)(count * .22), (int)(count * .28));
        Assert.InRange(first.Count(type => type == MegastationArchetype.RedBolon),
            (int)(count * .22), (int)(count * .28));
    }

    [Fact]
    public void MegastationProbabilityIsIndependentFromAuthoritativeArchetype()
    {
        const string identity = "B0 Selection:System:One Station";
        var selection = new MegastationDevelopmentSelection(
            MegastationPrototypeSelectionMode.Frequent,
            MegastationProbability: .5,
            ForceStarterStation: false);
        MegastationSelection[] results = Enum.GetValues<MegastationArchetype>()
            .Select(type => MegastationDevelopmentPolicy.Resolve(
                TestStation(identity, type), null, selection))
            .ToArray();

        Assert.All(results, result => Assert.Equal(results[0].IsMegastation, result.IsMegastation));
        Assert.Equal(Enum.GetValues<MegastationArchetype>(),
            results.Select(result => result.Archetype));
    }

    [Theory]
    [InlineData(MegastationArchetype.Standard, "Mega Station")]
    [InlineData(MegastationArchetype.Bolon, "Bolon Mega Station")]
    [InlineData(MegastationArchetype.RedBolon, "Red Bolon Mega Station")]
    public void AuthoritativeArchetypeSurvivesSelectionAndProvidesMapLabel(
        MegastationArchetype archetype,
        string expectedLabel)
    {
        Station station = TestStation($"B0 Type:System:{archetype}", archetype);
        MegastationSelection result = MegastationDevelopmentPolicy.Resolve(
            station,
            null,
            new(MegastationPrototypeSelectionMode.Frequent, 1.0, false));

        Assert.True(result.IsMegastation);
        Assert.Equal(archetype, result.Archetype);
        Assert.Equal(expectedLabel, result.DisplayName);
    }

    [Fact]
    public void DevelopmentOverrideForcesArchetypeWithoutChangingSelectionDecision()
    {
        Station station = TestStation(
            "B0 Override:System:Station",
            MegastationArchetype.Standard);
        MegastationSelection result = MegastationDevelopmentPolicy.Resolve(
            station,
            null,
            new(
                MegastationPrototypeSelectionMode.Frequent,
                1.0,
                false,
                MegastationArchetype.RedBolon));

        Assert.True(result.IsMegastation);
        Assert.Equal(MegastationArchetype.RedBolon, result.Archetype);
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void StructuralPlanIsDeterministicConnectedHierarchicalAndPhysicallySane(
        MegastationArchetype archetype)
    {
        const string identity = "B1 Fixture:System:Pressure Habitat";
        BolonMegastationPlan first = BolonMegastationGenerator.Plan(identity, archetype);
        BolonMegastationPlan second = BolonMegastationGenerator.Plan(identity, archetype);

        Assert.Equal(first.StructuralSignature, second.StructuralSignature);
        Assert.Equal(first.Vessels, second.Vessels);
        Assert.Equal(first.Relationships, second.Relationships);
        Assert.InRange(first.Vessels.Count,
            archetype == MegastationArchetype.RedBolon ? 6 : 8,
            archetype == MegastationArchetype.RedBolon ? 10 : 12);
        Assert.Equal(first.Vessels.Count - 1, first.Relationships.Count);
        Assert.Contains(first.Relationships,
            relationship => relationship.Mode == BolonVesselRelationshipMode.ShortConnector);
        Assert.Contains(first.Relationships,
            relationship => relationship.Mode == BolonVesselRelationshipMode.DirectFaceJoin);
        int connectors = first.Relationships.Count(
            relationship => relationship.Mode == BolonVesselRelationshipMode.ShortConnector);
        Assert.InRange((double)connectors / first.Relationships.Count, .70, .91);
        Assert.InRange(first.Vessels.Count(
            vessel => vessel.ScaleClass == BolonVesselScaleClass.Anchor), 1, 3);
        Assert.InRange(first.Vessels.Count(
            vessel => vessel.ScaleClass == BolonVesselScaleClass.Secondary), 1, 3);
        Assert.Contains(first.Vessels,
            vessel => vessel.ScaleClass == BolonVesselScaleClass.Standard);
        Assert.All(first.Vessels, vessel =>
        {
            Assert.True(IsFinite(vessel.Position));
            Assert.True(IsFinite(vessel.Orientation));
            Assert.InRange(vessel.Radius, 170f, 425f);
            Assert.InRange(vessel.Orientation.Length(), .999f, 1.001f);
            if (vessel.Index == 0)
                Assert.Equal(-1, vessel.ParentIndex);
            else
                Assert.InRange(vessel.ParentIndex, 0, vessel.Index - 1);
        });
        Assert.All(first.Relationships, relationship =>
        {
            Assert.InRange(relationship.A, 0, relationship.B - 1);
            Assert.InRange(relationship.B, 1, first.Vessels.Count - 1);
            Assert.InRange(relationship.FaceA, 0,
                BolonMegastationGenerator.AttachmentFaces.Count - 1);
            Assert.InRange(relationship.FaceB, 0,
                BolonMegastationGenerator.AttachmentFaces.Count - 1);
            if (relationship.Mode == BolonVesselRelationshipMode.ShortConnector)
            {
                Assert.True(relationship.ConnectorRadius > 0f);
                Assert.InRange(relationship.ConnectorLength, 48f, 105f);
            }
            else
            {
                Assert.Equal(0f, relationship.ConnectorRadius);
                Assert.Equal(0f, relationship.ConnectorLength);
            }
        });
        Assert.Equal(first.Relationships.Count * 2,
            first.Relationships
                .SelectMany(relationship => new[]
                {
                    (relationship.A, relationship.FaceA),
                    (relationship.B, relationship.FaceB),
                })
                .Distinct()
                .Count());
        Assert.True(IsFinite(first.Minimum));
        Assert.True(IsFinite(first.Maximum));
        Assert.True((first.Maximum - first.Minimum).Length() > 1_500f);
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void RelationshipsOwnAlignedC60FacesAndUnrelatedVesselsDoNotOverlap(
        MegastationArchetype archetype)
    {
        BolonMegastationPlan plan = BolonMegastationGenerator.Plan(
            "B1 Alignment:System:Molecular Habitat",
            archetype);
        foreach (BolonVesselRelationship relationship in plan.Relationships)
        {
            BolonVesselPlan a = plan.Vessels[relationship.A];
            BolonVesselPlan b = plan.Vessels[relationship.B];
            BolonAttachmentFace faceA = BolonMegastationGenerator.GetAttachmentFace(
                relationship.FaceA);
            BolonAttachmentFace faceB = BolonMegastationGenerator.GetAttachmentFace(
                relationship.FaceB);
            Vector3 normalA = Vector3.Normalize(Vector3.Transform(
                faceA.LocalNormal, a.Orientation));
            Vector3 normalB = Vector3.Normalize(Vector3.Transform(
                faceB.LocalNormal, b.Orientation));
            Vector3 centerA = a.Position + Vector3.Transform(
                faceA.LocalCenter * a.Radius, a.Orientation);
            Vector3 centerB = b.Position + Vector3.Transform(
                faceB.LocalCenter * b.Radius, b.Orientation);

            Assert.True(Vector3.Dot(normalA, -normalB) > .9999f);
            if (relationship.Mode == BolonVesselRelationshipMode.DirectFaceJoin)
            {
                Assert.Equal(faceA.SideCount, faceB.SideCount);
                Assert.Equal(a.Radius, b.Radius);
                Assert.True(Vector3.Distance(centerA, centerB) < .01f);
                Vector3[] boundaryA = BolonMegastationGenerator
                    .GetAttachmentFaceVertices(relationship.FaceA)
                    .Select(point => a.Position + Vector3.Transform(
                        point * a.Radius, a.Orientation))
                    .ToArray();
                Vector3[] boundaryB = BolonMegastationGenerator
                    .GetAttachmentFaceVertices(relationship.FaceB)
                    .Select(point => b.Position + Vector3.Transform(
                        point * b.Radius, b.Orientation))
                    .ToArray();
                Assert.All(boundaryA, point => Assert.Contains(
                    boundaryB,
                    candidate => Vector3.Distance(point, candidate) < .02f));
            }
            else
            {
                Vector3 connection = centerB - centerA;
                Assert.True(Vector3.Dot(Vector3.Normalize(connection), normalA) > .9999f);
                Assert.InRange(connection.Length(),
                    relationship.ConnectorLength - .01f,
                    relationship.ConnectorLength + .01f);
                float availableRadius = MathF.Min(
                    faceA.LocalInscribedRadius * a.Radius,
                    faceB.LocalInscribedRadius * b.Radius);
                Assert.InRange(relationship.ConnectorRadius / availableRadius, .62f, .76f);
            }
        }

        var connected = plan.Relationships
            .Select(relationship => (relationship.A, relationship.B))
            .ToHashSet();
        for (int a = 0; a < plan.Vessels.Count; a++)
        for (int b = a + 1; b < plan.Vessels.Count; b++)
        {
            if (connected.Contains((a, b)))
                continue;
            float distance = Vector3.Distance(
                plan.Vessels[a].Position, plan.Vessels[b].Position);
            Assert.True(distance >= plan.Vessels[a].Radius + plan.Vessels[b].Radius + 23.9f);
        }
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon, 8, 12)]
    [InlineData(MegastationArchetype.RedBolon, 6, 10)]
    public void SampledPopulationMaintainsB1GraphBoundsClearanceAndFaceOwnership(
        MegastationArchetype archetype,
        int minimumCount,
        int maximumCount)
    {
        for (int sample = 0; sample < 256; sample++)
        {
            BolonMegastationPlan plan = BolonMegastationGenerator.Plan(
                $"B1 Population:System {sample:D2}:Station {sample:D2}",
                archetype);
            Assert.InRange(plan.Vessels.Count, minimumCount, maximumCount);
            Assert.Equal(plan.Vessels.Count - 1, plan.Relationships.Count);

            var reached = new HashSet<int> { 0 };
            foreach (BolonVesselRelationship relationship in plan.Relationships)
            {
                Assert.Contains(relationship.A, reached);
                Assert.True(reached.Add(relationship.B));
            }
            Assert.Equal(plan.Vessels.Count, reached.Count);

            int[] degrees = new int[plan.Vessels.Count];
            var ownedFaces = new HashSet<(int Vessel, int Face)>();
            foreach (BolonVesselRelationship relationship in plan.Relationships)
            {
                degrees[relationship.A]++;
                degrees[relationship.B]++;
                Assert.True(ownedFaces.Add((relationship.A, relationship.FaceA)));
                Assert.True(ownedFaces.Add((relationship.B, relationship.FaceB)));
            }
            for (int index = 0; index < degrees.Length; index++)
            {
                int maximumDegree = plan.Vessels[index].ScaleClass switch
                {
                    BolonVesselScaleClass.Anchor => 4,
                    BolonVesselScaleClass.Standard => 3,
                    _ => 2,
                };
                Assert.InRange(degrees[index], 1, maximumDegree);
            }

            int connectorCount = plan.Relationships.Count(relationship =>
                relationship.Mode == BolonVesselRelationshipMode.ShortConnector);
            double connectorFraction = (double)connectorCount / plan.Relationships.Count;
            Assert.True(connectorFraction is >= .70 and <= .91,
                $"sample={sample}; connectors={connectorCount}; edges={plan.Relationships.Count}");

            var connected = plan.Relationships
                .Select(relationship => (relationship.A, relationship.B))
                .ToHashSet();
            for (int a = 0; a < plan.Vessels.Count; a++)
            for (int b = a + 1; b < plan.Vessels.Count; b++)
            {
                if (connected.Contains((a, b)))
                    continue;
                float minimum = plan.Vessels[a].Radius + plan.Vessels[b].Radius + 23.9f;
                Assert.True(Vector3.DistanceSquared(
                    plan.Vessels[a].Position,
                    plan.Vessels[b].Position) >= minimum * minimum);
            }
        }
    }

    [Fact]
    public void C60AttachmentCataloguePreservesTwentyHexagonsAndTwelvePentagons()
    {
        Assert.Equal(32, BolonMegastationGenerator.AttachmentFaces.Count);
        Assert.Equal(20, BolonMegastationGenerator.AttachmentFaces.Count(
            face => face.SideCount == 6));
        Assert.Equal(12, BolonMegastationGenerator.AttachmentFaces.Count(
            face => face.SideCount == 5));
        Assert.All(BolonMegastationGenerator.AttachmentFaces, face =>
        {
            Assert.InRange(face.LocalNormal.Length(), .999f, 1.001f);
            Assert.True(Vector3.Dot(face.LocalCenter, face.LocalNormal) > 0f);
            Assert.True(face.LocalInscribedRadius > 0f);
        });
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void C60VesselsProduceOneValidCombinedFacetedHull(
        MegastationArchetype archetype)
    {
        BolonMegastationCpuResult result = BolonMegastationGenerator.GenerateCpu(
            "B1 Mesh:System:Facet Cluster",
            archetype);
        var (vertices, indices) = result.Mesh.ToIntArrays();
        int connectors = result.Plan.Relationships.Count(
            relationship => relationship.Mode == BolonVesselRelationshipMode.ShortConnector);
        int omittedTriangles = result.Plan.Relationships
            .Where(relationship => relationship.Mode == BolonVesselRelationshipMode.DirectFaceJoin)
            .Sum(relationship =>
                BolonMegastationGenerator.GetAttachmentFace(relationship.FaceA).SideCount - 2
                + BolonMegastationGenerator.GetAttachmentFace(relationship.FaceB).SideCount - 2);

        int apertureCount = result.SurfacePlan.ApertureGroups.Sum(
            group => group.Apertures.Count);
        Assert.True(result.Diagnostics.SurfaceTriangleCount
            >= (result.Plan.Vessels.Count * 116 - omittedTriangles) * 16);
        Assert.Equal(apertureCount * 36 + result.Diagnostics.VentGrilleTriangleCount,
            result.Diagnostics.ApertureStructureTriangleCount);
        Assert.Equal(result.Diagnostics.SurfaceTriangleCount
            + connectors * 24
            + result.Diagnostics.ApertureStructureTriangleCount
            + result.Diagnostics.ReinforcementCollarTriangleCount
            + result.Diagnostics.IrisHatchTriangleCount
            + result.Diagnostics.ApparatusRosetteTriangleCount
            + result.Diagnostics.AmbassadorTriangleCount,
            indices.Length / 3);
        Assert.Equal(result.Diagnostics.VertexCount, vertices.Length);
        Assert.Equal(result.Diagnostics.TriangleCount, indices.Length / 3);
        Assert.All(vertices, vertex =>
        {
            Assert.True(IsFinite(vertex.Position));
            Assert.True(IsFinite(vertex.Normal));
            Assert.InRange(vertex.Normal.Length(), .999f, 1.001f);
        });
        Assert.All(indices, index => Assert.InRange(index, 0, vertices.Length - 1));
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]].Position;
            Vector3 b = vertices[indices[i + 1]].Position;
            Vector3 c = vertices[indices[i + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > .0001f,
                $"Triangle {i / 3}: {result.AmbassadorBay.Coordinates(a)}, {result.AmbassadorBay.Coordinates(b)}, {result.AmbassadorBay.Coordinates(c)}");
        }
    }

    [Fact]
    public void RedBolonUsesFewerSharedScaleVesselsAndCopperTint()
    {
        const string identity = "B1 Variant:System:Shared Fixture";
        BolonMegastationCpuResult bolon = BolonMegastationGenerator.GenerateCpu(
            identity,
            MegastationArchetype.Bolon);
        BolonMegastationCpuResult red = BolonMegastationGenerator.GenerateCpu(
            identity,
            MegastationArchetype.RedBolon);
        var (bolonVertices, _) = bolon.Mesh.ToIntArrays();
        var (redVertices, _) = red.Mesh.ToIntArrays();

        Assert.Equal(bolon.Plan.Vessels.Count - 2, red.Plan.Vessels.Count);
        Assert.Equal(bolon.Plan.Vessels[0].Radius, red.Plan.Vessels[0].Radius);
        Assert.InRange(red.Plan.Vessels.Average(vessel => vessel.Radius)
            / bolon.Plan.Vessels.Average(vessel => vessel.Radius), .80f, 1.20f);
        Assert.NotEqual(bolon.Plan.StructuralSignature, red.Plan.StructuralSignature);
        Assert.True(redVertices[0].Color.R > redVertices[0].Color.G);
        Assert.True(bolonVertices[0].Color.G > bolonVertices[0].Color.B);
        Assert.NotEqual(bolonVertices[0].Color, redVertices[0].Color);
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void B2PresentationIsDeterministicAndLeavesB1StructureUnchanged(
        MegastationArchetype archetype)
    {
        const string identity = "B2 Surface:System:History Fixture";
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(identity, archetype);
        BolonMegastationCpuResult first = BolonMegastationGenerator.GenerateCpu(identity, archetype);
        BolonMegastationCpuResult second = BolonMegastationGenerator.GenerateCpu(identity, archetype);

        Assert.Equal(structural.StructuralSignature, first.Plan.StructuralSignature);
        Assert.Equal(first.Plan.StructuralSignature, second.Plan.StructuralSignature);
        Assert.Equal(first.SurfacePlan.SurfaceHistorySignature,
            second.SurfacePlan.SurfaceHistorySignature);
        Assert.Equal(first.SurfacePlan.ApertureSignature,
            second.SurfacePlan.ApertureSignature);
        Assert.Equal(structural.Vessels, first.Plan.Vessels);
        Assert.Equal(structural.Relationships, first.Plan.Relationships);
        Assert.All(first.SurfacePlan.VesselHistories,
            history =>
            {
                Assert.InRange(history.Regions.Count, 2, 6);
                AssertAgeMatchesFinish(history.BaselineFinish, history.BaselineAge);
                Assert.All(history.Regions,
                    region => AssertAgeMatchesFinish(region.Finish, region.Age));
            });
    }

    [Fact]
    public void SurfaceHistoryRegionsCrossFacetBoundariesAndUseLargeLowFrequencyMasks()
    {
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
            "B2 Surface:System:Cross Facet Fixture", MegastationArchetype.Bolon);
        BolonSurfacePresentationPlan surface = BolonSurfacePresentationPlanner.Plan(structural);
        bool crossesFacetBoundary = false;
        foreach (BolonVesselSurfaceHistory history in surface.VesselHistories)
        {
            var facesByRegion = BolonMegastationGenerator.AttachmentFaces
                .GroupBy(face => BolonSurfacePresentationPlanner.ResolveRegionIdentity(
                    history, face.LocalCenter))
                .ToDictionary(group => group.Key, group => group.Count());
            if (history.Regions.Any(region => facesByRegion.GetValueOrDefault(region.Identity) >= 2))
                crossesFacetBoundary = true;
            Assert.All(history.Regions,
                region => Assert.InRange(region.AngularRadius, .56f, 1.22f));
        }
        Assert.True(crossesFacetBoundary);
    }

    [Fact]
    public void AperturesUseOnlyClearHexagonsRemainContainedAndLeaveManyFacesBlank()
    {
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
            "B2 Aperture:System:Containment Fixture", MegastationArchetype.Bolon);
        BolonSurfacePresentationPlan surface = BolonSurfacePresentationPlanner.Plan(structural);
        Assert.Equal(
            "9877968BFD4C25665729D1B2D8114A1379BA5AC5A520962A38F1D812A51ECD25",
            surface.ApertureSignature);
        HashSet<(int Vessel, int Face)> attached = structural.Relationships
            .SelectMany(relationship => new[]
            {
                (relationship.A, relationship.FaceA),
                (relationship.B, relationship.FaceB),
            })
            .ToHashSet();

        Assert.NotEmpty(surface.ApertureGroups);
        Assert.True(surface.BlankEligibleHexFaceCount > surface.ApertureGroups.Count);
        Assert.Contains(surface.ApertureGroups,
            group => group.Pattern == BolonAperturePattern.FourNineFour);
        Assert.All(surface.ApertureGroups, group =>
        {
            Assert.Equal(6, BolonMegastationGenerator.GetAttachmentFace(
                group.HostFaceIndex).SideCount);
            Assert.DoesNotContain((group.VesselIndex, group.HostFaceIndex), attached);
            Assert.InRange(group.RotationRadians, 0f, MathF.Tau);
            Assert.All(group.Apertures, aperture =>
            {
                Vector3 offset = aperture.Centre - group.HostFaceCenter;
                Assert.InRange(MathF.Abs(Vector3.Dot(offset, group.Normal)), 0f, .01f);
                Assert.True(offset.Length() + group.CollarOuterRadius <= group.HostSafeRadius);
            });
            int expected = group.Pattern switch
            {
                BolonAperturePattern.FourNineFour => 17,
                BolonAperturePattern.CompactFive => 5,
                _ => group.Apertures.Count,
            };
            Assert.Equal(expected, group.Apertures.Count);
            if (group.Pattern == BolonAperturePattern.SparseChain)
                Assert.InRange(group.Apertures.Count, 3, 5);
        });
    }

    [Fact]
    public void RecessedAperturesAreFiniteBehindHullAndUseDeterministicVisualStates()
    {
        BolonMegastationCpuResult result = BolonMegastationGenerator.GenerateCpu(
            "B2 Aperture:System:Geometry Fixture", MegastationArchetype.RedBolon);
        var (glassVertices, glassIndices) = result.ApertureGlassMesh.ToIntArrays();
        var (hullVertices, hullIndices) = result.Mesh.ToIntArrays();

        Assert.NotEmpty(glassVertices);
        Assert.Equal(result.Diagnostics.ApertureCount * 30, glassIndices.Length / 3);
        Assert.Equal(result.Diagnostics.ApertureGlassTriangleCount, glassIndices.Length / 3);
        Assert.All(glassVertices, vertex =>
        {
            Assert.True(IsFinite(vertex.Position));
            Assert.True(IsFinite(vertex.Normal));
        });
        Assert.All(glassIndices, index => Assert.InRange(index, 0, glassVertices.Length - 1));
        for (int i = 0; i < glassIndices.Length; i += 3)
        {
            Vector3 a = glassVertices[glassIndices[i]].Position;
            Vector3 b = glassVertices[glassIndices[i + 1]].Position;
            Vector3 c = glassVertices[glassIndices[i + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > .0001f);
        }
        Assert.All(result.SurfacePlan.ApertureGroups.Where(
                group => group.PatternFamily != BolonAperturePatternFamily.Vent),
            group => Assert.InRange(group.CollarHeight, 2.2f, 4.2f));
        BolonApertureInstance[] firstApertures = result.SurfacePlan.ApertureGroups
            .SelectMany(group => group.Apertures)
            .Where(aperture => aperture.PenetrationType
                == BolonShellPenetrationType.OpticalAperture)
            .ToArray();
        Assert.Contains(firstApertures,
            aperture => aperture.VisualState.Illumination == BolonApertureIlluminationState.Unlit);
        Assert.Contains(firstApertures,
            aperture => aperture.VisualState.Illumination == BolonApertureIlluminationState.Luminous);
        Assert.True(firstApertures.Select(aperture => aperture.VisualState.InnerColour)
            .Distinct().Count() > 3);
        int vertexOffset = 0;
        foreach (BolonApertureGroup group in result.SurfacePlan.ApertureGroups)
        {
            foreach (BolonApertureInstance aperture in group.Apertures.Where(
                         aperture => aperture.PenetrationType
                             == BolonShellPenetrationType.OpticalAperture))
            {
                float expectedDepth = group.CollarHeight
                    * aperture.VisualState.RecessDepthScale;
                foreach (VertexPositionNormalColorTexture vertex in glassVertices
                             .Skip(vertexOffset).Take(90))
                {
                    float depth = Vector3.Dot(
                        vertex.Position - aperture.Centre, group.Normal);
                    Assert.InRange(MathF.Abs(depth + expectedDepth), 0f, .001f);
                    Assert.True(Vector3.Dot(vertex.Normal, group.Normal) > .999f);
                }
                vertexOffset += 90;
            }
        }
        Assert.Equal(glassVertices.Length, vertexOffset);

        int surfaceIndexCount = result.Diagnostics.SurfaceTriangleCount * 3;
        foreach (BolonApertureGroup group in result.SurfacePlan.ApertureGroups)
        {
            foreach (BolonApertureInstance aperture in group.Apertures)
            {
                for (int index = 0; index < surfaceIndexCount; index += 3)
                {
                    Vector3 a = hullVertices[hullIndices[index]].Position;
                    if (MathF.Abs(Vector3.Dot(a - aperture.Centre, group.Normal)) > .01f)
                        continue;
                    Vector3 b = hullVertices[hullIndices[index + 1]].Position;
                    Vector3 c = hullVertices[hullIndices[index + 2]].Position;
                    Assert.False(PointInTriangle(
                        aperture.Centre, a, b, c, group.Normal));
                }
            }
        }

        Assert.True(result.Diagnostics.VentGrilleTriangleCount > 0);

        BolonMegastationCpuResult repeated = BolonMegastationGenerator.GenerateCpu(
            "B2 Aperture:System:Geometry Fixture", MegastationArchetype.RedBolon);
        Assert.Equal(result.SurfacePlan.ApertureSignature,
            repeated.SurfacePlan.ApertureSignature);
        Assert.Equal(result.SurfacePlan.ApertureVisualSignature,
            repeated.SurfacePlan.ApertureVisualSignature);
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void B2bGivesEveryVesselSeveralInstallationsAcrossSeveralHexFaces(
        MegastationArchetype archetype)
    {
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
            "B2b Coverage:System:Every Vessel", archetype);
        BolonSurfacePresentationPlan surface = BolonSurfacePresentationPlanner.Plan(structural);

        foreach (BolonVesselPlan vessel in structural.Vessels)
        {
            BolonApertureGroup[] opticalGroups = surface.ApertureGroups
                .Where(group => group.VesselIndex == vessel.Index
                    && group.PatternFamily != BolonAperturePatternFamily.Vent)
                .ToArray();
            int minimum = vessel.ScaleClass switch
            {
                BolonVesselScaleClass.Anchor => 5,
                BolonVesselScaleClass.Standard => 4,
                _ => 3,
            };
            Assert.True(opticalGroups.Length >= minimum);
            Assert.True(opticalGroups.Select(group => group.HostFaceIndex)
                .Distinct().Count() >= minimum);
        }

        int populatedFaces = surface.ApertureGroups
            .Select(group => (group.VesselIndex, group.HostFaceIndex))
            .Distinct().Count();
        Assert.True(surface.BlankEligibleHexFaceCount > populatedFaces);
        Assert.All(surface.ApertureGroups,
            group => Assert.Equal(6, BolonMegastationGenerator.GetAttachmentFace(
                group.HostFaceIndex).SideCount));
    }

    [Fact]
    public void B2bVocabularyIsDeterministicContainedAndUsesAllFiveFamilies()
    {
        var families = new HashSet<BolonAperturePatternFamily>();
        for (int fixture = 0; fixture < 8; fixture++)
        {
            string identity = $"B2b Vocabulary:System:Fixture {fixture}";
            BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
                identity, MegastationArchetype.Bolon);
            BolonSurfacePresentationPlan first = BolonSurfacePresentationPlanner.Plan(structural);
            BolonSurfacePresentationPlan second = BolonSurfacePresentationPlanner.Plan(structural);
            Assert.Equal(first.ApertureVocabularySignature,
                second.ApertureVocabularySignature);
            Assert.Equal(first.ApertureVisualSignature,
                second.ApertureVisualSignature);

            foreach (BolonApertureGroup group in first.ApertureGroups)
            {
                families.Add(group.PatternFamily);
                Assert.All(group.Apertures, aperture =>
                {
                    Vector3 offset = aperture.Centre - group.HostFaceCenter;
                    Assert.True(float.IsFinite(aperture.Radius));
                    Assert.True(aperture.Radius > 0f);
                    Assert.InRange(MathF.Abs(Vector3.Dot(offset, group.Normal)), 0f, .01f);
                    Assert.True(offset.Length() + group.CollarOuterRadius
                        <= group.HostSafeRadius + .01f);
                });
                for (int a = 0; a < group.Apertures.Count; a++)
                for (int b = a + 1; b < group.Apertures.Count; b++)
                    Assert.True(Vector3.Distance(
                            group.Apertures[a].Centre, group.Apertures[b].Centre)
                        > group.CollarOuterRadius * 1.02f);

                if (group.PatternFamily == BolonAperturePatternFamily.CornerFan)
                {
                    Assert.InRange(group.SelectedCorner, 0, 5);
                    if (group.SymmetricPattern)
                        AssertMirrorSymmetry(group);
                }
                if (group.PatternFamily == BolonAperturePatternFamily.EdgeRun)
                    Assert.InRange(group.SelectedEdge, 0, 5);
            }
        }

        Assert.Contains(BolonAperturePatternFamily.Band, families);
        Assert.Contains(BolonAperturePatternFamily.CompactCluster, families);
        Assert.Contains(BolonAperturePatternFamily.CornerFan, families);
        Assert.Contains(BolonAperturePatternFamily.EdgeRun, families);
        Assert.Contains(BolonAperturePatternFamily.SparseField, families);
    }

    [Fact]
    public void B2bVentsHaveExplicitScalePhysicalGrillesAndIndependentDeterminism()
    {
        BolonMegastationCpuResult first = BolonMegastationGenerator.GenerateCpu(
            "B2b Vent:System:Physical Grille", MegastationArchetype.RedBolon);
        BolonMegastationCpuResult second = BolonMegastationGenerator.GenerateCpu(
            "B2b Vent:System:Physical Grille", MegastationArchetype.RedBolon);
        BolonApertureInstance[] vents = first.SurfacePlan.ApertureGroups
            .SelectMany(group => group.Apertures)
            .Where(aperture => aperture.PenetrationType == BolonShellPenetrationType.Vent)
            .ToArray();

        Assert.NotEmpty(vents);
        Assert.All(vents, vent =>
        {
            Assert.NotEqual(BolonVentScale.None, vent.VentScale);
            Assert.InRange(vent.GrilleRibCount, 3, 7);
            Assert.InRange(vent.GrilleRotationRadians, 0f, MathF.Tau);
        });
        Assert.True(first.Diagnostics.VentGrilleTriangleCount > vents.Length * 6);
        Assert.Equal(first.Diagnostics.VentGrilleTriangleCount,
            second.Diagnostics.VentGrilleTriangleCount);
        Assert.Equal(first.SurfacePlan.ApertureVocabularySignature,
            second.SurfacePlan.ApertureVocabularySignature);
        Assert.All(first.SurfacePlan.ApertureGroups
                .Where(group => group.PatternFamily == BolonAperturePatternFamily.Vent),
            group => Assert.InRange(group.Apertures.Count, 1, 5));
    }

    [Fact]
    public void B2bPaletteHierarchyRemainsRubyDominantWithRareCoherentAlternatives()
    {
        var groups = new List<BolonApertureGroup>();
        for (int fixture = 0; fixture < 12; fixture++)
        {
            BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
                $"B2b Palette:System:Fixture {fixture}", MegastationArchetype.Bolon);
            groups.AddRange(BolonSurfacePresentationPlanner.Plan(structural).ApertureGroups
                .Where(group => group.PatternFamily != BolonAperturePatternFamily.Vent));
        }

        Assert.True(groups.Count(group => group.PaletteFamily == BolonAperturePaletteFamily.Ruby)
            > groups.Count * .78f);
        Assert.Contains(groups,
            group => group.PaletteFamily == BolonAperturePaletteFamily.Violet);
        Assert.All(groups, group =>
            Assert.Single(group.Apertures.Select(aperture =>
                    ClassifyPalette(aperture.VisualState.InnerColour, group.PaletteFamily))
                .Distinct()));
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void B3aUtilitiesUseOnlyClearPentagonsStaySparseAndRemainInBounds(
        MegastationArchetype archetype)
    {
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
            "B3a Utility:System:Face Ownership", archetype);
        BolonPentagonalUtilityPlan utility = BolonPentagonalUtilityPlanner.Plan(structural);
        HashSet<(int Vessel, int Face)> attached = structural.Relationships
            .SelectMany(relationship => new[]
            {
                (relationship.A, relationship.FaceA),
                (relationship.B, relationship.FaceB),
            })
            .ToHashSet();

        Assert.NotEmpty(utility.Fixtures);
        Assert.True(utility.BarePentagonCount > utility.Fixtures.Count);
        Assert.Equal(utility.Fixtures.Count,
            utility.Fixtures.Select(fixture =>
                (fixture.VesselIndex, fixture.HostFaceIndex)).Distinct().Count());
        Assert.All(utility.Fixtures, fixture =>
        {
            BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(
                fixture.HostFaceIndex);
            Assert.Equal(5, face.SideCount);
            Assert.DoesNotContain((fixture.VesselIndex, fixture.HostFaceIndex), attached);
            Assert.True(IsFinite(fixture.Centre));
            Assert.True(IsFinite(fixture.Normal));
            Assert.InRange(fixture.RotationRadians, 0f, MathF.Tau / 5f);
            Assert.True(fixture.OuterRadius > fixture.InnerRadius);
            Assert.True(fixture.OuterRadius <= fixture.HostSafeRadius);
            Assert.Equal(5, fixture.RadialElementCount);
        });
        Assert.All(BolonSurfacePresentationPlanner.Plan(structural).ApertureGroups,
            group => Assert.Equal(6, BolonMegastationGenerator.GetAttachmentFace(
                group.HostFaceIndex).SideCount));
    }

    [Fact]
    public void B3aPlanIsDeterministicAndPreservesAllAcceptedSignatures()
    {
        const string identity = "B3a Utility:System:Preservation";
        BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
            identity, MegastationArchetype.Bolon);
        BolonSurfacePresentationPlan accepted = BolonSurfacePresentationPlanner.Plan(structural);
        BolonPentagonalUtilityPlan first = BolonPentagonalUtilityPlanner.Plan(structural);
        BolonPentagonalUtilityPlan second = BolonPentagonalUtilityPlanner.Plan(structural);
        BolonMegastationCpuResult generated = BolonMegastationGenerator.GenerateCpu(
            identity, MegastationArchetype.Bolon);

        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(structural.StructuralSignature, generated.Plan.StructuralSignature);
        Assert.Equal(accepted.SurfaceHistorySignature,
            generated.SurfacePlan.SurfaceHistorySignature);
        // B4a only removes the reserved entrance host; B2/B3 still plan independently.
        Assert.Equal(accepted.ApertureGroups.Where(g => !generated.AmbassadorBay.ReservesFace(
            g.VesselIndex, g.HostFaceIndex)).Select(g => g.Identity), generated.SurfacePlan.ApertureGroups.Select(g => g.Identity));
        accepted = BolonSurfacePresentationPlanner.ReserveAmbassadorFace(accepted, generated.AmbassadorBay);
        Assert.Equal(accepted.ApertureSignature,
            generated.SurfacePlan.ApertureSignature);
        Assert.Equal(accepted.ApertureVisualSignature,
            generated.SurfacePlan.ApertureVisualSignature);
        Assert.Equal(accepted.ApertureVocabularySignature,
            generated.SurfacePlan.ApertureVocabularySignature);
    }

    [Fact]
    public void B3aIrisHasFiveThickLeavesAndARealRecessedHullOpening()
    {
        BolonMegastationCpuResult? result = null;
        for (int fixture = 0; fixture < 6 && result is null; fixture++)
        {
            string identity = $"B3a Iris:System:Fixture {fixture}";
            BolonMegastationPlan structural = BolonMegastationGenerator.Plan(
                identity, MegastationArchetype.Bolon);
            if (BolonPentagonalUtilityPlanner.Plan(structural).Fixtures.Any(
                    candidate => candidate.Family
                        == BolonPentagonalUtilityFamily.FiveLeafIris))
                result = BolonMegastationGenerator.GenerateCpu(
                    identity, MegastationArchetype.Bolon);
        }
        Assert.NotNull(result);
        BolonPentagonalUtilityFixture[] irises = result.PentagonalUtilityPlan.Fixtures
            .Where(fixture => fixture.Family == BolonPentagonalUtilityFamily.FiveLeafIris)
            .ToArray();
        Assert.NotEmpty(irises);
        Assert.All(irises, iris =>
        {
            Assert.Equal(5, iris.IrisLeafCount);
            Assert.Equal(5, iris.RadialElementCount);
            Assert.InRange(iris.RecessDepth, 5.5f, 10.5f);
            Assert.True(iris.InnerRadius < iris.OuterRadius);
        });
        Assert.Equal(irises.Length * 139,
            result.Diagnostics.IrisHatchTriangleCount);

        var (vertices, indices) = result.Mesh.ToIntArrays();
        int surfaceIndexCount = result.Diagnostics.SurfaceTriangleCount * 3;
        foreach (BolonPentagonalUtilityFixture iris in irises)
        {
            for (int index = 0; index < surfaceIndexCount; index += 3)
            {
                Vector3 a = vertices[indices[index]].Position;
                if (MathF.Abs(Vector3.Dot(a - iris.Centre, iris.Normal)) > .01f)
                    continue;
                Vector3 b = vertices[indices[index + 1]].Position;
                Vector3 c = vertices[indices[index + 2]].Position;
                Assert.False(PointInTriangle(iris.Centre, a, b, c, iris.Normal));
            }
        }
    }

    [Fact]
    public void B3aFamiliesEmitDeterministicFivefoldBatchedGeometry()
    {
        BolonMegastationCpuResult result = BolonMegastationGenerator.GenerateCpu(
            "B3a Utility:System:Geometry Costs", MegastationArchetype.RedBolon);
        int collars = result.PentagonalUtilityPlan.Fixtures.Count(fixture =>
            fixture.Family == BolonPentagonalUtilityFamily.ReinforcementCollar);
        int irises = result.PentagonalUtilityPlan.Fixtures.Count(fixture =>
            fixture.Family == BolonPentagonalUtilityFamily.FiveLeafIris);
        int rosettes = result.PentagonalUtilityPlan.Fixtures.Count(fixture =>
            fixture.Family == BolonPentagonalUtilityFamily.ApparatusRosette);

        Assert.Equal(collars * 83,
            result.Diagnostics.ReinforcementCollarTriangleCount);
        Assert.Equal(irises * 139,
            result.Diagnostics.IrisHatchTriangleCount);
        Assert.Equal(rosettes * 156,
            result.Diagnostics.ApparatusRosetteTriangleCount);
        Assert.All(result.PentagonalUtilityPlan.Fixtures, fixture =>
            Assert.Equal(5, fixture.RadialElementCount));
        Assert.Equal(3, StationGenerator.PrepareCpu(
            TestStation("B3a Utility:System:Residency", MegastationArchetype.Bolon),
            useMegastationPrototype: true,
            megastationArchetype: MegastationArchetype.Bolon).UploadPlan.Count);
    }

    [Fact]
    public void B3aExtrudedSideNormalsAreOutwardForEitherFootprintWinding()
    {
        Vector3[][] footprints =
        [
            // Clockwise tapered rib/blade order used by collars and rosettes.
            [new(-1f, -1f, 0f), new(-1f, 1f, 0f),
                new(2f, .5f, 0f), new(2f, -.5f, 0f)],
            // Counter-clockwise order used by regular pentagonal nodes.
            Enumerable.Range(0, 5).Select(index =>
            {
                float angle = index * MathF.Tau / 5f;
                return new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
            }).ToArray(),
        ];
        foreach (Vector3[] footprint in footprints)
        {
            Vector3 centroid = footprint.Aggregate(Vector3.Zero, (sum, point) => sum + point)
                / footprint.Length;
            for (int index = 0; index < footprint.Length; index++)
            {
                Vector3 midpoint = (footprint[index]
                    + footprint[(index + 1) % footprint.Length]) * .5f;
                Vector3 expectedOutward = Vector3.Normalize(midpoint - centroid);
                Vector3 actual = BolonSurfaceMeshBuilder.ExtrudedSideNormal(
                    centroid,
                    footprint[index],
                    footprint[(index + 1) % footprint.Length],
                    Vector3.UnitZ);
                Assert.True(Vector3.Dot(actual, expectedOutward) > .999f);
                Assert.InRange(MathF.Abs(Vector3.Dot(actual, Vector3.UnitZ)), 0f, 1e-5f);
            }
        }
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void ResidencyPreparationUsesOneHullNoOwnedTexturesAndNoStandardLayers(
        MegastationArchetype archetype)
    {
        Station station = TestStation("B1 Residency:System:Station", archetype);
        StationGenerationCpuResult prepared = StationGenerator.PrepareCpu(
            station,
            useMegastationPrototype: true,
            megastationArchetype: archetype);

        PlacedModule module = Assert.Single(prepared.Modules);
        Assert.NotNull(module.HullMesh);
        Assert.Same(module.HullMesh, module.HullShadowMesh);
        Assert.NotNull(module.GlassMesh);
        Assert.Empty(prepared.Textures);
        Assert.Empty(prepared.TextureAssignments);
        Assert.True(prepared.UsesSharedMegastationFallbackTextures);
        Assert.NotNull(prepared.BolonMegastationDiagnostics);
        Assert.Null(prepared.MegastationDiagnostics);
        Assert.Null(prepared.MegastationSemanticZoning);
        Assert.Null(prepared.MegastationInterior);
        Assert.Equal(3, prepared.UploadPlan.Count);
        Assert.Contains(prepared.UploadPlan,
            item => item.Kind == StationVisualUploadResourceKind.HullMesh);
        Assert.Contains(prepared.UploadPlan,
            item => item.Kind == StationVisualUploadResourceKind.ShadowHullMesh);
        Assert.Contains(prepared.UploadPlan,
            item => item.Kind == StationVisualUploadResourceKind.GlassMesh);
    }

    private static Station TestStation(string identity, MegastationArchetype archetype)
        => new()
        {
            Name = identity.Split(':')[^1],
            PersistenceId = identity,
            Size = StationSize.Large,
            MegastationArchetype = archetype,
        };

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void AssertMirrorSymmetry(BolonApertureGroup group)
    {
        Vector2[] points = group.Apertures.Select(aperture =>
        {
            Vector3 offset = aperture.Centre - group.Centre;
            return new Vector2(
                Vector3.Dot(offset, group.TangentU),
                Vector3.Dot(offset, group.TangentV));
        }).ToArray();
        Assert.All(points, point => Assert.Contains(points, candidate =>
            MathF.Abs(candidate.X - point.X) < .02f
            && MathF.Abs(candidate.Y + point.Y) < .02f));
    }

    private static BolonAperturePaletteFamily ClassifyPalette(
        Color colour,
        BolonAperturePaletteFamily expected)
        => expected switch
        {
            BolonAperturePaletteFamily.Violet when colour.B >= colour.G => expected,
            BolonAperturePaletteFamily.SpectralGreen when colour.G >= colour.R => expected,
            BolonAperturePaletteFamily.Ruby when colour.R >= colour.G => expected,
            _ => throw new Xunit.Sdk.XunitException(
                $"Aperture colour {colour} escaped group palette {expected}."),
        };

    private static void AssertAgeMatchesFinish(BolonSurfaceFinish finish, float age)
    {
        (float minimum, float maximum) = finish switch
        {
            BolonSurfaceFinish.Polished => (.05f, .34f),
            BolonSurfaceFinish.Brushed => (.16f, .55f),
            BolonSurfaceFinish.Eroded => (.72f, .99f),
            _ => (.43f, .88f),
        };
        Assert.InRange(age, minimum, maximum);
    }

    private static bool PointInTriangle(
        Vector3 point,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 normal)
    {
        float ab = Vector3.Dot(Vector3.Cross(b - a, point - a), normal);
        float bc = Vector3.Dot(Vector3.Cross(c - b, point - b), normal);
        float ca = Vector3.Dot(Vector3.Cross(a - c, point - c), normal);
        return ab >= -1e-4f && bc >= -1e-4f && ca >= -1e-4f
            || ab <= 1e-4f && bc <= 1e-4f && ca <= 1e-4f;
    }
}
