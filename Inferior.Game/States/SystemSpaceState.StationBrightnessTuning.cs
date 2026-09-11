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

    // Brief B4a Fix 1: RequiresRegeneration marks the parameters that only take effect at
    // STATION TEXTURE GENERATION time (StationTextureRegistry.OffsetPaletteForVariant) rather
    // than every draw — adjusting one of these needs a rebuild of the current station's
    // variant textures to actually show anything (see MarkStationBrightnessGenerationDirty).
    private readonly record struct StationBrightnessTuningParam(
        string Label, System.Func<float> Get, System.Action<float> Set, float Default,
        float Min, float Max, float CoarsePerSecond, float FinePerSecond, string DumpConstName,
        bool RequiresRegeneration);

    private StationBrightnessTuningParam[]? _stationBrightnessTuningParams;

    // Brief B4 non-goal: no individual DarkenColor constants exposed here — see the Fix-1
    // audit (Docs-ai/!current-state.md) for why a single global multiplier is the right
    // mechanism (every DarkenColor-tinted vertex colour feeds the same shader term this
    // multiplies, so raising it uniformly lifts them all without retuning ~20 separate
    // call sites individually).
    private StationBrightnessTuningParam[] StationBrightnessTuningParams => _stationBrightnessTuningParams ??=
    [
        new("Decoration brightness x",     () => StationBrightnessTuning.DecorationBrightnessMultiplier, v => StationBrightnessTuning.DecorationBrightnessMultiplier = v, StationBrightnessTuning.DefaultDecorationBrightnessMultiplier, 0.1f, 5f,   0.5f,  0.05f,  "DefaultDecorationBrightnessMultiplier", false),
        new("Ambient",                     () => StationBrightnessTuning.Ambient,                        v => StationBrightnessTuning.Ambient                        = v, StationBrightnessTuning.DefaultAmbient,                        0f,   1f,   0.1f,  0.01f,  "DefaultAmbient", false),
        new("Variant value floor",         () => StationBrightnessTuning.VariantValueFloor,              v => StationBrightnessTuning.VariantValueFloor              = v, StationBrightnessTuning.DefaultVariantValueFloor,              0f,   0.9f, 0.05f, 0.005f, "DefaultVariantValueFloor", true),
        new("Variant compression strength",() => StationBrightnessTuning.VariantCompressionStrength,     v => StationBrightnessTuning.VariantCompressionStrength     = v, StationBrightnessTuning.DefaultVariantCompressionStrength,     0f,   10f,  0.5f,  0.05f,  "DefaultVariantCompressionStrength", true),
        new("Saturation falloff",          () => StationBrightnessTuning.SaturationFalloff,              v => StationBrightnessTuning.SaturationFalloff              = v, StationBrightnessTuning.DefaultSaturationFalloff,              0f,   5f,   0.5f,  0.05f,  "DefaultSaturationFalloff", true),
    ];

    private static readonly Keys[] StationBrightnessTuningSelectKeys =
    [
        Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
    ];

    // Brief B4a Fix 1: a change to a RequiresRegeneration parameter arms this debounce
    // instead of regenerating immediately — Up/Down are held for a continuous dt-scaled
    // adjust, and regenerating a station's whole variant texture set (up to DefaultVariantCount
    // textures per surface, several surfaces) on every single frame while held would make the
    // panel unusable. Each further change while armed refreshes the countdown; it fires once
    // adjustment goes idle for this long, so tuning still feels live without hammering
    // GenerateVariantSet per frame. Brief allows "slow is acceptable" for the regen itself —
    // this debounce is about call FREQUENCY, not the individual call's cost.
    private bool   _stationBrightnessGenDirty;
    private double _stationBrightnessGenDebounceRemaining;
    private const double StationBrightnessGenerationRegenDebounceSeconds = 0.35;

    // Brief B4a Fix 3: readout diagnostic — resulting P10/median/max TEXTURE luminance across
    // the last-regenerated station's actual modules (one sample per module, textures deduped
    // by reference so a dominant variant isn't read from disk N times), so the panel shows
    // which of floor/compression/saturation is actually doing the work. Computed once per
    // regeneration (not per frame — GetData on several 512x512 textures every frame would not
    // be "cheap"), null until the first regen this session.
    private string? _stationBrightnessLastRegenStation;
    private float?  _stationBrightnessLastP10;
    private float?  _stationBrightnessLastMedian;
    private float?  _stationBrightnessLastMax;

    private void MarkStationBrightnessGenerationDirty()
    {
        _stationBrightnessGenDirty = true;
        _stationBrightnessGenDebounceRemaining = StationBrightnessGenerationRegenDebounceSeconds;
    }

    private void UpdateStationBrightnessTuningInput(KeyboardState keys, double dt)
    {
        // Brief B4a Fix 1: processed unconditionally, even if the panel gets closed with a
        // change still armed — a pending regeneration should still land rather than being
        // silently dropped because Ctrl+F8 was pressed before the debounce elapsed.
        if (_stationBrightnessGenDirty)
        {
            _stationBrightnessGenDebounceRemaining -= dt;
            if (_stationBrightnessGenDebounceRemaining <= 0)
            {
                _stationBrightnessGenDirty = false;
                RegenerateNearestStationTextures();
            }
        }

        bool ctrlDown     = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = _prevKeys.IsKeyDown(Keys.LeftControl) || _prevKeys.IsKeyDown(Keys.RightControl);
        bool ctrlF8JustPressed = ctrlDown && keys.IsKeyDown(Keys.F8)
            && !(prevCtrlDown && _prevKeys.IsKeyDown(Keys.F8));

        if (ctrlF8JustPressed)
        {
            _showStationBrightnessTuningPanel = !_showStationBrightnessTuningPanel;
            DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
                _showStationBrightnessTuningPanel
                    ? "Station brightness tuning panel ON — 1-5 select, Up/Down adjust (hold Shift for fine step), Left resets selected, Right resets all, P dumps source, J force-regenerates nearest station's textures (floor/compression/saturation now auto-regenerate shortly after you stop adjusting)."
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
        {
            float before = selected.Get();
            selected.Set(System.Math.Clamp(before + rate * (float)dt, selected.Min, selected.Max));
            if (selected.RequiresRegeneration && selected.Get() != before) MarkStationBrightnessGenerationDirty();
        }
        if (keys.IsKeyDown(Keys.Down))
        {
            float before = selected.Get();
            selected.Set(System.Math.Clamp(before - rate * (float)dt, selected.Min, selected.Max));
            if (selected.RequiresRegeneration && selected.Get() != before) MarkStationBrightnessGenerationDirty();
        }

        bool leftJustPressed = keys.IsKeyDown(Keys.Left) && !_prevKeys.IsKeyDown(Keys.Left);
        if (leftJustPressed)
        {
            selected.Set(selected.Default);
            if (selected.RequiresRegeneration) MarkStationBrightnessGenerationDirty();
            DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
                $"{selected.Label} reset to {selected.Default:F4}.", SystemMessagePriority.NB));
        }

        bool rightJustPressed = keys.IsKeyDown(Keys.Right) && !_prevKeys.IsKeyDown(Keys.Right);
        if (rightJustPressed)
        {
            StationBrightnessTuning.ResetToDefaults();
            // Simplicity over precision: always arm the debounce on a full reset (cheap —
            // one regen — versus checking whether any of the three generation-time params
            // actually moved from its already-default value).
            MarkStationBrightnessGenerationDirty();
            DataBus.SystemMessages.Publish(Topics.System.All,
                new SystemMessage("All station brightness parameters reset to defaults.", SystemMessagePriority.NB));
        }

        bool pJustPressed = keys.IsKeyDown(Keys.P) && !_prevKeys.IsKeyDown(Keys.P);
        if (pJustPressed)
            DumpStationBrightnessTuningValues();

        bool jJustPressed = keys.IsKeyDown(Keys.J) && !_prevKeys.IsKeyDown(Keys.J);
        if (jJustPressed)
        {
            // Force-regenerate now rather than leaving an already-armed debounce to fire
            // redundantly a moment later.
            _stationBrightnessGenDirty = false;
            RegenerateNearestStationTextures();
        }
    }

    private void DumpStationBrightnessTuningValues()
    {
        System.Console.WriteLine("[StationBrightnessTuning] === paste over the DefaultXxx consts in StationBrightnessTuning.cs ===");
        foreach (var p in StationBrightnessTuningParams)
            System.Console.WriteLine($"    public const float {p.DumpConstName,-38} = {p.Get():F4}f;");
        System.Console.WriteLine("[StationBrightnessTuning] === end dump ===");
        DataBus.SystemMessages.Publish(Topics.System.All,
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
    // Mega-stations merge note: only one station's geometry is resident at a time under the
    // visual residency system (StationVisualPackage), and the residency evaluation itself
    // already keeps the nearest-to-camera station resident — so "nearest station with
    // geometry loaded" is simply the resident one, not a separate scan.
    private void RegenerateNearestStationTextures()
    {
        if (ResidentStationVisual is not { } visual)
        {
            DataBus.SystemMessages.Publish(Topics.System.All,
                new SystemMessage("No station nearby to regenerate.", SystemMessagePriority.NB));
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Galaxy.Station nearest = visual.Descriptor.Station;
        var modules = visual.Modules;
        var oldTextures = visual.Textures.ToList();

        var newTextures = StationGenerator.RegenerateTextures(nearest, modules, _gd);

        foreach (var tex in oldTextures)
            visual.RemoveAndDisposeTexture(tex);
        visual.Textures.AddRange(newTextures);

        MeasureStationBrightnessLuminance(nearest, modules);
        sw.Stop();

        DataBus.SystemMessages.Publish(Topics.System.All, new SystemMessage(
            $"Regenerated {nearest.Name}'s panel textures ({sw.ElapsedMilliseconds}ms).", SystemMessagePriority.NB));
    }

    // Brief B4a Fix 3 (guidance): "show the resulting P10/median/max texture luminance for
    // the current station, if that's cheap to compute." Cheap once, per regeneration — not
    // once per frame, which is what would make raw GetData reads on several 512x512
    // textures NOT cheap. One mean-luminance sample per actual module (matching D-Bright's
    // own original measurement methodology, so these numbers are directly comparable to that
    // report's), but each distinct TextureInstance is only read back from the GPU once —
    // most modules on a station share a small number of dominant variants.
    private void MeasureStationBrightnessLuminance(Galaxy.Station station, System.Collections.Generic.List<PlacedModule> modules)
    {
        var luminanceByTexture = new System.Collections.Generic.Dictionary<Texture2D, float>();
        var moduleLuminances   = new System.Collections.Generic.List<float>(modules.Count);

        foreach (var mod in modules)
        {
            if (mod.TextureInstance == null) continue;
            if (!luminanceByTexture.TryGetValue(mod.TextureInstance, out float lum))
            {
                lum = ComputeMeanLuminance(mod.TextureInstance);
                luminanceByTexture[mod.TextureInstance] = lum;
            }
            moduleLuminances.Add(lum);
        }

        if (moduleLuminances.Count == 0)
        {
            _stationBrightnessLastRegenStation = null;
            _stationBrightnessLastP10 = _stationBrightnessLastMedian = _stationBrightnessLastMax = null;
            return;
        }

        moduleLuminances.Sort();
        _stationBrightnessLastRegenStation = station.Name;
        _stationBrightnessLastP10          = Percentile(moduleLuminances, 0.10f);
        _stationBrightnessLastMedian       = Percentile(moduleLuminances, 0.50f);
        _stationBrightnessLastMax          = moduleLuminances[^1];
    }

    private static float ComputeMeanLuminance(Texture2D tex)
    {
        var pixels = new Color[tex.Width * tex.Height];
        tex.GetData(pixels);
        double sum = 0;
        foreach (var p in pixels)
            sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        return (float)(sum / pixels.Length);
    }

    private static float Percentile(System.Collections.Generic.List<float> sorted, float p)
    {
        if (sorted.Count == 1) return sorted[0];
        float idx = p * (sorted.Count - 1);
        int   lo  = (int)MathF.Floor(idx);
        int   hi  = (int)MathF.Ceiling(idx);
        if (lo == hi) return sorted[lo];
        float t = idx - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
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

        // Brief B4a Fix 3: which of floor/compression/saturation is doing the work, made
        // visible as texture luminance rather than left implicit in the parameter values.
        if (_stationBrightnessLastMedian.HasValue)
            Line($"texture P10/median/max ({_stationBrightnessLastRegenStation}): {_stationBrightnessLastP10:F1} / {_stationBrightnessLastMedian:F1} / {_stationBrightnessLastMax:F1}", Color.LightGray);
        else
            Line("texture P10/median/max: adjust floor/compression/saturation or press J to measure", Color.LightGray);

        if (_stationBrightnessGenDirty)
            Line($"regenerating in {_stationBrightnessGenDebounceRemaining:F1}s...", Color.Yellow);

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
