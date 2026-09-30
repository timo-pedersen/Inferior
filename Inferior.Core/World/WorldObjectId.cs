using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inferior.Core.World;

/// <summary>
/// Stable simulation identity for an independently existing object in the universe.
/// Renderer and physics-engine handles are deliberately separate identities.
/// </summary>
public readonly record struct WorldObjectId
{
    public Guid Value { get; }

    public WorldObjectId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("World-object identity cannot be empty.", nameof(value));

        Value = value;
    }

    public static WorldObjectId New()
        => new(Guid.NewGuid());

    /// <summary>
    /// Derives a process-independent identity from stable semantic input. This is suitable
    /// for deterministically regenerated objects; persisted exceptional objects may instead
    /// retain an identity created by <see cref="New"/>.
    /// </summary>
    public static WorldObjectId CreateDeterministic(string scope, string stableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableName);

        string input = $"{scope.Length}:{scope}{stableName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new WorldObjectId(new Guid(hash.AsSpan(0, 16)));
    }

    // Do not inherit any runtime-dependent string hashing through the semantic inputs.
    // The stored Guid bytes alone define a stable, cheap hash.
    public override int GetHashCode()
    {
        Span<byte> bytes = stackalloc byte[16];
        Value.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
             ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
             ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
             ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }

    public override string ToString() => Value.ToString("D");
}
