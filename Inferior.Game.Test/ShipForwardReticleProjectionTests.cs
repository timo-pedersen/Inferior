using Inferior.Core.Math;
using Inferior.Game.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShipForwardReticleProjectionTests
{
    private const int Width = 1920;
    private const int Height = 1080;
    private static readonly Viewport Viewport = new(0, 0, Width, Height);
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(
        MathHelper.ToRadians(60.0f),
        (float)Width / Height,
        0.001f,
        50_000.0f);

    [Fact]
    public void AlignedCamera_PlacesReticleAtScreenCentre()
    {
        ShipForwardReticleProjection result = Project(
            DVec3.Zero,
            Quaternion.Identity,
            Quaternion.Identity)!.Value;

        Assert.InRange(Math.Abs(result.ScreenPosition.X - Width / 2.0f), 0.0f, 0.01f);
        Assert.InRange(Math.Abs(result.ScreenPosition.Y - Height / 2.0f), 0.0f, 0.01f);
        Assert.False(result.IsClampedToViewport);
    }

    [Fact]
    public void AriesInwardCameraYaw_OffsetsReticleOppositeByThreeDegrees()
    {
        Quaternion inwardCamera = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(-3.0f),
            0.0f,
            0.0f);

        ShipForwardReticleProjection result =
            Project(DVec3.Zero, Quaternion.Identity, inwardCamera)!.Value;

        Assert.True(result.ScreenPosition.X < Width / 2.0f);
        float ndcX = Math.Abs((result.ScreenPosition.X - Width / 2.0f) / (Width / 2.0f));
        float horizontalTangent =
            MathF.Tan(MathHelper.ToRadians(60.0f) * 0.5f) * Width / Height;
        float angularOffsetDegrees =
            MathHelper.ToDegrees(MathF.Atan(ndcX * horizontalTangent));
        Assert.InRange(angularOffsetDegrees, 2.99f, 3.01f);
    }

    [Fact]
    public void TranslationAndLateralCameraPlacement_DoNotChangeAngularPosition()
    {
        Quaternion inwardCamera = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(-3.0f),
            0.0f,
            0.0f);
        ShipForwardReticleProjection origin =
            Project(DVec3.Zero, Quaternion.Identity, inwardCamera)!.Value;
        ShipForwardReticleProjection translated =
            Project(new DVec3(8.5e10, -2.0e7, 4.0e11), Quaternion.Identity, inwardCamera)!.Value;
        ShipForwardReticleProjection lateral =
            Project(new DVec3(35.0, 0.0, 0.0), Quaternion.Identity, inwardCamera)!.Value;

        AssertVector(origin.ScreenPosition, translated.ScreenPosition);
        AssertVector(origin.ScreenPosition, lateral.ScreenPosition);
    }

    [Fact]
    public void RelativeCameraRoll_RotatesOffsetAroundScreenCentre()
    {
        Quaternion inwardCamera = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(-3.0f),
            0.0f,
            0.0f);
        Matrix unrolledView = BuildView(inwardCamera);
        Matrix rolledView = unrolledView * Matrix.CreateRotationZ(MathHelper.PiOver2);

        ShipForwardReticleProjection unrolled =
            ShipForwardReticleProjector.Project(
                DVec3.Zero,
                Quaternion.Identity,
                unrolledView,
                Projection,
                Viewport)!.Value;
        ShipForwardReticleProjection rolled =
            ShipForwardReticleProjector.Project(
                DVec3.Zero,
                Quaternion.Identity,
                rolledView,
                Projection,
                Viewport)!.Value;
        Vector2 centre = new(Width / 2.0f, Height / 2.0f);
        Vector2 a = unrolled.ScreenPosition - centre;
        Vector2 b = rolled.ScreenPosition - centre;

        Assert.InRange(Math.Abs(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b))), 0.0f, 0.001f);
        Assert.InRange(Math.Abs(a.Length() - b.Length()), 0.0f, 0.01f);
    }

    [Fact]
    public void ForwardRayBehindCamera_IsHidden()
    {
        Quaternion backwardsCamera = Quaternion.CreateFromYawPitchRoll(MathHelper.Pi, 0.0f, 0.0f);

        Assert.Null(Project(DVec3.Zero, Quaternion.Identity, backwardsCamera));
    }

    [Fact]
    public void ForwardRayOutsideFieldOfView_IsClampedToViewportEdge()
    {
        Quaternion sideLookingCamera = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(-80.0f),
            0.0f,
            0.0f);

        ShipForwardReticleProjection result =
            Project(DVec3.Zero, Quaternion.Identity, sideLookingCamera)!.Value;

        Assert.True(result.IsClampedToViewport);
        Assert.InRange(result.ScreenPosition.X, 17.9f, 18.1f);
        Assert.InRange(result.ScreenPosition.Y, Height / 2.0f - 0.1f, Height / 2.0f + 0.1f);
    }

    private static ShipForwardReticleProjection? Project(
        DVec3 cameraPosition,
        Quaternion shipOrientation,
        Quaternion cameraOrientation)
        => ShipForwardReticleProjector.Project(
            cameraPosition,
            shipOrientation,
            BuildView(cameraOrientation),
            Projection,
            Viewport);

    private static Matrix BuildView(Quaternion orientation)
    {
        Vector3 forward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, orientation));
        Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, orientation));
        return Matrix.CreateLookAt(Vector3.Zero, forward, up);
    }

    private static void AssertVector(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0.0f, 0.001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0.0f, 0.001f);
    }
}
