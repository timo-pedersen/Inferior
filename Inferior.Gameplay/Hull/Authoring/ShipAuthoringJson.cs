using System.Text.Json;
using System.Text.Json.Serialization;
using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;

namespace Inferior.Gameplay.Hull.Authoring;

public enum AuthoringDiagnosticSeverity
{
    Error,
    Warning,
}

public sealed record AuthoringDiagnostic(
    AuthoringDiagnosticSeverity Severity,
    string Message,
    string? EntityId = null)
{
    public string Code { get; init; } = "AUTHORING_VALIDATION";
    public string Summary { get; init; } = Message;
    public string? Details { get; init; }
    public double? MeasuredValue { get; init; }
    public double? Tolerance { get; init; }
    public IReadOnlyList<string> RelatedEntityIds { get; init; } = [];
    public string? StableFaceId { get; init; }
    public IReadOnlyList<string> StableVertexIds { get; init; } = [];
}

public sealed class ShipAuthoringLoadResult
{
    public required ShipAuthoringDocument Document { get; init; }
    public required HullDefinition HullDefinition { get; init; }
    public required IReadOnlyList<AuthoringDiagnostic> Diagnostics { get; init; }
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == AuthoringDiagnosticSeverity.Error);
}

public static class ShipAuthoringJson
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static ShipAuthoringLoadResult LoadHull(string relativeAssetPath)
        => LoadHullFromPath(AssetPathResolver.ResolveAssetPath(relativeAssetPath));

    public static ShipAuthoringLoadResult LoadHullFromPath(string path)
    {
        ShipAuthoringDocument? document = JsonSerializer.Deserialize<ShipAuthoringDocument>(
            File.ReadAllText(path),
            Options);
        if (document is null)
            throw new InvalidOperationException($"Authoring asset '{path}' did not contain a document.");

        HullDefinition hull = ShipAuthoringConverter.ToHullDefinition(document);
        IReadOnlyList<AuthoringDiagnostic> diagnostics = ShipAuthoringValidator.Validate(document, hull);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == AuthoringDiagnosticSeverity.Error))
        {
            string joined = string.Join(Environment.NewLine, diagnostics
                .Where(diagnostic => diagnostic.Severity == AuthoringDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.EntityId is null
                    ? diagnostic.Message
                    : $"{diagnostic.EntityId}: {diagnostic.Message}"));
            throw new InvalidOperationException($"Authoring asset '{path}' is invalid:{Environment.NewLine}{joined}");
        }

        return new ShipAuthoringLoadResult
        {
            Document = document,
            HullDefinition = hull,
            Diagnostics = diagnostics,
        };
    }

    public static void Save(string path, ShipAuthoringDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options) + Environment.NewLine);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            },
        };
        return options;
    }
}

public static class AssetPathResolver
{
    public static string ResolveAssetPath(string relativeAssetPath)
    {
        if (Path.IsPathRooted(relativeAssetPath))
            return relativeAssetPath;

        foreach (string root in CandidateRoots())
        {
            string candidate = Path.GetFullPath(Path.Combine(root, relativeAssetPath));
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate asset '{relativeAssetPath}'. Probed from AppContext.BaseDirectory and current directory.");
    }

    public static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? current = new(start);
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                    yield return current.FullName;
                current = current.Parent;
            }
        }
    }
}

public static class ShipAuthoringValidator
{
    public static IReadOnlyList<AuthoringDiagnostic> Validate(
        ShipAuthoringDocument document,
        HullDefinition hull)
    {
        var diagnostics = new List<AuthoringDiagnostic>();

        if (document.SchemaVersion != ShipAuthoringJson.CurrentSchemaVersion)
        {
            diagnostics.Add(new AuthoringDiagnostic(
                AuthoringDiagnosticSeverity.Error,
                $"Unsupported schema version {document.SchemaVersion}; expected {ShipAuthoringJson.CurrentSchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(document.AssetId))
            diagnostics.Add(new AuthoringDiagnostic(AuthoringDiagnosticSeverity.Error, "Asset id must not be empty."));
        if (!string.Equals(document.ObjectKind, "ship", StringComparison.Ordinal))
            diagnostics.Add(new AuthoringDiagnostic(AuthoringDiagnosticSeverity.Error, $"Unsupported object kind '{document.ObjectKind}'."));

        ValidateDocumentIdentity(document, diagnostics);
        ValidateReferences(document, diagnostics);
        ValidateSemanticGeometry(document.Hull.VisualGeometry, diagnostics);
        ValidateCockpitReferences(hull, diagnostics);
        ValidateEngineReferences(hull, diagnostics);

        foreach (string error in hull.Validate())
        {
            if (diagnostics.Any(diagnostic => string.Equals(diagnostic.Summary, error, StringComparison.Ordinal)))
                continue;
            diagnostics.Add(CreateHullDiagnostic(error));
        }

        return diagnostics;
    }

    private static void ValidateSemanticGeometry(
        SemanticHullGeometryDto geometry,
        List<AuthoringDiagnostic> diagnostics)
    {
        const double areaTolerance = 1e-8;
        const double planarityToleranceMeters = 1e-5;
        const double normalAgreementDotTolerance = 0.999;

        Dictionary<string, DVec3> verticesById = geometry.Vertices
            .Where(vertex => !string.IsNullOrWhiteSpace(vertex.Id))
            .GroupBy(vertex => vertex.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Position.ToDVec3(), StringComparer.Ordinal);

        foreach (SemanticHullVertexDto vertex in geometry.Vertices)
        {
            if (!IsFinite(vertex.Position.ToDVec3()))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    $"Semantic hull vertex '{vertex.Id}' has non-finite position {vertex.Position}.",
                    vertex.Id)
                {
                    Code = "HULL_VERTEX_NON_FINITE",
                    Summary = $"Semantic hull vertex '{vertex.Id}' has non-finite position {vertex.Position}.",
                    StableVertexIds = string.IsNullOrWhiteSpace(vertex.Id) ? [] : [vertex.Id],
                    RelatedEntityIds = string.IsNullOrWhiteSpace(vertex.Id) ? [] : [vertex.Id],
                });
            }
        }

        foreach (SemanticHullFaceDto face in geometry.Faces)
        {
            if (string.IsNullOrWhiteSpace(face.Id))
                continue;

            if (face.VertexIds.Count < 3)
            {
                AddFaceDiagnostic(
                    diagnostics,
                    face.Id,
                    "HULL_FACE_TOO_FEW_VERTICES",
                    $"Semantic hull face '{face.Id}' has fewer than three vertices.",
                    face.VertexIds);
            }

            var faceVertexIds = new HashSet<string>(StringComparer.Ordinal);
            var positions = new List<DVec3>(face.VertexIds.Count);
            bool hasInvalidReferences = false;
            foreach (string vertexId in face.VertexIds)
            {
                if (!faceVertexIds.Add(vertexId))
                {
                    hasInvalidReferences = true;
                    AddFaceDiagnostic(
                        diagnostics,
                        face.Id,
                        "HULL_FACE_REPEATED_VERTEX",
                        $"Semantic hull face '{face.Id}' repeats perimeter vertex '{vertexId}'.",
                        [vertexId]);
                }

                if (!verticesById.TryGetValue(vertexId, out DVec3 position))
                {
                    hasInvalidReferences = true;
                    AddFaceDiagnostic(
                        diagnostics,
                        face.Id,
                        "HULL_FACE_UNKNOWN_VERTEX",
                        $"Semantic hull face '{face.Id}' references unknown vertex '{vertexId}'.",
                        [vertexId]);
                }
                else
                {
                    positions.Add(position);
                }
            }

            DVec3 declaredNormal = face.OutwardNormal.ToDVec3();
            if (!IsFinite(declaredNormal) || declaredNormal.LengthSquared <= 1e-12)
            {
                AddFaceDiagnostic(
                    diagnostics,
                    face.Id,
                    "HULL_FACE_INVALID_NORMAL",
                    $"Semantic hull face '{face.Id}' has invalid outward normal {declaredNormal}.",
                    face.VertexIds);
                continue;
            }

            if (hasInvalidReferences || positions.Count != face.VertexIds.Count || positions.Count < 3)
                continue;

            DVec3 polygonNormal = ComputePolygonNormal(positions);
            double area2 = polygonNormal.Length;
            if (area2 <= areaTolerance)
            {
                AddFaceDiagnostic(
                    diagnostics,
                    face.Id,
                    "HULL_FACE_ZERO_AREA",
                    $"Semantic hull face '{face.Id}' has near-zero area.",
                    face.VertexIds);
                continue;
            }

            DVec3 geometricNormal = polygonNormal / area2;
            double dot = DVec3.Dot(geometricNormal, declaredNormal.Normalized());
            if (dot < normalAgreementDotTolerance)
            {
                AddFaceDiagnostic(
                    diagnostics,
                    face.Id,
                    "HULL_FACE_WINDING_MISMATCH",
                    $"Semantic hull face '{face.Id}' declared normal disagrees with ordered polygon vertices: dot={dot:F6}.",
                    face.VertexIds,
                    measuredValue: dot,
                    tolerance: normalAgreementDotTolerance);
            }

            DVec3 origin = positions[0];
            for (int i = 1; i < positions.Count; i++)
            {
                double distance = System.Math.Abs(DVec3.Dot(positions[i] - origin, geometricNormal));
                if (distance > planarityToleranceMeters)
                {
                    AddFaceDiagnostic(
                        diagnostics,
                        face.Id,
                        "HULL_FACE_NON_PLANAR",
                        $"Semantic hull face '{face.Id}' is non-planar at vertex {i}: distance={distance:F8}m.",
                        [face.VertexIds[i]],
                        measuredValue: distance,
                        tolerance: planarityToleranceMeters);
                    break;
                }
            }

            if (!IsConvex(positions, geometricNormal))
            {
                AddFaceDiagnostic(
                    diagnostics,
                    face.Id,
                    "HULL_FACE_NON_CONVEX",
                    $"Semantic hull face '{face.Id}' is not convex.",
                    face.VertexIds);
            }
        }
    }

    private static void AddFaceDiagnostic(
        List<AuthoringDiagnostic> diagnostics,
        string faceId,
        string code,
        string message,
        IReadOnlyList<string> vertexIds,
        double? measuredValue = null,
        double? tolerance = null)
    {
        string[] stableVertexIds = vertexIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        diagnostics.Add(new AuthoringDiagnostic(AuthoringDiagnosticSeverity.Error, message, faceId)
        {
            Code = code,
            Summary = message,
            Details = "Generated from structured semantic hull authoring validation.",
            MeasuredValue = measuredValue,
            Tolerance = tolerance,
            RelatedEntityIds = [faceId, .. stableVertexIds],
            StableFaceId = faceId,
            StableVertexIds = stableVertexIds,
        });
    }

    private static void ValidateDocumentIdentity(
        ShipAuthoringDocument document,
        List<AuthoringDiagnostic> diagnostics)
    {
        AddDuplicateDiagnostics(document.Hull.VisualGeometry.Vertices.Select(v => v.Id), "vertex", diagnostics);
        AddDuplicateDiagnostics(document.Hull.VisualGeometry.Faces.Select(f => f.Id), "face", diagnostics);
        AddDuplicateDiagnostics(document.Hull.VisualGeometry.Assemblies.Select(a => a.AssemblyId), "assembly", diagnostics);
        AddDuplicateDiagnostics(document.Hull.VisualGeometry.AttachmentPorts.Select(p => p.PortId), "attachment port", diagnostics);
        AddDuplicateDiagnostics(document.Hull.CockpitMounts.Select(m => m.MountId), "cockpit mount", diagnostics);
        AddDuplicateDiagnostics(document.Hull.Slots.Select(s => s.SlotId), "component slot", diagnostics);
    }

    private static void ValidateReferences(
        ShipAuthoringDocument document,
        List<AuthoringDiagnostic> diagnostics)
    {
        var assemblyIds = document.Hull.VisualGeometry.Assemblies
            .Select(assembly => assembly.AssemblyId)
            .ToHashSet(StringComparer.Ordinal);
        var faceIds = document.Hull.VisualGeometry.Faces
            .Select(face => face.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (document.Hull.CargoArrangement is { } cargo)
        {
            if (!assemblyIds.Contains(cargo.CargoDoorAssemblyId))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    $"Cargo arrangement references unknown cargo-door assembly '{cargo.CargoDoorAssemblyId}'.",
                    cargo.CargoDoorAssemblyId));
            }
            if (cargo.ContainerPlacements.Count != cargo.ContainerCapacity)
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Warning,
                    $"Cargo arrangement declares capacity {cargo.ContainerCapacity} but has {cargo.ContainerPlacements.Count} placements.",
                    cargo.CargoDoorAssemblyId));
            }
        }

        foreach (SemanticAssemblyDto assembly in document.Hull.VisualGeometry.Assemblies)
        {
            if (!faceIds.Contains(assembly.FaceId))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    $"Assembly '{assembly.AssemblyId}' references unknown face '{assembly.FaceId}'.",
                    assembly.AssemblyId));
            }
        }
    }

    private static void ValidateCockpitReferences(
        HullDefinition hull,
        List<AuthoringDiagnostic> diagnostics)
    {
        foreach (CockpitMountDefinition mount in hull.CockpitMounts)
        {
            if (mount.DefaultCockpitDefinitionId is null)
                continue;
            if (!CockpitDefinitionLibrary.TryGet(mount.DefaultCockpitDefinitionId, out _))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    $"Cockpit mount '{mount.MountId}' references unknown cockpit '{mount.DefaultCockpitDefinitionId}'.",
                    mount.MountId));
            }
        }
    }

    private static void ValidateEngineReferences(
        HullDefinition hull,
        List<AuthoringDiagnostic> diagnostics)
    {
        foreach (HullSlot slot in hull.Slots.Where(slot => slot.Category == SlotCategory.Engine))
        {
            if (string.IsNullOrWhiteSpace(slot.DefaultComponentDefinitionId))
                continue;
            try
            {
                _ = EngineDefinitionLibrary.GetVariant(slot.DefaultComponentDefinitionId);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    AuthoringDiagnosticSeverity.Error,
                    $"Engine slot '{slot.SlotId}' references unknown default engine '{slot.DefaultComponentDefinitionId}': {ex.Message}",
                    slot.SlotId));
            }
        }
    }

    private static void AddDuplicateDiagnostics(
        IEnumerable<string> ids,
        string label,
        List<AuthoringDiagnostic> diagnostics)
    {
        foreach (IGrouping<string, string> duplicate in ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new AuthoringDiagnostic(
                AuthoringDiagnosticSeverity.Error,
                $"Duplicate {label} id '{duplicate.Key}'.",
                duplicate.Key));
        }
    }

    private static string? ExtractQuotedId(string message)
    {
        int start = message.IndexOf('\'');
        if (start < 0)
            return null;
        int end = message.IndexOf('\'', start + 1);
        return end <= start ? null : message[(start + 1)..end];
    }

    private static AuthoringDiagnostic CreateHullDiagnostic(string message)
    {
        string? entityId = ExtractQuotedId(message);
        string code =
            message.Contains("non-planar", StringComparison.OrdinalIgnoreCase) ? "HULL_FACE_NON_PLANAR" :
            message.Contains("fewer than three", StringComparison.OrdinalIgnoreCase) ? "HULL_FACE_TOO_FEW_VERTICES" :
            message.Contains("unknown vertex", StringComparison.OrdinalIgnoreCase) ? "HULL_FACE_UNKNOWN_VERTEX" :
            message.Contains("zero-area", StringComparison.OrdinalIgnoreCase) ? "HULL_FACE_ZERO_AREA" :
            message.Contains("winding", StringComparison.OrdinalIgnoreCase) ? "HULL_FACE_WINDING_MISMATCH" :
            "HULL_VALIDATION";

        return new AuthoringDiagnostic(AuthoringDiagnosticSeverity.Error, message, entityId)
        {
            Code = code,
            Summary = message,
            Details = "Generated from semantic hull validation.",
            MeasuredValue = TryExtractMetric(message, "distance="),
            Tolerance = TryExtractMetric(message, "tolerance="),
            RelatedEntityIds = entityId is null ? [] : [entityId],
        };
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

    private static bool IsConvex(IReadOnlyList<DVec3> positions, DVec3 normal)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            DVec3 a = positions[i];
            DVec3 b = positions[(i + 1) % positions.Count];
            DVec3 c = positions[(i + 2) % positions.Count];
            DVec3 cross = DVec3.Cross(b - a, c - b);
            if (DVec3.Dot(cross, normal) < -1e-8)
                return false;
        }

        return true;
    }

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static double? TryExtractMetric(string message, string marker)
    {
        int markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;
        int start = markerIndex + marker.Length;
        int end = start;
        while (end < message.Length && (char.IsDigit(message[end]) || message[end] is '.' or '-' or '+' or 'e' or 'E'))
            end++;
        return double.TryParse(message[start..end], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }
}
