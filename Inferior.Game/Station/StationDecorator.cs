using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Adds per-module decoration geometry (windows, hatches, antennas, pipes, lights,
// chimneys, solar panels) to each PlacedModule.Mesh in local module space.
// Must be called after growth is complete so AttachmentPort and ChildPorts are
// fully populated. ApplyAmbientOcclusion is a separate pass run after BakeLighting.
public static partial class StationDecorator
{
    public static void Decorate(IReadOnlyList<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            var baseRng         = new System.Random(mod.Seed);
            var windowRng       = new System.Random(baseRng.Next());
            var hatchRng        = new System.Random(baseRng.Next());
            var antennaRng      = new System.Random(baseRng.Next());
            var pipeRng         = new System.Random(baseRng.Next());
            var lightRng        = new System.Random(baseRng.Next());
            var chimneyRng      = new System.Random(baseRng.Next());
            var surfacePipeRng  = new System.Random(baseRng.Next());
            var seamRng         = new System.Random(baseRng.Next());
            var ventRng         = new System.Random(baseRng.Next());
            var greebleRng      = new System.Random(baseRng.Next());
            var edgeTrimRng     = new System.Random(baseRng.Next());
            var ambientLightRng = new System.Random(baseRng.Next());
            var cableRng        = new System.Random(baseRng.Next());

            // If the module has a custom hull factory, seed its mesh now so
            // ComputeFaces can read the actual face normals and centres.
            StationModuleMesh mesh;
            if (mod.Definition.MeshFactory != null)
            {
                mesh        = mod.Definition.MeshFactory(mod.Seed);
                // Brief F1: capture the factory's own hull face count BEFORE anything
                // below advances BaseFaceCount to also cover decoration — HullFaceCount
                // stays fixed at "just the load-bearing hull" from here on, used to keep
                // the hull out of AO (Fix 2) and to split the draw call (Fix 1). Box
                // modules never touch this (default 0) since their hull isn't in this mesh
                // at all — SystemSpaceState.BuildHullMesh builds it separately.
                mesh.HullFaceCount = mesh.BaseFaceCount;
                mesh.Texture = TextureFor(mod.Definition.Category);
                mod.Mesh    = mesh;   // expose early for ComputeFaces
            }
            else
            {
                mesh = new StationModuleMesh { Texture = TextureFor(mod.Definition.Category) };
            }
            var glassMesh = new StationModuleMesh { Texture = SurfaceTexture.Glass };

            FaceInfo[] faces = ComputeFaces(mod);

            // Pass 0: panel seams — flat surface decoration that gets AO applied.
            // Must run first so BaseFaceCount captures only these faces.
            mesh.CurrentDecorClass = DecorClass.PanelSeams;
            foreach (var face in faces)
                GeneratePanelSeams(mod, face, seamRng, mesh);
            // Brief F1: advance BaseFaceCount to include panel seams for BOTH module kinds
            // now — previously box-only, leaving custom-mesh modules' BaseFaceCount frozen
            // at the factory's hull count, which meant AO applied to load-bearing hull
            // vertices instead of decoration (see the D-Dark measurement report). Now that
            // AO excludes [0, HullFaceCount) explicitly (see ApplyAmbientOcclusion), it's
            // correct to advance BaseFaceCount here unconditionally — seam decoration on
            // custom-mesh modules gets AO'd just like box modules' seams do.
            mesh.BaseFaceCount = mesh.FaceCount;

            // Pass 1-N: raised decoration (not included in AO range).
            var tankRng      = new System.Random(baseRng.Next());
            var dishRng      = new System.Random(baseRng.Next());
            var containerRng = new System.Random(baseRng.Next());

            // Brief Z1: independent from the baseRng chain above by construction (XORed
            // into mod.Seed directly, never drawn via baseRng.Next()) — inserting zoning
            // must not shift any existing per-pass RNG stream or re-roll every station in
            // the galaxy. Capped per-module across every face below, not just one.
            var zoneRng = new System.Random(mod.Seed ^ ZoneRngSalt);
            int moduleWindowZoneBudget = MaxWindowZonesPerModule;

            foreach (var face in faces)
            {
                // Landing pad faces must be clear — markings and lights are added by
                // GenerateLandingPads. Skip all decoration passes so nothing lands on them.
                if (IsDockingPadFace(mod, face)) continue;

                var (zones, isUnzoned) = ComputeZones(face, zoneRng);

                if (isUnzoned)
                {
                    // Brief Z1's critical no-op property: a face below the zoning
                    // threshold is bit-identical to pre-Z1 behaviour — every pass below
                    // runs unconditionally, in the exact original order, with no zone-type
                    // filtering at all. Do not "clean this up" into a 1-zone call through
                    // RunZonePasses; that would run a type-filtered SUBSET of these passes
                    // instead of all of them, breaking the no-op guarantee.
                    var occupancy         = new FaceOccupancy();
                    var greeblePlacements = new List<PlacedGreebleInfo>();
                    mesh.CurrentDecorClass = DecorClass.Windows;
                    GenerateWindows     (mod, face, windowRng,      mesh, glassMesh, occupancy);
                    mesh.CurrentDecorClass = DecorClass.Hatches;
                    GenerateHatches     (mod, face, hatchRng,       mesh, occupancy);
                    mesh.CurrentDecorClass = DecorClass.Antennas;
                    GenerateAntennas    (mod, face, antennaRng,     mesh, mod.GlowLights, occupancy, greeblePlacements);
                    mesh.CurrentDecorClass = DecorClass.Dishes;
                    GenerateDishes      (mod, face, dishRng,        mesh, occupancy, greeblePlacements);
                    mesh.CurrentDecorClass = DecorClass.Chimneys;
                    GenerateChimneys    (mod, face, chimneyRng,     mesh, mod.GlowLights, mod);
                    mesh.CurrentDecorClass = DecorClass.SurfacePipes;
                    GenerateSurfacePipes(mod, face, surfacePipeRng, mesh);
                    mesh.CurrentDecorClass = DecorClass.VentGrilles;
                    GenerateVentGrilles (mod, face, ventRng,        mesh, occupancy);
                    mesh.CurrentDecorClass = DecorClass.Greebles;
                    GenerateGreebles    (mod, face, greebleRng,     mesh, occupancy, greeblePlacements);
                    mesh.CurrentDecorClass = DecorClass.Tanks;
                    GenerateTanks       (mod, face, mesh, occupancy, new System.Random(tankRng.Next()));
                    mesh.CurrentDecorClass = DecorClass.Containers;
                    GenerateContainers  (mod, face, mesh, occupancy, new System.Random(containerRng.Next()));

                    if (face.IsExposed && greeblePlacements.Count >= 2 && face.Width * face.Height >= 4f)
                    {
                        mesh.CurrentDecorClass = DecorClass.Cables;
                        StationCableGenerator.GenerateFaceCables(
                            face.LocalNormal, face.LocalRight, face.LocalUp,
                            face.Width, face.Height, face.LocalCenter,
                            greeblePlacements, mesh,
                            new System.Random(cableRng.Next()));
                    }
                }
                else
                {
                    // Chimneys/surface pipes take no occupancy today and place blind
                    // (brief: "leave that as-is; note it, don't fix it") — they can't
                    // respect zone boundaries, so they still run once per FACE, not per
                    // zone, same call as the unzoned path above.
                    mesh.CurrentDecorClass = DecorClass.Chimneys;
                    GenerateChimneys    (mod, face, chimneyRng,     mesh, mod.GlowLights, mod);
                    mesh.CurrentDecorClass = DecorClass.SurfacePipes;
                    GenerateSurfacePipes(mod, face, surfacePipeRng, mesh);

                    var greeblePlacements = new List<PlacedGreebleInfo>();
                    var zoneTypes = AssignZoneTypes(mod.Definition.Category, zones, zoneRng, ref moduleWindowZoneBudget);

                    for (int i = 0; i < zones.Length; i++)
                    {
                        var occupancy = new FaceOccupancy(); // zones don't overlap, so no cross-zone conflicts by construction
                        RunZonePasses(mod, zones[i], zoneTypes[i], mesh, glassMesh, occupancy, greeblePlacements,
                            windowRng, hatchRng, antennaRng, dishRng, ventRng, greebleRng, tankRng, containerRng);
                    }

                    // Cables still run per-face, collecting placements from every zone on
                    // that face, so cables can still span between zones (brief: "Do not
                    // move them inside the zone loop").
                    if (face.IsExposed && greeblePlacements.Count >= 2 && face.Width * face.Height >= 4f)
                    {
                        mesh.CurrentDecorClass = DecorClass.Cables;
                        StationCableGenerator.GenerateFaceCables(
                            face.LocalNormal, face.LocalRight, face.LocalUp,
                            face.Width, face.Height, face.LocalCenter,
                            greeblePlacements, mesh,
                            new System.Random(cableRng.Next()));
                    }
                }
            }

            mesh.CurrentDecorClass = DecorClass.Pipes;
            GeneratePipes         (mod, faces, pipeRng,     mesh);
            mesh.CurrentDecorClass = DecorClass.Lights;
            GenerateLights        (mod, faces, lightRng,    mesh);
            mesh.CurrentDecorClass = DecorClass.EdgeTrim;
            GenerateEdgeTrimStrips(mod, mesh);
            // Guidance lights + hazard placard — same "tiny and/or emissive" never-casts
            // bucket as GenerateLights per the Phase C policy table.
            mesh.CurrentDecorClass = DecorClass.Lights;
            PlaceDockingBayDoorDecoration(mod, mesh);

            // No mesh geometry — only registers StationLightInfo glow entries.
            RegisterModuleAmbientLights(mod, faces, ambientLightRng);

            if (!mesh.IsEmpty)
                mod.Mesh = mesh;
            if (!glassMesh.IsEmpty)
                mod.GlassMesh = glassMesh;
        }

        // Station-wide passes that need all modules to be decorated first.
        int stationSeed = modules.Count > 0 ? modules[0].Seed : 42;
        PlaceLandmarkAntenna(modules, new System.Random(stationSeed ^ 0x12345678));
        RunSolarPanelPass   (modules, new System.Random(stationSeed ^ unchecked((int)0xABCDEF01)));
        RunLargeDishPass    (modules, new System.Random(stationSeed ^ 0x44157A3B));
        GenerateLandingPads (modules, new System.Random(stationSeed ^ 0x3D4E5F60));
    }

    // ── Phase C casting policy ────────────────────────────────────────────────
    //
    // Rollout stage groupings — same grouping as DecorCastingPolicy's comments below,
    // expressed as data so SystemSpaceState.Shadows.cs's Ctrl+F6 debug cycle can preview a
    // stage ahead of its landing in the policy table.
    public static readonly DecorClass[] C1Classes =
        [DecorClass.Pipes, DecorClass.SurfacePipes, DecorClass.PipeBrackets];
    public static readonly DecorClass[] C2Classes =
        [DecorClass.Tanks, DecorClass.Containers, DecorClass.Greebles,
         DecorClass.Chimneys, DecorClass.VentGrilles, DecorClass.SolarPanels];
    public static readonly DecorClass[] C3Classes =
        [DecorClass.Dishes, DecorClass.Antennas];
    public static readonly DecorClass[] C4Classes =
        [DecorClass.Windows, DecorClass.Hatches];

    // Docs/station-lighting-pipeline-spec.md §10 Phase C: the executable form of the
    // "documented casting policy." SystemSpaceState.Shadows.cs reads this to decide which
    // DecorClassRanges feed the per-module shadow-caster index buffer. Rollout is one
    // stage at a time (C1 → C2 → C3 → C4), each landed here only after its own in-engine
    // gate (F8 overlay + Timo's screenshots) — see Brief-LightingPhaseC.md "Rollout".
    // The Ctrl+F6 debug cycle can preview a later stage without this table saying true;
    // this table is what actually casts in the normal game.
    public static readonly IReadOnlyDictionary<DecorClass, bool> DecorCastingPolicy =
        new Dictionary<DecorClass, bool>
        {
            // Fail-safe default — nothing should reach this once Decorate wraps every pass.
            [DecorClass.Unclassified] = false,

            // Never casts (documented exclusions).
            [DecorClass.PanelSeams]         = false, // flat surface decoration, no height
            [DecorClass.EdgeTrim]           = false, // hugs the hull silhouette the hull already casts
            [DecorClass.Cables]             = false, // sub-texel thickness; proven inconsistent caster in the failed 2026 experiment
            [DecorClass.Lights]             = false, // lights/lenses, bay guidance lights, placards — tiny and/or emissive
            [DecorClass.Glass]              = false, // separate transparent mesh, per spec
            [DecorClass.LandingPadMarkings] = false, // flat; pad faces are kept clear anyway

            // C1 — structural. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            [DecorClass.Pipes]        = true,
            [DecorClass.SurfacePipes] = true,
            [DecorClass.PipeBrackets] = true,

            // C2 — equipment. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            [DecorClass.Tanks]       = true,
            [DecorClass.Containers]  = true,
            [DecorClass.Greebles]    = true,
            [DecorClass.Chimneys]    = true,
            [DecorClass.VentGrilles] = true,
            [DecorClass.SolarPanels] = true,

            // C3 — antennas. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            // Covers parabolic dishes + feed assemblies (RunLargeDishPass included) and yagi
            // masts/booms/elements (PlaceLandmarkAntenna included) as whole assemblies — no
            // finer-grained DecorClass split for individual yagi elements yet. Thin-element
            // rule (brief §"Thin-element rule"): if individual yagi elements prove
            // inconsistent casters at the station's texel density, excluding just the
            // elements while masts/booms keep casting is the documented correct outcome —
            // would need a new DecorClass (e.g. AntennaElements) split out from Antennas to
            // express. Not yet needed as of C3 landing; revisit if Timo's F8 check shows it.
            [DecorClass.Dishes]   = true,
            [DecorClass.Antennas] = true,

            // C4 — windows. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            // Covers window frames/braces/cupolas and hatches. Glass itself stays excluded —
            // separate GlassMesh, transparent, never a caster candidate (see [DecorClass.Glass]
            // above), unaffected by this class landing.
            [DecorClass.Windows] = true,
            [DecorClass.Hatches] = true,
        };

    // Darkens decoration vertices on occluded module faces.
    // Per-face algorithm: each seam face gets a darkening factor based on how many
    // of its 4 adjacent face normals are blocked (internal/connected).
    // Only processes BaseFaceCount faces — not raised decoration geometry.
    // Call after BakeLighting so lighting colours are already baked in.
    // Brief F1 Fix 2: starts at HullFaceCount, not 0 — AO is a decoration concept and must
    // never touch load-bearing hull vertices. 0 for box modules (default, unaffected —
    // their hull isn't in this mesh at all), the factory's hull count for MeshFactory
    // modules, so their hull draws flat (DynamicLit, Fix 1) while seam decoration past it
    // still gets AO'd exactly like a box module's seams do.
    public static void ApplyAmbientOcclusion(IReadOnlyList<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            if (mod.Mesh == null) continue;

            var internalNormals = BuildInternalNormalSet(mod);
            int start = mod.Mesh.HullFaceCount;
            int limit = mod.Mesh.BaseFaceCount;

            for (int faceIdx = start; faceIdx < limit; faceIdx++)
            {
                Vector3 faceNormal = mod.Mesh.LocalFaceNormal(faceIdx);
                if (internalNormals.Contains(faceNormal)) continue;

                int enclosed = AdjacentNormalsFor(faceNormal)
                    .Count(n => internalNormals.Contains(n));

                float aoFactor = enclosed switch { 1 => 0.90f, 2 => 0.76f, 3 => 0.60f, _ => 1.00f };
                if (aoFactor >= 1.0f) continue;
                mod.Mesh.MultiplyFaceColor(faceIdx, aoFactor);
            }
        }
    }

    // Returns the local-space normals for all blocked (internal/connected) faces.
    private static HashSet<Vector3> BuildInternalNormalSet(PlacedModule mod)
    {
        var result = new HashSet<Vector3>();
        foreach (var face in ComputeFaces(mod))
            if (!face.IsExposed) result.Add(face.LocalNormal);
        return result;
    }

    // Returns the 4 axis-aligned normals adjacent to a given face normal.
    private static Vector3[] AdjacentNormalsFor(Vector3 n)
    {
        if (MathF.Abs(n.X) > 0.9f)
            return [Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        if (MathF.Abs(n.Y) > 0.9f)
            return [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];
        return [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY];
    }

}
