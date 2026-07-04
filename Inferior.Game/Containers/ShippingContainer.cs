using Inferior.Core.Math;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.Containers;

public sealed class ShippingContainer
{
    public string              Id               { get; init; } = "";
    public Color               PrimaryColor     { get; init; }
    public float               Wear             { get; init; }   // 0.0–1.0
    public int                 SidePatternSeed  { get; init; }
    public string              ManufacturerText { get; init; } = "";
    public ContainerContents?  Contents         { get; init; }
    public LockGrade           Lock             { get; init; }
    public bool                IsLocked         { get; init; }

    // World state — mutable
    public DVec3               WorldPosition    { get; set; }
    public Quaternion          Orientation      { get; set; }
    public object?             Parent           { get; set; }   // null = free-floating

    // Mesh — lit dynamically at draw time (rotates, so a bake would go stale)
    public required VertexPositionNormalColorTexture[] Vertices { get; init; }
    public required short[]                            Indices  { get; init; }
}

public sealed record ContainerContents(CommodityType Type, int Units);

public enum LockGrade { None, Civilian, Military, Vault }
