using Inferior.Core.Math;
using Inferior.Gameplay.Hull.Authoring;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public enum FaceDragMode
{
    Plane,
    VisibleLine,
}

public enum ShiftDragAxis
{
    Horizontal,
    Vertical,
}

public sealed class VertexDragOperation
{
    public const float ShiftLockDeadZonePixels = 4f;

    private readonly IReadOnlyDictionary<string, DVec3> _originalPositions;
    private readonly string? _activeVertexId;
    private readonly DVec3 _referencePosition;
    private readonly EditingConstraintMode _constraintMode;
    private readonly CapturedFacePlane? _activeFacePlane;
    private readonly DVec3? _facePlaneStartPoint;
    private readonly Point _startMouse;
    private ShiftDragAxis? _shiftAxis;
    private bool _shiftOverrideActive;

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
    public FaceDragMode? ActiveFaceDragMode => _activeFacePlane?.Mode;
    public DVec3? ActiveFaceLineDirection => _activeFacePlane?.LineDirection;
    public ShiftDragAxis? ActiveShiftDragAxis => _shiftOverrideActive ? _shiftAxis : null;
    public bool IsShiftDragActive => _shiftOverrideActive;
    public string? ShiftDragStatus => !_shiftOverrideActive
        ? null
        : _shiftAxis switch
        {
            ShiftDragAxis.Horizontal => "SHIFT LOCK: HORIZONTAL",
            ShiftDragAxis.Vertical => "SHIFT LOCK: VERTICAL",
            _ => "SHIFT LOCK: move to choose axis",
        };
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
        string? captureMessage = null;

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
            double viewDot = Math.Abs(DVec3.Dot(plane.Normal, projection.ViewDirection));
            if (viewDot < OrthographicProjection.FacePlaneLineModeEpsilon)
            {
                DVec3 lineDirection = DVec3.Cross(plane.Normal, projection.ViewDirection);
                if (lineDirection.LengthSquared <= 1e-12)
                {
                    operation = null;
                    failure = "Cannot use Face Plane: active face is degenerate.";
                    return false;
                }

                activeFacePlane = plane with
                {
                    Mode = FaceDragMode.VisibleLine,
                    ViewDirection = projection.ViewDirection,
                    ViewHorizontal = projection.HorizontalAxis,
                    ViewVertical = projection.VerticalAxis,
                    WorldUnitsPerPixel = projection.WorldUnitsPerPixel,
                    PanPixels = projection.PanPixels,
                    LineDirection = lineDirection.Normalized(),
                };
                facePlaneStartPoint = null;
                captureMessage = "Active face is edge-on; movement is constrained to its visible line.";
            }
            else
            {
                if (!projection.TryIntersectScreenRayWithPlane(startMouse, viewport, plane.Origin, plane.Normal, out DVec3 startPoint))
                {
                    operation = null;
                    failure = "Cannot use Face Plane: projection bounds are unavailable.";
                    return false;
                }
                activeFacePlane = plane with
                {
                    Mode = FaceDragMode.Plane,
                    ViewDirection = projection.ViewDirection,
                    ViewHorizontal = projection.HorizontalAxis,
                    ViewVertical = projection.VerticalAxis,
                    WorldUnitsPerPixel = projection.WorldUnitsPerPixel,
                    PanPixels = projection.PanPixels,
                };
                facePlaneStartPoint = startPoint;
            }
        }

        operation = new VertexDragOperation(
            originalPositions,
            activeVertexId,
            referencePosition,
            constraintMode,
            activeFacePlane,
            facePlaneStartPoint,
            startMouse);
        failure = captureMessage;
        return true;
    }

    public IReadOnlyDictionary<string, DVec3> PositionsFor(OrthographicProjection projection, Point mousePosition)
        => PositionsFor(projection, mousePosition, shiftHeld: false);

    public IReadOnlyDictionary<string, DVec3> PositionsFor(OrthographicProjection projection, Point mousePosition, bool shiftHeld)
    {
        DVec3 delta = TotalDelta(projection, mousePosition, shiftHeld);
        return _originalPositions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value + delta,
            StringComparer.Ordinal);
    }

    public void Apply(ObjectDesignerSession session, OrthographicProjection projection, Point mousePosition)
        => Apply(session, projection, mousePosition, shiftHeld: false);

    public void Apply(ObjectDesignerSession session, OrthographicProjection projection, Point mousePosition, bool shiftHeld)
    {
        foreach ((string vertexId, DVec3 position) in PositionsFor(projection, mousePosition, shiftHeld))
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

    private DVec3 TotalDelta(OrthographicProjection projection, Point mousePosition, bool shiftHeld)
    {
        _shiftOverrideActive = shiftHeld;
        if (shiftHeld)
            return ShiftOverrideDelta(projection, mousePosition);

        _shiftAxis = null;
        return PersistentConstraintDelta(projection, mousePosition);
    }

    private DVec3 PersistentConstraintDelta(OrthographicProjection projection, Point mousePosition)
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

    private DVec3 ShiftOverrideDelta(OrthographicProjection projection, Point mousePosition)
    {
        Vector2 screenDelta = (mousePosition - _startMouse).ToVector2();
        if (screenDelta.LengthSquared() < ShiftLockDeadZonePixels * ShiftLockDeadZonePixels)
        {
            _shiftAxis = null;
            return DVec3.Zero;
        }

        _shiftAxis = Math.Abs(screenDelta.X) >= Math.Abs(screenDelta.Y)
            ? ShiftDragAxis.Horizontal
            : ShiftDragAxis.Vertical;

        Vector2 lockedDelta = _shiftAxis == ShiftDragAxis.Horizontal
            ? new Vector2(screenDelta.X, 0)
            : new Vector2(0, screenDelta.Y);
        return projection.ScreenDeltaToWorldPlaneDelta(lockedDelta);
    }

    private DVec3 FacePlaneDelta(OrthographicProjection projection, Point mousePosition)
    {
        if (_activeFacePlane is null)
            throw new InvalidOperationException("Cannot use Face Plane: no active face plane was captured.");
        if (_activeFacePlane.Mode == FaceDragMode.VisibleLine)
            return VisibleLineDelta(mousePosition);

        if (_facePlaneStartPoint is null)
            throw new InvalidOperationException("Cannot use Face Plane: no active face plane start point was captured.");
        if (!TryIntersectCapturedScreenRayWithPlane(mousePosition, _activeFacePlane, out DVec3 currentPoint))
            throw new InvalidOperationException("Cannot use Face Plane: captured plane movement became unstable.");
        return currentPoint - _facePlaneStartPoint.Value;
    }

    private static bool TryIntersectCapturedScreenRayWithPlane(Point mouse, CapturedFacePlane plane, out DVec3 point)
    {
        double a = (mouse.X - plane.Viewport.X - plane.Viewport.Width * 0.5 - plane.PanPixels.X) * plane.WorldUnitsPerPixel;
        double b = -(mouse.Y - plane.Viewport.Y - plane.Viewport.Height * 0.5 - plane.PanPixels.Y) * plane.WorldUnitsPerPixel;
        DVec3 rayOrigin = plane.ViewHorizontal * a + plane.ViewVertical * b;
        double denom = DVec3.Dot(plane.ViewDirection, plane.Normal);
        if (Math.Abs(denom) < OrthographicProjection.FacePlaneLineModeEpsilon)
        {
            point = DVec3.Zero;
            return false;
        }

        double t = DVec3.Dot(plane.Origin - rayOrigin, plane.Normal) / denom;
        point = rayOrigin + plane.ViewDirection * t;
        return double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }

    private DVec3 VisibleLineDelta(Point mousePosition)
    {
        if (_activeFacePlane is null || _activeFacePlane.LineDirection is not { } lineDirection)
            throw new InvalidOperationException("Cannot use Face Plane: no visible line direction was captured.");
        Vector2 screenDelta = (mousePosition - _startMouse).ToVector2();
        double a = screenDelta.X * _activeFacePlane.WorldUnitsPerPixel;
        double b = -screenDelta.Y * _activeFacePlane.WorldUnitsPerPixel;
        DVec3 mouseWorldDelta = _activeFacePlane.ViewHorizontal * a + _activeFacePlane.ViewVertical * b;
        return lineDirection * DVec3.Dot(mouseWorldDelta, lineDirection);
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

        plane = new CapturedFacePlane(
            face.Id,
            centroid,
            normal.Normalized(),
            viewport,
            FaceDragMode.Plane,
            DVec3.Zero,
            DVec3.Zero,
            DVec3.Zero,
            0,
            Vector2.Zero,
            null);
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

    private sealed record CapturedFacePlane(
        string FaceId,
        DVec3 Origin,
        DVec3 Normal,
        Rectangle Viewport,
        FaceDragMode Mode,
        DVec3 ViewDirection,
        DVec3 ViewHorizontal,
        DVec3 ViewVertical,
        double WorldUnitsPerPixel,
        Vector2 PanPixels,
        DVec3? LineDirection);
}
