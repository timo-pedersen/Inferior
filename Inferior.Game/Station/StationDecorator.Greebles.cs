using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 4: Chimneys & Exhausts ──────────────────────────────────────────

    private static void GenerateChimneys(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, List<StationLightInfo> lights,
        PlacedModule owner)
    {
        if (!face.IsExposed) return;
        float prob = mod.Definition.Category switch
        {
            "industrial" => 0.75f,
            "core"       => 0.35f,
            _            => 0f,
        };
        if (rng.NextDouble() > prob) return;

        Color baseCol = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        int   count   = rng.Next(1, mod.Definition.Category == "industrial" ? 4 : 3);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * face.Width  * 0.6f;
            float v = (float)(rng.NextDouble() - 0.5) * face.Height * 0.6f;
            Vector3 basePos = face.LocalCenter
                + face.LocalRight * u
                + face.LocalUp    * v;
            AddChimney(mesh, basePos, face.LocalNormal, rng, baseCol, owner, lights);
        }
    }

    private static void AddChimney(StationModuleMesh mesh, Vector3 basePos,
        Vector3 normal, System.Random rng, Color baseCol,
        PlacedModule mod, List<StationLightInfo> lights)
    {
        Color chimCol = DarkenColor(baseCol, 0.55f);
        Color tipCol  = new(50, 45, 40);

        float height;
        if (rng.NextDouble() < 0.55)
        {
            height = (float)(rng.NextDouble() * 4.0 + 2.5);
            float radius = (float)(rng.NextDouble() * 0.2  + 0.15);
            mesh.AddOrientedBox(basePos + normal * (height * 0.5f),
                normal, height, radius * 2, radius * 2, chimCol);
            mesh.AddOrientedBox(basePos + normal * (height + 0.12f),
                normal, 0.25f, radius * 2.2f, radius * 2.2f, tipCol);
        }
        else
        {
            float baseR  = (float)(rng.NextDouble() * 0.4 + 0.4);
            float exitR  = baseR * 0.5f;
            height = (float)(rng.NextDouble() * 1.5 + 1.0);
            mesh.AddOrientedBox(basePos + normal * (height * 0.3f),
                normal, height * 0.6f, baseR * 2, baseR * 2, chimCol);
            mesh.AddOrientedBox(basePos + normal * (height * 0.8f),
                normal, height * 0.4f, exitR * 2, exitR * 2, tipCol);
        }

        Vector3 chimTip = basePos + normal * (height + 0.15f);
        lights.Add(new StationLightInfo(
            WorldPosition: Vector3.Transform(chimTip, mod.Transform),
            Colour:        new Color(210, 30, 20),
            Type:          GlowType.AviationWarning,
            BaseIntensity: 1.0f,
            Rate:          0.65f,
            Phase:         (float)rng.NextDouble(),
            Pattern:       LightPattern.Strobe));
    }


    // ── Pass 6d: Greeble boxes ────────────────────────────────────────────────

    private enum GreebleType
    {
        JunctionBox, EquipmentHousing, ConduitEntry, SensorPod, TechPanel, ValveAssembly, YagiAntenna
    }

    private static GreebleType SelectGreebleType(string category, System.Random rng)
    {
        return category switch
        {
            "industrial" or "core" => (GreebleType)rng.Next(0, 7),
            "cargo"      or "fuel" => rng.NextDouble() < 0.5
                ? GreebleType.ValveAssembly : GreebleType.ConduitEntry,
            "science"              => rng.NextDouble() switch {
                                        < 0.15 => GreebleType.YagiAntenna,
                                        < 0.45 => GreebleType.SensorPod,
                                        < 0.75 => GreebleType.JunctionBox,
                                        _      => GreebleType.TechPanel,
                                     },
            "hab"                  => rng.NextDouble() < 0.65
                ? GreebleType.JunctionBox : GreebleType.TechPanel,
            _                      => (GreebleType)rng.Next(0, 3),
        };
    }

    private static (float halfW, float halfH) GreebleFootprint(GreebleType type, System.Random rng) => type switch
    {
        GreebleType.JunctionBox      => (0.40f + (float)rng.NextDouble() * 0.10f,
                                         0.30f + (float)rng.NextDouble() * 0.10f),
        GreebleType.EquipmentHousing => (0.75f + (float)rng.NextDouble() * 0.20f,
                                         0.50f + (float)rng.NextDouble() * 0.15f),
        GreebleType.ConduitEntry     => (0.35f, 0.35f),
        GreebleType.SensorPod        => (0.30f, 0.30f),
        GreebleType.TechPanel        => (0.60f + (float)rng.NextDouble() * 0.20f,
                                         0.45f + (float)rng.NextDouble() * 0.15f),
        GreebleType.ValveAssembly    => (0.45f, 0.45f),
        GreebleType.YagiAntenna      => (0.25f, 0.25f),
        _                            => (0.35f, 0.35f),
    };

    private static bool IsConnectableGreeble(GreebleType type) => type switch
    {
        GreebleType.JunctionBox      => true,
        GreebleType.EquipmentHousing => true,
        GreebleType.ConduitEntry     => true,
        GreebleType.ValveAssembly    => true,
        GreebleType.YagiAntenna      => true,
        _                            => false,
    };

    private static void GenerateGreebles(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> placements)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 12f) return;

        float prob = mod.Definition.Category switch
        {
            "industrial" or "core" => 0.90f,
            "cargo"      or "fuel" => 0.70f,
            "science"              => 0.75f,
            "connector"            => 0.55f,
            "hab"                  => 0.60f,
            _                      => 0.20f,
        };
        if (rng.NextDouble() > prob) return;

        int count    = rng.Next(2, 7);
        int attempts = count * 5;

        for (int i = 0; i < attempts && count > 0; i++)
        {
            var type       = SelectGreebleType(mod.Definition.Category, rng);
            var (hw, hh)   = GreebleFootprint(type, rng);
            const float margin = 0.8f;

            float cu = ((float)rng.NextDouble() - 0.5f) * (face.Width  - margin * 2 - hw * 2);
            float cv = ((float)rng.NextDouble() - 0.5f) * (face.Height - margin * 2 - hh * 2);

            if (!occupancy.TryOccupy(cu, cv, hw, hh, 0.10f)) continue;
            count--;

            placements.Add(new PlacedGreebleInfo(
                new Vector2(cu, cv),
                new Vector2(hw * 2, hh * 2),
                IsConnectableGreeble(type)));

            AddGreeble(mod, face, cu, cv, hw, hh, type, rng, mesh);
        }
    }

    private static void AddGreeble(PlacedModule mod, FaceInfo face,
        float cu, float cv, float hw, float hh,
        GreebleType type, System.Random rng, StationModuleMesh mesh)
    {
        Color baseCol    = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color greebleCol = DarkenColor(baseCol, 0.72f);
        Color detailCol  = DarkenColor(baseCol, 0.55f);
        Color darkCol    = DarkenColor(baseCol, 0.38f);

        switch (type)
        {
            case GreebleType.JunctionBox:
            {
                float boxH = 0.30f + (float)rng.NextDouble() * 0.15f;
                var t = FaceLocalTransform(face,
                    LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(t, new Vector3(hw * 2, hh * 2, boxH), greebleCol);
                mesh.AddQuad(
                    LocalPointAbs(face, cu - hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv + 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu - hw, cv + 0.02f, boxH + 0.005f), darkCol);
                break;
            }

            case GreebleType.EquipmentHousing:
            {
                float baseH = 0.40f + (float)rng.NextDouble() * 0.15f;
                float topH  = 0.20f;
                float topW  = hw * 0.6f;
                float topHH = hh * 0.5f;

                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, baseH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, baseH), greebleCol);

                float topOffset = cu + ((float)rng.NextDouble() - 0.5f) * hw * 0.4f;
                var tt = FaceLocalTransform(face,
                    LocalPointAbs(face, topOffset, cv, baseH + topH * 0.5f));
                mesh.AddOrientedBox(tt, new Vector3(topW * 2, topHH * 2, topH), detailCol);
                break;
            }

            case GreebleType.ConduitEntry:
            {
                float boxH = 0.35f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                float pipeLen = hw * 1.4f;
                // stubStart is the exposed far end; stubEnd sits at the equipment box
                // (same point as its own centre) and is covered by it.
                Vector3 stubStart = LocalPointAbs(face, cu - pipeLen, cv, boxH * 0.5f);
                Vector3 stubEnd   = LocalPointAbs(face, cu,           cv, boxH * 0.5f);
                mesh.AddPrismPipe(stubStart, stubEnd, 0.08f, 6, detailCol, capStart: true);
                break;
            }

            case GreebleType.SensorPod:
            {
                float podH = 0.50f + (float)rng.NextDouble() * 0.15f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, podH), greebleCol);

                float lensR = MathF.Min(hw, hh) * 0.5f;
                var lt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH + 0.04f));
                mesh.AddOrientedBox(lt, new Vector3(lensR * 2, lensR * 2, 0.08f),
                    new Color(60, 80, 100));
                break;
            }

            case GreebleType.TechPanel:
            {
                float panH = 0.12f + (float)rng.NextDouble() * 0.06f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, panH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, panH), greebleCol);

                for (int b = 0; b < 2; b++)
                {
                    float btnU  = cu + (b == 0 ? -hw * 0.4f : hw * 0.4f);
                    float btnSz = MathF.Min(hw, hh) * 0.3f;
                    var bt = FaceLocalTransform(face,
                        LocalPointAbs(face, btnU, cv, panH + 0.04f));
                    mesh.AddOrientedBox(bt, new Vector3(btnSz * 2, btnSz * 2, 0.07f),
                        b == 0 ? new Color(60, 100, 60) : detailCol);
                }
                break;
            }

            case GreebleType.ValveAssembly:
            {
                float boxH = 0.38f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                float armLen = hw * 0.9f;
                float armW   = 0.06f;
                float armH   = boxH + 0.10f;

                // Found during the AddPrismPipe end-cap sweep: all four spoke tips
                // are genuinely free-floating (nothing covers them), same category
                // as ConduitEntry's stub and the surface pipe runs.
                Vector3 hArmA = LocalPointAbs(face, cu - armLen, cv, armH);
                Vector3 hArmB = LocalPointAbs(face, cu + armLen, cv, armH);
                mesh.AddPrismPipe(hArmA, hArmB, armW, 4, darkCol, capStart: true, capEnd: true);

                Vector3 vArmA = LocalPointAbs(face, cu, cv - armLen, armH);
                Vector3 vArmB = LocalPointAbs(face, cu, cv + armLen, armH);
                mesh.AddPrismPipe(vArmA, vArmB, armW, 4, darkCol, capStart: true, capEnd: true);
                break;
            }

            case GreebleType.YagiAntenna:
            {
                var p = YagiAntennaParams.Generate(new System.Random(rng.Next()));
                StationYagiAntenna.Build(p, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, mesh);
                break;
            }
        }
    }

}
