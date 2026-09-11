using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationBayUtilityTests(ITestOutputHelper output)
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void NovaProducesLongSupportedCableNetworksWithExplicitJunctionsAndClamps()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationBayUtilityPlan plan = result.BayUtilityPlan;
        output.WriteLine($"L3d-B {Nova}: {plan.Diagnostics}; trussMounted="
            + plan.Bundles.Count(bundle => bundle.StructuralSupportIdentity is not null));

        Assert.NotEmpty(plan.Networks);
        Assert.Contains(plan.Bundles, bundle =>
            bundle.Level == MegastationBayUtilityRunLevel.Trunk && bundle.Length >= 90f);
        Assert.All(plan.Bundles, bundle =>
        {
            Assert.True(bundle.Length > 0f);
            Assert.True(bundle.CableCount >= 2);
            Assert.True(bundle.ClampSpacing > 0f);
            Assert.True(float.IsFinite(bundle.Start.X) && float.IsFinite(bundle.Start.Y));
            Assert.True(float.IsFinite(bundle.End.X) && float.IsFinite(bundle.End.Y));
        });
        Assert.Equal(plan.Bundles.Sum(bundle => Math.Max(2,
                (int)MathF.Floor(bundle.Length / bundle.ClampSpacing) + 1)),
            plan.Diagnostics.ClampCount);
        Assert.All(plan.Nodes.Where(node => node.Kind == MegastationBayUtilityNodeKind.JunctionBox),
            node => Assert.True(node.IncidentBundleIdentities.Count >= 2));
        Assert.True(plan.Diagnostics.VisibleTriangleCount > 0);

        foreach (MegastationBayCableBundle bundle in plan.Bundles)
        {
            Vector2 centre = (bundle.Start + bundle.End) * .5f;
            Vector2 size = new(MathF.Abs(bundle.End.X - bundle.Start.X) + 3.5f,
                MathF.Abs(bundle.End.Y - bundle.Start.Y) + 3.5f);
            Assert.DoesNotContain(result.BayFacilityPlan.Reservations, reservation =>
                reservation.WallIdentity == bundle.WallIdentity
                && reservation.Kind != MegastationBayHabitationReservationKind.PlainWindowBand
                && Overlaps(centre, size, reservation.Centre, reservation.Size, 2f));
            Assert.DoesNotContain(result.LandingDistrictPlan.SiteReservations, reservation =>
                reservation.HostWallIdentity == bundle.WallIdentity
                && Overlaps(centre, size, reservation.WallCentre, reservation.WallSize, 3f));
        }
    }

    [Fact]
    public void UtilityPlanIsDeterministicAndDoesNotPerturbUpstreamArchitecture()
    {
        MegastationPrototypeCpuResult first = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(Nova);
        Assert.Equal(first.BayUtilityPlan.Diagnostics.Signature,
            second.BayUtilityPlan.Diagnostics.Signature);
        Assert.Equal(first.BayStructuralTrussPlan.Diagnostics.Signature,
            second.BayStructuralTrussPlan.Diagnostics.Signature);
        Assert.Equal(first.BayFacilityPlan.Diagnostics.Signature,
            second.BayFacilityPlan.Diagnostics.Signature);
        Assert.Equal(first.LandingDistrictPlan.Diagnostics.Signature,
            second.LandingDistrictPlan.Diagnostics.Signature);
        Assert.Equal(first.MegaShelfPlan.Diagnostics.Signature,
            second.MegaShelfPlan.Diagnostics.Signature);
    }

    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
}
