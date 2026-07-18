using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Input;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class EngineExhaustGlowTests
{
    [Fact]
    public void Definitions_ProvideEngineSpecificGlowAndExhaustAnchors()
    {
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition needle = NeedleEngineDefinitionFactory.CreateDefinition();
        EngineExhaustDefinition muleAnchor =
            Assert.Single(mule.VisualGeometry!.Exhausts);
        EngineExhaustDefinition needleAnchor =
            Assert.Single(needle.VisualGeometry!.Exhausts);

        Assert.Equal(new DVec3(1.0, 0.24, 0.035), mule.VisualDefinition!.GlowColour);
        Assert.Equal(0.15f, mule.VisualDefinition.IdleIntensity);
        Assert.Equal(2.0f, mule.VisualDefinition.BoostIntensity);
        Assert.Equal(new DVec3(0.0, 0.0, 3.80), muleAnchor.Position);
        Assert.Equal(0.50, muleAnchor.RadiusMeters);
        Assert.Equal(DVec3.UnitZ, muleAnchor.Direction);

        Assert.Equal(new DVec3(0.48, 0.82, 1.0), needle.VisualDefinition!.GlowColour);
        Assert.Equal(0.10f, needle.VisualDefinition.IdleIntensity);
        Assert.Equal(3.0f, needle.VisualDefinition.BoostIntensity);
        Assert.Equal(new DVec3(-0.04, 0.12, 3.62), needleAnchor.Position);
        Assert.Equal(0.50, needleAnchor.RadiusMeters);
        Assert.Equal(DVec3.UnitZ, needleAnchor.Direction);
    }

    [Fact]
    public void AltF2_CyclesBothIndependentInstancesThroughAllVisualStates()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);

        AssertVisualState(ship, EngineVisualState.Idle);
        CycleExhaust(simulation);
        AssertVisualState(ship, EngineVisualState.Thrust);
        AssertSnapshotState(simulation, EngineVisualState.Thrust);

        CycleExhaust(simulation);
        AssertVisualState(ship, EngineVisualState.Braking);
        CycleExhaust(simulation);
        AssertVisualState(ship, EngineVisualState.Boosting);
        CycleExhaust(simulation);
        AssertVisualState(ship, EngineVisualState.Idle);
    }

    [Fact]
    public void VisualState_RemainsIndependentPerEngineInstance()
    {
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        EngineInstance[] engines = InstalledEngines(ship);

        engines[0].SetVisualState(EngineVisualState.Boosting);

        Assert.Equal(EngineVisualState.Boosting, engines[0].VisualState);
        Assert.Equal(EngineVisualState.Idle, engines[1].VisualState);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engines[0].SetVisualState(new EngineVisualState(1.1f, 0f, 0f)));
    }

    [Fact]
    public void SelectedExhaustState_PersistsAcrossPairReplacement()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);

        CycleExhaust(simulation);
        CycleExhaust(simulation);
        CycleExhaust(simulation);
        AssertVisualState(ship, EngineVisualState.Boosting);

        simulation.RequestDebugCycleEngineConfiguration();
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        EngineInstance[] engines = InstalledEngines(ship);
        Assert.All(engines, engine =>
            Assert.Equal(NeedleEngineDefinitionFactory.H2VariantId, engine.Variant.VariantId));
        Assert.All(engines, engine =>
            Assert.Equal(EngineVisualState.Boosting, engine.VisualState));
    }

    [Fact]
    public void GlowDraws_UseMirroredEngineAnchorsAndDisappearWithEngines()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        IReadOnlyList<EngineExhaustGlowDraw> draws =
            ShipMeshRenderer.BuildEngineExhaustGlowDraws(
                simulation.ShipState!.EngineMounts!,
                Matrix.Identity,
                metresToRenderScale: 1f);

        Assert.Equal(2, draws.Count);
        Assert.All(draws, draw => Assert.Equal(0.50f, draw.Radius));
        Assert.Equal(-draws[1].Center.X, draws[0].Center.X, 5);
        Assert.Equal(draws[1].Center.Y, draws[0].Center.Y, 5);
        Assert.Equal(draws[1].Center.Z, draws[0].Center.Z, 5);
        Assert.All(draws, draw => Assert.Equal(EngineVisualState.Idle, draw.VisualState));

        simulation.RequestDebugCycleEngineConfiguration();
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        Assert.Empty(ShipMeshRenderer.BuildEngineExhaustGlowDraws(
            simulation.ShipState!.EngineMounts!,
            Matrix.Identity,
            metresToRenderScale: 1f));
    }

    [Theory]
    [InlineData(Keys.LeftAlt)]
    [InlineData(Keys.RightAlt)]
    public void PlatformInput_RecognizesOnlyAltF2RisingEdge(Keys alt)
    {
        var pressed = new KeyboardState(alt, Keys.F2);

        Assert.True(EngineExhaustDebugPlatformInput.IsCycleJustPressed(
            pressed,
            new KeyboardState()));
        Assert.False(EngineExhaustDebugPlatformInput.IsCycleJustPressed(pressed, pressed));
        Assert.False(EngineExhaustDebugPlatformInput.IsCycleJustPressed(
            new KeyboardState(Keys.F2),
            new KeyboardState()));
        Assert.False(EngineExhaustDebugPlatformInput.IsCycleJustPressed(
            new KeyboardState(alt, Keys.LeftControl, Keys.F2),
            new KeyboardState()));
        Assert.False(EngineExhaustDebugPlatformInput.IsCycleJustPressed(
            new KeyboardState(alt, Keys.LeftShift, Keys.F2),
            new KeyboardState()));
    }

    private static void CycleExhaust(SpaceSimulation simulation)
    {
        simulation.RequestDebugCycleEngineExhaustState();
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);
    }

    private static void AssertVisualState(Ship ship, EngineVisualState expected)
        => Assert.All(InstalledEngines(ship), engine =>
            Assert.Equal(expected, engine.VisualState));

    private static void AssertSnapshotState(
        SpaceSimulation simulation,
        EngineVisualState expected)
    {
        EnginePresentationSnapshot[] engines = simulation.ShipState!.EngineMounts!
            .Select(mount => mount.InstalledEngine)
            .OfType<EnginePresentationSnapshot>()
            .ToArray();
        Assert.Equal(2, engines.Length);
        Assert.All(engines, engine => Assert.Equal(expected, engine.VisualState));
    }

    private static EngineInstance[] InstalledEngines(Ship ship)
    {
        EngineInstance[] engines = ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>()
            .ToArray();
        Assert.Equal(2, engines.Length);
        Assert.NotSame(engines[0], engines[1]);
        return engines;
    }
}
