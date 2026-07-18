using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public enum EngineVisualMaterial
{
    Structural,
    Casing,
    Nozzle,
    Accent,
}

public readonly record struct EngineVisualTriangle(
    DVec3 A,
    DVec3 B,
    DVec3 C);

public sealed record EngineVisualMeshPart(
    EngineVisualMaterial Material,
    IReadOnlyList<EngineVisualTriangle> Triangles);

public sealed record EngineExhaustDefinition(
    string ExhaustId,
    DVec3 Position,
    DVec3 Direction);

public sealed record EngineLightDefinition(
    string LightId,
    DVec3 Position,
    DVec3 Colour,
    double GlowSizeMeters,
    double Intensity);

/// <summary>Immutable CPU-side visual definition shared by engine instances.</summary>
public sealed class EngineVisualGeometry
{
    public EngineVisualGeometry(
        string geometryId,
        IReadOnlyList<EngineVisualMeshPart> meshParts,
        IReadOnlyList<EngineExhaustDefinition> exhausts,
        IReadOnlyList<EngineLightDefinition> lights)
    {
        if (string.IsNullOrWhiteSpace(geometryId))
            throw new ArgumentException("Engine geometry id must not be empty.", nameof(geometryId));
        ArgumentNullException.ThrowIfNull(meshParts);
        ArgumentNullException.ThrowIfNull(exhausts);
        ArgumentNullException.ThrowIfNull(lights);
        if (meshParts.Count == 0 || meshParts.Any(part => part.Triangles.Count == 0))
            throw new ArgumentException("Engine geometry must contain non-empty mesh parts.", nameof(meshParts));

        GeometryId = geometryId;
        MeshParts = Array.AsReadOnly(meshParts
            .Select(part => new EngineVisualMeshPart(
                part.Material,
                Array.AsReadOnly(part.Triangles.ToArray())))
            .ToArray());
        Exhausts = Array.AsReadOnly(exhausts.ToArray());
        Lights = Array.AsReadOnly(lights.ToArray());
    }

    public string GeometryId { get; }
    public IReadOnlyList<EngineVisualMeshPart> MeshParts { get; }
    public IReadOnlyList<EngineExhaustDefinition> Exhausts { get; }
    public IReadOnlyList<EngineLightDefinition> Lights { get; }
}
