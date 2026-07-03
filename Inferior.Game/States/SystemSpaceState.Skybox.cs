using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{

    // ── Skybox ────────────────────────────────────────────────────────────────

    private const float SkyboxRadius    = 20_000f;
    private const float SkyboxGlowSize = 12f;    // RU per unit MapDotSize
    private const float SkyboxGlowCutoff = 0.15f; // brightness threshold for glow quads

    // Desaturated sky tint — barely-coloured near-white so stars don't look like blobs.
    // These intentionally diverge from GlowColor (which is for up-close system views).
    private static Color SkyboxStarColor(SpectralClass sc, float brightness)
    {
        Vector3 tint = sc switch
        {
            SpectralClass.O           => new Vector3(0.88f, 0.92f, 1.00f),
            SpectralClass.B           => new Vector3(0.92f, 0.95f, 1.00f),
            SpectralClass.A           => new Vector3(0.97f, 0.98f, 1.00f),
            SpectralClass.F           => new Vector3(1.00f, 0.99f, 0.95f),
            SpectralClass.G           => new Vector3(1.00f, 0.97f, 0.91f),
            SpectralClass.K           => new Vector3(1.00f, 0.93f, 0.85f),
            SpectralClass.M           => new Vector3(1.00f, 0.89f, 0.80f),
            SpectralClass.WhiteDwarf  => new Vector3(0.94f, 0.96f, 1.00f),
            SpectralClass.NeutronStar => new Vector3(0.88f, 0.94f, 1.00f),
            SpectralClass.BlackHole   => new Vector3(0.08f, 0.08f, 0.10f),
            _                         => Vector3.One,
        };
        return new Color(tint * brightness);
    }

    // ── Skybox star targeting ─────────────────────────────────────────────────

    private const double SkyboxTargetRadiusLY = 1000.0;  // maximum targetable distance
    private const float  SkyboxHoverPixels    = 12f;      // cursor snap radius in screen pixels

    // Finds the nearest targetable skybox star to the cursor (each frame in UI mode).
    private void UpdateSkyboxHover(Vector2 cursor, Matrix viewProj)
    {
        _hoveredSkyboxStar = null;
        float bestSq = SkyboxHoverPixels * SkyboxHoverPixels;
        int   w = _gd.Viewport.Width;
        int   h = _gd.Viewport.Height;

        foreach (var (pos, star) in _targetableStars)
        {
            Vector4 clip = Vector4.Transform(new Vector4(pos, 1f), viewProj);
            if (clip.W <= 0f) continue;

            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;

            float dx = cursor.X - sx;
            float dy = cursor.Y - sy;
            float dSq = dx * dx + dy * dy;

            if (dSq < bestSq) { bestSq = dSq; _hoveredSkyboxStar = star; }
        }
    }

    // Draws the hover label and locked-star ring in UI mode.
    private void DrawSkyboxStarOverlay(SpriteBatch sb)
    {
        if (_hyperspace.Mode is FlightMode.FlatHyperspace) return;  // no skybox in hyperspace
        var  viewProj = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
        int  w        = _gd.Viewport.Width;
        int  h        = _gd.Viewport.Height;
        var  hypColor = new Color(80, 160, 255);  // matches dirball "hyp" colour

        // Locked star — persistent ring + name in all flight modes
        if (_lockedSkyboxStar != null)
        {
            Vector2? screen = SkyboxProject(_lockedSkyboxStar, viewProj, w, h);
            if (screen.HasValue)
            {
                DrawStarRing(sb, screen.Value, 10f, hypColor);

                string distStr = $"{StarMap.DistanceLY(_star, _lockedSkyboxStar):F1} ly";
                var    namePos = screen.Value + new Vector2(14f, -8f);
                FontHelper.Draw(sb, _font, _lockedSkyboxStar.Name, namePos,                        hypColor);
                FontHelper.Draw(sb, _font, distStr,                namePos + new Vector2(0f, 18f), new Color(55, 110, 178));
            }
        }

        // Hovered star — dim label near cursor (UI mode only)
        if (_uiMouseMode && _hoveredSkyboxStar != null && _hoveredSkyboxStar != _lockedSkyboxStar)
        {
            var labelPos = _uiCursorScreen + new Vector2(14f, -8f);
            FontHelper.Draw(sb, _font, _hoveredSkyboxStar.Name, labelPos, new Color(180, 200, 220));

            string distStr = $"{StarMap.DistanceLY(_star, _hoveredSkyboxStar):F1} ly";
            FontHelper.Draw(sb, _font, distStr, labelPos + new Vector2(0f, 18f), new Color(120, 140, 160));
        }
    }

    // Projects a targetable star's skybox position to screen pixels; null if behind camera.
    private Vector2? SkyboxProject(Star star, Matrix viewProj, int w, int h)
    {
        foreach (var (pos, s) in _targetableStars)
        {
            if (s.GalaxyIndex != star.GalaxyIndex) continue;
            Vector4 clip = Vector4.Transform(new Vector4(pos, 1f), viewProj);
            if (clip.W <= 0f) return null;
            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;
            return new Vector2(sx, sy);
        }
        return null;
    }

    // Draws a small dotted ring around a 2D screen position.
    private void DrawStarRing(SpriteBatch sb, Vector2 centre, float radius, Color color)
    {
        const int Segments = 24;
        for (int i = 0; i < Segments; i++)
        {
            float a  = i * MathF.Tau / Segments;
            float x  = centre.X + MathF.Cos(a) * radius;
            float y  = centre.Y + MathF.Sin(a) * radius;
            sb.Draw(_pixel, new Rectangle((int)x, (int)y, 2, 2), color);
        }
    }

    // ── Skybox build ──────────────────────────────────────────────────────────

    private static (VertexPositionColor[] points, VertexPositionColor[] glowVerts,
                    (Vector3 pos, Star star)[] targetable)
        BuildSkybox(Star currentStar, Star[] galaxy)
    {
        var points     = new List<VertexPositionColor>(galaxy.Length);
        var glows      = new List<VertexPositionColor>();
        var targetable = new List<(Vector3, Star)>();

        // Half the galaxy radius in ly — used for distance falloff so stars dim gradually
        // across galaxy-scale distances rather than popping.
        const double falloffScale = GalaxyGenerator.GalaxyRadiusLY * 0.4;

        foreach (var star in galaxy)
        {
            if (star.GalaxyIndex == currentStar.GalaxyIndex) continue;

            DVec3  offset = star.GalacticPos - currentStar.GalacticPos;
            double dist   = offset.Length;
            if (dist < 0.001) continue;

            var dir    = Vector3.Normalize(new Vector3(
                (float)(offset.X / dist), (float)(offset.Y / dist), (float)(offset.Z / dist)));
            var center = dir * SkyboxRadius;

            float brightness = (star.MapDotSize / 3.5f)
                             * (float)System.Math.Exp(-dist / falloffScale);
            brightness = System.Math.Clamp(brightness, 0.03f, 1.0f);

            points.Add(new VertexPositionColor(center, SkyboxStarColor(star.SpectralClass, brightness)));

            if (dist <= SkyboxTargetRadiusLY)
                targetable.Add((center, star));

            if (brightness >= SkyboxGlowCutoff)
            {
                float   size    = star.MapDotSize * SkyboxGlowSize;
                float   alpha   = brightness * 0.30f;
                Color   glowCol = SkyboxStarColor(star.SpectralClass, alpha);

                Vector3 worldUp = MathF.Abs(dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                Vector3 tan     = Vector3.Normalize(Vector3.Cross(dir, worldUp));
                Vector3 bitan   = Vector3.Cross(dir, tan);

                glows.Add(new VertexPositionColor(center + (-tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
            }
        }

        return ([.. points], [.. glows], [.. targetable]);
    }

    private void DrawSkybox()
    {
        if (_hyperspace.Mode is FlightMode.FlatHyperspace) return;  // sheets replace the skybox
        if (_skyboxPoints.Length == 0 && _skyboxGlowVerts.Length == 0) return;

        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.BlendState      = BlendState.AlphaBlend;

        _effect.World              = Matrix.Identity;
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = false;
        _effect.DiffuseColor       = Vector3.One;
        _effect.Alpha              = 1f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (_skyboxGlowVerts.Length >= 3)
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _skyboxGlowVerts, 0, _skyboxGlowVerts.Length / 3);

            if (_skyboxPoints.Length > 0)
                _gd.DrawUserPrimitives(PrimitiveType.PointList, _skyboxPoints, 0, _skyboxPoints.Length);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
        _gd.RasterizerState        = RasterizerState.CullCounterClockwise;
        _gd.BlendState             = BlendState.Opaque;
    }
}
