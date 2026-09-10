using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum StructuralTrussCrossSection
{
    Box,
    Triangular,
}

public enum StructuralTrussMemberKind
{
    Chord,
    Transverse,
    Brace,
}

public sealed record StructuralTrussSpec(
    float Length,
    float Width,
    float Height,
    StructuralTrussCrossSection CrossSection,
    float ChordThickness,
    float BraceThickness,
    float TargetBayLength,
    Matrix Transform);

public sealed record StructuralTrussDiagnostics(
    int BayCount,
    float ActualBayLength,
    int ChordCount,
    int TransverseMemberCount,
    int BraceCount,
    int MemberCount,
    int AddedVertexCount,
    int AddedTriangleCount,
    float StartPlane,
    float EndPlane);

/// <summary>
/// Stateless manufactured lattice geometry. Consumers own architectural meaning,
/// material selection, batching and caster policy; this factory owns only topology.
/// Local X/Y are the requested outside width/height and local Z is length.
/// </summary>
public static class StructuralTrussFactory
{
    public static StructuralTrussDiagnostics Append(
        StationModuleMesh mesh,
        StructuralTrussSpec spec,
        Color colour)
    {
        Validate(spec);
        int firstVertex = mesh.VertexCount;
        int firstIndex = mesh.IndexCount;
        int chordCount = spec.CrossSection == StructuralTrussCrossSection.Box ? 4 : 3;
        int sideCount = chordCount;
        int bayCount = Math.Max(1, (int)MathF.Round(spec.Length / spec.TargetBayLength));
        float nodeStart = -spec.Length * .5f + spec.BraceThickness * .5f;
        float nodeEnd = spec.Length * .5f - spec.BraceThickness * .5f;
        float actualBayLength = (nodeEnd - nodeStart) / bayCount;
        Vector2[] chordCentres = ChordCentres(spec);

        for (int chord = 0; chord < chordCount; chord++)
            AddMember(
                Local(chordCentres[chord], -spec.Length * .5f),
                Local(chordCentres[chord], spec.Length * .5f),
                spec.ChordThickness,
                Vector3.UnitY,
                StructuralTrussMemberKind.Chord);

        (int A, int B)[] sides = spec.CrossSection == StructuralTrussCrossSection.Box
            ? [(0, 1), (1, 2), (2, 3), (3, 0)]
            : [(0, 1), (1, 2), (2, 0)];

        for (int boundary = 0; boundary <= bayCount; boundary++)
        {
            float z = nodeStart + actualBayLength * boundary;
            foreach ((int a, int b) in sides)
                AddMember(Local(chordCentres[a], z), Local(chordCentres[b], z),
                    spec.BraceThickness, Vector3.UnitZ,
                    StructuralTrussMemberKind.Transverse);
        }

        for (int bay = 0; bay < bayCount; bay++)
        {
            float z0 = nodeStart + actualBayLength * bay;
            float z1 = z0 + actualBayLength;
            for (int face = 0; face < sides.Length; face++)
            {
                (int a, int b) = sides[face];
                bool reverse = ((bay + face) & 1) != 0;
                int start = reverse ? b : a;
                int end = reverse ? a : b;
                Vector2 faceCentre = (chordCentres[a] + chordCentres[b]) * .5f;
                Vector3 faceNormal = Vector3.Normalize(new Vector3(faceCentre.X, faceCentre.Y, 0f));
                AddMember(Local(chordCentres[start], z0), Local(chordCentres[end], z1),
                    spec.BraceThickness, faceNormal, StructuralTrussMemberKind.Brace);
            }
        }

        int transverseCount = (bayCount + 1) * sideCount;
        int braceCount = bayCount * sideCount;
        int memberCount = chordCount + transverseCount + braceCount;
        return new(
            bayCount,
            actualBayLength,
            chordCount,
            transverseCount,
            braceCount,
            memberCount,
            mesh.VertexCount - firstVertex,
            (mesh.IndexCount - firstIndex) / 3,
            -spec.Length * .5f,
            spec.Length * .5f);

        void AddMember(
            Vector3 localStart,
            Vector3 localEnd,
            float thickness,
            Vector3 localCrossHint,
            StructuralTrussMemberKind kind)
        {
            _ = kind;
            Vector3 start = Vector3.Transform(localStart, spec.Transform);
            Vector3 end = Vector3.Transform(localEnd, spec.Transform);
            Vector3 longAxis = Vector3.Normalize(end - start);
            Vector3 crossHint = Vector3.Normalize(Vector3.TransformNormal(
                localCrossHint, spec.Transform));
            Vector3 right = Vector3.Cross(crossHint, longAxis);
            if (right.LengthSquared() < 1e-6f)
            {
                crossHint = MathF.Abs(longAxis.Y) < .9f ? Vector3.UnitY : Vector3.UnitX;
                right = Vector3.Cross(crossHint, longAxis);
            }
            right.Normalize();
            Vector3 up = Vector3.Normalize(Vector3.Cross(longAxis, right));
            Vector3 centre = (start + end) * .5f;
            float length = Vector3.Distance(start, end);
            Matrix frame = new(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                longAxis.X, longAxis.Y, longAxis.Z, 0f,
                centre.X, centre.Y, centre.Z, 1f);
            mesh.AddOrientedBox(frame, new(thickness, thickness, length), colour);
        }
    }

    internal static Vector2[] ChordCentres(StructuralTrussSpec spec)
    {
        float halfWidth = spec.Width * .5f - spec.ChordThickness * .5f;
        float halfHeight = spec.Height * .5f - spec.ChordThickness * .5f;
        return spec.CrossSection == StructuralTrussCrossSection.Box
            ? [
                new(-halfWidth, -halfHeight),
                new(halfWidth, -halfHeight),
                new(halfWidth, halfHeight),
                new(-halfWidth, halfHeight),
            ]
            : [
                new(-halfWidth, -halfHeight),
                new(halfWidth, -halfHeight),
                new(0f, halfHeight),
            ];
    }

    private static Vector3 Local(Vector2 crossSection, float z)
        => new(crossSection.X, crossSection.Y, z);

    private static void Validate(StructuralTrussSpec spec)
    {
        if (!float.IsFinite(spec.Length) || spec.Length <= 0f)
            throw new ArgumentOutOfRangeException(nameof(spec), "Truss length must be finite and positive.");
        if (!float.IsFinite(spec.Width) || !float.IsFinite(spec.Height)
            || spec.Width <= 0f || spec.Height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(spec), "Truss cross-section must be finite and positive.");
        float minimumExtent = MathF.Min(spec.Width, spec.Height);
        if (!float.IsFinite(spec.ChordThickness) || spec.ChordThickness <= 0f
            || spec.ChordThickness >= minimumExtent * .45f)
            throw new ArgumentOutOfRangeException(nameof(spec), "Chord thickness is incompatible with the requested envelope.");
        if (!float.IsFinite(spec.TargetBayLength) || spec.TargetBayLength <= 0f)
            throw new ArgumentOutOfRangeException(nameof(spec), "Target bay length must be finite and positive.");
        if (!float.IsFinite(spec.BraceThickness) || spec.BraceThickness <= 0f
            || spec.BraceThickness > spec.ChordThickness
            || spec.BraceThickness >= spec.TargetBayLength * .5f)
            throw new ArgumentOutOfRangeException(nameof(spec), "Brace thickness is incompatible with chord/bay size.");
        Vector3 x = new(spec.Transform.M11, spec.Transform.M12, spec.Transform.M13);
        Vector3 y = new(spec.Transform.M21, spec.Transform.M22, spec.Transform.M23);
        Vector3 z = new(spec.Transform.M31, spec.Transform.M32, spec.Transform.M33);
        if (MathF.Abs(x.LengthSquared() - 1f) > .002f
            || MathF.Abs(y.LengthSquared() - 1f) > .002f
            || MathF.Abs(z.LengthSquared() - 1f) > .002f
            || MathF.Abs(Vector3.Dot(x, y)) > .002f
            || MathF.Abs(Vector3.Dot(x, z)) > .002f
            || MathF.Abs(Vector3.Dot(y, z)) > .002f
            || Vector3.Dot(Vector3.Cross(x, y), z) < .999f)
            throw new ArgumentException("Truss transform must contain a right-handed orthonormal frame.", nameof(spec));
        Debug.Assert(spec.Length > spec.BraceThickness);
    }
}
