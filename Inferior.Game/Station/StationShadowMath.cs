using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public readonly record struct StationShadowBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;
    public Vector3 Center => (Min + Max) * 0.5f;
}

public readonly record struct StationShadowDepthRange(float Near, float Far, float ZPadding)
{
    public float Length => Far - Near;
}

public static class StationShadowMath
{
    public static int GetStationShadowMapSize() => 2048;

    public static StationShadowBounds ExpandBounds(StationShadowBounds bounds, float padding)
    {
        Vector3 pad = new(MathF.Max(0f, padding));
        return new StationShadowBounds(bounds.Min - pad, bounds.Max + pad);
    }

    public static StationShadowBounds ComputeStationBounds(IEnumerable<PlacedModule> modules, float padding)
    {
        bool any = false;
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (var mod in modules)
        {
            Include(ref min, ref max, ref any, mod.AabbMin);
            Include(ref min, ref max, ref any, mod.AabbMax);

            IncludeMeshBounds(ref min, ref max, ref any, mod.Mesh, mod.Transform);
            IncludeMeshBounds(ref min, ref max, ref any, mod.GlassMesh, mod.Transform);
        }

        if (!any)
            return new StationShadowBounds(Vector3.Zero, Vector3.Zero);

        return ExpandBounds(new StationShadowBounds(min, max), padding);
    }

    public static Matrix CreateLightView(Vector3 lightDirection, StationShadowBounds bounds)
    {
        Vector3 dir = SafeNormalize(lightDirection, Vector3.UnitZ);
        Vector3 center = bounds.Center;
        float radius = bounds.Size.Length() * 0.5f;
        Vector3 eye = center + dir * MathF.Max(radius, 1f);
        Vector3 up = MathF.Abs(Vector3.Dot(dir, Vector3.Up)) > 0.92f ? Vector3.Forward : Vector3.Up;
        return Matrix.CreateLookAt(eye, center, up);
    }

    public static Matrix CreateLightProjection(StationShadowBounds bounds, Matrix lightView)
        => CreateLightProjection(bounds, lightView, 0f, 0f, out _);

    public static Matrix CreateLightProjection(
        StationShadowBounds bounds,
        Matrix lightView,
        float xyPadding,
        float zPadding,
        out StationShadowDepthRange depthRange)
    {
        GetLightSpaceExtents(bounds, lightView, out Vector3 min, out Vector3 max);

        xyPadding = MathF.Max(0f, xyPadding);
        zPadding = MathF.Max(0f, zPadding);

        float width = MathF.Max(max.X - min.X + xyPadding * 2f, 1f);
        float height = MathF.Max(max.Y - min.Y + xyPadding * 2f, 1f);
        float near = MathF.Max(0.01f, -max.Z - zPadding);
        float far = MathF.Max(near + 1f, -min.Z + zPadding);

        depthRange = new StationShadowDepthRange(near, far, zPadding);
        return Matrix.CreateOrthographic(width, height, near, far);
    }

    public static float NormalizeLightDepth(float lightViewZ, StationShadowDepthRange depthRange)
    {
        float length = MathF.Max(depthRange.Length, 1e-6f);
        return MathHelper.Clamp((-lightViewZ - depthRange.Near) / length, 0f, 1f);
    }

    public static Vector3 ToShadowTextureCoordinate(Vector3 lightClip)
        => new(lightClip.X * 0.5f + 0.5f, -lightClip.Y * 0.5f + 0.5f, lightClip.Z);

    public static float ComputeReceiverBias(
        float normalDotLight, float baseBias, float slopeBias, float maxBias)
    {
        float ndotl = MathHelper.Clamp(normalDotLight, 0f, 1f);
        float slopeFactor = 1f - ndotl;
        return MathF.Min(baseBias + slopeBias * slopeFactor, maxBias);
    }

    private static void IncludeMeshBounds(
        ref Vector3 min, ref Vector3 max, ref bool any, StationModuleMesh? mesh, Matrix transform)
    {
        if (mesh == null || !mesh.TryGetLocalBounds(out var localMin, out var localMax))
            return;

        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(localMin.X, localMin.Y, localMin.Z),
            new(localMax.X, localMin.Y, localMin.Z),
            new(localMin.X, localMax.Y, localMin.Z),
            new(localMax.X, localMax.Y, localMin.Z),
            new(localMin.X, localMin.Y, localMax.Z),
            new(localMax.X, localMin.Y, localMax.Z),
            new(localMin.X, localMax.Y, localMax.Z),
            new(localMax.X, localMax.Y, localMax.Z),
        };

        foreach (var corner in corners)
            Include(ref min, ref max, ref any, Vector3.Transform(corner, transform));
    }

    private static void Include(ref Vector3 min, ref Vector3 max, ref bool any, Vector3 point)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
        any = true;
    }

    private static void GetLightSpaceExtents(
        StationShadowBounds bounds, Matrix lightView, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);

        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
        };

        foreach (var corner in corners)
        {
            Vector3 light = Vector3.Transform(corner, lightView);
            min = Vector3.Min(min, light);
            max = Vector3.Max(max, light);
        }
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        float len = value.Length();
        return len < 1e-6f ? fallback : value / len;
    }
}
