using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationBaySecondaryUtilityTests(ITestOutputHelper output)
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void NovaProducesConnectedPipeVentAndAccessInstallations()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationBaySecondaryUtilityPlan plan = result.BaySecondaryUtilityPlan;
        output.WriteLine($"L3d-C {Nova}: {plan.Diagnostics}");

        Assert.NotEmpty(plan.Installations);
        Assert.Contains(plan.Installations,
            item => item.VentFamily == MegastationBayVentFamily.LouverBank);
        Assert.Contains(plan.Installations,
            item => item.VentFamily == MegastationBayVentFamily.ProjectingDuct);
        Assert.All(plan.Installations, item =>
        {
            Assert.Contains(result.BayUtilityPlan.Networks,
                network => network.Identity == item.SourceNetworkIdentity);
            Assert.InRange(item.Pipes.PipeCount, 2, 4);
            Assert.InRange(item.Pipes.Diameter, .5f, 3.2f);
            Assert.True(Vector2.Distance(item.Pipes.Start, item.Pipes.End) >= 60f);
            Assert.True(item.Pipes.SupportSpacing >= 19f);
            Assert.True(MathF.Abs(item.LadderTop - item.LadderBottom) >= 8f);
            Assert.True(float.IsFinite(item.ManifoldPosition.X));
            Assert.True(float.IsFinite(item.VentPosition.Y));

            Vector2 centre = (item.Pipes.Start + item.Pipes.End) * .5f;
            Vector2 size = new(Vector2.Distance(item.Pipes.Start, item.Pipes.End) + 22f, 52f);
            Assert.DoesNotContain(result.BayFacilityPlan.Reservations, reservation =>
                reservation.WallIdentity == item.WallIdentity
                && reservation.Kind != MegastationBayHabitationReservationKind.PlainWindowBand
                && Overlaps(centre, size, reservation.Centre, reservation.Size, 3f));
            Assert.DoesNotContain(result.LandingDistrictPlan.SiteReservations, reservation =>
                reservation.HostWallIdentity == item.WallIdentity
                && Overlaps(centre, size, reservation.WallCentre, reservation.WallSize, 5f));
        });
        Assert.True(plan.Diagnostics.VisibleTriangleCount > 0);
        Assert.True(plan.Diagnostics.MajorCasterTriangleCount > 0);
    }

    [Fact]
    public void SecondaryUtilityPlanIsDeterministicAndLeavesUpstreamPlansStable()
    {
        MegastationPrototypeCpuResult first = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(Nova);
        Assert.Equal(first.BaySecondaryUtilityPlan.Diagnostics.Signature,
            second.BaySecondaryUtilityPlan.Diagnostics.Signature);
        Assert.Equal(first.BayUtilityPlan.Diagnostics.Signature,
            second.BayUtilityPlan.Diagnostics.Signature);
        Assert.Equal(first.BayStructuralTrussPlan.Diagnostics.Signature,
            second.BayStructuralTrussPlan.Diagnostics.Signature);
        Assert.Equal(first.BayFacilityPlan.Diagnostics.Signature,
            second.BayFacilityPlan.Diagnostics.Signature);
    }

    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
}
