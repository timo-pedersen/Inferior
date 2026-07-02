using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Rendering;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// Pluggable renderer for the two hyperspace sheets (upper and lower corridor walls).
/// sheetsProgress: 0 = sheets not started, 1 = sheets fully cover the screen.
/// planeBasis carries the plane's world-space axes so sheets align with the hyperspace plane.
/// </summary>
public interface IHyperspaceSheetRenderer
{
    void Update(double dt, Camera3D camera, PlaneBasis planeBasis);
    void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress, PlaneBasis planeBasis);
    void Dispose();
}

/// <summary>
/// World-space basis of the hyperspace plane, supplied each frame so renderers can
/// orient geometry correctly regardless of the plane's galactic tilt.
/// </summary>
public readonly struct PlaneBasis
{
    public readonly Vector3 Normal;   // plane normal == ship up at entry
    public readonly Vector3 Forward;  // in-plane forward direction
    public readonly Vector3 Right;    // in-plane right direction

    public PlaneBasis(Vector3 normal, Vector3 forward, Vector3 right)
    {
        Normal  = normal;
        Forward = forward;
        Right   = right;
    }
}
