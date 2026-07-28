using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inferior.Game.StationGen.Megastations;

public sealed record BoundaryTopologySignature(string Semantic);

public static class BoundaryTopologySignatureBuilder
{
    private const int FormatVersion = 1;

    public static BoundaryTopologySignature Compute(BoundaryTopology topology, MegastationPrototypeSettings settings)
    {
        var writer = new CanonicalWriter();
        writer.WriteString("Inferior.Megastation.BoundaryTopologySignature");
        writer.WriteInt32(FormatVersion);
        writer.WriteInt32(settings.BoundaryTopologyAlgorithmVersion);
        writer.WriteInt32(settings.StructuralChamferAlgorithmVersion);
        writer.WriteInt32(topology.Faces.Count);
        foreach (var face in topology.Faces.OrderBy(f => f.Key))
        {
            writer.WriteInt32(face.Key.X);
            writer.WriteInt32(face.Key.Y);
            writer.WriteInt32(face.Key.Z);
            writer.WriteInt32((int)face.Key.Direction);
            writer.WriteByte((byte)face.Owner);
            writer.WriteString(face.RegionId);
        }

        writer.WriteInt32(topology.EdgeSegments.Count);
        foreach (var edge in topology.EdgeSegments.OrderBy(e => e.Key))
        {
            writer.WriteInt32((int)edge.Key.Axis);
            writer.WriteInt32(edge.Key.A);
            writer.WriteInt32(edge.Key.B);
            writer.WriteInt32(edge.Key.Start);
            writer.WriteInt32((int)edge.Classification);
            writer.WriteInt32((int)edge.ChamferEligibility);
            writer.WriteSingle(edge.ChamferWidth);
        }

        writer.WriteInt32(topology.Vertices.Count);
        foreach (var vertex in topology.Vertices.OrderBy(v => v.Key))
        {
            writer.WriteInt32(vertex.Key.X);
            writer.WriteInt32(vertex.Key.Y);
            writer.WriteInt32(vertex.Key.Z);
            writer.WriteInt32((int)vertex.Classification);
        }

        return new BoundaryTopologySignature(Convert.ToHexString(SHA256.HashData(writer.ToArray())));
    }

    private sealed class CanonicalWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly byte[] _buffer = new byte[4];

        public void WriteByte(byte value) => _stream.WriteByte(value);

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
