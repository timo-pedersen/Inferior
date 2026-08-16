using Inferior.Core.DataBus;
using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.States;

// Brief D-Z2 Measurement 1: zone-type debug overlay. Ctrl+F4 was free (F4 plain is the
// existing semantic-hull debug toggle — thematically the closest existing binding, since
// this is also "show geometry semantics that aren't otherwise visible"). Reads
// PlacedModule.DebugZones — populated by StationDecorator.Decorate() at the moment zone
// assignment and content actually happen (see ZoneDebugRecord's own comment) — so this
// view reflects the real production assignment, never a recomputation for display.
public sealed partial class SystemSpaceState
{
    private bool _showZoneTypeDebug;

    // Deliberately saturated and mutually distinct (the brief's own "F6 tier-tint lesson":
    // a subtle tint against an already-saturated station hull is invisible) — drawn fully
    // opaque, not blended, so there's no ambiguity about which zone is which type.
    private static readonly IReadOnlyDictionary<StationDecorator.ZoneType, Color> ZoneDebugPalette =
        new Dictionary<StationDecorator.ZoneType, Color>
        {
            [StationDecorator.ZoneType.Windows]      = new Color(0,   220, 255), // cyan
            [StationDecorator.ZoneType.Machinery]    = new Color(255, 140, 0),   // orange
            [StationDecorator.ZoneType.TankFarm]     = new Color(255, 30,  30),  // red
            [StationDecorator.ZoneType.ServiceCore]  = new Color(0,   220, 0),   // green
            [StationDecorator.ZoneType.Structural]   = new Color(210, 210, 210), // light grey
            [StationDecorator.ZoneType.Storage]      = new Color(255, 230, 0),   // yellow
            [StationDecorator.ZoneType.Signage]      = new Color(255, 0,   255), // magenta
            [StationDecorator.ZoneType.CommsArray]   = new Color(40,  90,  255), // blue
            [StationDecorator.ZoneType.PipeCorridor] = new Color(180, 0,   230), // purple
        };

    private void UpdateZoneDebugInput(KeyboardState keys)
    {
        bool ctrlDown = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = _prevKeys.IsKeyDown(Keys.LeftControl) || _prevKeys.IsKeyDown(Keys.RightControl);
        bool ctrlF4JustPressed = ctrlDown && keys.IsKeyDown(Keys.F4)
            && !(prevCtrlDown && _prevKeys.IsKeyDown(Keys.F4));

        if (!ctrlF4JustPressed) return;

        _showZoneTypeDebug = !_showZoneTypeDebug;

        string legend = string.Join(", ", ZoneDebugPalette.Select(kv => $"{kv.Key}={ColourName(kv.Value)}"));
        DataBus.System.Publish(Topics.System.All, new SystemMessage(
            _showZoneTypeDebug
                ? $"Zone-type debug overlay ON (dimmed = blocked by a neighbour). Legend: {legend}"
                : "Zone-type debug overlay OFF",
            SystemMessagePriority.NB));

        // Brief D-Z2 Measurement 2: same key also dumps Nova Anchorage's per-zone content
        // to the console, once per ON-toggle — pinned to that one deterministic station so
        // the dump and a screenshot are provably the same geometry, regardless of which
        // station the player is currently nearest to.
        if (_showZoneTypeDebug)
            DumpNovaAnchorageZoneContent();
    }

    private const string NovaAnchorageStationName = "Nova Anchorage";

    private void DumpNovaAnchorageZoneContent()
    {
        Galaxy.Station? novaAnchorage = null;
        List<PlacedModule>? modules = null;
        foreach (var (station, mods) in _stationGeometry)
        {
            if (station.Name != NovaAnchorageStationName) continue;
            novaAnchorage = station;
            modules = mods;
            break;
        }

        if (novaAnchorage == null || modules == null)
        {
            System.Console.WriteLine(
                $"[ZoneDebug] '{NovaAnchorageStationName}' not found in the current system — " +
                "this dump only works while its home system is loaded.");
            return;
        }

        System.Console.WriteLine($"[ZoneDebug] === {NovaAnchorageStationName} per-zone content dump ===");
        foreach (var mod in modules)
        {
            if (mod.DebugZones.Count == 0) continue;

            System.Console.WriteLine(
                $"[ZoneDebug] Module: category={mod.Definition.Category} id={mod.Definition.Id} seed={mod.Seed}");

            foreach (var z in mod.DebugZones)
            {
                // Approximate cell-rectangle size in ComputeZones' own 18m target cell
                // units (rounded) alongside the exact metre extent — StationDecorator.Zones
                // doesn't retain literal cell counts today, so this is derived, not stored,
                // but it's the same constant ComputeZones itself divides by.
                int cellsU = System.Math.Max(1, (int)System.MathF.Round(z.Zone.Width  / 18f));
                int cellsV = System.Math.Max(1, (int)System.MathF.Round(z.Zone.Height / 18f));
                System.Console.WriteLine(
                    $"[ZoneDebug]   face={z.FaceIndex} zoneOnFace={z.ZoneIndexOnFace} type={z.Type} " +
                    $"cellRect(~cells)={cellsU}x{cellsV} worldExtent(m)={z.Zone.Width:F1}x{z.Zone.Height:F1} " +
                    $"exposed={z.Zone.IsExposed} " +
                    $"produced(regionsClaimed={z.RegionsClaimed}, facesAdded={z.FacesAdded})");
            }

            if (mod.ZoneBudget != null)
            {
                System.Console.WriteLine(
                    $"[ZoneDebug]   ModuleZoneBudget after assignment: " +
                    $"TankFarmRemaining={mod.ZoneBudget.TankFarmRemaining} " +
                    $"NeedsPipeCorridor={mod.ZoneBudget.NeedsPipeCorridor} " +
                    $"NeedsCommsArray={mod.ZoneBudget.NeedsCommsArray} " +
                    $"NeedsSignage={mod.ZoneBudget.NeedsSignage} " +
                    $"WindowZonesRemaining={mod.ZoneBudget.WindowZonesRemaining}");
            }
        }
        System.Console.WriteLine("[ZoneDebug] === end dump ===");
    }

    private static string ColourName(Color c) => c switch
    {
        { R: 0,   G: 220, B: 255 } => "cyan",
        { R: 255, G: 140, B: 0   } => "orange",
        { R: 255, G: 30,  B: 30  } => "red",
        { R: 0,   G: 220, B: 0   } => "green",
        { R: 210, G: 210, B: 210 } => "light-grey",
        { R: 255, G: 230, B: 0   } => "yellow",
        { R: 255, G: 0,   B: 255 } => "magenta",
        { R: 40,  G: 90,  B: 255 } => "blue",
        { R: 180, G: 0,   B: 230 } => "purple",
        _                          => "?",
    };

    // Offset along the zone's own normal so the overlay quad clears panel-seam/AO geometry
    // underneath without visibly floating off the hull.
    private const float ZoneDebugOffsetMetres = 0.08f;

    // Brief Z3 Fix C: blocked zones (post-Fix-A' per-zone exposure) render as a heavily
    // dimmed, desaturated version of the SAME type colour, not a separate flat "blocked"
    // colour — keeping which type WOULD have gone there legible while unmistakably marking
    // it dead. With per-zone exposure now real (not a coarse per-face flag), this makes
    // neighbour footprints directly visible: "why is this patch bare" becomes "there's a
    // dim red rectangle there" instead of requiring the console dump to explain it.
    private const float BlockedZoneDimFactor = 0.22f;

    // Builds one opaque, unlit quad per retained zone for a single module — saturated for
    // exposed zones, dimmed for blocked ones — in the module's own local space. Same winding
    // as StationModuleMesh.AddQuad ("CW from normal side") so it's front-facing under the
    // same CullCounterClockwise rasterizer state everything else in this frame uses. Returns
    // null if the module has no retained zones (ordinary single-zone modules never populate
    // DebugZones at all).
    private static (VertexPositionColor[] verts, short[] indices)? BuildZoneDebugQuads(PlacedModule mod)
    {
        if (mod.DebugZones.Count == 0) return null;

        var verts = new VertexPositionColor[mod.DebugZones.Count * 4];
        var indices = new short[mod.DebugZones.Count * 6];

        for (int i = 0; i < mod.DebugZones.Count; i++)
        {
            var record = mod.DebugZones[i];
            var zone = record.Zone;
            Color colour = ZoneDebugPalette.TryGetValue(record.Type, out var c) ? c : Color.HotPink;
            if (!zone.IsExposed)
                colour = new Color(
                    (byte)(colour.R * BlockedZoneDimFactor),
                    (byte)(colour.G * BlockedZoneDimFactor),
                    (byte)(colour.B * BlockedZoneDimFactor));

            Vector3 centre = zone.LocalCenter + zone.LocalNormal * ZoneDebugOffsetMetres;
            Vector3 right  = zone.LocalRight * (zone.Width  * 0.5f);
            Vector3 up     = zone.LocalUp    * (zone.Height * 0.5f);

            int vb = i * 4;
            verts[vb + 0] = new VertexPositionColor(centre - right - up, colour);
            verts[vb + 1] = new VertexPositionColor(centre + right - up, colour);
            verts[vb + 2] = new VertexPositionColor(centre + right + up, colour);
            verts[vb + 3] = new VertexPositionColor(centre - right + up, colour);

            int ib = i * 6;
            indices[ib + 0] = (short)(vb + 0); indices[ib + 1] = (short)(vb + 2); indices[ib + 2] = (short)(vb + 1);
            indices[ib + 3] = (short)(vb + 0); indices[ib + 4] = (short)(vb + 3); indices[ib + 5] = (short)(vb + 2);
        }

        return (verts, indices);
    }
}
