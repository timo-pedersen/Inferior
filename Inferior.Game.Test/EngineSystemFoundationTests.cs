using Inferior.Core.Math;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class EngineSystemFoundationTests
{
    [Fact]
    public void CompatibleMount_AcceptsEngine()
    {
        EngineMount mount = CreateMount(EngineMountSide.Port, EngineMountStandardIds.H2);
        var engine = new EngineInstance("engine-instance.port", CreateVariant(EngineMountStandardIds.H2));

        bool installed = mount.TryInstall(engine);

        Assert.True(installed);
        Assert.Same(engine, mount.InstalledEngine);
        Assert.Equal(mount.MountId, engine.InstalledMountId);
    }

    [Fact]
    public void IncompatibleMount_RejectsEngine()
    {
        EngineMount mount = CreateMount(EngineMountSide.Port, EngineMountStandardIds.H2);
        var engine = new EngineInstance(
            "engine-instance.port",
            CreateVariant(EngineMountStandardIds.Eriksson));

        bool installed = mount.TryInstall(engine);

        Assert.False(installed);
        Assert.Null(mount.InstalledEngine);
        Assert.False(engine.IsInstalled);
    }

    [Fact]
    public void PairGeneration_CreatesAndInstallsTwoEngines()
    {
        GeneratedEnginePair pair = GeneratePair();

        Assert.Equal(2, pair.Engines.Count);
        Assert.NotSame(pair.Left, pair.Right);
        Assert.NotEqual(pair.Left.InstanceId, pair.Right.InstanceId);
        Assert.True(pair.Left.IsInstalled);
        Assert.True(pair.Right.IsInstalled);
    }

    [Fact]
    public void PairGeneration_CreatesMirroredLeftAndRightGeometryTransforms()
    {
        GeneratedEnginePair pair = GeneratePair();
        EngineGeometryTransform left = pair.Left.GeometryTransform!;
        EngineGeometryTransform right = pair.Right.GeometryTransform!;
        Matrix leftMatrix = left.LocalToHull;
        Matrix rightMatrix = right.LocalToHull;

        Assert.Equal(-right.Position.X, left.Position.X);
        Assert.Equal(right.Position.Y, left.Position.Y);
        Assert.Equal(right.Position.Z, left.Position.Z);
        Assert.True(left.MirroredAcrossHullX);
        Assert.False(right.MirroredAcrossHullX);
        Assert.Equal(rightMatrix.M11, leftMatrix.M11);
        Assert.Equal(rightMatrix.M22, leftMatrix.M22);
        Assert.Equal(rightMatrix.M33, leftMatrix.M33);
        Assert.Equal(-rightMatrix.Translation.X, leftMatrix.Translation.X);
        Assert.Equal(rightMatrix.Translation.Y, leftMatrix.Translation.Y);
        Assert.Equal(rightMatrix.Translation.Z, leftMatrix.Translation.Z);
    }

    [Fact]
    public void GeneratedPair_InstancesHaveIndependentMutableState()
    {
        GeneratedEnginePair pair = GeneratePair();

        pair.Left.SetDamageFraction(0.6);
        pair.Left.SetWearFraction(0.25);

        Assert.Equal(0.6, pair.Left.DamageFraction);
        Assert.Equal(0.25, pair.Left.WearFraction);
        Assert.Equal(0.0, pair.Right.DamageFraction);
        Assert.Equal(0.0, pair.Right.WearFraction);
    }

    [Fact]
    public void ShipBuilder_MaterializesAriesEngineMountsAsShipOwnedRuntimeObjects()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();

        Assert.Equal(2, ship.EngineMounts.Count);
        Assert.All(
            ship.EngineMounts,
            mount => Assert.Equal(EngineMountStandardIds.H2, mount.MountStandardId));
        Assert.Contains(ship.EngineMounts, mount =>
            mount.ComponentSlotId == "engine.port.01"
            && mount.Side == EngineMountSide.Port);
        Assert.Contains(ship.EngineMounts, mount =>
            mount.ComponentSlotId == "engine.starboard.01"
            && mount.Side == EngineMountSide.Starboard);
    }

    [Fact]
    public void ShipBuilder_InstallsIndependentMulePairOnAries()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();
        EngineInstance[] engines = ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>()
            .ToArray();

        Assert.Equal(2, engines.Length);
        Assert.All(engines, engine => Assert.Equal(MuleEngineDefinitionFactory.H2VariantId, engine.Variant.VariantId));
        Assert.NotSame(engines[0], engines[1]);

        engines[0].SetDamageFraction(0.7);
        Assert.Equal(0.7, engines[0].DamageFraction);
        Assert.Equal(0.0, engines[1].DamageFraction);
    }

    [Fact]
    public void MulePortMesh_IsPositionMirroredWithCorrectedWinding()
    {
        EngineVisualGeometry geometry = MuleEngineDefinitionFactory.CreateDefinition().VisualGeometry!;
        EngineCpuMesh starboard = EngineMeshBuilder.Build(geometry, mirroredAcrossHullX: false);
        EngineCpuMesh port = EngineMeshBuilder.Build(geometry, mirroredAcrossHullX: true);

        Assert.Equal(starboard.Parts.Count, port.Parts.Count);
        for (int partIndex = 0; partIndex < starboard.Parts.Count; partIndex++)
        {
            EngineCpuMeshPart rightPart = starboard.Parts[partIndex];
            EngineCpuMeshPart leftPart = port.Parts[partIndex];
            Assert.Equal(rightPart.Vertices.Count, leftPart.Vertices.Count);

            for (int i = 0; i < rightPart.Vertices.Count; i += 3)
            {
                Vector3 rightA = rightPart.Vertices[i].Position;
                Vector3 rightB = rightPart.Vertices[i + 1].Position;
                Vector3 rightC = rightPart.Vertices[i + 2].Position;
                Vector3 leftA = leftPart.Vertices[i].Position;
                Vector3 leftB = leftPart.Vertices[i + 1].Position;
                Vector3 leftC = leftPart.Vertices[i + 2].Position;

                Assert.Equal(-rightA.X, leftA.X);
                Assert.Equal(rightC.Y, leftB.Y);
                Assert.Equal(rightC.Z, leftB.Z);
                Assert.Equal(-rightC.X, leftB.X);
                Assert.Equal(-rightB.X, leftC.X);
                Assert.Equal(rightB.Y, leftC.Y);
                Assert.Equal(rightB.Z, leftC.Z);
                Assert.True(Vector3.Dot(
                    Vector3.Cross(leftB - leftA, leftC - leftA),
                    leftPart.Vertices[i].Normal) > 0f);
            }
        }
    }

    [Fact]
    public void RemovingPortEngine_LeavesStarboardEngineInstalled()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();
        EngineMount port = ship.EngineMounts.Single(mount => mount.Side == EngineMountSide.Port);
        EngineMount starboard = ship.EngineMounts.Single(mount => mount.Side == EngineMountSide.Starboard);

        EngineInstance? removed = port.RemoveInstalledEngine();

        Assert.NotNull(removed);
        Assert.False(removed.IsInstalled);
        Assert.Null(port.InstalledEngine);
        Assert.NotNull(starboard.InstalledEngine);
        Assert.True(starboard.InstalledEngine.IsInstalled);
    }

    [Fact]
    public void SimulationSnapshot_ReflectsOneSidedDebugEngineRemoval()
    {
        var simulation = new SpaceSimulation();
        var ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);

        Assert.Equal(2, simulation.ShipState!.EngineMounts!.Count(mount => mount.InstalledEngine is not null));

        simulation.RequestDebugRemoveEngine(EngineMountSide.Port);
        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);

        EngineMountPresentationSnapshot port = simulation.ShipState!.EngineMounts!
            .Single(mount => mount.Side == EngineMountSide.Port);
        EngineMountPresentationSnapshot starboard = simulation.ShipState.EngineMounts!
            .Single(mount => mount.Side == EngineMountSide.Starboard);
        Assert.Null(port.InstalledEngine);
        Assert.NotNull(starboard.InstalledEngine);
    }

    private static GeneratedEnginePair GeneratePair()
    {
        EngineVariantDefinition variant = CreateVariant(EngineMountStandardIds.H2);
        var definition = new EnginePairDefinition("pair.mule-2.h2", variant);
        return EnginePairGenerator.Generate(
            definition,
            CreateMount(EngineMountSide.Port, EngineMountStandardIds.H2),
            CreateMount(EngineMountSide.Starboard, EngineMountStandardIds.H2));
    }

    private static EngineVariantDefinition CreateVariant(string mountStandardId)
    {
        var family = new EngineDefinition(
            "mule-2",
            "Mule 2",
            new DVec3(1.8, 2.2, 5.5),
            dryMassKg: 2_400.0,
            forwardThrustN: 156_000.0,
            maneuveringThrustN: 78_000.0,
            rotationalTorqueNm: 600_000.0);
        return new EngineVariantDefinition(
            $"mule-2.{mountStandardId.ToLowerInvariant()}",
            family,
            mountStandardId);
    }

    private static EngineMount CreateMount(
        EngineMountSide side,
        string mountStandardId)
    {
        bool port = side == EngineMountSide.Port;
        return new EngineMount(
            port ? "type-1.port.engine-root.01" : "type-1.starboard.engine-root.01",
            port ? "engine.port.01" : "engine.starboard.01",
            mountStandardId,
            side,
            new EngineMountPose(
                new DVec3(port ? -4.05 : 4.05, -0.05, 2.75),
                port ? -DVec3.UnitX : DVec3.UnitX,
                DVec3.UnitY));
    }
}
