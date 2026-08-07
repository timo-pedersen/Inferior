using Inferior.Core.DataBus;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.States;

// Brief B2 Fix 3: live sun disc/glare tuning panel. Ctrl+F7 chosen after a full keybinding
// grep (see UpdateSunTuningInput's own comment) — F1-F11 and F12 (InferiorGame.cs) are all
// already bound to something in this codebase; Ctrl+F7 sits in the same "Ctrl+F-number
// debug toggle" family as Ctrl+F4 (zone debug) and Ctrl+F6 (shadow debug) without colliding
// with either. The number row (D1-D9, D0) and OemPlus/OemMinus were both completely free —
// grepped, not assumed.
public sealed partial class SystemSpaceState
{
    private bool _showSunTuningPanel;
    private int  _sunTuningSelectedIndex;

    // One descriptor per exposed parameter, in on-screen/number-row order (index 0 -> key
    // '1', ..., index 8 -> key '9', index 9 -> key '0') — matches the brief's own numbered
    // list exactly (1: disc floor ... 5: glare intensity multiplier, 6-10: per-layer alphas).
    private readonly record struct SunTuningParam(
        string Label, System.Func<float> Get, System.Action<float> Set,
        float Min, float Max, float CoarsePerSecond, float FinePerSecond, string DumpConstName);

    private SunTuningParam[]? _sunTuningParams;

    private SunTuningParam[] SunTuningParams => _sunTuningParams ??=
    [
        new("Disc floor (px)",             () => SunTuning.DiscFloorPixels,          v => SunTuning.DiscFloorPixels          = v, 0.1f, 20f, 2f,   0.2f,  "DefaultDiscFloorPixels"),
        new("Limb-darkening strength",     () => SunTuning.LimbDarkeningStrength,    v => SunTuning.LimbDarkeningStrength    = v, 0f,   1f,  0.5f, 0.05f, "DefaultLimbDarkeningStrength"),
        new("Glare compression exponent",  () => SunTuning.GlareCompressionExponent, v => SunTuning.GlareCompressionExponent = v, 0.05f, 2f, 0.2f, 0.02f, "DefaultGlareCompressionExponent"),
        new("Glare size multiplier",       () => SunTuning.GlareSizeMultiplier,      v => SunTuning.GlareSizeMultiplier      = v, 0.01f, 20f, 1f,  0.1f,  "DefaultGlareSizeMultiplier"),
        new("Glare intensity multiplier",  () => SunTuning.GlareIntensityMultiplier, v => SunTuning.GlareIntensityMultiplier = v, 0f,   5f,  0.5f, 0.05f, "DefaultGlareIntensityMultiplier"),
        new("Glare layer 0 alpha (white, innermost)", () => SunTuning.GlareLayer0Alpha, v => SunTuning.GlareLayer0Alpha = v, 0f, 1f, 0.2f, 0.02f, "DefaultGlareLayer0Alpha"),
        new("Glare layer 1 alpha",         () => SunTuning.GlareLayer1Alpha,         v => SunTuning.GlareLayer1Alpha         = v, 0f, 1f, 0.2f, 0.02f, "DefaultGlareLayer1Alpha"),
        new("Glare layer 2 alpha",         () => SunTuning.GlareLayer2Alpha,         v => SunTuning.GlareLayer2Alpha         = v, 0f, 1f, 0.2f, 0.02f, "DefaultGlareLayer2Alpha"),
        new("Glare layer 3 alpha",         () => SunTuning.GlareLayer3Alpha,         v => SunTuning.GlareLayer3Alpha         = v, 0f, 1f, 0.2f, 0.02f, "DefaultGlareLayer3Alpha"),
        new("Glare layer 4 alpha (outermost)", () => SunTuning.GlareLayer4Alpha,     v => SunTuning.GlareLayer4Alpha         = v, 0f, 1f, 0.2f, 0.02f, "DefaultGlareLayer4Alpha"),
    ];

    private static readonly Keys[] SunTuningSelectKeys =
    [
        Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0,
    ];

    private void UpdateSunTuningInput(KeyboardState keys, double dt)
    {
        bool ctrlDown     = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = _prevKeys.IsKeyDown(Keys.LeftControl) || _prevKeys.IsKeyDown(Keys.RightControl);
        bool ctrlF7JustPressed = ctrlDown && keys.IsKeyDown(Keys.F7)
            && !(prevCtrlDown && _prevKeys.IsKeyDown(Keys.F7));

        if (ctrlF7JustPressed)
        {
            _showSunTuningPanel = !_showSunTuningPanel;
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                _showSunTuningPanel
                    ? "Sun tuning panel ON — 1-9/0 select, +/- adjust (hold Shift for fine step), P dumps source."
                    : "Sun tuning panel OFF",
                SystemMessagePriority.NB));
        }

        if (!_showSunTuningPanel) return;

        var parms = SunTuningParams;
        for (int i = 0; i < SunTuningSelectKeys.Length; i++)
        {
            Keys k = SunTuningSelectKeys[i];
            if (keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k))
                _sunTuningSelectedIndex = i;
        }

        bool shiftDown = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var  selected  = parms[_sunTuningSelectedIndex];
        float rate     = shiftDown ? selected.FinePerSecond : selected.CoarsePerSecond;

        if (keys.IsKeyDown(Keys.OemPlus))
            selected.Set(System.Math.Clamp(selected.Get() + rate * (float)dt, selected.Min, selected.Max));
        if (keys.IsKeyDown(Keys.OemMinus))
            selected.Set(System.Math.Clamp(selected.Get() - rate * (float)dt, selected.Min, selected.Max));

        bool pJustPressed = keys.IsKeyDown(Keys.P) && !_prevKeys.IsKeyDown(Keys.P);
        if (pJustPressed)
            DumpSunTuningValues();
    }

    // Brief B2 Fix 3: "a dump key printing all current values as a paste-ready block in
    // source form, so tuned values return to the code losslessly rather than being read off
    // a screenshot" — emits exactly the DefaultXxx const declarations SunTuning.cs already
    // has, in the same order, so this can be pasted directly over them.
    private void DumpSunTuningValues()
    {
        System.Console.WriteLine("[SunTuning] === paste over the DefaultXxx consts in SunTuning.cs ===");
        foreach (var p in SunTuningParams)
            System.Console.WriteLine($"    public const float {p.DumpConstName,-32} = {p.Get():F4}f;");
        System.Console.WriteLine("[SunTuning] === end dump ===");
        DataBus.System.Publish(Topics.System.All,
            new SystemMessage("Sun tuning values dumped to console.", SystemMessagePriority.NB));
    }

    // Brief B2 Fix 3: readout. Drawn as plain sb.DrawString lines (no Inferior.UI panel —
    // this is a throwaway dev overlay, matching this codebase's established precedent for
    // debug-only tooling, e.g. ZoneDebug's console dump / quad overlay rather than a built
    // UI.Panel). Calls CelestialBodyRenderer.GetStarMetrics fresh each frame so the numbers
    // are exactly what the real draw calls computed, not a second, re-derived copy — the
    // discipline Brief D-SunSize's own root-cause finding argues for directly ("the 3x bug
    // would have been obvious immediately if 'disc: 3.0 px' had been visible while the brief
    // claimed 1.0").
    private void DrawSunTuningOverlay(SpriteBatch sb)
    {
        if (!_showSunTuningPanel) return;

        var metrics = _celestialBodies.GetStarMetrics(_camera, _star);
        var parms   = SunTuningParams;

        const float lineH = 16f;
        float y = 40f;
        const float x = 20f;

        void Line(string text, Color color)
        {
            sb.DrawString(_font, text, new Vector2(x, y), color);
            y += lineH;
        }

        Line($"SUN TUNING (Ctrl+F7) — star: {_star.Name} ({_star.SpectralClass})", Color.White);
        Line($"distance: {metrics.DistanceAU:F3} AU", Color.LightGray);
        Line($"disc: {metrics.DiscRadiusPixels:F2} px  [{(metrics.DiscFloorBound ? "FLOOR-bound" : "PHYS-bound")}]", Color.LightGray);
        Line($"glare outer: {metrics.GlareOuterRadiusPixels:F2} px", Color.LightGray);
        y += lineH * 0.5f;

        for (int i = 0; i < parms.Length; i++)
        {
            var p = parms[i];
            bool isSelected = i == _sunTuningSelectedIndex;
            string marker = isSelected ? ">" : " ";
            int keyLabel = i < 9 ? i + 1 : 0;
            Line($"{marker} [{keyLabel}] {p.Label}: {p.Get():F4}", isSelected ? Color.Yellow : Color.LightGray);
        }
    }
}
