using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Shared unit-circle line-strip mesh, reused by every ring draw (orbit rings,
/// station orbit rings) — one scratch vertex buffer recoloured per call instead
/// of rebuilding geometry per ring.
/// </summary>
public sealed class RingPrimitive
{
    private readonly VertexPositionColor[] _ringVerts;

    public RingPrimitive()
    {
        _ringVerts = MeshFactory.CreateRingVertices(128);
    }

    // Draws using whatever effect.World the caller has already set.
    public void Draw(GraphicsDevice gd, BasicEffect effect, Color color)
    {
        for (int i = 0; i < _ringVerts.Length; i++)
            _ringVerts[i].Color = color;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(
                PrimitiveType.LineStrip,
                _ringVerts, 0,
                _ringVerts.Length - 1); // n-1 lines from n+1 verts (closed loop)
        }
    }

    public void DrawScaled(GraphicsDevice gd, BasicEffect effect, float radius, Color color)
    {
        effect.World = Matrix.CreateScale(radius);
        Draw(gd, effect, color);
    }
}
