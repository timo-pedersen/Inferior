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
    public void KnownStationProducesDeterministicIndependentLightPlan()
    {
        MegastationPrototypeCpuResult first = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationArtificialLightingPlan replanned =
            MegastationArtificialLighting.Plan(first.InteriorPlan);

        Assert.Equal(12, replanned.Lights.Count);
        Assert.Equal(20, first.ArtificialLightingPlan.Lights.Count);
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
    }
}
