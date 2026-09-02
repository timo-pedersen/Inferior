using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct VertexPositionNormalColorTexture : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Color   Color;
    public Vector2 TextureCoordinate;
    // Station-local static incident illumination. Black for all established geometry;
    // H1c-A bakes this only onto explicit megastation interior receivers.
    public Color   ArtificialLight;

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color,   VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Color,   VertexElementUsage.Color, 1));

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public VertexPositionNormalColorTexture(Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TextureCoordinate = textureCoordinate;
        ArtificialLight = Color.Transparent;
    }

    public VertexPositionNormalColorTexture(
        Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate,
        Color artificialLight)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TextureCoordinate = textureCoordinate;
        ArtificialLight = artificialLight;
    }
}
