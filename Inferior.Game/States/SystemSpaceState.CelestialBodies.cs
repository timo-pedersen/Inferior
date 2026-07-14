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

    // Cubic-falloff radial gradient for nav light / strobe glow — bright centre, soft edge.
    // Stations-owned (used by DrawStationGlows in Stations.cs) — stays here untouched.
    private static Texture2D CreateNavGlowTexture(GraphicsDevice gd, int size = 64)
    {
        var   tex  = new Texture2D(gd, size, size);
        var   data = new Color[size * size];
        float r    = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist  = MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
            float t     = MathF.Max(0f, 1f - dist / r);
            float alpha = t * t * t;  // cubic: full brightness at centre, zero at rim
            data[y * size + x] = Color.White * alpha;
        }
        tex.SetData(data);
        return tex;
    }

    private static Texture2D CreateStationShadowUvGrid(GraphicsDevice gd, int size = 256)
    {
        var tex = new Texture2D(gd, size, size);
        var data = new Color[size * size];
        int gridStep = Math.Max(1, size / 8);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = size <= 1 ? 0f : x / (float)(size - 1);
            float v = size <= 1 ? 0f : y / (float)(size - 1);
            bool grid = x % gridStep == 0 || y % gridStep == 0;
            bool centre = Math.Abs(x - size / 2) <= 1 || Math.Abs(y - size / 2) <= 1;
            Color c = new(
                (byte)MathHelper.Clamp(u * 255f, 0f, 255f),
                (byte)MathHelper.Clamp(v * 255f, 0f, 255f),
                (byte)(u < 0.5f == v < 0.5f ? 48 : 160),
                (byte)255);

            if (grid)
                c = Color.White;
            if (centre)
                c = Color.Yellow;
            if (x < 10 && y < 10)
                c = Color.Red;
            else if (x >= size - 10 && y < 10)
                c = Color.Lime;
            else if (x < 10 && y >= size - 10)
                c = Color.Blue;
            else if (x >= size - 10 && y >= size - 10)
                c = Color.Magenta;

            data[y * size + x] = c;
        }

        tex.SetData(data);
        return tex;
    }
}
