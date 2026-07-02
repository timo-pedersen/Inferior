using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Rendering;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// 2001-style perspective grid: two sheets above and below, lines running away ahead.
/// All geometry is in front of the camera (v ≥ 0) to avoid behind-camera projection artifacts.
/// </summary>
public sealed class GridHyperspaceSheetRenderer : IHyperspaceSheetRenderer
{
    // ── Tuneable ──────────────────────────────────────────────────────────────
    private const float SheetHalfSeparation = 80f;    // render units half-gap between sheets
    private const float GridForwardExtent   = 3000f;  // how far ahead the grid runs
    private const float GridLateralExtent   = 1500f;  // half-width left/right
    private const int   GridLinesLateral    = 20;     // vertical lines across the width
    private const int   GridLinesLong       = 20;     // horizontal lines running forward
    private const float ScrollSpeed         = 200f;   // render units/s — scrolls toward camera
    private static readonly Color GridDim    = new(15,  50, 130);
    private static readonly Color GridBright = new(55, 130, 255);

    private readonly GraphicsDevice _gd;
    private readonly BasicEffect    _effect;
    private double                  _scrollOffset;

    public GridHyperspaceSheetRenderer(GraphicsDevice gd)
    {
        _gd    = gd;
        _effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            LightingEnabled    = false,
            TextureEnabled     = false,
        };
    }

    public void Update(double dt, Camera3D camera, PlaneBasis basis)
    {
        _scrollOffset = (_scrollOffset + ScrollSpeed * dt) % (GridForwardExtent / GridLinesLong);
    }

    public void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress, PlaneBasis basis)
    {
        if (sheetsProgress <= 0f) return;

        _effect.View       = camera.ViewMatrix;
        _effect.Projection = camera.ProjectionMatrix;
        _effect.World      = Matrix.Identity;

        float alpha  = MathHelper.Clamp(sheetsProgress, 0f, 1f);
        float sheetD = SheetHalfSeparation * alpha;

        var verts = new List<VertexPositionColor>(512);
        BuildSheet(verts,  sheetD, alpha, basis);
        BuildSheet(verts, -sheetD, alpha, basis);

        if (verts.Count < 2) return;

        gd.BlendState        = BlendState.Additive;
        gd.DepthStencilState = DepthStencilState.None;

        var arr = verts.ToArray();
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.LineList, arr, 0, arr.Length / 2);
        }
    }

    public void Dispose() => _effect.Dispose();

    // ─────────────────────────────────────────────────────────────────────────

    private void BuildSheet(List<VertexPositionColor> verts, float sheetOffset, float alpha, PlaneBasis basis)
    {
        // ── Longitudinal lines — run forward from camera, spaced laterally ─
        for (int i = 0; i <= GridLinesLateral; i++)
        {
            float u      = MathHelper.Lerp(-GridLateralExtent, GridLateralExtent, i / (float)GridLinesLateral);
            bool  accent = (i % 4 == 0);
            Color col    = Alpha(accent ? GridBright : GridDim, alpha * (accent ? 1.0f : 0.55f));

            AddLine(verts,
                ToWorld(u,  2f,               sheetOffset, basis),  // start just in front of camera
                ToWorld(u, GridForwardExtent, sheetOffset, basis),
                col);
        }

        // ── Lateral lines — run left-right, scroll toward camera ─
        float spacing = GridForwardExtent / GridLinesLong;
        for (int i = 0; i < GridLinesLong; i++)
        {
            // v is always positive (in front of camera)
            float v      = spacing * i + (float)_scrollOffset;
            bool  accent = (i % 5 == 0);
            Color col    = Alpha(accent ? GridBright : GridDim, alpha * (accent ? 1.0f : 0.55f));

            AddLine(verts,
                ToWorld(-GridLateralExtent, v, sheetOffset, basis),
                ToWorld( GridLateralExtent, v, sheetOffset, basis),
                col);
        }
    }

    private static Vector3 ToWorld(float u, float v, float w, PlaneBasis b)
        => b.Right * u + b.Forward * v + b.Normal * w;

    private static void AddLine(List<VertexPositionColor> verts, Vector3 a, Vector3 b, Color c)
    {
        verts.Add(new VertexPositionColor(a, c));
        verts.Add(new VertexPositionColor(b, c));
    }

    private static Color Alpha(Color c, float a)
    {
        int ai = (int)(255 * MathHelper.Clamp(a, 0f, 1f));
        return new Color(c.R, c.G, c.B, ai);
    }
}
