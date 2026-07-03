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
}
