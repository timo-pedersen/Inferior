using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationMassingSignature(
    string Complete,
    string Body,
    string SliceGrid,
    string PositiveYDepthMap);

public static class MegastationMassingSignatureBuilder
{
    private const int FormatVersion = 1;

    public static MegastationMassingSignature Compute(MegastationPrototypeCpuResult result)
    {
        string body = Hash(WriteBody(result));
        string complete = Hash(WriteComplete(result));
        string grid = Hash(WriteSliceGrid(result.Grid));
        string positiveY = Hash(WritePositiveYDepthMap(result));
        return new MegastationMassingSignature(complete, body, grid, positiveY);
    }

    private static byte[] WriteComplete(MegastationPrototypeCpuResult result)
    {
        var writer = new CanonicalWriter();
        writer.WriteString("Inferior.Megastation.CompleteMassingSignature");
        writer.WriteInt32(FormatVersion);
        writer.WriteInt32(result.Diagnostics.GeneratorVersion);
        writer.WriteBytes(WriteBody(result));
        return writer.ToArray();
    }

    private static byte[] WriteBody(MegastationPrototypeCpuResult result)
    {
        var writer = new CanonicalWriter();
        writer.WriteString("Inferior.Megastation.MassingBodySignature");
        writer.WriteInt32(FormatVersion);
        writer.WriteInt32(result.Diagnostics.SeedCompatibilityVersion);
        writer.WriteInt32(result.Diagnostics.RootSeed);
        WriteAlgorithmVersions(writer, result.Diagnostics);
        WriteSliceGrid(writer, result.Grid);
        WriteStyle(writer, result.Style);
        WriteCells(writer, result.Occupancy);
        WriteFacePlans(writer, result.Faces);
        WriteEdgePlans(writer, result.Edges);
        WriteCornerPlans(writer, result.Corners);
        return writer.ToArray();
    }

    private static byte[] WriteSliceGrid(SliceGrid grid)
    {
        var writer = new CanonicalWriter();
        writer.WriteString("Inferior.Megastation.SliceGridSignature");
        writer.WriteInt32(FormatVersion);
        WriteSliceGrid(writer, grid);
        return writer.ToArray();
    }

    private static byte[] WritePositiveYDepthMap(MegastationPrototypeCpuResult result)
    {
        UrbanGrowthResult face = result.Faces.Single(f => f.Patch.Direction == GridDirection.PositiveY);
        var writer = new CanonicalWriter();
        writer.WriteString("Inferior.Megastation.PositiveYDepthMapSignature");
        writer.WriteInt32(FormatVersion);
        writer.WriteInt32(result.Diagnostics.SeedCompatibilityVersion);
        writer.WriteInt32(result.Diagnostics.RootSeed);
        WriteDepthMap(writer, face);
        return writer.ToArray();
    }

    private static void WriteAlgorithmVersions(CanonicalWriter writer, MegastationPrototypeDiagnostics diagnostics)
    {
        writer.WriteInt32(diagnostics.PositiveYUrbanSeedVersion);
        writer.WriteInt32(diagnostics.FaceUrbanAlgorithmVersion);
        writer.WriteInt32(diagnostics.EdgeAlgorithmVersion);
        writer.WriteInt32(diagnostics.CornerAlgorithmVersion);
    }

    private static void WriteSliceGrid(CanonicalWriter writer, SliceGrid grid)
    {
        writer.WriteInt32(grid.XCount);
        writer.WriteInt32(grid.YCount);
        writer.WriteInt32(grid.ZCount);
        WriteRange(writer, grid.CoreX);
        WriteRange(writer, grid.CoreY);
        WriteRange(writer, grid.CoreZ);
        WriteAxis(writer, grid, GridAxis.X);
        WriteAxis(writer, grid, GridAxis.Y);
        WriteAxis(writer, grid, GridAxis.Z);
    }

    private static void WriteRange(CanonicalWriter writer, Range range)
    {
        writer.WriteInt32(range.Start.Value);
        writer.WriteInt32(range.End.Value);
    }

    private static void WriteAxis(CanonicalWriter writer, SliceGrid grid, GridAxis axis)
    {
        writer.WriteInt32(grid.Count(axis));
        for (int i = 0; i < grid.Count(axis); i++)
            writer.WriteSingle(grid.GetCellSize(axis, i));
    }

    private static void WriteStyle(CanonicalWriter writer, MegastationUrbanStyle style)
    {
        writer.WriteSingle(style.OverallDensity);
        writer.WriteInt32(style.BaseDepthOffset);
        writer.WriteSingle(style.TowerFrequency);
        writer.WriteInt32(style.TowerWidthBias);
        writer.WriteSingle(style.HeightContrast);
        writer.WriteSingle(style.TrenchFrequency);
        writer.WriteSingle(style.CourtyardFrequency);
        writer.WriteSingle(style.EdgeSpineStrength);
        writer.WriteSingle(style.CornerMassStrength);
        writer.WriteSingle(style.FragmentationTendency);
    }

    private static void WriteCells(CanonicalWriter writer, StructuralOccupancy occupancy)
    {
        SliceGrid grid = occupancy.Grid;
        writer.WriteInt32(grid.CellCount);
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            writer.WriteByte(occupancy.IsOccupied(x, y, z) ? (byte)1 : (byte)0);
            writer.WriteByte((byte)occupancy.Owner(x, y, z));
            writer.WriteString(occupancy.RegionId(x, y, z) ?? string.Empty);
        }
    }

    private static void WriteFacePlans(CanonicalWriter writer, IReadOnlyList<UrbanGrowthResult> faces)
    {
        var ordered = faces.OrderBy(f => (int)f.Patch.Direction).ToArray();
        writer.WriteInt32(ordered.Length);
        foreach (var face in ordered)
            WriteDepthMap(writer, face);
    }

    private static void WriteDepthMap(CanonicalWriter writer, UrbanGrowthResult face)
    {
        writer.WriteInt32((int)face.Patch.Direction);
        writer.WriteInt32(face.Patch.MinU);
        writer.WriteInt32(face.Patch.MaxU);
        writer.WriteInt32(face.Patch.MinV);
        writer.WriteInt32(face.Patch.MaxV);
        writer.WriteInt32(face.Depths.GetLength(0));
        writer.WriteInt32(face.Depths.GetLength(1));
        for (int u = 0; u < face.Depths.GetLength(0); u++)
        for (int v = 0; v < face.Depths.GetLength(1); v++)
            writer.WriteInt32(face.Depths[u, v]);
        writer.WriteInt32(face.Districts.Count);
        foreach (var district in face.Districts.OrderBy(d => d.Id))
        {
            writer.WriteInt32(district.Id);
            writer.WriteInt32(district.MinU);
            writer.WriteInt32(district.MaxU);
            writer.WriteInt32(district.MinV);
            writer.WriteInt32(district.MaxV);
            writer.WriteInt32(district.BaseDepth);
            writer.WriteInt32(district.MaxDepth);
        }
    }

    private static void WriteEdgePlans(CanonicalWriter writer, IReadOnlyList<EdgeRegionPlan> edges)
    {
        var ordered = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        writer.WriteInt32(ordered.Length);
        foreach (var edge in ordered)
        {
            writer.WriteString(edge.Id);
            writer.WriteInt32((int)edge.A);
            writer.WriteInt32((int)edge.B);
            writer.WriteInt32((int)edge.LengthAxis);
            writer.WriteInt32(edge.StartCornerDepthA);
            writer.WriteInt32(edge.StartCornerDepthB);
            writer.WriteInt32(edge.EndCornerDepthA);
            writer.WriteInt32(edge.EndCornerDepthB);
            WriteIntArray(writer, edge.DepthA);
            WriteIntArray(writer, edge.DepthB);
            writer.WriteString(edge.ProfileSummary);
        }
    }

    private static void WriteCornerPlans(CanonicalWriter writer, IReadOnlyList<CornerRegionPlan> corners)
    {
        var ordered = corners.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        writer.WriteInt32(ordered.Length);
        foreach (var corner in ordered)
        {
            writer.WriteString(corner.Id);
            writer.WriteInt32((int)corner.A);
            writer.WriteInt32((int)corner.B);
            writer.WriteInt32((int)corner.C);
            writer.WriteInt32(corner.DepthA);
            writer.WriteInt32(corner.DepthB);
            writer.WriteInt32(corner.DepthC);
            writer.WriteString(corner.Summary);
        }
    }

    private static void WriteIntArray(CanonicalWriter writer, int[] values)
    {
        writer.WriteInt32(values.Length);
        foreach (int value in values)
            writer.WriteInt32(value);
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class CanonicalWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly byte[] _buffer = new byte[8];

        public void WriteByte(byte value) => _stream.WriteByte(value);

        public void WriteBytes(byte[] value)
        {
            WriteInt32(value.Length);
            _stream.Write(value);
        }

        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer, value);
            _stream.Write(_buffer, 0, 4);
        }

        public void WriteSingle(float value)
        {
            WriteInt32(BitConverter.SingleToInt32Bits(value));
        }

        public void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt32(bytes.Length);
            _stream.Write(bytes);
        }

        public byte[] ToArray() => _stream.ToArray();
    }
}
