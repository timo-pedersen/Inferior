using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationBayStructuralTrussTests(ITestOutputHelper output)
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void NovaProducesCoherentSupportedMajorStructuralFields()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationBayStructuralTrussPlan plan = result.BayStructuralTrussPlan;

        output.WriteLine(plan.Diagnostics.ToString());
        Assert.NotEmpty(plan.Fields);
        Assert.NotEmpty(plan.Trusses);
        Assert.All(plan.Fields, field =>
        {
            Assert.NotEmpty(field.Trusses);
            Assert.All(field.Trusses, truss => Assert.Equal(field.Family, truss.Spec.CrossSection));
            StructuralTrussSpec first = field.Trusses[0].Spec;
            Assert.All(field.Trusses, truss =>
            {
                Assert.Equal(first.Length, truss.Spec.Length);
                Assert.Equal(first.Width, truss.Spec.Width);
                Assert.Equal(first.Height, truss.Spec.Height);
                Assert.Equal(first.ChordThickness, truss.Spec.ChordThickness);
                Assert.Equal(first.BraceThickness, truss.Spec.BraceThickness);
                Assert.Equal(first.TargetBayLength, truss.Spec.TargetBayLength);
                Assert.Equal(field.ColourBlend, truss.ColourBlend);
            });
        });
        Assert.All(plan.Trusses, truss =>
        {
            Assert.True(truss.CastsStellarShadow);
            Assert.True(truss.Spec.Length > 0f);
            Assert.True(Finite(truss.Start) && Finite(truss.End));
            Assert.Contains(result.BoundaryTopology.Faces, face =>
                face.Key.Equals(truss.SupportingFace)
                && face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary);
        });
        IReadOnlyDictionary<string, MegastationBayWallSurface> walls =
            result.BayHabitationPlan.Walls.ToDictionary(wall => wall.Identity);
        foreach (MegastationBayStructuralTruss truss in plan.Trusses.Where(item =>
                     item.HostKind == MegastationBayStructuralTrussHostKind.Wall))
        {
            MegastationBayWallSurface wall = walls[truss.HostIdentity];
            Vector3 centre = truss.Spec.Transform.Translation;
            var localCentre = new Vector2(
                Vector3.Dot(centre - wall.Centre, wall.Right),
                Vector3.Dot(centre - wall.Centre, wall.Up));
            var localSize = new Vector2(truss.Spec.Width, truss.Spec.Length);
            Assert.DoesNotContain(result.BayFacilityPlan.Reservations, reservation =>
                reservation.WallIdentity == wall.Identity
                && reservation.Kind != MegastationBayHabitationReservationKind.PlainWindowBand
                && Overlaps(localCentre, localSize, reservation.Centre, reservation.Size, 2f));
            Assert.DoesNotContain(result.LandingDistrictPlan.SiteReservations, reservation =>
                reservation.HostWallIdentity == wall.Identity
                && Overlaps(localCentre, localSize,
                    reservation.WallCentre, reservation.WallSize, 3f));
        }
        Assert.All(plan.Attachments, attachment => Assert.True(attachment.CastsStellarShadow));
        Assert.Equal(plan.Diagnostics.VisibleTriangleCount,
            plan.Diagnostics.CasterTriangleCount);
        Assert.True(plan.Diagnostics.VisibleTriangleCount > 0);

        output.WriteLine($"L3d-A {Nova}: {plan.Diagnostics.Summary}; "
            + $"wall={plan.Diagnostics.WallTrussCount}, roof={plan.Diagnostics.CeilingTrussCount}, "
            + $"triangles={plan.Diagnostics.VisibleTriangleCount}, "
            + $"hardReject={plan.Diagnostics.HardConflictRejectCount}, "
            + $"operationalReject={plan.Diagnostics.OperationalClearanceRejectCount}, "
            + $"supportReject={plan.Diagnostics.SupportRejectCount}, "
            + $"softWindowOverlap={plan.Diagnostics.SoftWindowOverlapCount}, "
            + $"planning={plan.Diagnostics.PlanningMilliseconds}ms; signature={plan.Diagnostics.Signature}");
    }

    [Fact]
    public void PlanIsDeterministicWithoutPerturbingAcceptedUpstreamPlans()
    {
        MegastationPrototypeCpuResult first = MegastationPrototypeGenerator.GenerateCpu(Nova);
        MegastationPrototypeCpuResult second = MegastationPrototypeGenerator.GenerateCpu(Nova);

        Assert.Equal(first.BayStructuralTrussPlan.Diagnostics.Signature,
            second.BayStructuralTrussPlan.Diagnostics.Signature);
        Assert.Equal(first.MegaShelfPlan.Diagnostics.Signature,
            second.MegaShelfPlan.Diagnostics.Signature);
        Assert.Equal(first.LandingDistrictPlan.Diagnostics.Signature,
            second.LandingDistrictPlan.Diagnostics.Signature);
        Assert.Equal(first.BayFacilityPlan.Diagnostics.Signature,
            second.BayFacilityPlan.Diagnostics.Signature);
        Assert.Equal(first.BayHabitationPlan.Diagnostics.Signature,
            second.BayHabitationPlan.Diagnostics.Signature);
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
}
