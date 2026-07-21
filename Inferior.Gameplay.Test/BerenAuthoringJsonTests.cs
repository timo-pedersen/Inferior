using System.Text.Json;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;
using Inferior.Rendering;
using Xunit;

namespace Inferior.Gameplay.Test;

public sealed class BerenAuthoringJsonTests
{
    [Fact]
    public void Beren_json_loads_as_runtime_hull()
    {
        HullDefinition hull = HullDefinitionLibrary.Get("beren");

        Assert.Equal("beren", hull.HullTypeId);
        Assert.Equal("Beren", hull.DisplayName);
        Assert.Equal(180_000.0, hull.HullMass);
        Assert.Equal(27.0, hull.Dimensions!.LengthMeters);
        Assert.Equal(20.0, hull.Dimensions.WidthMeters);
        Assert.Equal(6.2, hull.Dimensions.HeightMeters);
        Assert.Equal(9, hull.CargoArrangement!.ContainerCapacity);
        Assert.Equal(4, hull.VisualGeometry!.AttachmentPorts.Count(port => port.Capabilities.HasFlag(AttachmentCapability.Engine)));
        Assert.Single(hull.CockpitMounts);
        Assert.Empty(hull.Validate());
    }

    [Fact]
    public void Beren_json_triangulates_through_shared_semantic_builder()
    {
        HullDefinition hull = HullDefinitionLibrary.Get("beren");

        SemanticHullCpuMesh mesh = SemanticHullMeshBuilder.Build(hull.VisualGeometry!);

        Assert.True(mesh.TriangleCount > 0);
        Assert.Contains(mesh.FaceRanges, range => range.FaceId == "beren.aft.cargo-door.01");
    }

    [Fact]
    public void Unsupported_schema_version_is_rejected()
    {
        ShipAuthoringDocument doc = LoadDocumentCopy();
        doc.SchemaVersion = 999;
        string path = WriteTemp(doc);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            ShipAuthoringJson.LoadHullFromPath(path));
        Assert.Contains("Unsupported schema version", ex.Message);
    }

    [Fact]
    public void Unknown_vertex_reference_is_rejected_with_face_id()
    {
        ShipAuthoringDocument doc = LoadDocumentCopy();
        doc.Hull.VisualGeometry.Faces[0].VertexIds[0] = "missing.vertex";
        string faceId = doc.Hull.VisualGeometry.Faces[0].Id;
        string path = WriteTemp(doc);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            ShipAuthoringJson.LoadHullFromPath(path));
        Assert.Contains(faceId, ex.Message);
        Assert.Contains("missing.vertex", ex.Message);
    }

    [Fact]
    public void Validation_diagnostics_include_stable_codes_and_entity_ids()
    {
        ShipAuthoringDocument doc = LoadDocumentCopy();
        doc.Hull.VisualGeometry.Faces[0].VertexIds[0] = "missing.vertex";
        HullDefinition hull = ShipAuthoringConverter.ToHullDefinition(doc);

        AuthoringDiagnostic diagnostic = Assert.Single(
            ShipAuthoringValidator.Validate(doc, hull),
            diagnostic => diagnostic.Code == "HULL_FACE_UNKNOWN_VERTEX");

        Assert.Equal(doc.Hull.VisualGeometry.Faces[0].Id, diagnostic.EntityId);
        Assert.Contains("missing.vertex", diagnostic.Summary);
    }

    [Fact]
    public void Round_trip_preserves_beren_identity_and_counts()
    {
        ShipAuthoringDocument doc = LoadDocumentCopy();
        string path = WriteTemp(doc);

        ShipAuthoringLoadResult result = ShipAuthoringJson.LoadHullFromPath(path);

        Assert.Equal("beren", result.Document.AssetId);
        Assert.Equal(doc.Hull.VisualGeometry.Vertices.Count, result.Document.Hull.VisualGeometry.Vertices.Count);
        Assert.Equal(doc.Hull.VisualGeometry.Faces.Count, result.Document.Hull.VisualGeometry.Faces.Count);
        Assert.Equal(doc.Hull.VisualGeometry.Vertices[0].Id, result.Document.Hull.VisualGeometry.Vertices[0].Id);
    }

    private static ShipAuthoringDocument LoadDocumentCopy()
    {
        string json = File.ReadAllText(AssetPathResolver.ResolveAssetPath(BerenHullDefinitionFactory.AssetPath));
        return JsonSerializer.Deserialize<ShipAuthoringDocument>(json, ShipAuthoringJson.Options)!;
    }

    private static string WriteTemp(ShipAuthoringDocument document)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beren-{Guid.NewGuid():N}.ship.json");
        ShipAuthoringJson.Save(path, document);
        return path;
    }
}
