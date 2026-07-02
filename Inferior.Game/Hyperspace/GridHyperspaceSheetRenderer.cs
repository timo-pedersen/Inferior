using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Rendering;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// 2001-style perspective grid drawn as two infinite-looking sheets above and below the ship.
/// Grid lines are built in plane-local space (Right/Forward/Normal) so they always align with
/// the hyperspace plane regardless of the ship's galactic orientation at entry.
/// </summary>
public sealed class GridHyperspaceSheetRenderer : IHyperspaceSheetRenderer
{
    // ── Tuneable parameters ───────────────────────────────────────────────────
    private const float SheetHalfSeparation = 80f;    // render units — half-gap between sheets
    private const float GridExtent          = 2000f;  // how far along Right/Forward the grid extends
    private const int   GridLinesLateral    = 18;     // lines across Right axis
    private const int   GridLinesLong       = 24;     // lines along Forward axis
    private const float ScrollSpeed         = 120f;   // render units per second
    private static readonly Color GridColour = new(20, 60, 140);
    private static readonly Color GridBright = new(60, 140, 255);

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
        _scrollOffset = (_scrollOffset + ScrollSpeed * dt) % GridExtent;
    }

    public void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress, PlaneBasis basis)
    {
        if (sheetsProgress <= 0f) return;

        _effect.View       = camera.ViewMatrix;
        _effect.Projection = camera.ProjectionMatrix;
        _effect.World      = Matrix.Identity;

        float alpha  = MathHelper.Clamp(sheetsProgress, 0f, 1f);
        float sheetD = SheetHalfSeparation * alpha;  // animates from 0 outward as sheets grow

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

    // ── Private helpers ───────────────────────────────────────────────────────

    private void BuildSheet(List<VertexPositionColor> verts, float sheetOffset, float alpha, PlaneBasis basis)
    {
        // sheetOffset is measured along the plane Normal
        // Grid is built in plane-local space: u along Right, v along Forward, w = Normal

        float halfU = GridExtent;
        float halfV = GridExtent;
        float spacing = halfV / GridLinesLong;

        // Longitudinal lines — run along Forward, spaced along Right
        for (int i = 0; i <= GridLinesLateral; i++)
        {
            float u      = MathHelper.Lerp(-halfU, halfU, i / (float)GridLinesLateral);
            bool  accent = (i % 3 == 0);
            Color col    = ColorWithAlpha(accent ? GridBright : GridColour, alpha * (accent ? 0.9f : 0.5f));

            Vector3 a = ToWorld(u, -halfV, sheetOffset, basis);
            Vector3 b = ToWorld(u,  halfV, sheetOffset, basis);
            AddLine(verts, a, b, col);
        }

        // Lateral lines — run along Right, scroll along Forward
        for (int i = 0; i < GridLinesLong; i++)
        {
            float v      = -halfV + (i * spacing + (float)_scrollOffset) % halfV;
            bool  accent = (i % 4 == 0);
            Color col    = ColorWithAlpha(accent ? GridBright : GridColour, alpha * (accent ? 0.9f : 0.5f));

            Vector3 a = ToWorld(-halfU, v, sheetOffset, basis);
            Vector3 b = ToWorld( halfU, v, sheetOffset, basis);
            AddLine(verts, a, b, col);
        }
    }

    // Converts plane-local (u=right, v=forward, w=normal-offset) to world render space
    private static Vector3 ToWorld(float u, float v, float w, PlaneBasis basis)
        => basis.Right * u + basis.Forward * v + basis.Normal * w;

    private static void AddLine(List<VertexPositionColor> verts, Vector3 a, Vector3 b, Color c)
    {
        verts.Add(new VertexPositionColor(a, c));
        verts.Add(new VertexPositionColor(b, c));
    }

    private static Color ColorWithAlpha(Color c, float alpha)
    {
        int a = (int)(255 * MathHelper.Clamp(alpha, 0f, 1f));
        return new Color(c.R, c.G, c.B, a);
    }
}
