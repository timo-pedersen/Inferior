using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationArtificialLight(
    string Identity,
    Vector3 Position,
    Color Colour,
    float Intensity,
    float Range);

public sealed record MegastationArtificialLightingPlan(
    int AlgorithmVersion,
    int Seed,
    IReadOnlyList<MegastationArtificialLight> Lights,
    string Signature);

public static class MegastationArtificialLighting
{
    public const int AlgorithmVersion = 2;
    public const float IndirectStrength = .05f;
    public const float IndirectRangeScale = 1.5f;

    public static MegastationArtificialLightingPlan Plan(MegastationInteriorPlan interior)
    {
        int seed = MegastationSeed.Derive(interior.Seed, "interior-artificial-lighting:v1");
        (float rightMin, float rightMax) = Span(interior.MainFlightVolume, interior.PortalRight);
        (float upMin, float upMax) = Span(interior.MainFlightVolume, interior.PortalUp);
        Vector3 inward = -interior.OutwardNormal;
        (float depthMin, float depthMax) = Span(interior.MainFlightVolume, inward);
        float rightSpan = rightMax - rightMin;
        float upSpan = upMax - upMin;
        float rightInset = MathF.Min(12f, rightSpan * .08f);
        float upInset = MathF.Min(12f, upSpan * .10f);
        float[] depthFractions = [.14f, .36f, .61f, .84f];
        var lights = new List<MegastationArtificialLight>(12);

        for (int station = 0; station < depthFractions.Length; station++)
        {
            float depth = MathHelper.Lerp(depthMin, depthMax, depthFractions[station]);
            Add(station, "left", rightMin + rightInset,
                MathHelper.Lerp(upMin, upMax, station % 2 == 0 ? .38f : .62f), depth);
            Add(station, "right", rightMax - rightInset,
                MathHelper.Lerp(upMin, upMax, station % 2 == 0 ? .64f : .36f), depth);
            Add(station, "upper",
                MathHelper.Lerp(rightMin, rightMax, station % 2 == 0 ? .36f : .64f),
                upMax - upInset, depth);
        }

        string signature = Signature(seed, lights);
        return new(AlgorithmVersion, seed, lights, signature);

        void Add(int station, string role, float right, float up, float depth)
        {
            int child = MegastationSeed.Derive(seed, $"station:{station}:{role}");
            float range = 180f + Unit(child, 1) * 100f;
            float intensity = .72f + Unit(child, 2) * .30f;
            float warmth = Unit(child, 3);
            Color colour = new(
                (byte)MathHelper.Lerp(198f, 222f, warmth),
                (byte)MathHelper.Lerp(224f, 239f, warmth),
                (byte)MathHelper.Lerp(246f, 255f, warmth));
            Vector3 position = interior.PortalRight * right
                + interior.PortalUp * up
                + inward * depth;
            lights.Add(new($"interior/artificial:v1/station:{station}/{role}",
                position, colour, intensity, range));
        }
    }

    public static Vector3 Evaluate(
        Vector3 position,
        Vector3 normal,
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        (Vector3 direct, Vector3 indirect) = EvaluateComponents(position, normal, lights);
        return Vector3.Clamp(direct + indirect, Vector3.Zero, Vector3.One);
    }

    public static (Vector3 Direct, Vector3 Indirect) EvaluateComponents(
        Vector3 position,
        Vector3 normal,
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        Vector3 n = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.Zero;
        Vector3 direct = Vector3.Zero;
        Vector3 indirect = Vector3.Zero;
        foreach (MegastationArtificialLight light in lights)
        {
            Vector3 toLight = light.Position - position;
            float distanceSquared = toLight.LengthSquared();
            float distance = MathF.Sqrt(distanceSquared);
            Vector3 colour = light.Colour.ToVector3();

            // H1c-A direct term, deliberately unchanged.
            if (distanceSquared > 1e-8f && distance < light.Range)
            {
                float facing = MathF.Max(0f, Vector3.Dot(n, toLight / distance));
                if (facing > 0f)
                    direct += colour * (light.Intensity * SmoothFiniteFalloff(distance, light.Range) * facing);
            }

            // H1c-B: weak source-relative bounce approximation. It has no N.L because it
            // represents scattered arrival from many directions, but remains finite and
            // spatially tied to each real source. Occlusion remains deferred to H1c-C.
            float indirectRange = light.Range * IndirectRangeScale;
            if (distance < indirectRange)
                indirect += colour * (light.Intensity * IndirectStrength
                    * SmoothFiniteFalloff(distance, indirectRange));
        }
        return (direct, indirect);
    }

    private static float SmoothFiniteFalloff(float distance, float range)
    {
        float t = 1f - distance / range;
        return t * t * (3f - 2f * t);
    }

    private static (float Min, float Max) Span(MegastationInteriorVolume volume, Vector3 axis)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float x in new[] { volume.Minimum.X, volume.Maximum.X })
        foreach (float y in new[] { volume.Minimum.Y, volume.Maximum.Y })
        foreach (float z in new[] { volume.Minimum.Z, volume.Maximum.Z })
        {
            float projection = Vector3.Dot(new Vector3(x, y, z), axis);
            min = MathF.Min(min, projection);
            max = MathF.Max(max, projection);
        }
        return (min, max);
    }

    private static float Unit(int seed, int salt)
    {
        uint value = unchecked((uint)MegastationSeed.Derive(seed, $"parameter:{salt}"));
        return (value & 0x00ffffff) / 16777215f;
    }

    private static string Signature(int seed, IReadOnlyList<MegastationArtificialLight> lights)
    {
        var text = new StringBuilder().Append(AlgorithmVersion).Append('|').Append(seed);
        foreach (MegastationArtificialLight light in lights)
            text.Append('|').Append(light.Identity).Append(':')
                .Append(F(light.Position.X)).Append(',')
                .Append(F(light.Position.Y)).Append(',')
                .Append(F(light.Position.Z)).Append(':')
                .Append(light.Colour.PackedValue).Append(':')
                .Append(F(light.Intensity)).Append(':').Append(F(light.Range));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
