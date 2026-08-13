using Inferior.Core.DataBus;
using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.States;

// Brief B4 Fix 4: live station-brightness tuning panel, following B2/B3's own pattern
// exactly (it worked). Ctrl+F8 chosen after a full keybinding grep alongside the other keys
// below: F1-F12 are all bound plain already (see SystemSpaceState.cs/.Shadows.cs/
// InferiorGame.cs/StationCycleController.cs); of the "Ctrl+F-number debug toggle" family
// already in use (Ctrl+F3, Ctrl+F4 zone debug, Ctrl+F6 shadow debug, Ctrl+F7 sun tuning,
// Ctrl+F12 station cycle), Ctrl+F8 was free. The number row, Up/Down, Left/Right, and P are
// the SAME physical keys SunTuning already uses — deliberately: they only ever act on
// whichever panel(s) are currently toggled on, and having two tuning panels open at once
// responding to the same keys is an accepted, harmless edge case (each acts on its own
// parameter array), not a reason to invent per-panel key families. J (regenerate) was free.
public sealed partial class SystemSpaceState
{
    private bool _showStationBrightnessTuningPanel;
    private int  _stationBrightnessTuningSelectedIndex;

    private readonly record struct StationBrightnessTuningParam(
        string Label, System.Func<float> Get, System.Action<float> Set, float Default,
        float Min, float Max, float CoarsePerSecond, float FinePerSecond, string DumpConstName);

    private StationBrightnessTuningParam[]? _stationBrightnessTuningParams;

    // Brief B4 non-goal: no individual DarkenColor constants exposed here — see the Fix-1
    // audit (Docs-ai/!current-state.md) for why a single global multiplier is the right
    // mechanism (every DarkenColor-tinted vertex colour feeds the same shader term this
    // multiplies, so raising it uniformly lifts them all without retuning ~20 separate
    // call sites individually).
    private StationBrightnessTuningParam[] StationBrightnessTuningParams => _stationBrightnessTuningParams ??=
    [
        new("Decoration brightness x",     () => StationBrightnessTuning.DecorationBrightnessMultiplier, v => StationBrightnessTuning.DecorationBrightnessMultiplier = v, StationBrightnessTuning.DefaultDecorationBrightnessMultiplier, 0.1f, 5f,   0.5f,  0.05f,  "DefaultDecorationBrightnessMultiplier"),
        new("Ambient",                     () => StationBrightnessTuning.Ambient,                        v => StationBrightnessTuning.Ambient                        = v, StationBrightnessTuning.DefaultAmbient,                        0f,   1f,   0.1f,  0.01f,  "DefaultAmbient"),
        new("Variant value floor",         () => StationBrightnessTuning.VariantValueFloor,              v => StationBrightnessTuning.VariantValueFloor              = v, StationBrightnessTuning.DefaultVariantValueFloor,              0f,   0.9f, 0.05f, 0.005f, "DefaultVariantValueFloor"),
        new("Variant compression strength",() => StationBrightnessTuning.VariantCompressionStrength,     v => StationBrightnessTuning.VariantCompressionStrength     = v, StationBrightnessTuning.DefaultVariantCompressionStrength,     0f,   10f,  0.5f,  0.05f,  "DefaultVariantCompressionStrength"),
    ];

    private static readonly Keys[] StationBrightnessTuningSelectKeys =
    [
        Keys.D1, Keys.D2, Keys.D3, Keys.D4,
    ];

    private void UpdateStationBrightnessTuningInput(KeyboardState keys, double dt)
    {
        bool ctrlDown     = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = _prevKeys.IsKeyDown(Keys.LeftControl) || _prevKeys.IsKeyDown(Keys.RightControl);
        bool ctrlF8JustPressed = ctrlDown && keys.IsKeyDown(Keys.F8)
            && !(prevCtrlDown && _prevKeys.IsKeyDown(Keys.F8));

        if (ctrlF8JustPressed)
        {
            _showStationBrightnessTuningPanel = !_showStationBrightnessTuningPanel;
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                _showStationBrightnessTuningPanel
                    ? "Station brightness tuning panel ON — 1-4 select, Up/Down adjust (hold Shift for fine step), Left resets selected, Right resets all, P dumps source, J regenerates nearest station's textures."
                    : "Station brightness tuning panel OFF",
                SystemMessagePriority.NB));
        }

        if (!_showStationBrightnessTuningPanel) return;

        var parms = StationBrightnessTuningParams;
        for (int i = 0; i < StationBrightnessTuningSelectKeys.Length; i++)
        {
            Keys k = StationBrightnessTuningSelectKeys[i];
            if (keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k))
                _stationBrightnessTuningSelectedIndex = i;
        }

        bool shiftDown = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var  selected  = parms[_stationBrightnessTuningSelectedIndex];
        float rate     = shiftDown ? selected.FinePerSecond : selected.CoarsePerSecond;

        if (keys.IsKeyDown(Keys.Up))
            selected.Set(System.Math.Clamp(selected.Get() + rate * (float)dt, selected.Min, selected.Max));
        if (keys.IsKeyDown(Keys.Down))
            selected.Set(System.Math.Clamp(selected.Get() - rate * (float)dt, selected.Min, selected.Max));

        bool leftJustPressed = keys.IsKeyDown(Keys.Left) && !_prevKeys.IsKeyDown(Keys.Left);
        if (leftJustPressed)
        {
            selected.Set(selected.Default);
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                $"{selected.Label} reset to {selected.Default:F4}.", SystemMessagePriority.NB));
        }

        bool rightJustPressed = keys.IsKeyDown(Keys.Right) && !_prevKeys.IsKeyDown(Keys.Right);
        if (rightJustPressed)
        {
            StationBrightnessTuning.ResetToDefaults();
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("All station brightness parameters reset to defaults.", SystemMessagePriority.NB));
        }

        bool pJustPressed = keys.IsKeyDown(Keys.P) && !_prevKeys.IsKeyDown(Keys.P);
        if (pJustPressed)
            DumpStationBrightnessTuningValues();

        bool jJustPressed = keys.IsKeyDown(Keys.J) && !_prevKeys.IsKeyDown(Keys.J);
        if (jJustPressed)
            RegenerateNearestStationTextures();
    }

    private void DumpStationBrightnessTuningValues()
    {
        System.Console.WriteLine("[StationBrightnessTuning] === paste over the DefaultXxx consts in StationBrightnessTuning.cs ===");
        foreach (var p in StationBrightnessTuningParams)
            System.Console.WriteLine($"    public const float {p.DumpConstName,-38} = {p.Get():F4}f;");
        System.Console.WriteLine("[StationBrightnessTuning] === end dump ===");
        DataBus.System.Publish(Topics.System.All,
            new SystemMessage("Station brightness tuning values dumped to console.", SystemMessagePriority.NB));
    }

    // Brief B4 Fix 2: VariantValueFloor/VariantCompressionStrength only affect textures
    // generated AFTER a change (StationBrightnessTuning's own doc comment) — a station
    // already loaded keeps its already-baked textures until something regenerates them.
    // "The station's variant textures" (the brief's own phrasing) is the NEAREST station to
    // the camera, not every station in the system — regenerating all of them on every tuning
    // tick would be needlessly slow for a preview action; nearest is the one Timo is actually
    // looking at while tuning. Reuses StationGenerator.RegenerateTextures (re-derives the
    // deterministic seed/profile/palette rather than caching them from the original
    // generation pass — see that method's own doc comment) and disposes the OLD texture list
    // only after every module has been repointed at the new one, so nothing samples a
    // disposed texture mid-swap.
    private void RegenerateNearestStationTextures()
    {
        if (_stationGeometry.Count == 0)
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("No station nearby to regenerate.", SystemMessagePriority.NB));
            return;
        }

        Galaxy.Station? nearest = null;
        double nearestDistSq = double.MaxValue;
        foreach (var (station, universePos) in _stationPositions)
        {
            if (!_stationGeometry.ContainsKey(station)) continue;
            double distSq = (universePos - _camera.UniversePosition).LengthSquared;
            if (distSq < nearestDistSq) { nearestDistSq = distSq; nearest = station; }
        }
        if (nearest == null)
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("No station nearby to regenerate.", SystemMessagePriority.NB));
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var modules = _stationGeometry[nearest];
        var oldTextures = _stationPanelTextures.TryGetValue(nearest, out var existing) ? existing : null;

        var newTextures = StationGenerator.RegenerateTextures(nearest, modules, _gd);
        _stationPanelTextures[nearest] = newTextures;

        if (oldTextures != null)
            foreach (var tex in oldTextures) tex.Dispose();
        sw.Stop();

        DataBus.System.Publish(Topics.System.All, new SystemMessage(
            $"Regenerated {nearest.Name}'s panel textures ({sw.ElapsedMilliseconds}ms).", SystemMessagePriority.NB));
    }

    private void DrawStationBrightnessTuningOverlay(SpriteBatch sb)
    {
        if (!_showStationBrightnessTuningPanel) return;

        var parms = StationBrightnessTuningParams;

        const float lineH = 16f;
        float y = 40f;
        // Right-aligned column, clear of the sun tuning panel's own left-column text.
        float x = _gd.Viewport.Width - 420f;

        void Line(string text, Color color)
        {
            sb.DrawString(_font, text, new Vector2(x, y), color);
            y += lineH;
        }

        // Brief B4 Fix 4: "computed final luminance for a hull face and a decoration face at
        // full sun and unlit" — must match LitSurface.fx's PS_DynamicLit (hull) and
        // PS_BakedColorLit (decoration) formulas exactly, since there is no shared C# method
        // to call into here (unlike CelestialBodyRenderer.GetStarMetrics, the real
        // computation happens entirely in HLSL) — if either shader formula changes, this
        // block must change with it. sunLum uses the same luma weights SceneLighting.
        // SunColourForStar already uses, for consistency with that established convention.
        Vector3 sunColour = SceneLighting.SunColour;
        float sunLum = 0.299f * sunColour.X + 0.587f * sunColour.Y + 0.114f * sunColour.Z;
        float ambient = StationBrightnessTuning.Ambient;
        float decoMult = StationBrightnessTuning.DecorationBrightnessMultiplier;

        // Hull (PS_DynamicLit): lit = Ambient + SunColour*nl; nl=1 full sun, nl=0 unlit.
        float hullFullSun = ambient + sunLum;
        float hullUnlit    = ambient;
        // Decoration (PS_BakedColorLit): factor = max(max(nl,Ambient),s), s=0 assumed;
        // rgb = vertexColor * tex * factor * SunColour * DecorationBrightness — reported at
        // vertexColor=tex=white (1,1,1) to isolate the LIGHTING contribution these four
        // parameters actually control, same spirit as the hull figure above.
        float decoFullSun = 1f * sunLum * decoMult;
        float decoUnlit    = ambient * sunLum * decoMult;

        Line("STATION BRIGHTNESS (Ctrl+F8)", Color.White);
        Line($"hull:  full sun {hullFullSun:F3}  unlit {hullUnlit:F3}", Color.LightGray);
        Line($"deco:  full sun {decoFullSun:F3}  unlit {decoUnlit:F3}", Color.LightGray);
        y += lineH * 0.5f;

        for (int i = 0; i < parms.Length; i++)
        {
            var p = parms[i];
            bool isSelected = i == _stationBrightnessTuningSelectedIndex;
            string marker = isSelected ? ">" : " ";
            Line($"{marker} [{i + 1}] {p.Label}: {p.Get():F4}", isSelected ? Color.Yellow : Color.LightGray);
        }
    }
}
