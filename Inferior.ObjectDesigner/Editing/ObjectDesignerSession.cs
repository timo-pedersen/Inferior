using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;

namespace Inferior.ObjectDesigner.Editing;

public sealed class ObjectDesignerSession
{
    private readonly string _assetPath;

    public ShipAuthoringDocument Document { get; private set; }
    public HullDefinition HullDefinition { get; private set; }
    public IReadOnlyList<AuthoringDiagnostic> Diagnostics { get; private set; }
    public EditHistory History { get; } = new();
    public string? SelectedVertexId { get; set; }

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
        Diagnostics = diagnostics;
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
        Diagnostics = result.Diagnostics;
        History.ResetClean();
        if (SelectedVertexId is not null && FindVertex(SelectedVertexId) is null)
            SelectedVertexId = null;
    }

    public void Save()
    {
        Rebuild();
        if (HasValidationErrors)
            throw new InvalidOperationException("Cannot save while authoring validation has errors.");

        ShipAuthoringJson.Save(_assetPath, Document);
        History.MarkClean();
    }

    public SemanticHullVertexDto? FindVertex(string vertexId)
        => Document.Hull.VisualGeometry.Vertices.SingleOrDefault(vertex =>
            string.Equals(vertex.Id, vertexId, StringComparison.Ordinal));

    public DVec3 GetVertexPosition(string vertexId)
        => FindVertex(vertexId)?.Position.ToDVec3()
            ?? throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");

    public void SetVertexPosition(string vertexId, DVec3 position)
    {
        SemanticHullVertexDto vertex = FindVertex(vertexId)
            ?? throw new KeyNotFoundException($"No semantic vertex '{vertexId}'.");
        vertex.Position = Vec3Dto.From(position);
        RecomputeFaceNormals();
        Rebuild();
    }

    public void Execute(IEditCommand command) => History.Execute(command, this);
    public void Undo() => History.Undo(this);
    public void Redo() => History.Redo(this);

    private void Rebuild()
    {
        HullDefinition = ShipAuthoringConverter.ToHullDefinition(Document);
        Diagnostics = ShipAuthoringValidator.Validate(Document, HullDefinition);
    }

    private void RecomputeFaceNormals()
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
}
