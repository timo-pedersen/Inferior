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
    private readonly DVec3? _activeFaceNormal;
    private readonly Point _startMouse;

    private VertexDragOperation(
        IReadOnlyDictionary<string, DVec3> originalPositions,
        string? activeVertexId,
        DVec3 referencePosition,
        EditingConstraintMode constraintMode,
        DVec3? activeFaceNormal,
        Point startMouse)
    {
        _originalPositions = originalPositions;
        _activeVertexId = activeVertexId;
        _referencePosition = referencePosition;
        _constraintMode = constraintMode;
        _activeFaceNormal = activeFaceNormal;
        _startMouse = startMouse;
    }

    public IReadOnlyDictionary<string, DVec3> OriginalPositions => _originalPositions;
    public string? ActiveVertexId => _activeVertexId;
    public EditingConstraintMode ConstraintMode => _constraintMode;
    public Point StartMouse => _startMouse;

    public static VertexDragOperation Capture(
        ObjectDesignerSession session,
        EditingConstraintMode constraintMode,
        Point startMouse)
    {
        Dictionary<string, DVec3> originalPositions = session.SelectedVertexIds
            .ToDictionary(id => id, session.GetVertexPosition, StringComparer.Ordinal);
        string? activeVertexId = session.ActiveVertexId;
        DVec3 referencePosition = activeVertexId is not null && originalPositions.TryGetValue(activeVertexId, out DVec3 activePosition)
            ? activePosition
            : originalPositions.Values.FirstOrDefault();
        DVec3? activeFaceNormal = constraintMode == EditingConstraintMode.ActiveFacePlane
            ? CapturedActiveFaceNormal(session.GetActiveIncidentFace())
            : null;

        return new VertexDragOperation(
            originalPositions,
            activeVertexId,
            referencePosition,
            constraintMode,
            activeFaceNormal,
            startMouse);
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
            EditingConstraintMode.ActiveFacePlane => ProjectDeltaOntoActiveFace(rawDelta),
            _ => rawDelta,
        };
    }

    private DVec3 ProjectDeltaOntoActiveFace(DVec3 delta)
    {
        if (_activeFaceNormal is not { } normal || normal.LengthSquared <= 1e-12)
            return delta;
        DVec3 unit = normal.Normalized();
        return delta - unit * DVec3.Dot(delta, unit);
    }

    private static DVec3? CapturedActiveFaceNormal(SemanticHullFaceDto? face)
    {
        if (face is null)
            return null;
        DVec3 normal = face.OutwardNormal.ToDVec3();
        return normal.LengthSquared <= 1e-12 ? null : normal;
    }
}
