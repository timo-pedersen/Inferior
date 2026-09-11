using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum BolonPentagonalUtilityFamily
{
    ReinforcementCollar,
    FiveLeafIris,
    ApparatusRosette,
}

public sealed record BolonPentagonalUtilityFixture(
    string Identity,
    int VesselIndex,
    int HostFaceIndex,
    BolonPentagonalUtilityFamily Family,
    Vector3 HostFaceCenter,
    Vector3 Centre,
    Vector3 Normal,
    Vector3 TangentU,
    Vector3 TangentV,
    float RotationRadians,
    float HostSafeRadius,
    float OuterRadius,
    float InnerRadius,
    float ReliefHeight,
    float RecessDepth,
    int IrisLeafCount,
    int RadialElementCount,
    bool HasOpticalAccent,
    Color StructuralColour,
    Color SecondaryColour,
    Color AccentColour,
    SystemMaterialFamilyId MaterialFamily);

public sealed record BolonPentagonalUtilityPlan(
    string StationIdentity,
    MegastationArchetype Archetype,
    IReadOnlyList<BolonPentagonalUtilityFixture> Fixtures,
    int EligiblePentagonCount,
    int BarePentagonCount,
    string Signature);

public static class BolonPentagonalUtilityPlanner
{
    public const int AlgorithmVersion = 1;
    private const int StructuralAlgorithmVersion = 2;

    public static BolonPentagonalUtilityPlan Plan(
        BolonMegastationPlan structuralPlan,
        CancellationToken cancellationToken = default)
    {
        int structuralRoot = MegastationSeed.Root(
            structuralPlan.StationIdentity, StructuralAlgorithmVersion);
        int selectionSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-utility-selection:v1");
        int familySeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-utility-family:v1");
        int scaleSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-utility-scale:v1");
        int irisSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-iris-leaves:v1");
        int rosetteSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-rosette:v1");
        int materialSeed = MegastationSeed.Derive(
            structuralRoot, "bolon-pentagonal-material-accent:v1");
        HashSet<(int Vessel, int Face)> attachedFaces = structuralPlan.Relationships
            .SelectMany(relationship => new[]
            {
                (relationship.A, relationship.FaceA),
                (relationship.B, relationship.FaceB),
            })
            .ToHashSet();
        var fixtures = new List<BolonPentagonalUtilityFixture>();
        int eligiblePentagons = 0;
        foreach (BolonVesselPlan vessel in structuralPlan.Vessels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] eligible = BolonMegastationGenerator.AttachmentFaces
                .Where(face => face.SideCount == 5
                    && !attachedFaces.Contains((vessel.Index, face.Index)))
                .Select(face => face.Index)
                .ToArray();
            eligiblePentagons += eligible.Length;
            BolonPentagonalUtilityFamily[] families = SelectFamilies(
                vessel, eligible.Length, familySeed);
            int[] hostFaces = eligible
                .OrderBy(face => MegastationSeed.Derive(
                    selectionSeed, $"vessel:{vessel.Index}:face:{face}"))
                .Take(families.Length)
                .ToArray();
            for (int index = 0; index < hostFaces.Length; index++)
            {
                fixtures.Add(PlanFixture(
                    structuralPlan,
                    vessel,
                    hostFaces[index],
                    index,
                    families[index],
                    scaleSeed,
                    irisSeed,
                    rosetteSeed,
                    materialSeed));
            }
        }
        return new(
            structuralPlan.StationIdentity,
            structuralPlan.Archetype,
            fixtures,
            eligiblePentagons,
            eligiblePentagons - fixtures.Count,
            Signature(structuralPlan.StationIdentity, fixtures));
    }

    private static BolonPentagonalUtilityFamily[] SelectFamilies(
        BolonVesselPlan vessel,
        int maximumCount,
        int familySeed)
    {
        var countRng = new Random(MegastationSeed.Derive(
            familySeed, $"vessel:{vessel.Index}:counts"));
        int collarCount = countRng.Next(1, 4);
        int irisCount = countRng.NextDouble() < .28
            ? 0
            : countRng.NextDouble() < .79 ? 1 : 2;
        int rosetteCount = countRng.NextDouble() < .42
            ? 0
            : countRng.NextDouble() < .82 ? 1 : 2;
        if (vessel.ScaleClass == BolonVesselScaleClass.Anchor
            && irisCount + rosetteCount == 0)
            irisCount = 1;
        var families = Enumerable.Repeat(
                BolonPentagonalUtilityFamily.ReinforcementCollar, collarCount)
            .Concat(Enumerable.Repeat(
                BolonPentagonalUtilityFamily.FiveLeafIris, irisCount))
            .Concat(Enumerable.Repeat(
                BolonPentagonalUtilityFamily.ApparatusRosette, rosetteCount))
            .Select((family, ordinal) => (family, ordinal))
            .OrderBy(item => MegastationSeed.Derive(
                familySeed,
                $"vessel:{vessel.Index}:family:{item.family}:ordinal:{item.ordinal}"))
            .Select(item => item.family)
            .Take(maximumCount)
            .ToArray();
        return families;
    }

    private static BolonPentagonalUtilityFixture PlanFixture(
        BolonMegastationPlan plan,
        BolonVesselPlan vessel,
        int hostFaceIndex,
        int fixtureIndex,
        BolonPentagonalUtilityFamily family,
        int scaleSeed,
        int irisSeed,
        int rosetteSeed,
        int materialSeed)
    {
        string identity =
            $"vessel:{vessel.Index}/pentagon:{hostFaceIndex}/utility:{fixtureIndex}:{family}";
        var scaleRng = new Random(MegastationSeed.Derive(scaleSeed, identity));
        BolonAttachmentFace face = BolonMegastationGenerator.GetAttachmentFace(hostFaceIndex);
        IReadOnlyList<Vector3> faceVertices =
            BolonMegastationGenerator.GetAttachmentFaceVertices(hostFaceIndex);
        Vector3 normal = Vector3.Normalize(Vector3.Transform(
            face.LocalNormal, vessel.Orientation));
        Vector3 localTangent = Vector3.Normalize(faceVertices[0] - face.LocalCenter);
        Vector3 tangentU = Vector3.Normalize(Vector3.Transform(
            localTangent, vessel.Orientation));
        Vector3 tangentV = Vector3.Normalize(Vector3.Cross(normal, tangentU));
        float rotation = Lerp(0f, MathF.Tau / 5f, scaleRng.NextDouble());
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector3 fixtureU = tangentU * cos + tangentV * sin;
        Vector3 fixtureV = -tangentU * sin + tangentV * cos;
        Vector3 faceCenter = vessel.Position + Vector3.Transform(
            face.LocalCenter * vessel.Radius, vessel.Orientation);
        float safeRadius = face.LocalInscribedRadius * vessel.Radius * .76f;
        (float minimumScale, float maximumScale) = family switch
        {
            BolonPentagonalUtilityFamily.ReinforcementCollar => (.52f, .72f),
            BolonPentagonalUtilityFamily.FiveLeafIris => (.55f, .75f),
            _ => (.34f, .52f),
        };
        float outerRadius = safeRadius * Lerp(
            minimumScale, maximumScale, scaleRng.NextDouble());
        float innerRadius = outerRadius * (family switch
        {
            BolonPentagonalUtilityFamily.ReinforcementCollar
                => Lerp(.54f, .64f, scaleRng.NextDouble()),
            BolonPentagonalUtilityFamily.FiveLeafIris
                => Lerp(.72f, .79f, scaleRng.NextDouble()),
            _ => Lerp(.28f, .38f, scaleRng.NextDouble()),
        });
        float relief = family switch
        {
            BolonPentagonalUtilityFamily.ReinforcementCollar
                => Lerp(.45f, 1.15f, scaleRng.NextDouble()),
            BolonPentagonalUtilityFamily.FiveLeafIris
                => Lerp(.30f, .70f, scaleRng.NextDouble()),
            _ => Lerp(.40f, 1.25f, scaleRng.NextDouble()),
        };
        float recessDepth = family == BolonPentagonalUtilityFamily.FiveLeafIris
            ? Lerp(5.5f, 10.5f, new Random(MegastationSeed.Derive(
                irisSeed, identity + ":depth")).NextDouble())
            : 0f;
        var materialRng = new Random(MegastationSeed.Derive(materialSeed, identity));
        bool newer = materialRng.NextDouble() < .34;
        SystemMaterialFamilyId material = newer
            ? SystemMaterialFamilyId.PolishedMetal
            : SystemMaterialFamilyId.AgedMetal;
        Color structural = plan.Archetype == MegastationArchetype.RedBolon
            ? new Color((int)Lerp(151f, 186f, materialRng.NextDouble()), 72, 38)
            : new Color((int)Lerp(174f, 211f, materialRng.NextDouble()), 127, 42);
        Color secondary = plan.Archetype == MegastationArchetype.RedBolon
            ? new Color(92, 39, 27)
            : new Color(112, 72, 30);
        bool hasAccent = family == BolonPentagonalUtilityFamily.ApparatusRosette
            && new Random(MegastationSeed.Derive(
                rosetteSeed, identity + ":accent-enabled")).NextDouble() < .28;
        Color accent = AccentColour(
            plan.Archetype,
            new Random(MegastationSeed.Derive(materialSeed, identity + ":accent")));
        return new(
            identity,
            vessel.Index,
            hostFaceIndex,
            family,
            faceCenter,
            faceCenter,
            normal,
            fixtureU,
            fixtureV,
            rotation,
            safeRadius,
            outerRadius,
            innerRadius,
            relief,
            recessDepth,
            family == BolonPentagonalUtilityFamily.FiveLeafIris ? 5 : 0,
            5,
            hasAccent,
            structural,
            secondary,
            accent,
            material);
    }

    private static Color AccentColour(MegastationArchetype archetype, Random rng)
    {
        double roll = rng.NextDouble();
        if (roll < .16)
            return new Color(47, 74, 87);
        if (roll < .42)
            return new Color(88, 31, 104);
        return archetype == MegastationArchetype.RedBolon
            ? new Color(124, 19, 15)
            : new Color(108, 9, 19);
    }

    private static string Signature(
        string stationIdentity,
        IReadOnlyList<BolonPentagonalUtilityFixture> fixtures)
    {
        var text = new StringBuilder("bolon-pentagonal-utilities:v1|")
            .Append(stationIdentity);
        foreach (BolonPentagonalUtilityFixture fixture in fixtures)
        {
            text.Append('|').Append(fixture.Identity).Append(':')
                .Append(fixture.VesselIndex).Append('.')
                .Append(fixture.HostFaceIndex).Append(':')
                .Append(fixture.Family).Append(':')
                .Append(V(fixture.Centre)).Append(':')
                .Append(V(fixture.Normal)).Append(':')
                .Append(V(fixture.TangentU)).Append(':')
                .Append(V(fixture.TangentV)).Append(':')
                .Append(F(fixture.RotationRadians)).Append(':')
                .Append(F(fixture.HostSafeRadius)).Append(':')
                .Append(F(fixture.OuterRadius)).Append(':')
                .Append(F(fixture.InnerRadius)).Append(':')
                .Append(F(fixture.ReliefHeight)).Append(':')
                .Append(F(fixture.RecessDepth)).Append(':')
                .Append(fixture.IrisLeafCount).Append(':')
                .Append(fixture.RadialElementCount).Append(':')
                .Append(fixture.HasOpticalAccent).Append(':')
                .Append(fixture.StructuralColour.PackedValue.ToString(
                    "X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(fixture.SecondaryColour.PackedValue.ToString(
                    "X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(fixture.AccentColour.PackedValue.ToString(
                    "X8", CultureInfo.InvariantCulture)).Append(':')
                .Append(fixture.MaterialFamily);
        }
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string F(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string V(Vector3 value)
        => $"{F(value.X)},{F(value.Y)},{F(value.Z)}";

    private static float Lerp(float minimum, float maximum, double amount)
        => minimum + (maximum - minimum) * (float)amount;
}
