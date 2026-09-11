using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

/// <summary>
/// Authoritative bitmap-font geometry for text placed on a planar world surface.
/// </summary>
public static class PlanarTextGeometry
{
    /// <summary>
    /// Adds raised pixel-quads starting at the text's lower-left origin.
    /// <paramref name="readingDirection"/> is the direction glyphs advance from left to right;
    /// <paramref name="surfaceNormal"/> is the side from which the text is visible. Glyph-up is
    /// derived as cross(surfaceNormal, readingDirection), guaranteeing that Right, Up, Normal
    /// form a proper frame: dot(cross(Right, Up), Normal) is positive. Callers therefore choose
    /// placement and rotation, but cannot accidentally reflect glyphs or reverse their winding.
    /// </summary>
    public static void Add(
        StationModuleMesh destination,
        string text,
        Vector3 origin,
        Vector3 surfaceNormal,
        Vector3 readingDirection,
        float pixelSize,
        Color colour)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(text);
        if (!IsFinite(origin))
            throw new ArgumentException("Planar text origin must be finite.", nameof(origin));
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        (Vector3 right, Vector3 up, Vector3 normal) =
            DeriveFrame(surfaceNormal, readingDirection);

        float cursor = 0f;
        foreach (char character in text.ToUpperInvariant())
        {
            if (!BitmapFonts.HasGlyph(character))
            {
                cursor += (BitmapFonts.CharW + 1) * pixelSize;
                continue;
            }

            for (int row = 0; row < BitmapFonts.CharH; row++)
            for (int column = 0; column < BitmapFonts.CharW; column++)
            {
                if (!BitmapFonts.IsLit(character, column, row))
                    continue;

                float x = cursor + (column + .5f) * pixelSize;
                float y = (BitmapFonts.CharH - row - .5f) * pixelSize;
                destination.AddQuad(
                    origin + right * x + up * y,
                    normal,
                    up,
                    pixelSize * .88f,
                    pixelSize * .88f,
                    colour);
            }

            cursor += (BitmapFonts.CharW + 1) * pixelSize;
        }
    }

    internal static (Vector3 Right, Vector3 Up, Vector3 Normal) DeriveFrame(
        Vector3 surfaceNormal,
        Vector3 readingDirection)
    {
        if (!IsFinite(surfaceNormal) || surfaceNormal.LengthSquared() <= 1e-12f)
            throw new ArgumentException("Planar text surface normal must be finite and non-zero.",
                nameof(surfaceNormal));
        if (!IsFinite(readingDirection) || readingDirection.LengthSquared() <= 1e-12f)
            throw new ArgumentException("Planar text reading direction must be finite and non-zero.",
                nameof(readingDirection));

        Vector3 normal = Vector3.Normalize(surfaceNormal);
        Vector3 planarRight = readingDirection
            - normal * Vector3.Dot(readingDirection, normal);
        if (planarRight.LengthSquared() <= 1e-10f)
            throw new ArgumentException(
                "Planar text reading direction must not be parallel to its surface normal.",
                nameof(readingDirection));

        Vector3 right = Vector3.Normalize(planarRight);
        Vector3 up = Vector3.Normalize(Vector3.Cross(normal, right));
        if (Vector3.Dot(Vector3.Cross(right, up), normal) <= 0f)
            throw new InvalidOperationException("Planar text frame is reflected.");
        return (right, up, normal);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
