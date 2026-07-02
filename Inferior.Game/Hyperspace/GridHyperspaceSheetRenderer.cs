using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Rendering;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// 2001-style perspective grid drawn as two infinite-looking sheets above and below the ship.
/// Each sheet is a fan of lines converging to a vanishing point ahead, crossed by
/// concentric rings that advance over time to give the illusion of forward motion.
/// </summary>
public sealed class GridHyperspaceSheetRenderer : IHyperspaceSheetRenderer
{
    // ── Tuneable parameters ───────────────────────────────────────────────────
    private const float SheetHalfSeparation = 80f;     // render units — half-gap between sheets
    private const float GridExtent          = 2000f;   // how far left/right/fwd the grid extends
    private const int   GridLinesLateral    = 18;      // lines across the width
    private const int   GridLinesLong       = 24;      // lines along the length (longitudinal)
    private const float ScrollSpeed         = 120f;    // render units per second — grid scroll speed
    private static readonly Color GridColour = new(20, 60, 140);  // dark blue-white
    private static readonly Color GridBright = new(60, 140, 255);  // brighter accent lines

    private readonly GraphicsDevice _gd;
    private readonly BasicEffect    _effect;
    private double                  _scrollOffset;
    private float                   _time;

    public GridHyperspaceSheetRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        _effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            LightingEnabled    = false,
            TextureEnabled     = false,
        };
    }

    public void Update(double dt, Camera3D camera)
    {
        _scrollOffset = (_scrollOffset + ScrollSpeed * dt) % GridExtent;
        _time        += (float)dt;
    }

    public void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress)
    {
        if (sheetsProgress <= 0f) return;

        _effect.View       = camera.ViewMatrix;
        _effect.Projection = camera.ProjectionMatrix;
        _effect.World      = Matrix.Identity;

        // Fade sheets in as sheetsProgress rises 0→1
        float alpha = MathHelper.Clamp(sheetsProgress, 0f, 1f);

        // The sheet Y positions animate in: start at Y=0 (centre) and expand outward
        float sheetY = SheetHalfSeparation * alpha;

        var verts = new List<VertexPositionColor>(512);

        BuildSheet(verts,  sheetY, alpha);
        BuildSheet(verts, -sheetY, alpha);

        if (verts.Count < 2) return;

        gd.BlendState        = BlendState.Additive;
        gd.DepthStencilState = DepthStencilState.None;

        var arr = verts.ToArray();
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            // Draw as line-list pairs
            gd.DrawUserPrimitives(PrimitiveType.LineList, arr, 0, arr.Length / 2);
        }
    }

    public void Dispose() => _effect.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private void BuildSheet(List<VertexPositionColor> verts, float sheetY, float alpha)
    {
        float halfW = GridExtent;

        // Longitudinal lines — run forward, evenly spaced left→right
        for (int i = 0; i <= GridLinesLateral; i++)
        {
            float x    = MathHelper.Lerp(-halfW, halfW, i / (float)GridLinesLateral);
            bool  accent = (i % 3 == 0);
            Color col  = ColorWithAlpha(accent ? GridBright : GridColour, alpha * (accent ? 0.9f : 0.5f));

            AddLine(verts,
                new Vector3(x, sheetY, -GridExtent),
                new Vector3(x, sheetY,  GridExtent),
                col);
        }

        // Lateral lines — run left-right, scrolling toward player
        float spacing = GridExtent / GridLinesLong;
        for (int i = 0; i < GridLinesLong; i++)
        {
            float z     = -GridExtent + (i * spacing + (float)_scrollOffset) % GridExtent;
            bool  accent = (i % 4 == 0);
            Color col   = ColorWithAlpha(accent ? GridBright : GridColour, alpha * (accent ? 0.9f : 0.5f));

            AddLine(verts,
                new Vector3(-halfW, sheetY, z),
                new Vector3( halfW, sheetY, z),
                col);
        }
    }

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
