using Inferior.Gameplay.Hull;
using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public sealed record VertexHitCandidate(
    string VertexId,
    float ScreenDistanceSquared,
    double ViewDepth);

public static class OrthographicVertexHitTester
{
    public const float DefaultHitRadiusPixels = 8f;

    public static IReadOnlyList<VertexHitCandidate> GetVertexHitCandidates(
        IEnumerable<SemanticHullVertex> vertices,
        OrthographicProjection projection,
        Rectangle viewport,
        Point mouse,
        float hitRadiusPixels = DefaultHitRadiusPixels)
    {
        float radiusSquared = hitRadiusPixels * hitRadiusPixels;
        Vector2 mouseVector = mouse.ToVector2();
        return vertices
            .Select(vertex =>
            {
                Vector2 screen = projection.Project(vertex.Position, viewport);
                return new VertexHitCandidate(
                    vertex.Id,
                    Vector2.DistanceSquared(screen, mouseVector),
                    DVec3.Dot(vertex.Position, projection.ViewDirection));
            })
            .Where(candidate => candidate.ScreenDistanceSquared <= radiusSquared)
            .OrderBy(candidate => candidate.ScreenDistanceSquared)
            .ThenBy(candidate => candidate.ViewDepth)
            .ThenBy(candidate => candidate.VertexId, StringComparer.Ordinal)
            .ToArray();
    }

    public static string? PickVertexId(
        IEnumerable<SemanticHullVertex> vertices,
        OrthographicProjection projection,
        Rectangle viewport,
        Point mouse,
        float hitRadiusPixels = DefaultHitRadiusPixels)
        => GetVertexHitCandidates(vertices, projection, viewport, mouse, hitRadiusPixels)
            .FirstOrDefault()
            ?.VertexId;
}
