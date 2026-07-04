using Inferior.Core.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Draws the player ship mesh (hull, nacelles, pylons) in third-person view.
/// Caller decides when to call Draw() (third-person mode gate, null-snapshot guard) —
/// same pattern as the hyperspace-mode guard on SkyboxRenderer.Draw. Takes ship
/// position/orientation directly rather than SpaceSimulation.ShipSnapshot — that type
/// lives in Inferior.Game, which Inferior.Rendering cannot reference.
/// </summary>
public sealed class ShipMeshRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly MeshRenderer   _meshRenderer;

    private readonly VertexBuffer _shipHullVb,    _shipNacelleVb,    _shipPylonVb;
    private readonly IndexBuffer  _shipHullIb,    _shipNacelleIb,    _shipPylonIb;

    public ShipMeshRenderer(GraphicsDevice gd, MeshRenderer meshRenderer)
    {
        _gd           = gd;
        _meshRenderer = meshRenderer;

        var (hullMesh, nacelleMesh, pylonMesh) = Type1HullFactory.BuildAll(gd);
        _shipHullVb    = hullMesh.vb;    _shipHullIb    = hullMesh.ib;
        _shipNacelleVb = nacelleMesh.vb; _shipNacelleIb = nacelleMesh.ib;
        _shipPylonVb   = pylonMesh.vb;   _shipPylonIb   = pylonMesh.ib;
    }

    // currentView is the already-rolled view matrix (was _effect.View at the old call
    // site) — same reasoning as CockpitUI.DrawTargetingHud. Do NOT read camera.ViewMatrix
    // directly here; that would silently regress the clunk-roll fix from two briefs ago.
    public void Draw(Camera3D camera, Matrix currentView, DVec3 shipPosition, Quaternion shipOrientation)
    {
        float   rs        = (float)Camera3D.RenderScale;
        Vector3 renderPos = camera.ToRenderSpace(shipPosition);
        Matrix  proj      = camera.ProjectionMatrix;

        // RotationY(PI) maps the model's +Z-forward nose to the ship's -Z-forward convention.
        Matrix world = Matrix.CreateScale(rs)
                     * Matrix.CreateRotationY(MathF.PI)
                     * Matrix.CreateFromQuaternion(shipOrientation)
                     * Matrix.CreateTranslation(renderPos);

        var sunCol = new Color(SceneLighting.SunColour);
        _meshRenderer.DrawDynamic(_shipHullVb,    _shipHullIb,    world, currentView, proj,
            Type1HullFactory.HullColour,    SceneLighting.SunDirection, sunCol);
        _meshRenderer.DrawDynamic(_shipNacelleVb, _shipNacelleIb, world, currentView, proj,
            Type1HullFactory.NacelleColour, SceneLighting.SunDirection, sunCol);
        _meshRenderer.DrawDynamic(_shipPylonVb,   _shipPylonIb,   world, currentView, proj,
            Type1HullFactory.PylonColour,   SceneLighting.SunDirection, sunCol);

        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    public void Dispose()
    {
        _shipHullVb?.Dispose();    _shipHullIb?.Dispose();
        _shipNacelleVb?.Dispose(); _shipNacelleIb?.Dispose();
        _shipPylonVb?.Dispose();   _shipPylonIb?.Dispose();
    }
}
