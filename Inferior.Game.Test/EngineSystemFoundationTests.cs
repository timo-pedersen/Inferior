using Inferior.Core.Math;
using Inferior.Game.Ships;
using Inferior.Gameplay.Engines;
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
        Assert.Equal(-rightMatrix.M11, leftMatrix.M11);
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
            dryMassKg: 2_400.0);
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
