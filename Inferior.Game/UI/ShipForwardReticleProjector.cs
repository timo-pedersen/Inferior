using Inferior.Core.Math;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.UI;

public readonly record struct ShipForwardReticleProjection(
    Vector2 ScreenPosition,
    bool IsClampedToViewport);

public static class ShipForwardReticleProjector
{
    private const double DirectionRayLengthMeters = 1.0e9;
    private const float EdgePaddingPixels = 18.0f;

    public static ShipForwardReticleProjection? Project(
        DVec3 cockpitCameraWorldPosition,
        Quaternion shipWorldOrientation,
        Matrix cameraView,
        Matrix cameraProjection,
        Viewport viewport)
    {
        Vector3 shipForward = Vector3.Normalize(Vector3.Transform(
            -Vector3.UnitZ,
            shipWorldOrientation));
        DVec3 forward = new(shipForward.X, shipForward.Y, shipForward.Z);
        DVec3 reticleWorldPoint =
            cockpitCameraWorldPosition + forward * DirectionRayLengthMeters;
        Vector3 cameraRelativeRenderPoint =
            (reticleWorldPoint - cockpitCameraWorldPosition).ToVector3()
            * (float)Camera3D.RenderScale;

        Vector4 clip = Vector4.Transform(
            new Vector4(cameraRelativeRenderPoint, 1.0f),
            cameraView * cameraProjection);
        if (!float.IsFinite(clip.W) || clip.W <= 0.0f)
            return null;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        if (!float.IsFinite(ndcX) || !float.IsFinite(ndcY))
            return null;

        float maxNdcX = Math.Max(
            0.0f,
            1.0f - 2.0f * EdgePaddingPixels / viewport.Width);
        float maxNdcY = Math.Max(
            0.0f,
            1.0f - 2.0f * EdgePaddingPixels / viewport.Height);
        bool clamped = Math.Abs(ndcX) > maxNdcX || Math.Abs(ndcY) > maxNdcY;
        if (clamped)
        {
            float xScale = Math.Abs(ndcX) > 1e-7f
                ? maxNdcX / Math.Abs(ndcX)
                : float.PositiveInfinity;
            float yScale = Math.Abs(ndcY) > 1e-7f
                ? maxNdcY / Math.Abs(ndcY)
                : float.PositiveInfinity;
            float scale = Math.Min(xScale, yScale);
            ndcX *= scale;
            ndcY *= scale;
        }

        float screenX = viewport.X + (ndcX + 1.0f) * 0.5f * viewport.Width;
        float screenY = viewport.Y + (-ndcY + 1.0f) * 0.5f * viewport.Height;
        return new ShipForwardReticleProjection(new Vector2(screenX, screenY), clamped);
    }
}
