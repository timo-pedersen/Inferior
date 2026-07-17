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

public sealed record HullDimensions(
    double LengthMeters,
    double WidthMeters,
    double HeightMeters,
    double StructuralHullWidthMeters,
    double StructuralHullHeightMeters);

public sealed record CargoArrangementDefinition(
    int ContainerCapacity,
    string Arrangement,
    DVec3 StackBoundsMeters,
    DVec3 DesignVolumeCenterMeters,
    DVec3 DesignVolumeBoundsMeters,
    string CargoDoorAssemblyId,
    DVec3 RearOpeningBoundsMeters,
    DVec3 TransferAxis);

public sealed record SemanticAssemblyDefinition(
    string AssemblyId,
    string Kind,
    string FaceId);

public sealed record HullLightDefinition(
    string LightId,
    DVec3 Position,
    DVec3 Direction,
    string Colour,
    double GlowSizeMeters,
    double Intensity,
    string Pattern);

public sealed record BeamLightDefinition(
    string LightId,
    DVec3 Position,
    DVec3 Direction,
    double ConeAngleDegrees,
    double RangeMeters,
    double Intensity,
    string Colour);

/// <summary>
/// Semantic attachment point for external equipment. Position/normal/up define the
/// mount pose in hull-local space; footprint and clearance are authored contract
/// dimensions, not renderer-derived approximations.
/// </summary>
public sealed record AttachmentPortDefinition(
    string PortId,
    DVec3 Position,
    DVec3 Normal,
    AttachmentCapability Capabilities)
{
    public DVec3 Up { get; init; } = DVec3.UnitY;
    public DVec3 FootprintMeters { get; init; } = DVec3.Zero;
    public DVec3 ClearanceMinMeters { get; init; } = DVec3.Zero;
    public DVec3 ClearanceMaxMeters { get; init; } = DVec3.Zero;
}

public sealed class SemanticHullGeometry
{
    private const double AreaTolerance = 1e-8;
    private const double PlanarityToleranceMeters = 1e-5;
    private const double NormalAgreementDotTolerance = 0.999;

    public required IReadOnlyList<SemanticHullVertex> Vertices { get; init; }
    public required IReadOnlyList<SemanticHullFace> Faces { get; init; }
    public IReadOnlyList<AttachmentPortDefinition> AttachmentPorts { get; init; } = [];
    public IReadOnlyList<SemanticAssemblyDefinition> Assemblies { get; init; } = [];
    public IReadOnlyList<HullLightDefinition> MarkerLights { get; init; } = [];
    public IReadOnlyList<BeamLightDefinition> BeamLights { get; init; } = [];
    public bool RequireClosedHull { get; init; }

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
        var assemblyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in Assemblies)
        {
            if (string.IsNullOrWhiteSpace(assembly.AssemblyId))
                errors.Add("Semantic assembly has an empty id.");
            else if (!assemblyIds.Add(assembly.AssemblyId))
                errors.Add($"Duplicate semantic assembly id '{assembly.AssemblyId}'.");
        }

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

            if (!string.IsNullOrWhiteSpace(face.AssemblyId) && !assemblyIds.Contains(face.AssemblyId))
                errors.Add($"Semantic hull face '{face.Id}' references unknown assembly '{face.AssemblyId}'.");
        }

        foreach (var assembly in Assemblies)
        {
            if (!faceIds.Contains(assembly.FaceId))
                errors.Add($"Semantic assembly '{assembly.AssemblyId}' references unknown face '{assembly.FaceId}'.");
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

            if (!IsFinite(port.Up) || port.Up.LengthSquared <= 1e-12)
                errors.Add($"Attachment port '{port.PortId}' has invalid up vector {port.Up}.");

            if (!IsFinite(port.FootprintMeters) || !IsFinite(port.ClearanceMinMeters) || !IsFinite(port.ClearanceMaxMeters))
                errors.Add($"Attachment port '{port.PortId}' has non-finite footprint or clearance bounds.");

            if (port.Capabilities.HasFlag(AttachmentCapability.Engine))
            {
                if (port.FootprintMeters.X <= 0 || port.FootprintMeters.Y <= 0)
                    errors.Add($"Engine attachment port '{port.PortId}' has invalid mount footprint {port.FootprintMeters}.");

                if (port.ClearanceMaxMeters.X <= port.ClearanceMinMeters.X ||
                    port.ClearanceMaxMeters.Y <= port.ClearanceMinMeters.Y ||
                    port.ClearanceMaxMeters.Z <= port.ClearanceMinMeters.Z)
                {
                    errors.Add($"Engine attachment port '{port.PortId}' has invalid clearance bounds.");
                }
            }

            if (port.Capabilities == AttachmentCapability.None)
                errors.Add($"Attachment port '{port.PortId}' declares no capabilities.");
        }

        ValidateLights(MarkerLights, "marker light", errors);
        ValidateLights(BeamLights, "beam light", errors);

        if (RequireClosedHull)
            ValidateClosedHull(errors);

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

        if (!IsConvex(positions, geometricNormal))
            errors.Add($"Semantic hull face '{face.Id}' is not convex.");
    }

    private void ValidateClosedHull(List<string> errors)
    {
        var edgeCounts = new Dictionary<(string a, string b), int>();
        var directedEdges = new HashSet<(string a, string b)>();
        foreach (var face in Faces)
        {
            for (int i = 0; i < face.VertexIds.Count; i++)
            {
                string a = face.VertexIds[i];
                string b = face.VertexIds[(i + 1) % face.VertexIds.Count];
                if (a == b)
                    continue;

                var key = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
                edgeCounts[key] = edgeCounts.TryGetValue(key, out int count) ? count + 1 : 1;

                if (!directedEdges.Add((a, b)))
                    errors.Add($"Closed semantic hull edge '{a}'-'{b}' is reused with the same direction.");
            }
        }

        foreach (var (edge, count) in edgeCounts)
        {
            if (count != 2)
                errors.Add($"Closed semantic hull edge '{edge.a}'-'{edge.b}' is used {count} time(s), expected 2.");
            else if (!directedEdges.Contains((edge.a, edge.b)) || !directedEdges.Contains((edge.b, edge.a)))
                errors.Add($"Closed semantic hull edge '{edge.a}'-'{edge.b}' does not have opposing face winding.");
        }
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

    private static void ValidateLights(IEnumerable<HullLightDefinition> lights, string label, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var light in lights)
        {
            if (string.IsNullOrWhiteSpace(light.LightId))
                errors.Add($"Hull {label} has an empty id.");
            else if (!ids.Add(light.LightId))
                errors.Add($"Duplicate hull {label} id '{light.LightId}'.");

            if (!IsFinite(light.Position) || !IsFinite(light.Direction) || light.Direction.LengthSquared <= 1e-12)
                errors.Add($"Hull {label} '{light.LightId}' has invalid position or direction.");

            if (light.GlowSizeMeters <= 0 || light.Intensity <= 0)
                errors.Add($"Hull {label} '{light.LightId}' has invalid size or intensity.");
        }
    }

    private static void ValidateLights(IEnumerable<BeamLightDefinition> lights, string label, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var light in lights)
        {
            if (string.IsNullOrWhiteSpace(light.LightId))
                errors.Add($"Hull {label} has an empty id.");
            else if (!ids.Add(light.LightId))
                errors.Add($"Duplicate hull {label} id '{light.LightId}'.");

            if (!IsFinite(light.Position) || !IsFinite(light.Direction) || light.Direction.LengthSquared <= 1e-12)
                errors.Add($"Hull {label} '{light.LightId}' has invalid position or direction.");

            if (light.ConeAngleDegrees <= 0 || light.RangeMeters <= 0 || light.Intensity <= 0)
                errors.Add($"Hull {label} '{light.LightId}' has invalid cone, range, or intensity.");
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
