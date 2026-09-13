using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.Containers;

/// <summary>
/// Container-specific placement of a projected surface marking — the proof case for the
/// generic Inferior.Rendering/SurfaceMarkingProjector.cs + SurfaceMarkingGeometry.cs
/// substrate. Targets a container's Z+ long face, which carries the seeded inset-cell pattern
/// (BuildFaceInsets in ShippingContainerFactory) — Y+/Y- are deliberately plain flat panels
/// (see ShippingContainerFactory.BuildLongFaceInsets), so they would not demonstrate the
/// clipping crossing real depth changes the way Z+ does.
///
/// Does not know anything about how the receiver mesh was built beyond its own physical
/// constants (face plane position/extent) — the projector/clip engine itself has no idea a
/// container or an inset even exists; this file is the only container-aware part of the
/// pipeline, matching the brief's "same projector can later target ship or station receiver
/// geometry without knowing what a container is."
/// </summary>
public static class ContainerSurfaceMarking
{
    private const string ProofText = "TEST 123";
    private const int PixelScale = 4;

    // Same physical constants ShippingContainerFactory uses for the Z+ face — duplicated
    // rather than exposed from there, since this is placement (an application of the
    // marking substrate), not container geometry itself.
    private const float HalfLz = 1.25f;      // container half-depth along Z (Lz=2.5)
    private const float MarkingWidthMeters = 4.4f;     // comfortably spans the 4.0m inset zone; well within the 5.6m face length
    private const float MarkingDepthMeters = 0.08f;    // exceeds the 0.03-0.05m seeded inset depth

    /// <summary>
    /// Builds the decal geometry for "TEST 123" projected onto <paramref name="container"/>'s
    /// Z+ face. Returns empty arrays if the projected text does not intersect any receiver
    /// triangle (should not happen for an ordinary container, but the caller should still
    /// handle it rather than assume a non-empty result).
    /// </summary>
    public static (VertexPositionNormalColorTexture[] Vertices, short[] Indices) BuildDecal(
        ShippingContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        int textPixelWidth = TextPainter.MeasureText(ProofText, PixelScale);
        int textPixelHeight = TextPainter.MeasureHeight(PixelScale);
        float markingHeightMeters = MarkingWidthMeters * textPixelHeight / textPixelWidth;

        var projector = SurfaceMarkingProjector.Create(
            origin: new Vector3(-MarkingWidthMeters * 0.5f, -markingHeightMeters * 0.5f, HalfLz),
            forward: -Vector3.UnitZ,
            upReference: Vector3.UnitY,
            width: MarkingWidthMeters,
            height: markingHeightMeters,
            depth: MarkingDepthMeters,
            atlasRegion: new Vector4(0f, 0f, 1f, 1f),
            tint: Color.White);

        return SurfaceMarkingGeometry.Project(projector, container.Vertices, container.Indices);
    }

    /// <summary>Shared atlas texture for the proof marking — one texture, every container's decal samples it.</summary>
    public static Texture2D GetAtlas(GraphicsDevice graphicsDevice)
        => TextMarkingAtlas.GetOrCreate(graphicsDevice, ProofText, PixelScale);
}
