using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationArtificialLightingTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void DirectAndIndirectContributionsHaveDistinctFacingAndRangeBehaviour()
    {
        MegastationArtificialLight light = new(
            "test", new Vector3(0f, 10f, 0f), Color.White, 1f, 100f);

        (Vector3 facingDirect, Vector3 facingIndirect) =
            MegastationArtificialLighting.EvaluateComponents(
            Vector3.Zero, Vector3.UnitY, [light]);
        (Vector3 awayDirect, Vector3 awayIndirect) =
            MegastationArtificialLighting.EvaluateComponents(
            Vector3.Zero, -Vector3.UnitY, [light]);
        (Vector3 outsideDirect, Vector3 outsideIndirect) =
            MegastationArtificialLighting.EvaluateComponents(
            new Vector3(0f, -150f, 0f), Vector3.UnitY, [light]);

        Assert.True(facingDirect.X > 0f && facingIndirect.X > 0f);
        Assert.Equal(.972f, facingDirect.X, 5); // H1c-A smoothstep direct baseline at 10/100 m.
        Assert.Equal(Vector3.Zero, awayDirect);
        Assert.True(awayIndirect.X > 0f);
        Assert.Equal(Vector3.Zero, outsideDirect);
        Assert.Equal(Vector3.Zero, outsideIndirect);
        Assert.All(new[]
        {
            facingDirect, facingIndirect, awayDirect, awayIndirect,
            outsideDirect, outsideIndirect,
        }, value =>
        {
            Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z));
            Assert.True(value.X >= 0f && value.Y >= 0f && value.Z >= 0f);
        });
    }

    [Fact]
    public void IndirectIsWeakSourceColouredAndDecreasesWithDistance()
    {
        MegastationArtificialLight light = new(
            "test", Vector3.Zero, new Color(180, 220, 255), 1f, 100f);
        Vector3 near = MegastationArtificialLighting.EvaluateComponents(
            new Vector3(0f, 10f, 0f), Vector3.UnitY, [light]).Indirect;
        Vector3 far = MegastationArtificialLighting.EvaluateComponents(
            new Vector3(0f, 100f, 0f), Vector3.UnitY, [light]).Indirect;

        Assert.True(near.Z > near.Y && near.Y > near.X);
        Assert.True(near.X > far.X && far.X > 0f);
        Assert.InRange(near.Z, 0f, MegastationArtificialLighting.IndirectStrength + .001f);
    }

    [Fact]
    public void LocalColourSpillPreservesChromaAgainstSaturatedNeutralBaseline()
    {
        MegastationArtificialLight baseline = new(
            "baseline", new Vector3(0f, 1f, 0f), Color.White, 2f, 100f);
        MegastationArtificialLight amber = new(
            "amber", new Vector3(0f, 1f, 0f), new Color(255, 174, 72), .34f, 5.5f);
        MegastationArtificialLight cyan = new(
            "cyan", new Vector3(0f, 1f, 0f), new Color(76, 196, 255), .34f, 5.5f);

        Vector3 unchanged = MegastationArtificialLighting.EvaluateLocalColourSpill(
            Vector3.Zero, Vector3.UnitY, [baseline], []);
        Vector3 warm = MegastationArtificialLighting.EvaluateLocalColourSpill(
            Vector3.Zero, Vector3.UnitY, [baseline], [amber]);
        Vector3 cool = MegastationArtificialLighting.EvaluateLocalColourSpill(
            Vector3.Zero, Vector3.UnitY, [baseline], [cyan]);

        Assert.Equal(Vector3.One, unchanged);
        Assert.True(warm.X > warm.Y && warm.Y > warm.Z);
        Assert.True(cool.Z > cool.Y && cool.Y > cool.X);
        Assert.All(new[] { unchanged, warm, cool }, value =>
        {
            Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z));
            Assert.InRange(value.X, 0f, 1f);
            Assert.InRange(value.Y, 0f, 1f);
            Assert.InRange(value.Z, 0f, 1f);
        });
    }

    [Fact]
    public void DirectionalDirectLightFavoursFrontSuppressesBackAndRetainsBroadIndirect()
    {
        MegastationArtificialLight light = new(
            "directional",
            Vector3.Zero,
            Color.White,
            1f,
            100f,
            Vector3.UnitZ,
            -.30f);

        var front = MegastationArtificialLighting.EvaluateComponents(
            new Vector3(0f, 0f, 10f), -Vector3.UnitZ, [light]);
        var back = MegastationArtificialLighting.EvaluateComponents(
            new Vector3(0f, 0f, -10f), Vector3.UnitZ, [light]);
        var side = MegastationArtificialLighting.EvaluateComponents(
            new Vector3(10f, 0f, 0f), -Vector3.UnitX, [light]);

        Assert.True(front.Direct.X > 0f);
        Assert.Equal(Vector3.Zero, back.Direct);
        Assert.True(side.Direct.X > 0f);
        Assert.True(side.Direct.X < front.Direct.X);
        Assert.True(back.Indirect.X > 0f);
        Assert.Equal(front.Indirect.X, back.Indirect.X, 5);
    }

    [Fact]
    public void SubstantialOccluderSuppressesDirectButPreservesIndirect()
    {
        MegastationArtificialLight light = new(
            "test", new Vector3(0f, 10f, 0f), Color.White, 1f, 100f);
        MegastationArtificialOcclusion occlusion = MegastationArtificialOcclusion.CreateForTests(
            Box(MegastationArtificialOccluderRole.ServiceBuilding,
                new Vector3(0f, 5f, 0f), new Vector3(4f, 2f, 4f)));

        var blocked = MegastationArtificialLighting.EvaluateComponents(
            Vector3.Zero, Vector3.UnitY, [light], occlusion);

        Assert.Equal(Vector3.Zero, blocked.Direct);
        Assert.True(blocked.Indirect.X > 0f);
        Assert.Equal(1, occlusion.Diagnostics(1).BlockedVisibilityTestCount);
    }

    [Fact]
    public void OccluderBeyondReceiverDoesNotBlockFiniteSegment()
    {
        MegastationArtificialLight light = new(
            "test", new Vector3(0f, 10f, 0f), Color.White, 1f, 100f);
        MegastationArtificialOcclusion occlusion = MegastationArtificialOcclusion.CreateForTests(
            Box(MegastationArtificialOccluderRole.LandingPadSlab,
                new Vector3(0f, -5f, 0f), new Vector3(4f, 2f, 4f)));

        var result = MegastationArtificialLighting.EvaluateComponents(
            Vector3.Zero, Vector3.UnitY, [light], occlusion);

        Assert.True(result.Direct.X > 0f);
        Assert.Equal(0, occlusion.Diagnostics(1).BlockedVisibilityTestCount);
    }

    [Fact]
    public void ExplicitMinorDetailDoesNotEnterOccluderSet()
    {
        MegastationArtificialLight light = new(
            "test", new Vector3(0f, 10f, 0f), Color.White, 1f, 100f);
        MegastationArtificialOcclusion occlusion = MegastationArtificialOcclusion.CreateForTests(
            Box(MegastationArtificialOccluderRole.MinorDetail,
                new Vector3(0f, 5f, 0f), new Vector3(4f, 2f, 4f)));

        var result = MegastationArtificialLighting.EvaluateComponents(
            Vector3.Zero, Vector3.UnitY, [light], occlusion);

        Assert.True(result.Direct.X > 0f);
        Assert.Equal(0, occlusion.Diagnostics(1).OccluderCount);
        Assert.False(MegastationArtificialOcclusion.CastsStaticArtificialShadow(
            MegastationArtificialOccluderRole.MinorDetail));
    }

    [Fact]
    public void EveryArtificialOccluderRoleHasExplicitPolicy()
    {
        foreach (MegastationArtificialOccluderRole role in
                 Enum.GetValues<MegastationArtificialOccluderRole>())
        {
            bool expected = role != MegastationArtificialOccluderRole.MinorDetail;
            Assert.Equal(expected,
                MegastationArtificialOcclusion.CastsStaticArtificialShadow(role));
        }
    }

    [Fact]
    public void AuthoritativeStructuralOccupancyBlocksDirectSegmentDeterministically()
    {
        float[] widths = [10f, 10f, 10f];
        var grid = new SliceGrid(widths, widths, widths, 0..3, 0..3, 0..3);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkStructural(1, 1, 1);
        MegastationArtificialLight light = new(
            "test", new Vector3(0f, 10f, 0f), Color.White, 1f, 100f);

        static (Vector3 Direct, Vector3 Indirect) Evaluate(
            StructuralOccupancy occupancy, MegastationArtificialLight light)
        {
            MegastationArtificialOcclusion scene =
                MegastationArtificialOcclusion.CreateForTests(occupancy);
            return MegastationArtificialLighting.EvaluateComponents(
                new Vector3(0f, -10f, 0f), Vector3.UnitY, [light], scene);
        }

        var first = Evaluate(occupancy, light);
        var second = Evaluate(occupancy, light);
        Assert.Equal(Vector3.Zero, first.Direct);
        Assert.True(first.Indirect.X > 0f);
        Assert.Equal(first, second);
    }

    [Fact]
    public void KnownStationProducesDeterministicIndependentLightPlan()
    {
        MegastationPrototypeCpuResult first = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationArtificialLightingPlan replanned =
            MegastationArtificialLighting.Plan(first.InteriorPlan);

        Assert.Equal(12, replanned.Lights.Count);
        Assert.Equal(50, first.ArtificialLightingPlan.Lights.Count);
        Assert.Equal(replanned.Lights, first.ArtificialLightingPlan.Lights.Take(12));
        Assert.All(replanned.Lights, light =>
        {
            Assert.InRange(light.Range, 180f, 280f);
            Assert.InRange(light.Intensity, .72f, 1.02f);
            Assert.True(first.InteriorPlan.MainFlightVolume.Minimum.X <= light.Position.X
                && light.Position.X <= first.InteriorPlan.MainFlightVolume.Maximum.X);
            Assert.True(first.InteriorPlan.MainFlightVolume.Minimum.Y <= light.Position.Y
                && light.Position.Y <= first.InteriorPlan.MainFlightVolume.Maximum.Y);
            Assert.True(first.InteriorPlan.MainFlightVolume.Minimum.Z <= light.Position.Z
                && light.Position.Z <= first.InteriorPlan.MainFlightVolume.Maximum.Z);
        });
    }

    [Fact]
    public void OnlyInteriorBoundaryReceivesBakedArtificialLightAndBayFloorIsRemoved()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(Nova);
        var (vertices, _) = result.Mesh.ToIntArrays();
        Assert.True(vertices.Length >= result.BoundaryTopology.Faces.Count * 4);
        int litInteriorVertices = 0;

        for (int faceIndex = 0; faceIndex < result.BoundaryTopology.Faces.Count; faceIndex++)
        {
            BoundaryFace face = result.BoundaryTopology.Faces[faceIndex];
            for (int corner = 0; corner < 4; corner++)
            {
                var vertex = vertices[faceIndex * 4 + corner];
                bool hasArtificial = vertex.ArtificialLight.R != 0
                    || vertex.ArtificialLight.G != 0
                    || vertex.ArtificialLight.B != 0;
                if (face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary)
                {
                    Assert.Equal(0, vertex.Color.A);
                    if (hasArtificial) litInteriorVertices++;
                }
                else
                {
                    Assert.False(hasArtificial);
                }
            }
        }

        Assert.True(litInteriorVertices > 0);
        Assert.Equal(50, result.InteriorPlan.Diagnostics.ArtificialLightSourceCount);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialOccluderCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialLightReceiverSampleCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialLightVisibilityTestCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialLightBlockedVisibilityTestCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialLightBakeMilliseconds >= 0d);
    }

    private static MegastationArtificialOccluder Box(
        MegastationArtificialOccluderRole role, Vector3 centre, Vector3 size)
        => new(role, centre, size * .5f, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
}
