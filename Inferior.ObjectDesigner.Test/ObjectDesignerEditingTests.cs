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

    [Fact]
    public void Drag_start_on_selected_vertex_preserves_group_and_makes_active()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);

        bool canDrag = session.BeginVertexDragSelection(ids[0], shift: false);

        Assert.True(canDrag);
        Assert.Equal(ids, session.SelectedVertexIds);
        Assert.Equal(ids[0], session.ActiveVertexId);
    }

    [Fact]
    public void Drag_start_on_unselected_vertex_without_shift_replaces_group()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 4);
        session.SelectVertices(ids.Take(3), replace: true);

        bool canDrag = session.BeginVertexDragSelection(ids[3], shift: false);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        drag.Apply(session, new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f }, new Point(20, -30));

        Assert.True(canDrag);
        Assert.Equal([ids[3]], session.SelectedVertexIds);
        Assert.Equal(ids[3], session.ActiveVertexId);
        Assert.Equal([ids[3]], drag.OriginalPositions.Keys);
        Assert.Equal(drag.OriginalPositions[ids[3]] + new DVec3(2, 0, 3), session.GetVertexPosition(ids[3]));
        foreach (string id in ids.Take(3))
            Assert.NotEqual(drag.OriginalPositions[ids[3]] + new DVec3(2, 0, 3), session.GetVertexPosition(id));
    }

    [Fact]
    public void Shift_drag_start_retains_toggle_selection_semantics_without_collapsing_group()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids.Take(2), replace: true);

        bool canDrag = session.BeginVertexDragSelection(ids[2], shift: true);

        Assert.True(canDrag);
        Assert.Equal(ids, session.SelectedVertexIds);
        Assert.Equal(ids[2], session.ActiveVertexId);
    }

    [Fact]
    public void Group_drag_applies_identical_delta_to_every_captured_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);
        session.BeginVertexDragSelection(ids[1], shift: false);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };

        drag.Apply(session, projection, new Point(30, -20));

        AssertGroupDelta(session, drag.OriginalPositions, new DVec3(3, 0, 2));
    }

    [Theory]
    [InlineData(EditingConstraintMode.ViewPlane, 3, 0, 2)]
    [InlineData(EditingConstraintMode.AxisX, 3, 0, 0)]
    [InlineData(EditingConstraintMode.AxisY, 0, 0, 0)]
    [InlineData(EditingConstraintMode.AxisZ, 0, 0, 2)]
    public void Group_drag_uses_one_constrained_delta_for_each_supported_constraint(
        EditingConstraintMode constraint,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);
        session.BeginVertexDragSelection(ids[0], shift: false);
        VertexDragOperation drag = VertexDragOperation.Capture(session, constraint, Point.Zero);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };

        drag.Apply(session, projection, new Point(30, -20));

        AssertGroupDelta(session, drag.OriginalPositions, new DVec3(expectedX, expectedY, expectedZ));
    }

    [Fact]
    public void Group_drag_undo_redo_is_one_stable_id_command_independent_of_later_selection()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 4);
        session.SelectVertices(ids.Take(3), replace: true);
        session.BeginVertexDragSelection(ids[1], shift: false);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        drag.Apply(session, projection, new Point(30, -20));
        Dictionary<string, DVec3> after = drag.OriginalPositions.Keys.ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);
        drag.Restore(session);

        session.Execute(new MoveVerticesCommand(drag.OriginalPositions, after));
        session.SelectVertex(ids[3], extend: false);
        session.Undo();

        Assert.Equal(1, session.History.Count);
        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));

        session.Redo();

        foreach ((string id, DVec3 position) in after)
            Assert.Equal(position, session.GetVertexPosition(id));
    }

    [Fact]
    public void Zero_distance_group_drag_creates_no_command_when_released()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        IReadOnlyDictionary<string, DVec3> after = drag.PositionsFor(new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f }, Point.Zero);

        if (after.Any(pair => (pair.Value - drag.OriginalPositions[pair.Key]).Length > 1e-9))
            session.Execute(new MoveVerticesCommand(drag.OriginalPositions, after));

        Assert.Equal(0, session.History.Count);
    }

    [Fact]
    public void Drag_cancellation_restores_every_captured_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        drag.Apply(session, new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f }, new Point(30, -20));

        drag.Restore(session);

        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));
    }

    [Fact]
    public void Invalid_multi_vertex_edit_preserves_positions_blocks_save_and_undo_restores_preview()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = session.Document.Hull.VisualGeometry.Faces
            .First(face => face.VertexIds.Count >= 3)
            .VertexIds
            .Take(3)
            .ToArray();
        Dictionary<string, DVec3> before = ids.ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);
        DVec3 collapsed = before[ids[0]];
        Dictionary<string, DVec3> after = ids.ToDictionary(id => id, _ => collapsed, StringComparer.Ordinal);

        Exception? exception = Record.Exception(() => session.Execute(new MoveVerticesCommand(before, after)));

        Assert.Null(exception);
        foreach (string id in ids)
            Assert.Equal(collapsed, session.GetVertexPosition(id));
        Assert.True(session.HasValidationErrors || session.IsPreviewStale);
        Assert.False(session.Save());
        Assert.True(session.IsPreviewStale);

        session.Undo();

        foreach ((string id, DVec3 position) in before)
            Assert.Equal(position, session.GetVertexPosition(id));
        Assert.False(session.HasValidationErrors);
        Assert.False(session.IsPreviewStale);
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

    private static string[] FirstVertexIds(ObjectDesignerSession session, int count)
        => session.Document.Hull.VisualGeometry.Vertices
            .Take(count)
            .Select(vertex => vertex.Id)
            .ToArray();

    private static void AssertGroupDelta(
        ObjectDesignerSession session,
        IReadOnlyDictionary<string, DVec3> before,
        DVec3 expectedDelta)
    {
        foreach ((string id, DVec3 position) in before)
            AssertDVec3Close(expectedDelta, session.GetVertexPosition(id) - position);
    }

    private static void AssertDVec3Close(DVec3 expected, DVec3 actual, double tolerance = 1e-9)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Z - expected.Z), 0, tolerance);
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
