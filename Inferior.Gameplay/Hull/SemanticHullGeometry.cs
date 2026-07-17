using Inferior.Core.Math;

namespace Inferior.Gameplay.Hull;

public sealed record SemanticHullVertex(string Id, DVec3 Position);

public sealed record SemanticHullFace(
    string Id,
    IReadOnlyList<string> VertexIds,
    HullSurfaceRole Role,
    string MaterialGroup,
    DVec3 OutwardNormal,
    string? PanelSlotId = null,
    string? AssemblyId = null);

/// <summary>
/// Minimal semantic attachment point. Full engine-mount support still requires an
/// attachment plane, footprint, clearance bounds, and complete pose/orientation.
/// </summary>
public sealed record AttachmentPortDefinition(
    string PortId,
    DVec3 Position,
    DVec3 Normal,
    AttachmentCapability Capabilities);

public sealed class SemanticHullGeometry
{
    private const double AreaTolerance = 1e-8;
    private const double PlanarityToleranceMeters = 1e-5;
    private const double NormalAgreementDotTolerance = 0.999;

    public required IReadOnlyList<SemanticHullVertex> Vertices { get; init; }
    public required IReadOnlyList<SemanticHullFace> Faces { get; init; }
    public IReadOnlyList<AttachmentPortDefinition> AttachmentPorts { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var verticesById = new Dictionary<string, DVec3>(StringComparer.Ordinal);

        foreach (var vertex in Vertices)
        {
            if (string.IsNullOrWhiteSpace(vertex.Id))
                errors.Add("Semantic hull vertex has an empty id.");
            else if (!verticesById.TryAdd(vertex.Id, vertex.Position))
                errors.Add($"Duplicate semantic hull vertex id '{vertex.Id}'.");

            if (!IsFinite(vertex.Position))
                errors.Add($"Semantic hull vertex '{vertex.Id}' has non-finite position {vertex.Position}.");
        }

        var faceIds = new HashSet<string>(StringComparer.Ordinal);
        var panelSlotIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var face in Faces)
        {
            if (string.IsNullOrWhiteSpace(face.Id))
                errors.Add("Semantic hull face has an empty id.");
            else if (!faceIds.Add(face.Id))
                errors.Add($"Duplicate semantic hull face id '{face.Id}'.");

            if (face.VertexIds.Count < 3)
                errors.Add($"Semantic hull face '{face.Id}' has fewer than three vertices.");

            var faceVertexIds = new HashSet<string>(StringComparer.Ordinal);
            var positions = new List<DVec3>(face.VertexIds.Count);
            foreach (var vertexId in face.VertexIds)
            {
                if (!faceVertexIds.Add(vertexId))
                    errors.Add($"Semantic hull face '{face.Id}' repeats perimeter vertex '{vertexId}'.");

                if (!verticesById.TryGetValue(vertexId, out var position))
                    errors.Add($"Semantic hull face '{face.Id}' references unknown vertex '{vertexId}'.");
                else
                    positions.Add(position);
            }

            if (!IsFinite(face.OutwardNormal) || face.OutwardNormal.LengthSquared <= 1e-12)
            {
                errors.Add($"Semantic hull face '{face.Id}' has invalid outward normal {face.OutwardNormal}.");
            }
            else if (positions.Count == face.VertexIds.Count && positions.Count >= 3)
            {
                ValidateFaceGeometry(face, positions, errors);
            }

            if (face.Role == HullSurfaceRole.PanelSeat)
            {
                if (string.IsNullOrWhiteSpace(face.PanelSlotId))
                    errors.Add($"PanelSeat face '{face.Id}' has no panel slot id.");
                else if (!panelSlotIds.Add(face.PanelSlotId))
                    errors.Add($"Duplicate panel slot id '{face.PanelSlotId}'.");
            }
            else if (!string.IsNullOrWhiteSpace(face.PanelSlotId))
            {
                errors.Add($"Non-PanelSeat face '{face.Id}' carries panel slot id '{face.PanelSlotId}'.");
            }
        }

        var portIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in AttachmentPorts)
        {
            if (string.IsNullOrWhiteSpace(port.PortId))
                errors.Add("Attachment port has an empty id.");
            else if (!portIds.Add(port.PortId))
                errors.Add($"Duplicate attachment port id '{port.PortId}'.");

            if (!IsFinite(port.Position))
                errors.Add($"Attachment port '{port.PortId}' has non-finite position {port.Position}.");

            if (!IsFinite(port.Normal) || port.Normal.LengthSquared <= 1e-12)
                errors.Add($"Attachment port '{port.PortId}' has invalid normal {port.Normal}.");

            if (port.Capabilities == AttachmentCapability.None)
                errors.Add($"Attachment port '{port.PortId}' declares no capabilities.");
        }

        return errors;
    }

    private static void ValidateFaceGeometry(
        SemanticHullFace face,
        IReadOnlyList<DVec3> positions,
        List<string> errors)
    {
        DVec3 polygonNormal = ComputePolygonNormal(positions);
        double area2 = polygonNormal.Length;
        if (area2 <= AreaTolerance)
        {
            errors.Add($"Semantic hull face '{face.Id}' has near-zero area.");
            return;
        }

        DVec3 geometricNormal = polygonNormal / area2;
        DVec3 declaredNormal = face.OutwardNormal.Normalized();
        double dot = DVec3.Dot(geometricNormal, declaredNormal);
        if (dot < NormalAgreementDotTolerance)
        {
            errors.Add(
                $"Semantic hull face '{face.Id}' declared normal disagrees with ordered polygon vertices: dot={dot:F6}.");
        }

        DVec3 origin = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            double distance = System.Math.Abs(DVec3.Dot(positions[i] - origin, geometricNormal));
            if (distance > PlanarityToleranceMeters)
            {
                errors.Add(
                    $"Semantic hull face '{face.Id}' is non-planar at vertex {i}: distance={distance:F8}m.");
                break;
            }
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
