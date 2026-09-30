using Inferior.Core.World;
using Microsoft.Xna.Framework;

namespace Inferior.Game.Containers;

/// <summary>
/// Immutable container-specific domain data. Position and motion live in the associated
/// WorldObject; generated mesh data lives in ShippingContainerGeometry.
/// </summary>
public sealed record ShippingContainer
{
    public required WorldObjectId Id             { get; init; }
    public string              Name              { get; init; } = "Shipping Container";
    public Color               PrimaryColor     { get; init; }
    public float               Wear             { get; init; }   // 0.0–1.0
    public int                 SidePatternSeed  { get; init; }
    public string              ManufacturerText { get; init; } = "";
    public ContainerContents?  Contents         { get; init; }
    public LockGrade           Lock             { get; init; }
    public bool                IsLocked         { get; init; }
}

public sealed record ContainerContents(CommodityType Type, int Units);

public enum LockGrade { None, Civilian, Military, Vault }
