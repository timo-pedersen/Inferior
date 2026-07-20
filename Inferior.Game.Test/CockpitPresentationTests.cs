using Inferior.Gameplay.Cockpit;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class CockpitPresentationTests
{
    [Fact]
    public void AriesCockpitDefinition_OwnsCompleteExteriorGeometry()
    {
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(CockpitDefinitionLibrary.AriesCivilianCanopyId);
        CockpitVisualGeometry geometry = Assert.IsType<CockpitVisualGeometry>(
            definition.VisualGeometry);
        CockpitVisualMaterial[] materials = geometry.MeshParts
            .Select(part => part.Material)
            .Order()
            .ToArray();

        Assert.Equal(Enum.GetValues<CockpitVisualMaterial>(), materials);
        Assert.DoesNotContain(geometry.MeshParts, part => part.Triangles.Count == 0);
    }

    [Fact]
    public void AriesCockpitCamera_LiesInsideCanopyBounds()
    {
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(CockpitDefinitionLibrary.AriesCivilianCanopyId);
        CockpitVisualGeometry geometry = definition.VisualGeometry!;
        CockpitVisualTriangle[] canopyTriangles = geometry.MeshParts
            .Single(part => part.Material == CockpitVisualMaterial.Canopy)
            .Triangles
            .ToArray();
        var points = canopyTriangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();

        Assert.InRange(definition.CameraLocalPosition.X, points.Min(p => p.X), points.Max(p => p.X));
        Assert.InRange(definition.CameraLocalPosition.Y, points.Min(p => p.Y), points.Max(p => p.Y));
        Assert.InRange(definition.CameraLocalPosition.Z, points.Min(p => p.Z), points.Max(p => p.Z));
    }

    [Fact]
    public void CockpitMeshBuilder_ProducesFiniteNonDegenerateGeometry()
    {
        CockpitVisualGeometry geometry = CockpitDefinitionLibrary
            .Get(CockpitDefinitionLibrary.AriesCivilianCanopyId)
            .VisualGeometry!;

        CockpitCpuMesh mesh = CockpitMeshBuilder.Build(geometry);

        Assert.Equal(geometry.MeshParts.Count, mesh.Parts.Count);
        Assert.All(mesh.Parts, part =>
        {
            Assert.NotEmpty(part.Vertices);
            Assert.NotEmpty(part.Indices);
            Assert.Equal(0, part.Indices.Count % 3);
            Assert.All(part.Vertices, vertex =>
            {
                Assert.True(IsFinite(vertex.Position));
                Assert.True(IsFinite(vertex.Normal));
                Assert.InRange(vertex.Normal.Length(), 0.9999f, 1.0001f);
            });
        });
    }

    [Fact]
    public void CockpitMaterialLights_AreIndependentAndRestrained()
    {
        Color canopyOff = ShipMeshRenderer.CockpitMaterialColour(
            CockpitVisualMaterial.Canopy,
            canopyLightsOn: false,
            cockpitLightsOn: false);
        Color canopyInternalOn = ShipMeshRenderer.CockpitMaterialColour(
            CockpitVisualMaterial.Canopy,
            canopyLightsOn: false,
            cockpitLightsOn: true);
        Color markerOff = ShipMeshRenderer.CockpitMaterialColour(
            CockpitVisualMaterial.CanopyLight,
            canopyLightsOn: false,
            cockpitLightsOn: true);
        Color markerOn = ShipMeshRenderer.CockpitMaterialColour(
            CockpitVisualMaterial.CanopyLight,
            canopyLightsOn: true,
            cockpitLightsOn: false);

        Assert.NotEqual(canopyOff, canopyInternalOn);
        Assert.True(canopyInternalOn.R < 40 && canopyInternalOn.G < 60 && canopyInternalOn.B < 60);
        Assert.NotEqual(markerOff, markerOn);
        Assert.True(markerOn.R > markerOff.R);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);
}
