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

    // ── Skybox star targeting ─────────────────────────────────────────────────

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
    private void DrawSkyboxStarOverlay(SpriteBatch sb, bool markersVisible)
    {
        if (!markersVisible) return;
        if (_hyperspace.Mode is FlightMode.FlatHyperspace) return;  // no skybox in hyperspace
        var  viewProj = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
        int  w        = _gd.Viewport.Width;
        int  h        = _gd.Viewport.Height;
        var  hypColor = new Color(80, 160, 255);  // matches dirball "hyp" colour

        // Locked star — persistent ring + name in all flight modes
        if (_lockedSkyboxStar != null)
        {
            Vector2? screen = SkyboxProject(_lockedSkyboxStar, viewProj, w, h);
            if (screen.HasValue)
            {
                DrawStarDiamond(sb, screen.Value, 12f, hypColor);

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

        DVec3 offset = star.GalacticPos - _star.GalacticPos;
        double dist = offset.Length;
        if (dist <= 0.001) return null;
        var dir = Vector3.Normalize(new Vector3(
            (float)(offset.X / dist),
            (float)(offset.Y / dist),
            (float)(offset.Z / dist)));
        const float SkyboxRadius = 20_000f;
        Vector4 fallbackClip = Vector4.Transform(new Vector4(dir * SkyboxRadius, 1f), viewProj);
        if (fallbackClip.W <= 0f) return null;
        float fallbackSx = ( fallbackClip.X / fallbackClip.W * 0.5f + 0.5f) * w;
        float fallbackSy = (-fallbackClip.Y / fallbackClip.W * 0.5f + 0.5f) * h;
        return new Vector2(fallbackSx, fallbackSy);
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

    private void DrawStarDiamond(SpriteBatch sb, Vector2 centre, float radius, Color color)
    {
        var top = centre + new Vector2(0f, -radius);
        var right = centre + new Vector2(radius, 0f);
        var bottom = centre + new Vector2(0f, radius);
        var left = centre + new Vector2(-radius, 0f);
        DrawSkyboxLine(sb, top, right, color);
        DrawSkyboxLine(sb, right, bottom, color);
        DrawSkyboxLine(sb, bottom, left, color);
        DrawSkyboxLine(sb, left, top, color);
    }

    private void DrawSkyboxLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color)
    {
        Vector2 delta = b - a;
        float length = delta.Length();
        if (length <= 0.001f) return;
        float angle = MathF.Atan2(delta.Y, delta.X);
        sb.Draw(_pixel, a, null, color, angle, Vector2.Zero,
            new Vector2(length, 2f), SpriteEffects.None, 0f);
    }

}
