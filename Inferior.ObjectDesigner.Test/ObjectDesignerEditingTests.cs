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

        bool canDrag = session.BeginVertexDragSelection(ids[0], ctrl: false);

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

        bool canDrag = session.BeginVertexDragSelection(ids[3], ctrl: false);
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
    public void Ctrl_drag_start_changes_membership_without_starting_drag_or_activity()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids.Take(2), replace: true);
        session.BeginVertexDragSelection(ids[0], ctrl: false);

        bool canDrag = session.BeginVertexDragSelection(ids[2], ctrl: true);
        session.ToggleVertexSelection(ids[2]);

        Assert.False(canDrag);
        Assert.Equal(ids, session.SelectedVertexIds);
        Assert.Equal(ids[0], session.ActiveVertexId);
    }

    [Fact]
    public void Shift_click_follows_ordinary_click_semantics()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids.Take(2), replace: true);
        session.BeginVertexDragSelection(ids[0], ctrl: false);

        bool canDrag = session.BeginVertexDragSelection(ids[2], ctrl: false);

        Assert.True(canDrag);
        Assert.Equal([ids[2]], session.SelectedVertexIds);
        Assert.Equal(ids[2], session.ActiveVertexId);
    }

    [Fact]
    public void Ctrl_membership_does_not_promote_active_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 4);
        session.SelectVertex(ids[0], extend: false);

        session.ToggleVertexSelection(ids[1]);
        Assert.Equal([ids[0], ids[1]], session.SelectedVertexIds);
        Assert.Equal(ids[0], session.ActiveVertexId);

        session.ToggleVertexSelection(ids[1]);
        Assert.Equal([ids[0]], session.SelectedVertexIds);
        Assert.Equal(ids[0], session.ActiveVertexId);

        session.ToggleVertexSelection(ids[0]);
        Assert.Empty(session.SelectedVertexIds);
        Assert.Null(session.ActiveVertexId);
        Assert.Null(session.ActiveFaceId);

        session.ToggleVertexSelection(ids[2]);
        Assert.Equal([ids[2]], session.SelectedVertexIds);
        Assert.Null(session.ActiveVertexId);
    }

    [Fact]
    public void Empty_click_clears_selection_active_vertex_and_active_face()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.a", extend: false);
        session.SelectActiveFace("test.face.sloped");

        session.ClearSelection();

        Assert.Empty(session.SelectedVertexIds);
        Assert.Null(session.ActiveVertexId);
        Assert.Null(session.ActiveFaceId);
    }

    [Fact]
    public void Marquee_replaces_membership_and_preserves_only_existing_active_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 4);
        session.SelectVertices(ids.Take(3), replace: true);
        session.BeginVertexDragSelection(ids[1], ctrl: false);

        session.SelectVertices([ids[1], ids[3]], replace: true);

        Assert.Equal([ids[1], ids[3]], session.SelectedVertexIds);
        Assert.Equal(ids[1], session.ActiveVertexId);

        session.SelectVertices([ids[0], ids[2]], replace: true);

        Assert.Equal([ids[0], ids[2]], session.SelectedVertexIds);
        Assert.Null(session.ActiveVertexId);
        Assert.Null(session.ActiveFaceId);
    }

    [Fact]
    public void Group_drag_applies_identical_delta_to_every_captured_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string[] ids = FirstVertexIds(session, 3);
        session.SelectVertices(ids, replace: true);
        session.BeginVertexDragSelection(ids[1], ctrl: false);
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
        session.BeginVertexDragSelection(ids[0], ctrl: false);
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
        session.BeginVertexDragSelection(ids[1], ctrl: false);
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

    [Fact]
    public void Incident_faces_are_stable_ordered_and_do_not_change_selection_when_selected()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.b", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.b", ctrl: false);

        IReadOnlyList<SemanticHullFaceDto> faces = session.GetIncidentFaces("test.b");
        bool selected = session.SelectActiveFace("test.face.secondary");

        Assert.Equal(["test.face.sloped", "test.face.secondary"], faces.Select(face => face.Id));
        Assert.True(selected);
        Assert.Equal(["test.a", "test.b", "test.off"], session.SelectedVertexIds);
        Assert.Equal("test.b", session.ActiveVertexId);
        Assert.Equal("test.face.secondary", session.ActiveFaceId);
    }

    [Fact]
    public void Active_face_lifecycle_follows_active_vertex_without_arbitrary_choice()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);

        session.SelectVertex("test.a", extend: false);
        Assert.Null(session.ActiveFaceId);

        session.SelectActiveFace("test.face.sloped");
        session.SelectVertex("test.b", extend: true);
        Assert.Equal("test.face.sloped", session.ActiveFaceId);

        session.SelectActiveFace("test.face.secondary");
        session.SelectVertex("test.off", extend: true);
        Assert.Null(session.ActiveFaceId);

        session.SelectVertex("test.b", extend: false);
        session.SelectActiveFace("test.face.secondary");
        session.ToggleVertexSelection("test.a");
        session.ToggleVertexSelection("test.a");
        Assert.Equal("test.face.secondary", session.ActiveFaceId);

        session.ToggleVertexSelection("test.b");
        Assert.Null(session.ActiveVertexId);
        Assert.Null(session.ActiveFaceId);
        Assert.Equal([], session.SelectedVertexIds);
    }

    [Fact]
    public void Active_face_cycles_forward_backward_and_wraps()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.b", extend: false);

        Assert.True(session.CycleActiveFace(1));
        Assert.Equal("test.face.sloped", session.ActiveFaceId);
        Assert.True(session.CycleActiveFace(1));
        Assert.Equal("test.face.secondary", session.ActiveFaceId);
        Assert.True(session.CycleActiveFace(1));
        Assert.Equal("test.face.sloped", session.ActiveFaceId);
        Assert.True(session.CycleActiveFace(-1));
        Assert.Equal("test.face.secondary", session.ActiveFaceId);

        session.SelectVertex("test.isolated", extend: false);
        Assert.False(session.CycleActiveFace(1));
        Assert.Null(session.ActiveFaceId);
    }

    [Fact]
    public void Face_plane_drag_uses_non_axis_aligned_plane_intersection()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        Assert.True(session.SelectActiveFace("test.face.sloped"));
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);

        Assert.Equal(FaceDragMode.Plane, drag.ActiveFaceDragMode);
        drag.Apply(session, projection, new Point(60, 40));

        DVec3 delta = session.GetVertexPosition("test.a") - drag.OriginalPositions["test.a"];
        Assert.InRange(Math.Abs(DVec3.Dot(delta, drag.ActiveFaceNormal!.Value)), 0, 1e-9);
        AssertDVec3Close(new DVec3(1, 1.5, 1), delta);
        Assert.NotEqual(0, delta.Y);
        Assert.NotEqual(0, delta.Z);
        AssertGroupDelta(session, drag.OriginalPositions, delta);
    }

    [Fact]
    public void Captured_face_plane_does_not_recompute_mid_drag()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.b", "test.c"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);
        drag.Apply(session, projection, new Point(60, 40));
        DVec3 firstDelta = session.GetVertexPosition("test.a") - drag.OriginalPositions["test.a"];

        drag.Apply(session, projection, new Point(70, 30));
        DVec3 secondDelta = session.GetVertexPosition("test.a") - drag.OriginalPositions["test.a"];

        AssertDVec3Close(new DVec3(1, 1.5, 1), firstDelta);
        AssertDVec3Close(new DVec3(2, 3, 2), secondDelta);
        Assert.InRange(Math.Abs(DVec3.Dot(secondDelta, drag.ActiveFaceNormal!.Value)), 0, 1e-9);
    }

    [Fact]
    public void Face_drag_without_active_face_blocks_without_mutation_or_command()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.b", extend: false);
        session.ClearActiveFace();
        DVec3 before = session.GetVertexPosition("test.b");

        bool captured = VertexDragOperation.TryCapture(
            session,
            EditingConstraintMode.ActiveFacePlane,
            new Point(50, 50),
            new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f },
            new Rectangle(0, 0, 100, 100),
            out _,
            out string? failure);

        Assert.False(captured);
        Assert.Contains("no active face", failure);
        Assert.Equal(before, session.GetVertexPosition("test.b"));
        Assert.Equal(0, session.History.Count);
    }

    [Fact]
    public void Edge_on_face_plane_drag_uses_visible_intersection_line()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);

        bool captured = VertexDragOperation.TryCapture(
            session,
            EditingConstraintMode.ActiveFacePlane,
            new Point(50, 50),
            projection,
            viewport,
            out VertexDragOperation? drag,
            out string? message);

        Assert.True(captured);
        Assert.NotNull(drag);
        Assert.Contains("edge-on", message);
        Assert.Equal(FaceDragMode.VisibleLine, drag.ActiveFaceDragMode);
        DVec3 line = drag.ActiveFaceLineDirection!.Value;
        drag.Apply(session, projection, new Point(60, 40));

        DVec3 delta = session.GetVertexPosition("test.a") - drag.OriginalPositions["test.a"];
        Assert.True(delta.Length > 1e-9);
        Assert.InRange(Math.Abs(DVec3.Dot(delta, drag.ActiveFaceNormal!.Value)), 0, 1e-9);
        Assert.InRange(Math.Abs(DVec3.Dot(delta, projection.ViewDirection)), 0, 1e-9);
        Assert.InRange((delta - line * DVec3.Dot(delta, line)).Length, 0, 1e-9);
        AssertGroupDelta(session, drag.OriginalPositions, delta);
    }

    [Fact]
    public void Edge_on_face_plane_drag_ignores_perpendicular_screen_motion()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.a", extend: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);

        drag.Apply(session, projection, new Point(80, 70));

        DVec3 delta = session.GetVertexPosition("test.a") - drag.OriginalPositions["test.a"];
        Assert.InRange(delta.Length, 0, 1e-9);
    }

    [Fact]
    public void Near_edge_on_face_plane_drag_chooses_visible_line_mode()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddNearEdgeOnFaceFixture(session);
        session.SelectVertex("test.near.a", extend: false);
        session.SelectActiveFace("test.face.near-edge");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);

        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);

        Assert.Equal(FaceDragMode.VisibleLine, drag.ActiveFaceDragMode);
        double viewDot = Math.Abs(DVec3.Dot(drag.ActiveFaceNormal!.Value, projection.ViewDirection));
        Assert.InRange(viewDot, 0, OrthographicProjection.FacePlaneLineModeEpsilon);
    }

    [Fact]
    public void Degenerate_face_plane_drag_blocks_safely()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session, includeDegenerate: true);
        var viewport = new Rectangle(0, 0, 100, 100);

        session.SelectVertex("test.deg.a", extend: false);
        session.SelectActiveFace("test.face.degenerate");
        Assert.False(VertexDragOperation.TryCapture(
            session,
            EditingConstraintMode.ActiveFacePlane,
            new Point(50, 50),
            new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f },
            viewport,
            out _,
            out string? degenerateFailure));
        Assert.Contains("degenerate", degenerateFailure);

        session.SelectVertex("test.a", extend: false);
        session.SelectActiveFace("test.face.sloped");
        Assert.True(VertexDragOperation.TryCapture(
            session,
            EditingConstraintMode.ActiveFacePlane,
            new Point(50, 50),
            new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f },
            viewport,
            out VertexDragOperation? drag,
            out _));
        Assert.NotNull(drag);
        Assert.Equal(0, session.History.Count);
    }

    [Fact]
    public void Edge_on_face_plane_undo_redo_and_cancellation_restore_complete_group()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);
        drag.Apply(session, projection, new Point(60, 40));
        Dictionary<string, DVec3> after = drag.OriginalPositions.Keys.ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);

        drag.Restore(session);
        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));

        session.Execute(new MoveVerticesCommand(drag.OriginalPositions, after));
        session.Undo();
        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));

        session.Redo();
        foreach ((string id, DVec3 position) in after)
            Assert.Equal(position, session.GetVertexPosition(id));
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void Face_plane_undo_redo_and_cancellation_restore_complete_group()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport);
        drag.Apply(session, projection, new Point(60, 40));
        Dictionary<string, DVec3> after = drag.OriginalPositions.Keys.ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);

        drag.Restore(session);
        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));

        session.Execute(new MoveVerticesCommand(drag.OriginalPositions, after));
        session.Undo();
        foreach ((string id, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(id));

        session.Redo();
        foreach ((string id, DVec3 position) in after)
            Assert.Equal(position, session.GetVertexPosition(id));
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void Active_face_overlay_data_contains_face_vertices_and_active_vertex()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.b", extend: false);
        session.SelectActiveFace("test.face.sloped");

        ActiveFaceOverlayData overlay = session.GetActiveFaceOverlayData();

        Assert.Equal("test.face.sloped", overlay.ActiveFaceId);
        Assert.Equal("test.b", overlay.ActiveVertexId);
        Assert.Equal(session.GetVertexPosition("test.b"), overlay.ActiveVertexPosition);
        Assert.Equal(
            new[] { "test.a", "test.b", "test.c", "test.d" }.Select(session.GetVertexPosition).ToArray(),
            overlay.FaceVertices);
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

    [Fact]
    public void Overlapping_vertex_hit_candidates_are_deterministic_and_pick_first_candidate()
    {
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        var vertices = new[]
        {
            new SemanticHullVertex("tie.b", new DVec3(0, 2, 0)),
            new SemanticHullVertex("near", new DVec3(0.2, 10, 0)),
            new SemanticHullVertex("tie.a", new DVec3(0, 2, 0)),
            new SemanticHullVertex("front", new DVec3(0, -1, 0)),
        };

        IReadOnlyList<VertexHitCandidate> first = OrthographicVertexHitTester.GetVertexHitCandidates(vertices, projection, viewport, new Point(50, 50));
        IReadOnlyList<VertexHitCandidate> second = OrthographicVertexHitTester.GetVertexHitCandidates(vertices.Reverse(), projection, viewport, new Point(50, 50));

        Assert.Equal(["front", "tie.a", "tie.b", "near"], first.Select(candidate => candidate.VertexId));
        Assert.Equal(first.Select(candidate => candidate.VertexId), second.Select(candidate => candidate.VertexId));
        Assert.Equal(first[0].VertexId, OrthographicVertexHitTester.PickVertexId(vertices, projection, viewport, new Point(50, 50)));
    }

    [Fact]
    public void Coincident_vertex_membership_does_not_transfer_active_state()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddVertex(session, "test.coincident.a", new DVec3(0, 0, 0));
        AddVertex(session, "test.coincident.b", new DVec3(0, 1, 0));
        session.SelectVertex("test.coincident.a", extend: false);
        session.ToggleVertexSelection("test.coincident.b");

        session.ToggleVertexSelection("test.coincident.a");

        Assert.Equal(["test.coincident.b"], session.SelectedVertexIds);
        Assert.Null(session.ActiveVertexId);
        Assert.Null(session.ActiveFaceId);
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

    private static void AddFaceSelectionFixture(ObjectDesignerSession session, bool includeDegenerate = false)
    {
        AddVertex(session, "test.a", new DVec3(0, 0, 0));
        AddVertex(session, "test.b", new DVec3(4, 0, 0));
        AddVertex(session, "test.c", new DVec3(4, 3, 2));
        AddVertex(session, "test.d", new DVec3(0, 3, 2));
        AddVertex(session, "test.e", new DVec3(4, 0, 3));
        AddVertex(session, "test.off", new DVec3(10, 10, 10));
        AddVertex(session, "test.isolated", new DVec3(20, 0, 0));
        AddFace(session, "test.face.sloped", ["test.a", "test.b", "test.c", "test.d"], HullSurfaceRole.PanelSeat, "test-metal");
        AddFace(session, "test.face.secondary", ["test.b", "test.e", "test.c"], HullSurfaceRole.ServiceSurface, "test-service");

        if (includeDegenerate)
        {
            AddVertex(session, "test.deg.a", new DVec3(30, 0, 0));
            AddVertex(session, "test.deg.b", new DVec3(31, 0, 0));
            AddVertex(session, "test.deg.c", new DVec3(32, 0, 0));
            AddFace(session, "test.face.degenerate", ["test.deg.a", "test.deg.b", "test.deg.c"], HullSurfaceRole.PanelSeat, "test-metal");
        }

        session.RecomputeFaceNormals();
        session.Rebuild();
        session.ClearSelection();
    }

    private static void AddNearEdgeOnFaceFixture(ObjectDesignerSession session)
    {
        AddVertex(session, "test.near.a", new DVec3(0, 0, 0));
        AddVertex(session, "test.near.b", new DVec3(4, 0, -0.0006666666666666666));
        AddVertex(session, "test.near.c", new DVec3(4, 3, 1.9993333333333334));
        AddVertex(session, "test.near.d", new DVec3(0, 3, 2));
        AddFace(session, "test.face.near-edge", ["test.near.a", "test.near.b", "test.near.c", "test.near.d"], HullSurfaceRole.PanelSeat, "test-metal");
        session.RecomputeFaceNormals();
        session.Rebuild();
        session.ClearSelection();
    }

    private static void AddVertex(ObjectDesignerSession session, string id, DVec3 position)
        => session.Document.Hull.VisualGeometry.Vertices.Add(new SemanticHullVertexDto
        {
            Id = id,
            Position = Vec3Dto.From(position),
        });

    private static void AddFace(
        ObjectDesignerSession session,
        string id,
        IEnumerable<string> vertexIds,
        HullSurfaceRole role,
        string materialGroup)
        => session.Document.Hull.VisualGeometry.Faces.Add(new SemanticHullFaceDto
        {
            Id = id,
            VertexIds = vertexIds.ToList(),
            Role = role,
            MaterialGroup = materialGroup,
            OutwardNormal = Vec3Dto.From(DVec3.UnitY),
            ContributesToClosedHull = false,
        });

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
