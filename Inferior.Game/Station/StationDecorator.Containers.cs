using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Shipping containers ───────────────────────────────────────────────────

    // Container body: 6.0 × 2.5 × 2.5 m (L × W × H).
    // Placed flat on module faces, one long side resting on the surface.
    // Orientation: long axis horizontal (60%) or vertical (40%) relative to face axes.

    private const float ContainerL = 6.0f;
    private const float ContainerS = 2.5f;  // short dimension (square cross-section)

    // Per-station colour palette derived from station seed; varies by category.
    private static readonly Color[] ContainerColorsBase =
    [
        new Color(180, 55,  40),   // freight red
        new Color( 40, 80, 160),   // shipping blue
        new Color( 50,115,  50),   // industrial green
        new Color(145,120,  40),   // mustard yellow
        new Color( 90, 90,  90),   // neutral grey
        new Color(155, 80,  30),   // rust orange
        new Color( 60,100,130),    // slate blue
        new Color(100, 55,  55),   // dark red
    ];

    // Brief Z4 Fix 1: guaranteed (default false, unchanged behaviour) — Storage's whole
    // purpose is containers ("container yard... containers scaled by area"), so an
    // allocated Storage zone must not lose them to docking-bay falling to this switch's
    // 0.12 default (note: the "docking" entry here is docking-arm's category, a distinct
    // string from docking-bay's own — the two have never shared a table row).
    private static void GenerateContainers(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng, bool guaranteed = false)
    {
        if (!face.IsExposed) return;
        // Need at least enough space for one container laid on its smallest footprint
        if (face.Width < ContainerS + 0.6f || face.Height < ContainerS + 0.6f) return;

        if (!guaranteed)
        {
            float prob = mod.Definition.Category switch
            {
                "cargo"      => 0.85f,
                "docking"    => 0.70f,
                "industrial" => 0.60f,
                "core"       => 0.40f,
                "military"   => 0.35f,
                "fuel"       => 0.20f,
                "hab"        => 0.15f,
                "connector"  => 0.08f,
                _            => 0.12f,
            };
            if (rng.NextDouble() > prob) return;
        }

        // Brief Z4 Fix 2: guaranteed (Storage-zone) calls scale count with area via the
        // named density constant; ordinary/other-category calls keep the original fixed
        // area-tier switch unchanged.
        int maxContainers = guaranteed
            ? Math.Clamp(
                (int)MathF.Round(face.Width * face.Height * ZoneContentDensity.StorageContainersPerSqm),
                1, ZoneContentDensity.StorageMaxContainers)
            : (face.Width * face.Height) switch
            {
                >= 60f => 4,
                >= 30f => 3,
                >= 15f => 2,
                _      => 1,
            };

        // Pick 2–3 colours from the palette so containers on the same face have variety
        Color[] palette = PickContainerPalette(mod.Seed, rng);

        int placed = 0;
        double nextProb = 1.0;
        while (placed < maxContainers && rng.NextDouble() < nextProb)
        {
            PlaceContainer(mod, face, mesh, occupancy, rng, palette[rng.Next(palette.Length)]);
            placed++;
            nextProb = placed == 1 ? 0.60 : 0.35;
        }
    }

    private static Color[] PickContainerPalette(int modSeed, System.Random rng)
    {
        // 2–3 randomly chosen colours, offset by mod seed so different stations have variety
        var pool = ContainerColorsBase;
        int startIdx = (modSeed & 0x7FFF) % pool.Length;
        return
        [
            pool[startIdx % pool.Length],
            pool[(startIdx + 1 + rng.Next(3)) % pool.Length],
            pool[(startIdx + 3 + rng.Next(2)) % pool.Length],
        ];
    }

    private static void PlaceContainer(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng, Color color)
    {
        // Decide orientation: long axis along Right (horizontal) or Up (vertical)
        bool longHoriz = rng.NextDouble() < 0.6;

        // Footprint on the face
        float footRight = longHoriz ? ContainerL : ContainerS;
        float footUp    = longHoriz ? ContainerS : ContainerL;
        float halfFR    = footRight * 0.5f;
        float halfFU    = footUp    * 0.5f;

        // Check it can fit inside the face at all — stay clear of the chamfer bevel the
        // same way GeneratePanelSeams does, box modules only.
        float chamferInset = mod.Definition.MeshFactory == null ? mod.ChamferDepth * 0.707f : 0f;
        float marginR = face.Width  * 0.5f - chamferInset - halfFR;
        float marginU = face.Height * 0.5f - chamferInset - halfFU;
        if (marginR < 0f || marginU < 0f) return;

        // Try a few random positions
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float cu = (float)(rng.NextDouble() * 2 - 1) * MathF.Max(marginR - 0.3f, 0f);
            float cv = (float)(rng.NextDouble() * 2 - 1) * MathF.Max(marginU - 0.3f, 0f);

            if (!occupancy.TryOccupy(cu, cv, halfFR, halfFU, 0.20f)) continue;

            // Module-local centre of the container
            // Container sits on the face: stick out ContainerS * 0.5 in normal direction
            Vector3 centre = face.LocalCenter
                + face.LocalRight  * cu
                + face.LocalUp     * cv
                + face.LocalNormal * (ContainerS * 0.5f);

            // Build the oriented-box transform.
            // X axis = long axis of container, Z axis = face normal (depth off surface).
            Matrix t;
            if (longHoriz)
            {
                // Long axis along face.Right
                t = new Matrix(
                    face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                    face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                    face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                    centre.X,           centre.Y,           centre.Z,           1);
            }
            else
            {
                // Long axis along face.Up
                t = new Matrix(
                    face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                    face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                    face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                    centre.X,           centre.Y,           centre.Z,           1);
            }

            // Same factory used for standalone/debug-spawn containers — station-placed
            // greeble containers now get the identical chamfer/inset/fastener/text/wear
            // geometry instead of a separately hand-maintained reimplementation (that
            // reimplementation had drifted from the factory's own conventions, which is
            // why station-placed container text mirrored differently than standalone).
            // MergeTransformed detects and corrects handedness automatically, so the old
            // manual axisY-flip check that used to live here is gone — t is passed through
            // unchanged. No lighting pre-rotation is needed any more either: the sun term
            // is computed per frame in LitSurface.fx from each vertex's real world normal
            // (t maps container-local space into module-local space; the module's own
            // rotation and the station's spin are applied once, at draw time, to every
            // vertex uniformly — there is no separate bake-time basis to get wrong).
            var (verts, indices) = ShippingContainerFactory.GenerateVertices(
                color, wear: (float)(0.1 + rng.NextDouble() * 0.5), sidePatternSeed: rng.Next(),
                text: null, lockGrade: LockGrade.Civilian);
            mesh.MergeTransformed(verts, indices, t);
            break;
        }
    }
}
