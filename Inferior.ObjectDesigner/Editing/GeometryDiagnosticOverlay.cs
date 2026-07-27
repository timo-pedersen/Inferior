using Inferior.Gameplay.Hull.Authoring;

namespace Inferior.ObjectDesigner.Editing;

public sealed record GeometryDiagnosticOverlay(
    IReadOnlySet<string> InvalidFaceIds,
    IReadOnlySet<string> WarningFaceIds,
    IReadOnlySet<string> InvalidVertexIds,
    IReadOnlyDictionary<string, IReadOnlyList<AuthoringDiagnostic>> FaceDiagnosticsById)
{
    public static GeometryDiagnosticOverlay Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<AuthoringDiagnostic>>(StringComparer.Ordinal));

    public static GeometryDiagnosticOverlay From(IReadOnlyList<AuthoringDiagnostic> diagnostics)
    {
        var invalidFaceIds = new HashSet<string>(StringComparer.Ordinal);
        var warningFaceIds = new HashSet<string>(StringComparer.Ordinal);
        var invalidVertexIds = new HashSet<string>(StringComparer.Ordinal);
        var faceDiagnostics = new Dictionary<string, List<AuthoringDiagnostic>>(StringComparer.Ordinal);

        foreach (AuthoringDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.StableFaceId is { Length: > 0 } faceId)
            {
                if (diagnostic.Severity == AuthoringDiagnosticSeverity.Error)
                    invalidFaceIds.Add(faceId);
                else
                    warningFaceIds.Add(faceId);

                if (!faceDiagnostics.TryGetValue(faceId, out List<AuthoringDiagnostic>? list))
                {
                    list = [];
                    faceDiagnostics.Add(faceId, list);
                }
                list.Add(diagnostic);
            }

            if (diagnostic.Severity == AuthoringDiagnosticSeverity.Error)
            {
                foreach (string vertexId in diagnostic.StableVertexIds)
                    invalidVertexIds.Add(vertexId);
            }
        }

        return new GeometryDiagnosticOverlay(
            invalidFaceIds,
            warningFaceIds,
            invalidVertexIds,
            faceDiagnostics.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<AuthoringDiagnostic>)pair.Value.ToArray(),
                StringComparer.Ordinal));
    }

    public AuthoringDiagnostic? MostRelevantForFace(string faceId)
        => FaceDiagnosticsById.TryGetValue(faceId, out IReadOnlyList<AuthoringDiagnostic>? diagnostics)
            ? diagnostics.OrderBy(d => d.Severity == AuthoringDiagnosticSeverity.Error ? 0 : 1).FirstOrDefault()
            : null;
}
