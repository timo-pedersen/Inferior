using Inferior.Core.DataBus;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    // Brief S1: single-source specular highlight on the DynamicLit/DynamicLitShadowed
    // techniques (ships, containers, calibration cube, and station hulls — everything on
    // MeshRenderer.DrawDynamicLit*; station decoration stays BakedColorLit* and untouched
    // until S2). Eye-tuned aesthetic pass, so presets exist to A/B live instead of
    // rebuilding per adjustment — same spirit as Shift+F6 for the shadow kernel (Brief
    // E1). Default is Tight — Timo's in-engine gate on the calibration cube, a container,
    // and a ship hull picked it over Off/Subtle/Default/Strong ("looks nicest by far");
    // K still cycles live for further A/B.
    private enum SpecularPreset { Off, Subtle, Default, Strong, Tight }
    private SpecularPreset _specularPreset = SpecularPreset.Tight;

    // Off carries strength 0 — that alone zeroes SpecularHighlight's contribution exactly
    // (0 * anything = 0), so "Off" is provably byte-identical to pre-S1 output without a
    // separate enable/disable flag. Shininess for Off is arbitrary (unused at strength 0);
    // kept at the Default value rather than 0 only so an accidental read isn't degenerate.
    private static (float strength, float shininess) SpecularParamsFor(SpecularPreset preset) => preset switch
    {
        SpecularPreset.Subtle  => (0.2f, 16f),
        SpecularPreset.Default => (0.4f, 32f),
        SpecularPreset.Strong  => (0.7f, 24f),
        SpecularPreset.Tight   => (0.5f, 96f),
        _                      => (0f, 32f),
    };

    private void UpdateSpecularInput(KeyboardState keys)
    {
        // K checked for conflicts the same way as every other debug key in this codebase
        // (the Ctrl+C lesson): grep of Keys.* across Inferior.Game found every letter
        // bound except B/I/J/K/O/P/U/Y, and every F-key (1-12) already claimed at least
        // one binding — K was free, no modifier needed, and it's a fresh key rather than
        // another modifier on F6/F7 (already the shadow-debug family, a different system).
        bool kJustPressed = keys.IsKeyDown(Keys.K) && !_prevKeys.IsKeyDown(Keys.K);
        if (!kJustPressed) return;

        _specularPreset = (SpecularPreset)(((int)_specularPreset + 1) % 5);
        var (strength, shininess) = SpecularParamsFor(_specularPreset);
        DataBus.System.Publish(Topics.System.All, new SystemMessage(
            $"Specular: {_specularPreset} (strength {strength:F2}, shininess {shininess:F0})",
            SystemMessagePriority.NB));
    }
}
