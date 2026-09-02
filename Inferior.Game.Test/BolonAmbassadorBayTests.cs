using Inferior.Game.Ships;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

public sealed class BolonAmbassadorBayTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    public void SweepHasOneStableClearBayAndRealC60Containment(MegastationArchetype archetype)
    {
        for (int i = 0; i < 12; i++)
        {
            var structural = BolonMegastationGenerator.Plan($"B4a:Sweep:{i}", archetype);
            var p = BolonAmbassadorBayPlanner.Plan(structural);
            Assert.Equal(p, BolonAmbassadorBayPlanner.Plan(structural));
            var vessel = structural.Vessels[p.VesselIndex];
            var face = BolonMegastationGenerator.GetAttachmentFace(p.HostFaceIndex);
            Assert.Equal(6, face.SideCount);
            Assert.NotEqual(BolonVesselScaleClass.Secondary, vessel.ScaleClass);
            Assert.True(p.ApproachClearance > 0);
            Assert.Equal(22f, p.ClearHeight);
            Assert.Equal(5f, p.ThroatLength);
            Assert.Equal(66f, p.BayHeight);
            Assert.InRange(p.ChamferDepth, 3f, 6f);
            Assert.True(p.BayWidth > p.ClearWidth * 1.2f);
            Assert.True(p.BayLength > 200f);
            Assert.InRange(Vector3.Distance(Vector3.Cross(p.Right, p.Up), p.Outward), 0, .00001f);
            Vector3 expected = vessel.Position + Vector3.Transform(face.LocalCenter * vessel.Radius, vessel.Orientation);
            Assert.InRange(Vector3.Distance(expected, p.MouthCenter), 0, .001f);
            Vector3 corner = Vector3.Transform(BolonMegastationGenerator.GetAttachmentFaceVertices(p.HostFaceIndex)[p.CornerAxis]
                - face.LocalCenter, vessel.Orientation);
            Assert.True(Vector3.Dot(Vector3.Normalize(corner), p.Right) > .99999f);
            Assert.All(p.MouthCorners(), q => Assert.True(BolonAmbassadorBayPlanner.InsideVessel(vessel, q, 0)));
            Assert.All(p.Octagon(p.BayFrontWidth, p.BayStartDepth),
                q => Assert.True(BolonAmbassadorBayPlanner.InsideVessel(vessel, q, 16f, p.HostFaceIndex)));
            foreach (float d in new[] { p.BayStartDepth + p.ExpansionLength, p.BayStartDepth + p.BayLength })
                Assert.All(p.Octagon(p.BayWidth, d), q => Assert.True(BolonAmbassadorBayPlanner.InsideVessel(vessel, q, 16f)));
            var fixtures = p.ApproachFixtures();
            Assert.Equal(4, fixtures.Count);
            foreach (var fixture in fixtures)
            {
                var light = fixture.Marker;
                Vector3 q = p.Coordinates(light.Position);
                Assert.True(MathF.Abs(q.Y) > p.MouthHeight / 2f);
                var plate = fixture.Elements[0];
                Vector3 mount = plate.Centre - p.Outward * MegastationApproachFixtures.PlateDepth * .5f;
                Assert.True(BolonAmbassadorBayPlanner.InsideVessel(vessel,
                    mount, 0));
                foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                    Assert.True(BolonAmbassadorBayPlanner.InsideVessel(vessel,
                        mount + p.Right * x * plate.Size.X / 2f + p.Up * y * plate.Size.Y / 2f, 0));
                Assert.True(q.Y > 0 ? light.Colour.B > light.Colour.R : light.Colour.R > light.Colour.B);
            }
        }
    }

    [Fact]
    public void ReservationDoesNotRerollUnrelatedSurfaceHistoryOrUtilities()
    {
        var structural = BolonMegastationGenerator.Plan("B4a:Reservation:Fixture", MegastationArchetype.Bolon);
        var p = BolonAmbassadorBayPlanner.Plan(structural);
        var baseline = BolonSurfacePresentationPlanner.Plan(structural);
        var actual = BolonSurfacePresentationPlanner.ReserveAmbassadorFace(baseline, p);
        Assert.Equal(baseline.SurfaceHistorySignature, actual.SurfaceHistorySignature);
        Assert.Same(baseline.VesselHistories, actual.VesselHistories);
        Assert.Equal(baseline.ApertureGroups.Where(g => !p.ReservesFace(g.VesselIndex, g.HostFaceIndex)), actual.ApertureGroups);
        Assert.DoesNotContain(actual.ApertureGroups, g => p.ReservesFace(g.VesselIndex, g.HostFaceIndex));
        foreach (var fixture in BolonPentagonalUtilityPlanner.Plan(structural).Fixtures)
        {
            Assert.False(p.ReservesFace(fixture.VesselIndex, fixture.HostFaceIndex));
            // Face-local fixtures elsewhere do not protrude across the flight rectangle.
            Assert.False(p.InApproachReservation(fixture.Centre, fixture.OuterRadius));
        }
    }

    [Fact]
    public void WindingNormalsAndFlightPathAreValidInFinalUploadedHull()
    {
        var result = BolonMegastationGenerator.GenerateCpu("Oranae:Oranae I:Nova Anchorage", MegastationArchetype.Bolon);
        var p = result.AmbassadorBay;
        var (vertices, indices) = result.Mesh.ToIntArrays();
        Assert.All(vertices, v => Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z)));
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
            Vector3 cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Assert.True(cross.LengthSquared() > 1e-7f);
            // StationModuleMesh deliberately emits CW indices, opposite its stored normal.
            Assert.True(Vector3.Dot(cross, a.Normal) < 0);
        }
        // Parallel rays covering a level 20 m-high ship must encounter only the BACK
        // of the bay, never the original facet, chamfer, bulkhead or throat cap.
        foreach (float x in new[] { -p.ClearWidth / 2f + .1f, -17f, 0f, 17f, p.ClearWidth / 2f - .1f })
        foreach (float y in new[] { -10.9f, 0f, 10.9f })
        {
            Vector3 origin = p.Point(x, y, -5f);
            float first = float.MaxValue;
            for (int i = 0; i < indices.Length; i += 3)
            {
                float? hit = RayTriangle(origin, -p.Outward, vertices[indices[i]].Position,
                    vertices[indices[i + 1]].Position, vertices[indices[i + 2]].Position);
                if (hit.HasValue) first = MathF.Min(first, hit.Value);
            }
            Assert.InRange(first, p.BayStartDepth + p.BayLength + 4.98f, p.BayStartDepth + p.BayLength + 5.02f);
        }
        // The cavern is enclosed in all radial directions and its surfaces face inward.
        Vector3 centre = p.Point(0, 0, p.BayStartDepth + p.ExpansionLength + 20);
        foreach (Vector3 direction in new[] { p.Up, -p.Up, p.Right, -p.Right })
            Assert.Contains(Enumerable.Range(0, indices.Length / 3), i => RayTriangle(centre, direction,
                vertices[indices[i * 3]].Position, vertices[indices[i * 3 + 1]].Position,
                vertices[indices[i * 3 + 2]].Position) is > 0f);
        int bayStart = vertices.Length - BuildBay(p).VertexCount - p.ApproachFixtures().Sum(f => f.Elements.Count * 24);
        Assert.All(vertices.Take(bayStart), v => Assert.Equal(0, v.Color.A));
        Assert.Contains(vertices.Skip(bayStart), v => v.Color.A == 255);
        Assert.Contains(vertices.Skip(bayStart), v => v.Color.A is > 0 and < 255);
        var module = BolonMegastationGenerator.CreatePlacedModule(result);
        Assert.True(module.UsesHullVertexIllumination);
        Assert.Same(module.HullMesh, module.HullShadowMesh);
        Assert.Equal(4, module.GlowLights.Count);
        Assert.NotEmpty(module.HullMaterialRanges);
        Assert.Equal(indices.Length, module.HullMaterialRanges.Sum(r => r.IndexCount));
        output.WriteLine($"Nova vessel={p.VesselIndex}; face={p.HostFaceIndex}; width={p.ClearWidth:F2}; height={p.ClearHeight}; chamfer={p.VisibleChamferDepth:F2}; reveal={p.OuterRevealDepth:F2}; throat={p.ThroatLength}; bay={p.BayWidth:F2}x{p.BayHeight}x{p.BayLength:F2}; clearance={p.ApproachClearance:F2}; signature={p.Signature}");
        output.WriteLine($"Whole hull={vertices.Length}v/{indices.Length / 3}t; B4a={result.Diagnostics.AmbassadorTriangleCount}t; planMs={result.Diagnostics.PlanningMilliseconds:F1}; meshMs={result.Diagnostics.MeshBuildMilliseconds:F1}");
    }

    [Fact]
    public void NewArchitectureAloneHasNoInteriorGapsAndCorrectFacing()
    {
        var p = BolonAmbassadorBayPlanner.Plan(BolonMegastationGenerator.Plan("B4a:Geometry:Fixture", MegastationArchetype.RedBolon));
        var (verts, idx) = BuildBay(p).ToIntArrays();
        for (int i = 0; i < idx.Length; i += 3)
        {
            var a = verts[idx[i]]; var b = verts[idx[i + 1]]; var c = verts[idx[i + 2]];
            Vector3 mid = (a.Position + b.Position + c.Position) / 3;
            Vector3 q = p.Coordinates(mid);
            if (q.Z <= 0) continue; // outward-facing orientation patches
            Vector3 target = q.Z > p.BayEndDepth
                ? p.Point(0, -p.BayHeight / 2f + p.RearPortHeight / 2f,
                    Math.Min(q.Z, p.BayEndDepth + p.RearPortChamferDepth + p.RearPortCorridorLength - .1f))
                : p.Point(0, 0, Math.Clamp(q.Z, p.BayStartDepth + .1f, p.BayEndDepth - .1f));
            Assert.True(Vector3.Dot(a.Normal, target - mid) > 0);
        }
    }

    [Fact]
    public void LargestCurrentAssembledShipFitsLevelAndDesignEnvelopeHasLittleRollRoom()
    {
        var ship = ShipBuilder.NewShip(AntegaHullDefinitionFactory.HullId).Build();
        ShipPresentationBounds bounds = ShipPresentationBoundsCalculator.Calculate(ship);
        output.WriteLine($"Antega composite bounds={bounds.Size}; centre={bounds.Center}");
        Assert.True(bounds.Size.Y < BolonAmbassadorBayPlanner.EntranceClearHeight);
        // Max design envelope, not a claim that today's 12m Antega hull is 20m high.
        static double RolledHeight(double degrees) => 20 * Math.Cos(degrees * Math.PI / 180)
            + 34 * Math.Sin(degrees * Math.PI / 180);
        Assert.True(RolledHeight(3) < 22);
        Assert.True(RolledHeight(5) > 22);
    }

    [Fact]
    public void CorrectionKeepsAcceptedNovaSpatialPlanAndUsesActualHullMaterialForChamfer()
    {
        var structural = BolonMegastationGenerator.Plan("Oranae:Oranae I:Nova Anchorage", MegastationArchetype.Bolon);
        var p = BolonAmbassadorBayPlanner.Plan(structural);
        Assert.Equal("22F53C19F98CA8BEA09D96226B3CCC3DEBB4AD993F943F28A5B3BB4C45018373", p.Signature);
        Assert.Equal(p.ChamferDepth / 2f, p.VisibleChamferDepth);
        Assert.Equal(3f, p.MouthWidth - p.ClearWidth, 3);
        Assert.Equal(3f, p.MouthHeight - p.ClearHeight, 3);
        Assert.Equal(5f, p.ThroatLength);
        Assert.Equal(p.ChamferDepth + 5f, p.BayStartDepth);
        var surface = BolonSurfacePresentationPlanner.Plan(structural);
        var chamfer = new StationModuleMesh();
        BolonSurfaceMeshBuilder.EmitAmbassadorChamfer(chamfer, structural, surface, p);
        var (vertices, indices) = chamfer.ToIntArrays();
        Assert.NotEmpty(indices);
        Assert.All(vertices, v => Assert.Equal(0, v.Color.A));
        var history = surface.VesselHistories[p.VesselIndex];
        var vessel = structural.Vessels[p.VesselIndex];
        var face = BolonMegastationGenerator.GetAttachmentFace(p.HostFaceIndex);
        var grouped = chamfer.PrepareMaterialGroups()!;
        foreach (var range in grouped.Ranges)
        for (int i = range.StartIndex; i < range.StartIndex + range.IndexCount; i += 3)
        {
            var data = grouped.Mesh;
            Vector3 centre = (data.Vertices[data.Indices[i]].Position
                + data.Vertices[data.Indices[i + 1]].Position + data.Vertices[data.Indices[i + 2]].Position) / 3f;
            Vector3 local = Vector3.Transform(centre - vessel.Position, Quaternion.Inverse(vessel.Orientation));
            local -= face.LocalNormal * Vector3.Dot(local - face.LocalCenter * vessel.Radius, face.LocalNormal);
            string identity = BolonSurfacePresentationPlanner.ResolveRegionIdentity(history, Vector3.Normalize(local));
            BolonSurfaceFinish finish = history.Regions.FirstOrDefault(r => r.Identity == identity)?.Finish ?? history.BaselineFinish;
            Assert.Equal(finish switch
            {
                BolonSurfaceFinish.Polished => SystemMaterialFamilyId.PolishedMetal,
                BolonSurfaceFinish.Brushed => SystemMaterialFamilyId.BrushedMetal,
                BolonSurfaceFinish.Eroded => SystemMaterialFamilyId.ErodedMetal,
                _ => SystemMaterialFamilyId.AgedMetal,
            }, range.FamilyId);
        }
    }

    [Fact]
    public void RearPortIsFloorAlignedRecessedAndHasAClosedTermination()
    {
        var structural = BolonMegastationGenerator.Plan("B4a:Rear:Fixture", MegastationArchetype.Bolon);
        var p = BolonAmbassadorBayPlanner.Plan(structural);
        var (v, ix) = BuildBay(p).ToIntArrays();
        Assert.Equal(20f, p.RearPortWidth);
        Assert.Equal(8f, p.RearPortHeight);
        var opening = p.RearPortRectangle(p.RearPortWidth, p.RearPortHeight, p.BayEndDepth);
        Assert.InRange(MathF.Abs(p.Coordinates(opening[0]).Y + p.BayHeight / 2f), 0, .001f);
        Assert.InRange(MathF.Abs(p.Coordinates(opening[1]).Y + p.BayHeight / 2f), 0, .001f);
        var termination = p.RearPortRectangle(p.RearPortWidth - 1.5f, p.RearPortHeight - .75f,
            p.BayEndDepth + p.RearPortChamferDepth + p.RearPortCorridorLength);
        Assert.All(termination, q => Assert.True(BolonAmbassadorBayPlanner.InsideVessel(structural.Vessels[p.VesselIndex], q, 1f)));
        foreach (float x in new[] { -8f, 0f, 8f })
        foreach (float height in new[] { 1f, 4f, 6f })
        {
            Vector3 origin = p.Point(x, -p.BayHeight / 2f + height, p.BayEndDepth - 2f);
            float nearest = Enumerable.Range(0, ix.Length / 3).Select(i => RayTriangle(origin, -p.Outward,
                v[ix[i * 3]].Position, v[ix[i * 3 + 1]].Position, v[ix[i * 3 + 2]].Position) ?? float.MaxValue).Min();
            Assert.InRange(nearest, 2f + p.RearPortChamferDepth + p.RearPortCorridorLength - .01f,
                2f + p.RearPortChamferDepth + p.RearPortCorridorLength + .01f);
        }
    }

    [Fact]
    public void H1eFixturesAreFaceMountedAndTheAcceptedBeamMeshReachesTheModule()
    {
        var result = BolonMegastationGenerator.GenerateCpu("B4a:Beams:Fixture", MegastationArchetype.RedBolon);
        var p = result.AmbassadorBay;
        var fixtures = p.ApproachFixtures();
        Assert.Equal(4, fixtures.Count);
        Assert.Equal(16, fixtures.Sum(f => f.Elements.Count));
        foreach (var fixture in fixtures)
        {
            Assert.Equal(p.Outward, fixture.Beam.Axis);
            Assert.InRange(fixture.Beam.Length, 1400f, 1600f);
            Assert.InRange(fixture.Beam.HalfAngleDegrees, .7f, 1.2f);
            var plate = fixture.Elements[0];
            Vector3 mount = plate.Centre - p.Outward * plate.Size.Z / 2f;
            Assert.InRange(MathF.Abs(p.Coordinates(mount).Z), 0, .001f);
            foreach (int x in new[] { -1, 1 })
            foreach (int y in new[] { -1, 1 })
            {
                Vector3 corner = mount + p.Right * x * plate.Size.X / 2f + p.Up * y * plate.Size.Y / 2f;
                Assert.True(BolonAmbassadorBayPlanner.InsideVessel(result.Plan.Vessels[p.VesselIndex], corner, 0));
                Assert.True(MathF.Abs(p.Coordinates(corner).Y) > p.MouthHeight / 2f);
            }
            Assert.Equal(new Vector3(11, 11, 2.2f), plate.Size);
            Assert.Equal(new Vector3(11f * .68f, 11f * .68f, 7f), fixture.Elements[1].Size);
            Assert.True(fixture.Elements[0].CastsShadow);
            Assert.True(fixture.Elements[1].CastsShadow);
            Assert.False(fixture.Elements[2].CastsShadow);
            Assert.False(fixture.Elements[3].CastsShadow);
        }
        var module = BolonMegastationGenerator.CreatePlacedModule(result);
        var expected = MegastationApproachBeamMeshBuilder.Build(fixtures.Select(f => f.Beam).ToArray());
        Assert.Equal(expected, module.NativeApproachBeamVertices);
        Assert.Equal(1440, expected.Length);
        Assert.Equal(fixtures.Select(f => f.Marker.Position), module.GlowLights.Select(l => l.WorldPosition));
    }

    private static StationModuleMesh BuildBay(BolonAmbassadorBayPlan p)
    {
        var mesh = new StationModuleMesh(); BolonAmbassadorBayMeshBuilder.Emit(mesh, p); return mesh;
    }
    private static float? RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (MathF.Abs(det) < 1e-7f) return null;
        Vector3 s = o - a; float u = Vector3.Dot(s, h) / det;
        if (u < -.00001f || u > 1.00001f) return null;
        Vector3 q = Vector3.Cross(s, e1); float v = Vector3.Dot(d, q) / det;
        if (v < -.00001f || u + v > 1.00001f) return null;
        float t = Vector3.Dot(e2, q) / det; return t > .0001f ? t : null;
    }
}
