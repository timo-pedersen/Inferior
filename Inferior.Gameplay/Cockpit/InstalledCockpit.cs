using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public sealed class InstalledCockpit
{
    public required string MountId { get; init; }
    public required string DefinitionId { get; init; }
    public required CockpitRotationStep InstallationRotation { get; init; }

    public bool CanopyLightsOn { get; set; }
    public bool CockpitLightsOn { get; set; }

    public DVec3 ResolveShipLocalCameraPosition(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        ValidateInstallation(mount, definition);
        Quaternion orientation = mount.ShipLocalOrientation * InstallationQuaternion;
        Vector3 offset = Vector3.Transform(definition.CameraLocalPosition.ToVector3(), orientation);
        return mount.ShipLocalPosition + new DVec3(offset.X, offset.Y, offset.Z);
    }

    public Quaternion ResolveShipLocalCameraOrientation(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        ValidateInstallation(mount, definition);
        return Quaternion.Normalize(
            mount.ShipLocalOrientation
            * InstallationQuaternion
            * definition.CameraLocalOrientation);
    }

    public DVec3 ResolveShipLocalRootPosition(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        ValidateInstallation(mount, definition);
        return mount.ShipLocalPosition;
    }

    public Quaternion ResolveShipLocalRootOrientation(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        ValidateInstallation(mount, definition);
        return Quaternion.Normalize(mount.ShipLocalOrientation * InstallationQuaternion);
    }

    public bool ApplyCommand(ComponentCommand command, CockpitModuleDefinition definition)
    {
        switch (command.Topic)
        {
            case CockpitCommandTopics.CanopyLightsToggle when definition.HasCanopyLights:
                CanopyLightsOn = !CanopyLightsOn;
                return true;
            case CockpitCommandTopics.CanopyLightsSet when definition.HasCanopyLights:
                CanopyLightsOn = command.Value >= 0.5;
                return true;
            case CockpitCommandTopics.InternalLightsToggle when definition.HasCockpitLights:
                CockpitLightsOn = !CockpitLightsOn;
                return true;
            case CockpitCommandTopics.InternalLightsSet when definition.HasCockpitLights:
                CockpitLightsOn = command.Value >= 0.5;
                return true;
            default:
                return false;
        }
    }

    public void ValidateInstallation(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        if (!string.Equals(MountId, mount.MountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cockpit installation targets mount '{MountId}', not '{mount.MountId}'.");
        }

        if (!string.Equals(DefinitionId, definition.DefinitionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cockpit installation selects definition '{DefinitionId}', not '{definition.DefinitionId}'.");
        }

        if (definition.RequiredMountClass != mount.MountClass)
        {
            throw new InvalidOperationException(
                $"Cockpit '{definition.DefinitionId}' requires mount class " +
                $"{definition.RequiredMountClass}, but mount '{mount.MountId}' is {mount.MountClass}.");
        }

        if (!mount.AllowedRotations.Contains(InstallationRotation))
        {
            throw new InvalidOperationException(
                $"Cockpit rotation {InstallationRotation} is not allowed by mount '{mount.MountId}'.");
        }
    }

    private Quaternion InstallationQuaternion => Quaternion.CreateFromAxisAngle(
        Vector3.UnitY,
        InstallationRotation switch
        {
            CockpitRotationStep.Deg0 => 0.0f,
            CockpitRotationStep.Deg90 => MathHelper.PiOver2,
            CockpitRotationStep.Deg180 => MathHelper.Pi,
            CockpitRotationStep.Deg270 => MathHelper.Pi + MathHelper.PiOver2,
            _ => throw new ArgumentOutOfRangeException(nameof(InstallationRotation)),
        });
}
