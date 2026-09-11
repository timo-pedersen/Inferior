using System.Text;

namespace Inferior.Game.StationGen.Megastations;

internal static class MegastationSeed
{
    public static int Root(string persistenceId, int generatorVersion)
        => Derive(0x4D454741, $"v{generatorVersion}:{persistenceId}");

    public static int Derive(int parent, string semanticKey)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(semanticKey))
            {
                h ^= b;
                h *= 16777619u;
            }

            uint p = (uint)parent;
            h ^= p + 0x9e3779b9u + (h << 6) + (h >> 2);
            return (int)h;
        }
    }
}
