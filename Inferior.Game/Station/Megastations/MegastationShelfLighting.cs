using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationShelfBeaconMount
{
    Top,
    Underside,
}

public sealed record MegastationShelfObstacleBeacon(
    string Identity,
    string InstallationIdentity,
    string ShelfIdentity,
    Vector3 Position,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward,
    MegastationShelfBeaconMount Mount,
    Color Colour,
    float Rate,
    float Phase);

public sealed record MegastationShelfFloodFixture(
    string Identity,
    string InstallationIdentity,
    string ShelfIdentity,
    string? TrussIdentity,
    Vector3 MountPoint,
    Vector3 Centre,
    Vector3 Right,
    Vector3 Up,
    Vector3 Direction,
    Color Colour,
    float Intensity,
    float Range,
    float AngularCutoffCosine);

public sealed record MegastationShelfLightingPlan(
    int AlgorithmVersion,
    int Seed,
    IReadOnlyList<MegastationShelfObstacleBeacon> Beacons,
    IReadOnlyList<MegastationShelfFloodFixture> FloodFixtures,
    IReadOnlyList<MegastationArtificialLight> ArtificialLights,
    string Signature);

/// <summary>
/// Plans shelf-owned safety and work lighting. Obstacle beacons are presentation-only;
/// flood fixtures are the sole entries contributed to the static H1c light bake.
/// </summary>
public static class MegastationShelfLightingPlanner
{
    public const int AlgorithmVersion = 3;
    public const float WorkLightIntensity = .665f;
    public static readonly Color BeaconColour = new(238, 34, 26);
    public static readonly Color BeaconGlassOffColour = new(76, 8, 6);

    public static MegastationShelfLightingPlan Plan(
        MegastationInteriorPlan interior,
        MegastationMegaShelfPlan shelves)
        => Plan(interior.Seed, shelves);

    internal static MegastationShelfLightingPlan Plan(
        int interiorSeed,
        MegastationMegaShelfPlan shelves)
    {
        int seed = MegastationSeed.Derive(interiorSeed, "mega-shelf-lighting:v1");
        var beacons = new List<MegastationShelfObstacleBeacon>();
        var floods = new List<MegastationShelfFloodFixture>();
        Dictionary<string, string> installationByShelf = shelves.Bookcases
            .SelectMany(bookcase => bookcase.ShelfIdentities.Select(id => (id, bookcase.Identity)))
            .ToDictionary(pair => pair.id, pair => pair.Identity, StringComparer.Ordinal);

        foreach (MegastationMegaShelf shelf in shelves.Shelves
                     .OrderBy(shelf => shelf.Identity, StringComparer.Ordinal))
        {
            string installation = installationByShelf.GetValueOrDefault(
                shelf.Identity, shelf.Identity);
            int phaseSeed = MegastationSeed.Derive(seed, $"beacon-phase:{installation}");
            float phase = Unit(phaseSeed, "phase");
            const float rate = .5f;
            Vector3[] corners = ExposedCorners(shelf);
            for (int i = 0; i < corners.Length; i++)
            {
                AddBeacon(MegastationShelfBeaconMount.Top,
                    corners[i] + shelf.Body.Up * .34f,
                    shelf.Body.Up, shelf.Body.Forward);
                AddBeacon(MegastationShelfBeaconMount.Underside,
                    corners[i] - shelf.Body.Up * (shelf.Body.Size.Y + .34f),
                    -shelf.Body.Up, -shelf.Body.Forward);

                void AddBeacon(
                    MegastationShelfBeaconMount mount,
                    Vector3 position,
                    Vector3 outward,
                    Vector3 forward)
                    => beacons.Add(new(
                        $"{shelf.Identity}/obstacle-beacon:{i}:{mount.ToString().ToLowerInvariant()}",
                        installation, shelf.Identity, position, shelf.Body.Right,
                        outward, forward, mount, BeaconColour, rate, phase));
            }

            AddFloods(shelf, installation, seed, floods);
        }

        MegastationArtificialLight[] artificial = floods.Select(flood => new
            MegastationArtificialLight(
                flood.Identity + "/static-light", flood.Centre + flood.Direction * .28f,
                flood.Colour, flood.Intensity, flood.Range,
                flood.Direction, flood.AngularCutoffCosine)).ToArray();
        return new(AlgorithmVersion, seed, beacons, floods, artificial,
            BuildSignature(seed, beacons, floods));
    }

    internal static Vector3[] ExposedCorners(MegastationMegaShelf shelf)
    {
        MegastationInteriorStructuralSolid body = shelf.Body;
        float x = body.Size.X * .5f;
        float y = body.Size.Y * .5f;
        float z = body.Size.Z * .5f;
        Vector3 P(float right, float forward) => body.Centre
            + body.Right * right + body.Up * y + body.Forward * forward;

        return shelf.Family switch
        {
            // Both terminal ends of a full-span bridge are wall-supported roots.
            MegastationMegaShelfFamily.FullSpan => [],
            MegastationMegaShelfFamily.Corner when body.ExposedCornerClip > 0f
                && body.ExposedCornerRightSign != 0 =>
            [
                P(body.ExposedCornerRightSign * (x - body.ExposedCornerClip), z),
                P(body.ExposedCornerRightSign * x, z - body.ExposedCornerClip),
            ],
            MegastationMegaShelfFamily.Corner =>
                [P(body.ExposedCornerRightSign == 0 ? x : body.ExposedCornerRightSign * x, z)],
            _ => [P(-x, z), P(x, z)],
        };
    }

    private static void AddFloods(
        MegastationMegaShelf shelf,
        string installationIdentity,
        int rootSeed,
        List<MegastationShelfFloodFixture> result)
    {
        int seed = MegastationSeed.Derive(rootSeed, $"floods:{shelf.Identity}");
        float scale = MathF.Max(shelf.Body.Size.X, shelf.Body.Size.Z);
        int nominal = scale < 180f ? 1 : scale < 420f ? 2 : scale < 760f ? 3 : 4;
        int count = Math.Clamp(nominal + (Unit(seed, "count") > .72f ? 1 : 0), 1, 6);
        MegastationMegaShelfTruss[] preferred = PreferredTrusses(shelf);

        for (int index = 0; index < count; index++)
        {
            int child = MegastationSeed.Derive(seed, $"fixture:{index}");
            MegastationMegaShelfTruss? truss = preferred.Length == 0
                ? null : preferred[index % preferred.Length];
            Vector3 mount;
            Vector3 tangent;
            Vector3 shelfDown = -shelf.Body.Up;
            if (truss is not null)
            {
                StructuralTrussSpec spec = truss.Spec;
                Vector2[] chords = StructuralTrussFactory.ChordCentres(spec);
                Vector2 chord = chords[index % Math.Min(2, chords.Length)];
                float fraction = (index + 1f) / (count + 1f);
                fraction = MathHelper.Clamp(fraction + (Unit(child, "along") - .5f) * .12f,
                    .12f, .88f);
                Vector3 local = new(chord.X, chord.Y,
                    MathHelper.Lerp(-spec.Length * .5f, spec.Length * .5f, fraction));
                mount = Vector3.Transform(local, spec.Transform);
                tangent = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, spec.Transform));
            }
            else
            {
                float fraction = (index + 1f) / (count + 1f);
                mount = shelf.Body.Centre - shelf.Body.Up * shelf.Body.Size.Y * .5f
                    + shelf.Body.Forward * (shelf.Body.Size.Z * .42f)
                    + shelf.Body.Right * MathHelper.Lerp(-shelf.Body.Size.X * .35f,
                        shelf.Body.Size.X * .35f, fraction);
                tangent = shelf.Body.Right;
            }

            float angle = MathHelper.ToRadians(MathHelper.Lerp(0f, 20f, Unit(child, "cant")));
            float sign = Unit(child, "cant-side") < .5f ? -1f : 1f;
            Vector3 direction = Vector3.Normalize(
                shelfDown * MathF.Cos(angle) + tangent * sign * MathF.Sin(angle));
            Vector3 right = tangent - direction * Vector3.Dot(tangent, direction);
            if (right.LengthSquared() < 1e-5f) right = shelf.Body.Forward;
            right.Normalize();
            Vector3 up = Vector3.Normalize(Vector3.Cross(direction, right));
            Vector3 centre = mount + shelfDown * .62f;
            Color colour = WorkLightColour(rootSeed, installationIdentity, child);
            result.Add(new(
                $"{shelf.Identity}/work-flood:{index}", installationIdentity, shelf.Identity,
                truss?.Identity, mount, centre, right, up, direction, colour,
                WorkLightIntensity,
                MathHelper.Lerp(105f, 175f, Unit(child, "range")),
                MathF.Cos(MathHelper.ToRadians(MathHelper.Lerp(58f, 68f,
                    Unit(child, "spread"))))));
        }
    }

    internal static Color WorkLightColour(
        int rootSeed,
        string installationIdentity,
        int fixtureSeed)
    {
        int installationSeed = MegastationSeed.Derive(
            rootSeed, $"flood-white-point:{installationIdentity}");
        float family = Unit(installationSeed, "family");
        Color basis = family switch
        {
            < .64f => new Color(247, 248, 247), // neutral majority
            < .84f => new Color(250, 248, 244), // slightly warm
            < .97f => new Color(244, 248, 251), // slightly cool
            _ => new Color(250, 246, 248),      // rare faint rosy white
        };
        float a = Unit(fixtureSeed, "white-point-a") * 2f - 1f;
        float b = Unit(fixtureSeed, "white-point-b") * 2f - 1f;
        return new Color(
            ClampByte(basis.R + a * 1.8f),
            ClampByte(basis.G + b * .8f),
            ClampByte(basis.B - a * 1.5f));

        static byte ClampByte(float value) =>
            (byte)MathF.Round(MathHelper.Clamp(value, 0f, 255f));
    }

    private static MegastationMegaShelfTruss[] PreferredTrusses(MegastationMegaShelf shelf)
    {
        IEnumerable<MegastationMegaShelfTruss> query = shelf.Trusses;
        if (shelf.Family is MegastationMegaShelfFamily.Bookcase)
            query = query.Where(truss => truss.Identity.EndsWith("truss:0", StringComparison.Ordinal));
        else if (shelf.Family is MegastationMegaShelfFamily.Cantilever
                 && query.Any(truss => truss.Identity.EndsWith("truss:2", StringComparison.Ordinal)))
            query = query.Where(truss => truss.Identity.EndsWith("truss:2", StringComparison.Ordinal));
        return query.OrderBy(truss => truss.Identity, StringComparer.Ordinal).ToArray();
    }

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static string BuildSignature(
        int seed,
        IEnumerable<MegastationShelfObstacleBeacon> beacons,
        IEnumerable<MegastationShelfFloodFixture> floods)
    {
        var text = new StringBuilder().Append(AlgorithmVersion).Append('|').Append(seed);
        foreach (var beacon in beacons)
            text.Append('|').Append(beacon.Identity).Append(':')
                .Append(beacon.Position.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(beacon.Position.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(beacon.Position.Z.ToString("R", CultureInfo.InvariantCulture)).Append('@')
                .Append(beacon.Phase.ToString("R", CultureInfo.InvariantCulture));
        foreach (var flood in floods)
            text.Append('|').Append(flood.Identity).Append(':')
                .Append(flood.Centre.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(flood.Centre.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(flood.Centre.Z.ToString("R", CultureInfo.InvariantCulture)).Append('@')
                .Append(flood.Colour.PackedValue.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
