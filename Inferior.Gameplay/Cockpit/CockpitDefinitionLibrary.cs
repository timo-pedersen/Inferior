using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public static class CockpitDefinitionLibrary
{
    public const string AriesCivilianCanopyId = "aries-civilian-canopy-cockpit";
    public const string AsteriskStarboardCockpitId = "asterisk-starboard-cockpit";
    public const string BerenUnderslungCockpitId = "beren-underslung-cockpit";
    public const string AntegaCivilianBridgeId = "antega-civilian-bridge";

    private static readonly Dictionary<string, CockpitModuleDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase);

    static CockpitDefinitionLibrary()
    {
        Register(new CockpitModuleDefinition
        {
            DefinitionId = AriesCivilianCanopyId,
            DisplayName = "Aries Civilian Canopy Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = new DVec3(0.0, -0.55, 0.25),
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = DVec3.Zero,
            CameraLocalOrientation = Quaternion.Identity,
            CanopyLocalPosition = new DVec3(0.0, 0.35, 0.0),
            CanopyLocalOrientation = Quaternion.Identity,
            PreferredFacing = MountFacing.Up,
            HasCanopyLights = true,
            HasCockpitLights = true,
            VisualGeometry = AriesCivilianCockpitGeometryFactory.Create(),
        });
        Register(new CockpitModuleDefinition
        {
            DefinitionId = AsteriskStarboardCockpitId,
            DisplayName = "Asterisk Starboard Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = new DVec3(0.0, 0.08, 0.05),
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = new DVec3(0.0, 0.42, -0.60),
            CameraLocalOrientation = CreateAsteriskCameraOrientation(),
            CanopyLocalPosition = new DVec3(0.0, 0.45, -0.20),
            CanopyLocalOrientation = Quaternion.Identity,
            PreferredFacing = MountFacing.Starboard,
            HasCanopyLights = true,
            HasCockpitLights = true,
            VisualGeometry = AsteriskStarboardCockpitGeometryFactory.Create(),
        });
        Register(new CockpitModuleDefinition
        {
            DefinitionId = BerenUnderslungCockpitId,
            DisplayName = "Beren Underslung Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = new DVec3(0.0, 1.05, -0.10),
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = new DVec3(0.0, 1.55, -0.60),
            CameraLocalOrientation = CreateBerenCameraOrientation(),
            CanopyLocalPosition = new DVec3(0.0, 1.45, -0.55),
            CanopyLocalOrientation = Quaternion.Identity,
            PreferredFacing = MountFacing.Down,
            HasCanopyLights = true,
            HasCockpitLights = true,
            VisualGeometry = BerenUnderslungCockpitGeometryFactory.Create(),
        });
        Register(new CockpitModuleDefinition
        {
            DefinitionId = AntegaCivilianBridgeId,
            DisplayName = "Antega Civilian Bridge",
            RequiredMountClass = CockpitMountClass.C5,
            PilotLocalPosition = new DVec3(0.0, 2.55, -3.20),
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = new DVec3(0.0, 3.15, -3.95),
            CameraLocalOrientation = CreateForwardPitchOrientation(5.0f),
            CanopyLocalPosition = new DVec3(0.0, 3.30, -3.65),
            CanopyLocalOrientation = Quaternion.Identity,
            PreferredFacing = MountFacing.Up,
            HasCanopyLights = true,
            HasCockpitLights = true,
            VisualGeometry = AntegaCivilianBridgeGeometryFactory.Create(),
        });
    }

    public static CockpitModuleDefinition Get(string definitionId)
    {
        if (Definitions.TryGetValue(definitionId, out CockpitModuleDefinition? definition))
            return definition;

        throw new KeyNotFoundException($"No cockpit definition found for '{definitionId}'.");
    }

    public static bool TryGet(string definitionId, out CockpitModuleDefinition? definition)
        => Definitions.TryGetValue(definitionId, out definition);

    public static IReadOnlyCollection<CockpitModuleDefinition> All => Definitions.Values;

    private static void Register(CockpitModuleDefinition definition)
    {
        if (!Definitions.TryAdd(definition.DefinitionId, definition))
        {
            throw new InvalidOperationException(
                $"Duplicate cockpit definition '{definition.DefinitionId}'.");
        }
    }

    private static Quaternion CreateAsteriskCameraOrientation()
    {
        float angle = MathHelper.ToRadians(20.0f);
        var forward = new Vector3(0.0f, MathF.Sin(angle), -MathF.Cos(angle));
        return CreateLookOrientation(forward, -Vector3.UnitX);
    }

    private static Quaternion CreateBerenCameraOrientation()
    {
        float angle = MathHelper.ToRadians(10.0f);
        var forward = new Vector3(0.0f, MathF.Sin(angle), -MathF.Cos(angle));
        return CreateLookOrientation(forward, -Vector3.UnitY);
    }

    private static Quaternion CreateForwardPitchOrientation(float downDegrees)
    {
        float angle = MathHelper.ToRadians(downDegrees);
        var forward = new Vector3(0.0f, -MathF.Sin(angle), -MathF.Cos(angle));
        return CreateLookOrientation(forward, Vector3.UnitY);
    }

    private static Quaternion CreateLookOrientation(Vector3 forward, Vector3 up)
    {
        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, up));
        up = Vector3.Normalize(Vector3.Cross(right, forward));
        var basis = new Matrix(
            right.X, right.Y, right.Z, 0.0f,
            up.X, up.Y, up.Z, 0.0f,
            -forward.X, -forward.Y, -forward.Z, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
        return Quaternion.CreateFromRotationMatrix(basis);
    }
}
