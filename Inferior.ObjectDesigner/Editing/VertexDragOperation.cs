using Inferior.Core.Math;
using Inferior.Gameplay.Hull.Authoring;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public sealed class VertexDragOperation
{
    private readonly IReadOnlyDictionary<string, DVec3> _originalPositions;
    private readonly string? _activeVertexId;
    private readonly DVec3 _referencePosition;
    private readonly EditingConstraintMode _constraintMode;
    private readonly CapturedFacePlane? _activeFacePlane;
    private readonly DVec3? _facePlaneStartPoint;
    private readonly Point _startMouse;

    private VertexDragOperation(
        IReadOnlyDictionary<string, DVec3> originalPositions,
        string? activeVertexId,
        DVec3 referencePosition,
        EditingConstraintMode constraintMode,
        CapturedFacePlane? activeFacePlane,
        DVec3? facePlaneStartPoint,
        Point startMouse)
    {
        _originalPositions = originalPositions;
        _activeVertexId = activeVertexId;
        _referencePosition = referencePosition;
        _constraintMode = constraintMode;
        _activeFacePlane = activeFacePlane;
        _facePlaneStartPoint = facePlaneStartPoint;
        _startMouse = startMouse;
    }

    public IReadOnlyDictionary<string, DVec3> OriginalPositions => _originalPositions;
    public string? ActiveVertexId => _activeVertexId;
    public string? ActiveFaceId => _activeFacePlane?.FaceId;
    public DVec3? ActiveFaceNormal => _activeFacePlane?.Normal;
    public EditingConstraintMode ConstraintMode => _constraintMode;
    public Point StartMouse => _startMouse;

    public static VertexDragOperation Capture(
        ObjectDesignerSession session,
        EditingConstraintMode constraintMode,
        Point startMouse,
        OrthographicProjection? projection = null,
        Rectangle viewport = default)
    {
        if (!TryCapture(session, constraintMode, startMouse, projection, viewport, out VertexDragOperation? operation, out string? failure))
            throw new InvalidOperationException(failure);
        return operation ?? throw new InvalidOperationException("Vertex drag capture did not return an operation.");
    }

    public static bool TryCapture(
        ObjectDesignerSession session,
        EditingConstraintMode constraintMode,
        Point startMouse,
        OrthographicProjection? projection,
        Rectangle viewport,
        out VertexDragOperation? operation,
        out string? failure)
    {
        Dictionary<string, DVec3> originalPositions = session.SelectedVertexIds
            .ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);
        string? activeVertexId = session.ActiveVertexId;
        DVec3 referencePosition = activeVertexId is not null && originalPositions.TryGetValue(activeVertexId, out DVec3 activePosition)
            ? activePosition
            : originalPositions.Values.FirstOrDefault();
        CapturedFacePlane? activeFacePlane = null;
        DVec3? facePlaneStartPoint = null;

        if (constraintMode == EditingConstraintMode.ActiveFacePlane)
        {
            if (projection is null || viewport.Width <= 0 || viewport.Height <= 0)
            {
                operation = null;
                failure = "Cannot use Face Plane: projection bounds are unavailable.";
                return false;
            }
            if (session.ActiveFaceId is null)
            {
                operation = null;
                failure = "Cannot use Face Plane: no active face selected.";
                return false;
            }
            if (!TryCaptureFacePlane(session, session.ActiveFaceId, viewport, out activeFacePlane, out failure))
            {
                operation = null;
                return false;
            }
            CapturedFacePlane plane = activeFacePlane ?? throw new InvalidOperationException("Face plane capture did not return a plane.");
            if (!projection.TryIntersectScreenRayWithPlane(startMouse, viewport, plane.Origin, plane.Normal, out DVec3 startPoint))
            {
                operation = null;
                failure = "Active face is edge-on in the current 2D view. Switch Top, Side, or Front view.";
                return false;
            }
            facePlaneStartPoint = startPoint;
        }

        operation = new VertexDragOperation(
            originalPositions,
            activeVertexId,
            referencePosition,
            constraintMode,
            activeFacePlane,
            facePlaneStartPoint,
            startMouse);
        failure = null;
        return true;
    }

    public IReadOnlyDictionary<string, DVec3> PositionsFor(OrthographicProjection projection, Point mousePosition)
    {
        DVec3 delta = ConstrainedDelta(projection, mousePosition);
        return _originalPositions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value + delta,
            StringComparer.Ordinal);
    }

    public void Apply(ObjectDesignerSession session, OrthographicProjection projection, Point mousePosition)
    {
        foreach ((string vertexId, DVec3 position) in PositionsFor(projection, mousePosition))
            session.SetVertexPosition(vertexId, position, rebuild: false);
        session.RecomputeFaceNormals();
        session.Rebuild();
    }

    public void Restore(ObjectDesignerSession session)
    {
        foreach ((string vertexId, DVec3 position) in _originalPositions)
            session.SetVertexPosition(vertexId, position, rebuild: false);
        session.RecomputeFaceNormals();
        session.Rebuild();
    }

    private DVec3 ConstrainedDelta(OrthographicProjection projection, Point mousePosition)
    {
        Vector2 screenDelta = (mousePosition - _startMouse).ToVector2();
        DVec3 rawDelta = projection.ApplyScreenDelta(_referencePosition, screenDelta) - _referencePosition;
        return _constraintMode switch
        {
            EditingConstraintMode.AxisX => new DVec3(rawDelta.X, 0, 0),
            EditingConstraintMode.AxisY => new DVec3(0, rawDelta.Y, 0),
            EditingConstraintMode.AxisZ => new DVec3(0, 0, rawDelta.Z),
            EditingConstraintMode.ActiveFacePlane => FacePlaneDelta(projection, mousePosition),
            _ => rawDelta,
        };
    }

    private DVec3 FacePlaneDelta(OrthographicProjection projection, Point mousePosition)
    {
        if (_activeFacePlane is null || _facePlaneStartPoint is null)
            throw new InvalidOperationException("Cannot use Face Plane: no active face plane was captured.");
        if (!projection.TryIntersectScreenRayWithPlane(mousePosition, _activeFacePlane.Viewport, _activeFacePlane.Origin, _activeFacePlane.Normal, out DVec3 currentPoint))
            throw new InvalidOperationException("Active face is edge-on in the current 2D view. Switch Top, Side, or Front view.");
        return currentPoint - _facePlaneStartPoint.Value;
    }

    private static bool TryCaptureFacePlane(
        ObjectDesignerSession session,
        string faceId,
        Rectangle viewport,
        out CapturedFacePlane? plane,
        out string? failure)
    {
        plane = null;
        SemanticHullFaceDto? face = session.GetActiveFace();
        if (face is null || !string.Equals(face.Id, faceId, StringComparison.Ordinal))
        {
            failure = "Cannot use Face Plane: active face no longer exists.";
            return false;
        }
        if (session.ActiveVertexId is null || !face.VertexIds.Contains(session.ActiveVertexId, StringComparer.Ordinal))
        {
            failure = "Cannot use Face Plane: active vertex is not on the active face.";
            return false;
        }

        var positions = new List<DVec3>(face.VertexIds.Count);
        foreach (string vertexId in face.VertexIds)
            positions.Add(session.GetVertexPosition(vertexId));

        DVec3 normal = ComputePolygonNormal(positions);
        if (normal.LengthSquared <= 1e-12)
        {
            failure = "Cannot use Face Plane: active face is degenerate.";
            return false;
        }

        DVec3 centroid = DVec3.Zero;
        foreach (DVec3 position in positions)
            centroid += position;
        centroid /= positions.Count;

        plane = new CapturedFacePlane(face.Id, centroid, normal.Normalized(), viewport);
        failure = null;
        return true;
    }

    private static DVec3 ComputePolygonNormal(IReadOnlyList<DVec3> positions)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        for (int i = 0; i < positions.Count; i++)
        {
            DVec3 current = positions[i];
            DVec3 next = positions[(i + 1) % positions.Count];
            x += (current.Y - next.Y) * (current.Z + next.Z);
            y += (current.Z - next.Z) * (current.X + next.X);
            z += (current.X - next.X) * (current.Y + next.Y);
        }
        return new DVec3(x, y, z);
    }

    private sealed record CapturedFacePlane(string FaceId, DVec3 Origin, DVec3 Normal, Rectangle Viewport);
}
