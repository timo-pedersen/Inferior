using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;
using Inferior.ObjectDesigner.Controls;
using Inferior.ObjectDesigner.Editing;
using Inferior.UI;
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

    [Theory]
    [InlineData(ProjectionKind.Top, true, 1, 0, 0)]
    [InlineData(ProjectionKind.Top, false, 0, 0, 1)]
    [InlineData(ProjectionKind.Side, true, 0, 0, 1)]
    [InlineData(ProjectionKind.Side, false, 0, 1, 0)]
    [InlineData(ProjectionKind.Front, true, 1, 0, 0)]
    [InlineData(ProjectionKind.Front, false, 0, 1, 0)]
    public void Shift_drag_lock_uses_projection_view_axes(
        ProjectionKind kind,
        bool horizontal,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string id = FirstVertexIds(session, 1)[0];
        session.SelectVertex(id, extend: false);
        var projection = new OrthographicProjection { Kind = kind, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);

        Point mouse = horizontal ? new Point(10, 2) : new Point(2, -10);
        DVec3 delta = DeltaFor(drag, projection, id, mouse, shiftHeld: true);

        Assert.Equal(horizontal ? ShiftDragAxis.Horizontal : ShiftDragAxis.Vertical, drag.ActiveShiftDragAxis);
        AssertDVec3Close(new DVec3(expectedX, expectedY, expectedZ), delta);
    }

    [Fact]
    public void Shift_drag_axis_choice_dead_zone_tie_and_dynamic_switch_are_deterministic()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string id = FirstVertexIds(session, 1)[0];
        session.SelectVertex(id, extend: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };

        VertexDragOperation deadZone = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        AssertDVec3Close(DVec3.Zero, DeltaFor(deadZone, projection, id, new Point(3, 0), shiftHeld: true));
        Assert.Null(deadZone.ActiveShiftDragAxis);
        Assert.Equal("SHIFT LOCK: move to choose axis", deadZone.ShiftDragStatus);

        VertexDragOperation tie = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        AssertDVec3Close(new DVec3(0.5, 0, 0.5), DeltaFor(tie, projection, id, new Point(5, -5), shiftHeld: true));
        Assert.Equal(ShiftDragAxis.DiagonalUpRight, tie.ActiveShiftDragAxis);

        VertexDragOperation switching = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);
        DeltaFor(switching, projection, id, new Point(10, -2), shiftHeld: true);
        DVec3 crossed = DeltaFor(switching, projection, id, new Point(2, -30), shiftHeld: true);

        Assert.Equal(ShiftDragAxis.Vertical, switching.ActiveShiftDragAxis);
        AssertDVec3Close(new DVec3(0, 0, 3), crossed);
    }

    [Fact]
    public void Shift_drag_uses_original_drag_origin_and_persistent_constraint_resumes()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string id = FirstVertexIds(session, 1)[0];
        session.SelectVertex(id, extend: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);

        DVec3 beforeShift = DeltaFor(drag, projection, id, new Point(20, -10), shiftHeld: false);
        DVec3 shiftPressed = DeltaFor(drag, projection, id, new Point(20, -10), shiftHeld: true);
        DVec3 shifted = DeltaFor(drag, projection, id, new Point(100, -20), shiftHeld: true);
        DVec3 shiftReleased = DeltaFor(drag, projection, id, new Point(100, -20), shiftHeld: false);
        DVec3 resumed = DeltaFor(drag, projection, id, new Point(110, -30), shiftHeld: false);

        AssertDVec3Close(new DVec3(2, 0, 1), beforeShift);
        AssertDVec3Close(new DVec3(1.5, 0, 1.5), shiftPressed);
        AssertDVec3Close(new DVec3(10, 0, 0), shifted);
        AssertDVec3Close(new DVec3(10, 0, 2), shiftReleased);
        AssertDVec3Close(new DVec3(11, 0, 3), resumed);
    }

    [Fact]
    public void Shift_drag_overrides_axis_constraint_temporarily_then_resumes_it()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        string id = FirstVertexIds(session, 1)[0];
        session.SelectVertex(id, extend: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.AxisX, Point.Zero);

        DVec3 beforeShift = DeltaFor(drag, projection, id, new Point(20, -10), shiftHeld: false);
        DVec3 shiftPressed = DeltaFor(drag, projection, id, new Point(20, -10), shiftHeld: true);
        DVec3 shifted = DeltaFor(drag, projection, id, new Point(20, -30), shiftHeld: true);
        DVec3 shiftReleased = DeltaFor(drag, projection, id, new Point(20, -30), shiftHeld: false);
        DVec3 resumed = DeltaFor(drag, projection, id, new Point(30, -40), shiftHeld: false);

        AssertDVec3Close(new DVec3(2, 0, 0), beforeShift);
        AssertDVec3Close(new DVec3(1.5, 0, 1.5), shiftPressed);
        AssertDVec3Close(new DVec3(2.5, 0, 2.5), shifted);
        AssertDVec3Close(new DVec3(2, 0, 0), shiftReleased);
        AssertDVec3Close(new DVec3(3, 0, 0), resumed);
        Assert.Equal(EditingConstraintMode.AxisX, drag.ConstraintMode);
    }

    [Fact]
    public void Shift_drag_transitions_preserve_face_plane_and_visible_line_resume()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession planeSession = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(planeSession);
        planeSession.SelectVertices(["test.a", "test.off"], replace: true);
        planeSession.BeginVertexDragSelection("test.a", ctrl: false);
        planeSession.SelectActiveFace("test.face.sloped");
        var top = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation planeDrag = VertexDragOperation.Capture(planeSession, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), top, viewport);

        DVec3 planeBeforeShift = DeltaFor(planeDrag, top, "test.a", new Point(60, 40), shiftHeld: false);
        DVec3 planeShiftPressed = DeltaFor(planeDrag, top, "test.a", new Point(60, 40), shiftHeld: true);
        DVec3 planeShiftReleased = DeltaFor(planeDrag, top, "test.a", new Point(70, 30), shiftHeld: false);
        DVec3 planeResumed = DeltaFor(planeDrag, top, "test.a", new Point(80, 20), shiftHeld: false);

        AssertDVec3Close(new DVec3(1, 1.5, 1), planeBeforeShift);
        AssertDVec3Close(new DVec3(1, 0, 1), planeShiftPressed);
        AssertDVec3Close(new DVec3(2, 3, 2), planeShiftReleased);
        AssertDVec3Close(new DVec3(3, 4.5, 3), planeResumed);

        ObjectDesignerSession lineSession = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(lineSession);
        lineSession.SelectVertices(["test.a", "test.off"], replace: true);
        lineSession.BeginVertexDragSelection("test.a", ctrl: false);
        lineSession.SelectActiveFace("test.face.sloped");
        var side = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        VertexDragOperation lineDrag = VertexDragOperation.Capture(lineSession, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), side, viewport);

        DVec3 lineBeforeShift = DeltaFor(lineDrag, side, "test.a", new Point(60, 40), shiftHeld: false);
        DVec3 lineShiftPressed = DeltaFor(lineDrag, side, "test.a", new Point(60, 40), shiftHeld: true);
        DVec3 lineShiftReleased = DeltaFor(lineDrag, side, "test.a", new Point(70, 30), shiftHeld: false);

        Assert.Equal(FaceDragMode.VisibleLine, lineDrag.ActiveFaceDragMode);
        AssertDVec3Close(new DVec3(0, 1.1538461538461537, 0.7692307692307693), lineBeforeShift);
        AssertDVec3Close(new DVec3(0, 1, 1), lineShiftPressed);
        AssertDVec3Close(new DVec3(0, 2.3076923076923075, 1.5384615384615385), lineShiftReleased);
    }

    [Fact]
    public void Shift_drag_preserves_selection_active_face_group_delta_and_history()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.b", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero);

        drag.Apply(session, projection, new Point(20, -10), shiftHeld: false);
        drag.Apply(session, projection, new Point(100, -20), shiftHeld: true);
        drag.Apply(session, projection, new Point(110, -30), shiftHeld: false);
        Dictionary<string, DVec3> after = drag.OriginalPositions.Keys.ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);

        Assert.Equal(["test.a", "test.b", "test.off"], session.SelectedVertexIds);
        Assert.Equal("test.a", session.ActiveVertexId);
        Assert.Equal("test.face.sloped", session.ActiveFaceId);
        AssertGroupDelta(session, drag.OriginalPositions, new DVec3(11, 0, 3));

        drag.Restore(session);
        session.Execute(new MoveVerticesCommand(drag.OriginalPositions, after));
        Assert.Equal(1, session.History.Count);
        session.Undo();
        foreach ((string vertexId, DVec3 before) in drag.OriginalPositions)
            Assert.Equal(before, session.GetVertexPosition(vertexId));
        session.Redo();
        foreach ((string vertexId, DVec3 position) in after)
            Assert.Equal(position, session.GetVertexPosition(vertexId));
    }

    [Fact]
    public void Incident_face_row_composes_two_lines_and_uses_full_hit_bounds()
    {
        var panel = new Inferior.UI.Controls.Panel { Bounds = new Rectangle(300, 20, 330, 340), ContentPadding = 8, Overflow = OverflowMode.Clip };
        var row = new IncidentFaceRow
        {
            Bounds = new Rectangle(0, 88, 304, 36),
            FaceId = "beren.top.platform.face.with.a.very.long.authored.identifier",
            Metadata = IncidentFaceRow.BuildMetadata("PanelSeat", "panel-exterior", 9),
            IsActiveFace = true,
        };
        panel.Add(row);

        Assert.Equal("PanelSeat / panel-exterior / 9 vertices", row.Metadata);
        Assert.DoesNotContain("beren.vertex", row.Metadata);
        Assert.True(panel.ContentBounds.Contains(row.AbsoluteBounds.Left, row.AbsoluteBounds.Top));
        Assert.True(panel.ContentBounds.Contains(row.AbsoluteBounds.Right - 1, row.AbsoluteBounds.Bottom - 1));
        Assert.True(row.HitTest(new Point(row.AbsoluteBounds.Right - 2, row.AbsoluteBounds.Bottom - 2)));
        Assert.Equal(36, row.Bounds.Height);
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

    [Theory]
    [InlineData(LinearSnapMode.Metre, 1.24, 1)]
    [InlineData(LinearSnapMode.Metre, 1.5, 2)]
    [InlineData(LinearSnapMode.Metre, -1.5, -2)]
    [InlineData(LinearSnapMode.Decimetre, 1.24, 1.2)]
    [InlineData(LinearSnapMode.Centimetre, -1.235, -1.24)]
    public void Linear_snap_rounds_coordinates_with_midpoints_away_from_zero(LinearSnapMode mode, double value, double expected)
    {
        double spacing = LinearSnap.SpacingFor(mode)!.Value;

        double snapped = LinearSnap.SnapCoordinate(value, spacing);

        Assert.InRange(Math.Abs(snapped - expected), 0, 1e-9);
    }

    [Fact]
    public void Linear_snap_off_preserves_unsnapped_drag_result()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddVertex(session, "test.snap.a", new DVec3(0.24, 0, 0.26));
        session.SelectVertex("test.snap.a", extend: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero, snapMode: LinearSnapMode.Off);

        DVec3 delta = DeltaFor(drag, projection, "test.snap.a", new Point(4, -6), shiftHeld: false);

        Assert.Null(drag.SnapSpacing);
        AssertDVec3Close(new DVec3(0.4, 0, 0.6), delta, 1e-7);
    }

    [Fact]
    public void Plane_snap_quantizes_active_vertex_visible_coordinates_and_moves_group_rigidly()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddVertex(session, "test.snap.a", new DVec3(0.24, 0, 0.26));
        AddVertex(session, "test.snap.b", new DVec3(2.31, 1, 3.39));
        session.SelectVertices(["test.snap.a", "test.snap.b"], replace: true);
        session.BeginVertexDragSelection("test.snap.a", ctrl: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero, snapMode: LinearSnapMode.Decimetre);

        drag.Apply(session, projection, new Point(4, -6));

        DVec3 expectedDelta = new(0.36, 0, 0.64);
        AssertDVec3Close(new DVec3(0.6, 0, 0.9), session.GetVertexPosition("test.snap.a"), 1e-7);
        AssertGroupDelta(session, drag.OriginalPositions, expectedDelta, 1e-7);
        Assert.Equal("SNAP 10 cm", drag.SnapStatus);
    }

    [Theory]
    [InlineData(EditingConstraintMode.AxisX, ProjectionKind.Top, 0.24, 0.26, 4, -6, 0.76, 0, 0)]
    [InlineData(EditingConstraintMode.AxisZ, ProjectionKind.Top, 0.24, 0.26, 4, -6, 0, 0, 0.74)]
    [InlineData(EditingConstraintMode.AxisY, ProjectionKind.Front, 0.24, 0.26, 4, -6, 0, 0.74, 0)]
    public void Axis_snap_quantizes_only_the_moving_world_coordinate(
        EditingConstraintMode constraint,
        ProjectionKind kind,
        double x,
        double visibleY,
        int mouseX,
        int mouseY,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        DVec3 start = kind == ProjectionKind.Front
            ? new DVec3(x, visibleY, 0)
            : new DVec3(x, 0, visibleY);
        AddVertex(session, "test.snap.a", start);
        session.SelectVertex("test.snap.a", extend: false);
        var projection = new OrthographicProjection { Kind = kind, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, constraint, Point.Zero, snapMode: LinearSnapMode.Metre);

        DVec3 delta = DeltaFor(drag, projection, "test.snap.a", new Point(mouseX, mouseY), shiftHeld: false);

        AssertDVec3Close(new DVec3(expectedX, expectedY, expectedZ), delta);
    }

    [Fact]
    public void Shift_snap_quantizes_only_the_current_shift_axis()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddVertex(session, "test.snap.a", new DVec3(0.24, 0, 0.26));
        session.SelectVertex("test.snap.a", extend: false);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero, snapMode: LinearSnapMode.Metre);

        DVec3 horizontal = DeltaFor(drag, projection, "test.snap.a", new Point(4, -1), shiftHeld: true);
        VertexDragOperation verticalDrag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero, snapMode: LinearSnapMode.Metre);
        DVec3 vertical = DeltaFor(verticalDrag, projection, "test.snap.a", new Point(1, -6), shiftHeld: true);
        VertexDragOperation diagonalDrag = VertexDragOperation.Capture(session, EditingConstraintMode.ViewPlane, Point.Zero, snapMode: LinearSnapMode.Metre);
        DVec3 diagonal = DeltaFor(diagonalDrag, projection, "test.snap.a", new Point(7, -7), shiftHeld: true);

        AssertDVec3Close(new DVec3(0.76, 0, 0), horizontal, 1e-7);
        AssertDVec3Close(new DVec3(0, 0, 0.74), vertical, 1e-7);
        Assert.Equal(ShiftDragAxis.DiagonalUpRight, diagonalDrag.ActiveShiftDragAxis);
        AssertDVec3Close(new DVec3(0.76, 0, 0.76), diagonal, 1e-7);
    }

    [Fact]
    public void Face_plane_snap_keeps_oblique_face_constraint_and_lands_on_visible_grid_intersection()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertices(["test.a", "test.off"], replace: true);
        session.BeginVertexDragSelection("test.a", ctrl: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport, LinearSnapMode.Metre);

        DVec3 delta = DeltaFor(drag, projection, "test.a", new Point(56, 44), shiftHeld: false);

        AssertDVec3Close(new DVec3(1, 1.5, 1), delta);
        Assert.InRange(Math.Abs(DVec3.Dot(delta, drag.ActiveFaceNormal!.Value)), 0, 1e-9);
    }

    [Fact]
    public void Edge_on_face_snap_stays_on_visible_line_and_reaches_a_grid_line()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        AddFaceSelectionFixture(session);
        session.SelectVertex("test.a", extend: false);
        session.SelectActiveFace("test.face.sloped");
        var projection = new OrthographicProjection { Kind = ProjectionKind.Side, PixelsPerMeter = 10f };
        var viewport = new Rectangle(0, 0, 100, 100);
        VertexDragOperation drag = VertexDragOperation.Capture(session, EditingConstraintMode.ActiveFacePlane, new Point(50, 50), projection, viewport, LinearSnapMode.Metre);

        DVec3 delta = DeltaFor(drag, projection, "test.a", new Point(60, 40), shiftHeld: false);
        Vector2 axes = projection.ToProjectionAxes(drag.OriginalPositions["test.a"] + delta);

        Assert.Equal(FaceDragMode.VisibleLine, drag.ActiveFaceDragMode);
        Assert.InRange(Math.Abs(DVec3.Dot(delta, drag.ActiveFaceNormal!.Value)), 0, 1e-9);
        Assert.InRange(Math.Abs(DVec3.Dot(delta, projection.ViewDirection)), 0, 1e-9);
        Assert.True(LinearSnap.IsMultiple(axes.X, 1.0) || LinearSnap.IsMultiple(axes.Y, 1.0));
    }

    [Fact]
    public void Metric_grid_is_world_anchored_excludes_coarser_lines_and_uses_zoom_thresholds()
    {
        var viewport = new Rectangle(0, 0, 100, 100);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 100f };

        IReadOnlyList<MetricGridLine> centered = MetricGrid.Generate(projection, viewport);
        projection.PanPixels = new Vector2(13, -7);
        IReadOnlyList<MetricGridLine> panned = MetricGrid.Generate(projection, viewport);

        Assert.Contains(centered, line => line.Vertical && line.Spacing == MetricGrid.MetreSpacing && Math.Abs(line.Coordinate) <= 1e-9);
        Assert.Contains(centered, line => !line.Vertical && line.Spacing == MetricGrid.DecimetreSpacing && Math.Abs(line.Coordinate - 0.1) <= 1e-9);
        Assert.DoesNotContain(centered, line => line.Spacing == MetricGrid.DecimetreSpacing && LinearSnap.IsMultiple(line.Coordinate, 1.0));
        Assert.DoesNotContain(centered, line => line.Spacing == MetricGrid.CentimetreSpacing && LinearSnap.IsMultiple(line.Coordinate, 0.1));
        Assert.NotEqual(centered.Where(line => line.Vertical).Select(line => line.Coordinate), panned.Where(line => line.Vertical).Select(line => line.Coordinate));

        Assert.Equal(1f, MetricGrid.OpacityForSpacing(28f, MetricGrid.MetreSpacing));
        Assert.Equal(0f, MetricGrid.OpacityForSpacing(28f, MetricGrid.CentimetreSpacing));
        Assert.True(MetricGrid.OpacityForSpacing(120f, MetricGrid.DecimetreSpacing) > 0f);
        Assert.True(MetricGrid.OpacityForSpacing(1200f, MetricGrid.CentimetreSpacing) > 0f);
    }

    [Fact]
    public void Orthographic_zoom_reaches_centimetre_grid_requirement_and_clamps_cleanly()
    {
        float max = OrthographicNavigation.ApplyWheelZoom(OrthographicNavigation.MaximumPixelsPerMeter, 120);
        float min = OrthographicNavigation.ApplyWheelZoom(OrthographicNavigation.MinimumPixelsPerMeter, -120);

        Assert.Equal(OrthographicNavigation.MaximumPixelsPerMeter, max);
        Assert.Equal(OrthographicNavigation.MinimumPixelsPerMeter, min);
        Assert.True(OrthographicNavigation.MaximumPixelsPerMeter * 0.01 >= OrthographicNavigation.MinimumCentimetreGridPixels);
        Assert.Equal(OrthographicNavigation.MinimumWorldUnitsPerPixel, 1.0 / OrthographicNavigation.MaximumPixelsPerMeter, 12);
        Assert.Equal(OrthographicNavigation.MaximumWorldUnitsPerPixel, 1.0 / OrthographicNavigation.MinimumPixelsPerMeter, 12);
    }

    [Fact]
    public void Orthographic_zoom_is_multiplicative_reversible_and_cursor_centred()
    {
        float start = 28f;
        float zoomed = OrthographicNavigation.ApplyWheelZoom(start, 120);
        float restored = OrthographicNavigation.ApplyWheelZoom(zoomed, -120);
        var viewport = new Rectangle(10, 20, 300, 200);
        var cursor = new Point(87, 142);
        var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = start, PanPixels = new Vector2(17, -23) };
        Vector2 before = projection.ScreenToProjectionAxes(cursor, viewport);

        OrthographicNavigation.ZoomAroundCursor(projection, viewport, cursor, 120);
        Vector2 after = projection.ScreenToProjectionAxes(cursor, viewport);

        Assert.InRange(Math.Abs(zoomed - start * OrthographicNavigation.ZoomStepFactor), 0, 1e-6);
        Assert.InRange(Math.Abs(restored - start), 0, 1e-5);
        Assert.InRange(Vector2.Distance(before, after), 0, 1e-5);
    }

    [Fact]
    public void Metric_grid_opacity_is_smooth_monotonic_and_strength_hierarchical()
    {
        float below = MetricGrid.OpacityForSpacing(40f, MetricGrid.DecimetreSpacing);
        float partial = MetricGrid.OpacityForSpacing(65f, MetricGrid.DecimetreSpacing);
        float full = MetricGrid.OpacityForSpacing(80f, MetricGrid.DecimetreSpacing);
        float centimetreBeforeMax = MetricGrid.OpacityForSpacing(900f, MetricGrid.CentimetreSpacing);

        Assert.Equal(0f, below);
        Assert.InRange(partial, 0.01f, 0.99f);
        Assert.Equal(1f, full);
        Assert.True(below < partial && partial < full);
        Assert.True(centimetreBeforeMax > 0f);
        Assert.True(MetricGrid.StrengthForSpacing(MetricGrid.MetreSpacing) > MetricGrid.StrengthForSpacing(MetricGrid.DecimetreSpacing));
        Assert.True(MetricGrid.StrengthForSpacing(MetricGrid.DecimetreSpacing) > MetricGrid.StrengthForSpacing(MetricGrid.CentimetreSpacing));
    }

    [Fact]
    public void Orthographic_recenter_targets_selected_centroid_or_hull_bounds_center_without_zoom_change()
    {
        var top = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = 77f };
        DVec3[] hull = [new(-2, -4, -6), new(6, 8, 10)];
        DVec3[] selected = [new(1, 9, 2), new(5, -3, 10)];

        Vector2 hullCenter = OrthographicNavigation.HullBoundsCenter(hull, top);
        Vector2 selectedCenter = OrthographicNavigation.Centroid(selected, top);
        top.CenterOnProjectionAxes(selectedCenter);

        Assert.Equal(new Vector2(2, 2), hullCenter);
        Assert.Equal(new Vector2(3, 6), selectedCenter);
        Assert.Equal(77f, top.PixelsPerMeter);
        Assert.Equal(new Vector2(-231, 462), top.PanPixels);
    }

    [Fact]
    public void Toolbar_projection_callback_restores_independent_pan_states()
    {
        ObjectDesignerGame game = ProjectionSwitchHarness(
            ProjectionKind.Side,
            top: new Vector2(10, 1),
            side: new Vector2(200, 2),
            front: new Vector2(-50, 3));

        ProjectionOf(game).PanPixels += new Vector2(25, 0);
        PansOf(game)[ProjectionKind.Side] = ProjectionOf(game).PanPixels;
        InvokeToolbarProjection(game, ProjectionKind.Front);

        Assert.Equal(ProjectionKind.Front, ProjectionOf(game).Kind);
        Assert.Equal(new Vector2(-50, 3), ProjectionOf(game).PanPixels);

        InvokeToolbarProjection(game, ProjectionKind.Side);

        Assert.Equal(new Vector2(225, 2), ProjectionOf(game).PanPixels);
    }

    [Fact]
    public void Toolbar_projection_callback_preserves_three_independent_centres()
    {
        ObjectDesignerGame game = ProjectionSwitchHarness(
            ProjectionKind.Top,
            top: new Vector2(11, 101),
            side: new Vector2(22, 202),
            front: new Vector2(33, 303));

        InvokeToolbarProjection(game, ProjectionKind.Side);
        Assert.Equal(new Vector2(22, 202), ProjectionOf(game).PanPixels);
        InvokeToolbarProjection(game, ProjectionKind.Front);
        Assert.Equal(new Vector2(33, 303), ProjectionOf(game).PanPixels);
        InvokeToolbarProjection(game, ProjectionKind.Top);
        Assert.Equal(new Vector2(11, 101), ProjectionOf(game).PanPixels);
        InvokeToolbarProjection(game, ProjectionKind.Front);
        Assert.Equal(new Vector2(33, 303), ProjectionOf(game).PanPixels);
    }

    [Fact]
    public void Recenter_active_view_does_not_change_other_toolbar_projection_states_or_history()
    {
        using TempAsset asset = TempAsset.FromBeren();
        ObjectDesignerSession session = ObjectDesignerSession.Load(asset.Path);
        session.History.MarkClean();
        ObjectDesignerGame game = ProjectionSwitchHarness(
            ProjectionKind.Top,
            top: new Vector2(99, 99),
            side: new Vector2(22, 202),
            front: new Vector2(33, 303),
            session);

        InvokePrivate(game, "RecenterOrthographicView");
        Vector2 side = PansOf(game)[ProjectionKind.Side];
        InvokeToolbarProjection(game, ProjectionKind.Side);

        Assert.Equal(side, ProjectionOf(game).PanPixels);
        Assert.Equal(new Vector2(22, 202), side);
        Assert.Equal(0, session.History.Count);
    }

    [Fact]
    public void Keyboard_and_toolbar_projection_paths_produce_identical_pan_transitions()
    {
        ObjectDesignerGame keyboard = ProjectionSwitchHarness(ProjectionKind.Top, new Vector2(1, 10), new Vector2(2, 20), new Vector2(3, 30));
        ObjectDesignerGame toolbar = ProjectionSwitchHarness(ProjectionKind.Top, new Vector2(1, 10), new Vector2(2, 20), new Vector2(3, 30));

        InvokePrivate(keyboard, "SetProjection", ProjectionKind.Side);
        InvokeToolbarProjection(toolbar, ProjectionKind.Side);

        Assert.Equal(ProjectionOf(keyboard).Kind, ProjectionOf(toolbar).Kind);
        Assert.Equal(ProjectionOf(keyboard).PanPixels, ProjectionOf(toolbar).PanPixels);
        Assert.Equal(PansOf(keyboard)[ProjectionKind.Top], PansOf(toolbar)[ProjectionKind.Top]);
        Assert.Equal(PansOf(keyboard)[ProjectionKind.Side], PansOf(toolbar)[ProjectionKind.Side]);
    }

    private static string[] FirstVertexIds(ObjectDesignerSession session, int count)
        => session.Document.Hull.VisualGeometry.Vertices
            .Take(count)
            .Select(vertex => vertex.Id)
            .ToArray();

    private static ObjectDesignerGame ProjectionSwitchHarness(
        ProjectionKind kind,
        Vector2 top,
        Vector2 side,
        Vector2 front,
        ObjectDesignerSession? session = null)
    {
        var game = (ObjectDesignerGame)RuntimeHelpers.GetUninitializedObject(typeof(ObjectDesignerGame));
        var projection = new OrthographicProjection { Kind = kind, PixelsPerMeter = 10f };
        var pans = new Dictionary<ProjectionKind, Vector2>
        {
            [ProjectionKind.Top] = top,
            [ProjectionKind.Side] = side,
            [ProjectionKind.Front] = front,
        };
        projection.PanPixels = pans[kind];
        SetField(game, "_projection", projection);
        SetField(game, "_projectionPans", pans);
        SetField(game, "_initializedProjectionPans", new HashSet<ProjectionKind> { ProjectionKind.Top, ProjectionKind.Side, ProjectionKind.Front });
        if (session is not null)
            SetField(game, "_session", session);
        return game;
    }

    private static OrthographicProjection ProjectionOf(ObjectDesignerGame game)
        => (OrthographicProjection)GetField(game, "_projection");

    private static Dictionary<ProjectionKind, Vector2> PansOf(ObjectDesignerGame game)
        => (Dictionary<ProjectionKind, Vector2>)GetField(game, "_projectionPans");

    private static void InvokeToolbarProjection(ObjectDesignerGame game, ProjectionKind kind)
        => InvokePrivate(game, "OnProjectionChoiceChanged", kind);

    private static object? InvokePrivate(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static object GetField(object target, string field)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void SetField(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void AssertGroupDelta(
        ObjectDesignerSession session,
        IReadOnlyDictionary<string, DVec3> before,
        DVec3 expectedDelta,
        double tolerance = 1e-9)
    {
        foreach ((string id, DVec3 position) in before)
            AssertDVec3Close(expectedDelta, session.GetVertexPosition(id) - position, tolerance);
    }

    private static DVec3 DeltaFor(
        VertexDragOperation drag,
        OrthographicProjection projection,
        string vertexId,
        Point mouse,
        bool shiftHeld)
        => drag.PositionsFor(projection, mouse, shiftHeld)[vertexId] - drag.OriginalPositions[vertexId];

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
