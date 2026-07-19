using Inferior.Core.Math;

namespace Inferior.Gameplay.Cockpit;

public enum CockpitVisualMaterial
{
    MountingBase,
    Housing,
    Frame,
    Canopy,
    Interior,
    CanopyLight,
    InternalGlow,
}

public readonly record struct CockpitVisualTriangle(
    DVec3 A,
    DVec3 B,
    DVec3 C);

public sealed record CockpitVisualMeshPart(
    string PartId,
    CockpitVisualMaterial Material,
    IReadOnlyList<CockpitVisualTriangle> Triangles);

public sealed class CockpitVisualGeometry
{
    public CockpitVisualGeometry(
        string geometryId,
        IReadOnlyList<CockpitVisualMeshPart> meshParts)
    {
        if (string.IsNullOrWhiteSpace(geometryId))
            throw new ArgumentException("Cockpit geometry id must not be empty.", nameof(geometryId));
        ArgumentNullException.ThrowIfNull(meshParts);
        if (meshParts.Count == 0 || meshParts.Any(part =>
                string.IsNullOrWhiteSpace(part.PartId) || part.Triangles.Count == 0))
        {
            throw new ArgumentException(
                "Cockpit geometry must contain identified, non-empty mesh parts.",
                nameof(meshParts));
        }

        GeometryId = geometryId;
        MeshParts = Array.AsReadOnly(meshParts
            .Select(part => new CockpitVisualMeshPart(
                part.PartId,
                part.Material,
                Array.AsReadOnly(part.Triangles.ToArray())))
            .ToArray());
    }

    public string GeometryId { get; }
    public IReadOnlyList<CockpitVisualMeshPart> MeshParts { get; }
}
