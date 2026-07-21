using System.Text.Json;
using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;
using Inferior.ObjectDesigner.Editing;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.ObjectDesigner.Test;

public sealed class ObjectDesignerEditingTests
{
    [Fact]
    public void Move_vertex_undo_redo_and_dirty_state_work()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        session.History.MarkClean();
        string vertexId = session.Document.Hull.VisualGeometry.Vertices[0].Id;
        DVec3 before = session.GetVertexPosition(vertexId);
        DVec3 after = before + new DVec3(1.25, -0.5, 0.75);

        session.Execute(new MoveVertexCommand(vertexId, before, after));
        Assert.True(session.IsDirty);
        Assert.Equal(after, session.GetVertexPosition(vertexId));

        session.Undo();
        Assert.False(session.IsDirty);
        Assert.Equal(before, session.GetVertexPosition(vertexId));

        session.Redo();
        Assert.True(session.IsDirty);
        Assert.Equal(after, session.GetVertexPosition(vertexId));
    }

    [Fact]
    public void Move_vertices_undoes_as_one_history_entry()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string a = session.Document.Hull.VisualGeometry.Vertices[0].Id;
        string b = session.Document.Hull.VisualGeometry.Vertices[1].Id;
        Dictionary<string, DVec3> before = new(StringComparer.Ordinal)
        {
            [a] = session.GetVertexPosition(a),
            [b] = session.GetVertexPosition(b),
        };
        Dictionary<string, DVec3> after = before.ToDictionary(pair => pair.Key, pair => pair.Value + DVec3.UnitX, StringComparer.Ordinal);

        session.Execute(new MoveVerticesCommand(before, after));
        session.Undo();

        Assert.Equal(before[a], session.GetVertexPosition(a));
        Assert.Equal(before[b], session.GetVertexPosition(b));
    }

    [Fact]
    public void New_command_after_undo_clears_redo()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string vertexId = session.Document.Hull.VisualGeometry.Vertices[0].Id;
        DVec3 before = session.GetVertexPosition(vertexId);

        session.Execute(new MoveVertexCommand(vertexId, before, before + DVec3.UnitX));
        session.Undo();
        session.Execute(new MoveVertexCommand(vertexId, before, before + DVec3.UnitY));
        session.Redo();

        Assert.Equal(before + DVec3.UnitY, session.GetVertexPosition(vertexId));
    }

    [Fact]
    public void Stable_id_lookup_survives_vertex_reordering()
    {
        using TempAsset asset = TempAsset.FromBeren(reverseVertices: true);
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);

        session.Execute(new MoveVertexCommand("beren.platform.top.01", session.GetVertexPosition("beren.platform.top.01"), new DVec3(2, 3, 4)));

        Assert.Equal(new DVec3(2, 3, 4), session.GetVertexPosition("beren.platform.top.01"));
    }

    [Fact]
    public void Save_blocks_without_throwing_when_validation_has_errors()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        session.Document.Hull.VisualGeometry.Faces[0].VertexIds[0] = "missing";
        session.Rebuild();

        Assert.False(session.Save());
        Assert.True(session.HasValidationErrors);
        Assert.True(session.IsPreviewStale);
    }

    [Fact]
    public void Selection_tracks_multiple_vertices_and_active_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string a = session.Document.Hull.VisualGeometry.Vertices[0].Id;
        string b = session.Document.Hull.VisualGeometry.Vertices[1].Id;

        session.SelectVertex(a, extend: false);
        session.SelectVertex(b, extend: true);

        Assert.Equal([a, b], session.SelectedVertexIds);
        Assert.Equal(b, session.ActiveVertexId);
    }

    [Theory]
    [InlineData(ProjectionKind.Top, 10, 20, 30, 4, -6, 14, 20, 36)]
    [InlineData(ProjectionKind.Side, 10, 20, 30, 4, -6, 10, 26, 34)]
    [InlineData(ProjectionKind.Front, 10, 20, 30, 4, -6, 14, 26, 30)]
    public void Projection_delta_edits_only_visible_axes(
        ProjectionKind kind,
        double x,
        double y,
        double z,
        float screenDx,
        float screenDy,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        var projection = new OrthographicProjection { Kind = kind, PixelsPerMeter = 1.0f };
        DVec3 edited = projection.ApplyScreenDelta(new DVec3(x, y, z), new Vector2(screenDx, screenDy));

        Assert.Equal(new DVec3(expectedX, expectedY, expectedZ), edited);
    }

    private sealed class TempAsset : IDisposable
    {
        public required string Path { get; init; }

        public static TempAsset FromBeren(bool reverseVertices = false)
        {
            string json = File.ReadAllText(AssetPathResolver.ResolveAssetPath(BerenHullDefinitionFactory.AssetPath));
            ShipAuthoringDocument doc = JsonSerializer.Deserialize<ShipAuthoringDocument>(json, ShipAuthoringJson.Options)!;
            if (reverseVertices)
                doc.Hull.VisualGeometry.Vertices.Reverse();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"beren-edit-{Guid.NewGuid():N}.ship.json");
            ShipAuthoringJson.Save(path, doc);
            return new TempAsset { Path = path };
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
