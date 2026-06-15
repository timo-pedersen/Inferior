using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum PortSize { Small, Medium, Large }

public sealed class StationPort
{
    public required string   Id                { get; init; }
    public required Vector3  LocalPosition     { get; init; }
    public required Vector3  OutwardNormal     { get; init; }
    public          PortSize Size              { get; init; } = PortSize.Medium;
    public          string[] AcceptsCategories { get; init; } = [];
    public          bool     IsDocking         { get; init; }
    public          bool     IsTerminal        { get; init; }
}
