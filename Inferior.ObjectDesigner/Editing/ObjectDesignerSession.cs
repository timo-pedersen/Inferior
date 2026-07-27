using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;
using Inferior.Rendering;

namespace Inferior.ObjectDesigner.Editing;

public enum EditingConstraintMode
{
    ViewPlane,
    AxisX,
    AxisY,
    AxisZ,
    ActiveFacePlane,
}

public sealed record ActiveFaceOverlayData(
    string? ActiveFaceId,
    IReadOnlyList<DVec3> FaceVertices,
    string? ActiveVertexId,
    DVec3? ActiveVertexPosition);

public sealed class ObjectDesignerSession
{
    private readonly string _assetPath;

    public ShipAuthoringDocument Document { get; private set; }
    public HullDefinition HullDefinition { get; private set; }
    public HullDefinition PreviewHullDefinition { get; private set; }
    public IReadOnlyList<AuthoringDiagnostic> Diagnostics { get; private set; }
    public EditHistory History { get; } = new();
    public string? SelectedVertexId
    {
        get => ActiveVertexId;
        set
        {
            _selectedVertexIds.Clear();
            ActiveVertexId = value;
            if (value is not null)
                _selectedVertexIds.Add(value);
        }
    }

    private readonly List<string> _selectedVertexIds = [];

    public string? ActiveVertexId { get; private set; }
    public string? ActiveFaceId { get; private set; }
    public IReadOnlyList<string> SelectedVertexIds => _selectedVertexIds;
    public bool IsPreviewStale { get; private set; }

    public bool IsDirty => History.IsDirty;
    public bool HasValidationErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == AuthoringDiagnosticSeverity.Error);

    private ObjectDesignerSession(
        string assetPath,
        ShipAuthoringDocument document,
        HullDefinition hullDefinition,
        IReadOnlyList<AuthoringDiagnostic> diagnostics)
    {
        _assetPath = assetPath;
        Document = document;
        HullDefinition = hullDefinition;
        PreviewHullDefinition = hullDefinition;
        Diagnostics = diagnostics;
        RefreshPreviewState();
    }

    public static ObjectDesignerSession Load(string assetPath)
    {
        ShipAuthoringLoadResult result = ShipAuthoringJson.LoadHullFromPath(assetPath);
        return new ObjectDesignerSession(assetPath, result.Document, result.HullDefinition, result.Diagnostics);
    }

    public void Reload()
    {
        ShipAuthoringLoadResult result = ShipAuthoringJson.LoadHullFromPath(_assetPath);
        Document = result.Document;
        HullDefinition = result.HullDefinition;
        PreviewHullDefinition = result.HullDefinition;
        Diagnostics = result.Diagnostics;
        IsPreviewStale = false;
        History.ResetClean();
        RemoveMissingSelections();
        ReconcileActiveFaceForActiveVertex();
    }

    public bool Save()
    {
        Rebuild();
        if (HasValidationErrors)
            return false;

        ShipAuthoringJson.Save(_assetPath, Document);
        History.MarkClean();
        return true;
    }

    public SemanticHullVertexDto? FindVertex(string vertexId)
        => Document.Hull.VisualGeometry.Vertices.SingleOrDefault(vertex =>
            string.Equals(vertex.Id, vertexId, StringComparison.Ordinal));

    public DVec3 GetVertexPosition(string vertexId)
        => FindVertex(vertexId)?.Position.ToDVec3()
            ?? throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");

    public void SetVertexPosition(string vertexId, DVec3 position, bool rebuild = true)
    {
        SemanticHullVertexDto vertex = FindVertex(vertexId)
            ?? throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");
        vertex.Position = Vec3Dto.From(position);
        if (rebuild)
        {
            RecomputeFaceNormals();
            Rebuild();
        }
    }

    public void Execute(IEditCommand command) => History.Execute(command, this);
    public void Undo() => History.Undo(this);
    public void Redo() => History.Redo(this);

    public void Rebuild()
    {
        HullDefinition = ShipAuthoringConverter.ToHullDefinition(Document);
        Diagnostics = ShipAuthoringValidator.Validate(Document, HullDefinition);
        RefreshPreviewState();
        ReconcileActiveFaceForActiveVertex();
    }

    public void ClearSelection()
    {
        _selectedVertexIds.Clear();
        ActiveVertexId = null;
        ActiveFaceId = null;
    }

    public void SelectVertex(string vertexId, bool extend)
    {
        if (FindVertex(vertexId) is null)
            throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");
        if (!extend)
            _selectedVertexIds.Clear();
        if (!_selectedVertexIds.Contains(vertexId, StringComparer.Ordinal))
            _selectedVertexIds.Add(vertexId);
        ActiveVertexId = vertexId;
        ReconcileActiveFaceForActiveVertex();
    }

    public bool BeginVertexDragSelection(string vertexId, bool ctrl)
    {
        if (FindVertex(vertexId) is null)
            throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");

        if (ctrl)
            return false;

        if (_selectedVertexIds.Contains(vertexId, StringComparer.Ordinal))
        {
            ActiveVertexId = vertexId;
            ReconcileActiveFaceForActiveVertex();
            return true;
        }

        SelectVertex(vertexId, extend: false);
        return true;
    }

    public void SelectVertices(IEnumerable<string> vertexIds, bool replace)
    {
        string? previousActive = ActiveVertexId;
        if (replace)
            _selectedVertexIds.Clear();
        foreach (string vertexId in vertexIds)
        {
            if (FindVertex(vertexId) is not null && !_selectedVertexIds.Contains(vertexId, StringComparer.Ordinal))
                _selectedVertexIds.Add(vertexId);
        }
        if (previousActive is not null && _selectedVertexIds.Contains(previousActive, StringComparer.Ordinal))
            ActiveVertexId = previousActive;
        else
            ActiveVertexId = null;
        ReconcileActiveFaceForActiveVertex();
    }

    public void ToggleVertexSelection(string vertexId)
    {
        if (_selectedVertexIds.Remove(vertexId))
        {
            if (string.Equals(ActiveVertexId, vertexId, StringComparison.Ordinal))
                ActiveVertexId = null;
            ReconcileActiveFaceForActiveVertex();
            return;
        }
        if (FindVertex(vertexId) is null)
            throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");
        _selectedVertexIds.Add(vertexId);
    }

    public IReadOnlyList<SemanticHullFaceDto> GetIncidentFaces(string vertexId)
        => Document.Hull.VisualGeometry.Faces
            .Where(face => face.VertexIds.Contains(vertexId, StringComparer.Ordinal))
            .ToArray();

    public SemanticHullFaceDto? GetActiveFace()
        => ActiveFaceId is null
            ? null
            : Document.Hull.VisualGeometry.Faces.SingleOrDefault(face =>
                string.Equals(face.Id, ActiveFaceId, StringComparison.Ordinal));

    public bool SelectActiveFace(string faceId)
    {
        if (ActiveVertexId is null)
            return false;
        SemanticHullFaceDto? face = Document.Hull.VisualGeometry.Faces.SingleOrDefault(face =>
            string.Equals(face.Id, faceId, StringComparison.Ordinal));
        if (face is null || !face.VertexIds.Contains(ActiveVertexId, StringComparer.Ordinal))
            return false;
        ActiveFaceId = face.Id;
        return true;
    }

    public void ClearActiveFace() => ActiveFaceId = null;

    public bool CycleActiveFace(int direction)
    {
        if (ActiveVertexId is null)
            return false;
        IReadOnlyList<SemanticHullFaceDto> faces = GetIncidentFaces(ActiveVertexId);
        if (faces.Count == 0)
        {
            ActiveFaceId = null;
            return false;
        }

        int current = ActiveFaceId is null
            ? -1
            : faces.ToList().FindIndex(face => string.Equals(face.Id, ActiveFaceId, StringComparison.Ordinal));
        int next = direction >= 0
            ? (current + 1 + faces.Count) % faces.Count
            : (current < 0 ? faces.Count - 1 : current - 1 + faces.Count) % faces.Count;
        ActiveFaceId = faces[next].Id;
        return true;
    }

    public ActiveFaceOverlayData GetActiveFaceOverlayData()
    {
        var faceVertices = new List<DVec3>();
        if (GetActiveFace() is { } face)
        {
            foreach (string vertexId in face.VertexIds)
            {
                SemanticHullVertexDto? vertex = FindVertex(vertexId);
                if (vertex is null)
                {
                    faceVertices.Clear();
                    break;
                }
                faceVertices.Add(vertex.Position.ToDVec3());
            }
        }

        DVec3? activeVertexPosition = ActiveVertexId is not null && FindVertex(ActiveVertexId) is { } activeVertex
            ? activeVertex.Position.ToDVec3()
            : null;
        return new ActiveFaceOverlayData(ActiveFaceId, faceVertices, ActiveVertexId, activeVertexPosition);
    }

    private void RefreshPreviewState()
    {
        if (!HasValidationErrors && HullDefinition.VisualGeometry is not null)
        {
            try
            {
                _ = SemanticHullMeshBuilder.Build(HullDefinition.VisualGeometry);
                PreviewHullDefinition = HullDefinition;
                IsPreviewStale = false;
                return;
            }
            catch (Exception ex)
            {
                Diagnostics = Diagnostics.Append(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    ex.Message,
                    ExtractQuotedId(ex.Message))
                {
                    Code = "HULL_PREVIEW_BUILD_FAILED",
                    Summary = ex.Message,
                    Details = "The current hull document is kept editable, but the 3D preview remains on the last renderable hull.",
                }).ToArray();
            }
        }
        IsPreviewStale = true;
    }

    private void RemoveMissingSelections()
    {
        _selectedVertexIds.RemoveAll(id => FindVertex(id) is null);
        if (ActiveVertexId is not null && FindVertex(ActiveVertexId) is null)
            ActiveVertexId = null;
        if (ActiveVertexId is not null && !_selectedVertexIds.Contains(ActiveVertexId, StringComparer.Ordinal))
            ActiveVertexId = null;
        ReconcileActiveFaceForActiveVertex();
    }

    private void ReconcileActiveFaceForActiveVertex()
    {
        if (ActiveVertexId is null)
        {
            ActiveFaceId = null;
            return;
        }

        IReadOnlyList<SemanticHullFaceDto> incident = GetIncidentFaces(ActiveVertexId);
        if (ActiveFaceId is not null && incident.Any(face => string.Equals(face.Id, ActiveFaceId, StringComparison.Ordinal)))
            return;

        ActiveFaceId = null;
    }

    public void RecomputeFaceNormals()
    {
        Dictionary<string, DVec3> vertices = Document.Hull.VisualGeometry.Vertices
            .ToDictionary(vertex => vertex.Id, vertex => vertex.Position.ToDVec3(), StringComparer.Ordinal);
        foreach (SemanticHullFaceDto face in Document.Hull.VisualGeometry.Faces)
        {
            if (face.VertexIds.Count < 3)
                continue;
            var positions = new List<DVec3>(face.VertexIds.Count);
            bool missing = false;
            foreach (string vertexId in face.VertexIds)
            {
                if (!vertices.TryGetValue(vertexId, out DVec3 position))
                {
                    missing = true;
                    break;
                }
                positions.Add(position);
            }
            if (missing)
                continue;
            DVec3 normal = ComputePolygonNormal(positions);
            if (normal.LengthSquared > 1e-12 && IsFinite(normal))
                face.OutwardNormal = Vec3Dto.From(normal.Normalized());
        }
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

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static string? ExtractQuotedId(string message)
    {
        int start = message.IndexOf('\'');
        if (start < 0)
            return null;
        int end = message.IndexOf('\'', start + 1);
        return end <= start ? null : message[(start + 1)..end];
    }
}
