using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundTransportation.Shared;

public enum PacketType : byte
{
    Hello = 1,
    Audio = 2
}

public sealed record HelloPacket(Guid SenderId, string Name);

public sealed record AudioPacket(Guid SenderId, uint Sequence, long TimestampTicks, ushort FrameCount, float[] Samples);

public static class AudioProtocol
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int MaxFramesPerPacket = 128;

    private const byte Version = 1;
    private const int Magic = 0x31505453; // "STP1" in little-endian.
    private const int CommonHeaderLength = 8;
    private const int HelloHeaderLength = CommonHeaderLength + 16 + 2;
    private const int AudioHeaderLength = CommonHeaderLength + 16 + 4 + 8 + 2 + 2;

    public static byte[] WriteHello(Guid senderId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        if (nameBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Sender name is too long.");
        }

        var packet = new byte[HelloHeaderLength + nameBytes.Length];
        WriteCommonHeader(packet, PacketType.Hello);
        senderId.TryWriteBytes(packet.AsSpan(CommonHeaderLength, 16));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(CommonHeaderLength + 16, 2), (ushort)nameBytes.Length);
        nameBytes.CopyTo(packet.AsSpan(HelloHeaderLength));
        return packet;
    }

    public static byte[] WriteAudio(Guid senderId, uint sequence, ReadOnlySpan<float> samples)
    {
        if (samples.Length % Channels != 0)
        {
            throw new ArgumentException("Samples must contain interleaved stereo frames.", nameof(samples));
        }

        var frameCount = samples.Length / Channels;
        if (frameCount is <= 0 or > MaxFramesPerPacket)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), $"Audio packets must contain 1-{MaxFramesPerPacket} frames.");
        }

        var packet = new byte[AudioHeaderLength + samples.Length * sizeof(float)];
        WriteCommonHeader(packet, PacketType.Audio);
        senderId.TryWriteBytes(packet.AsSpan(CommonHeaderLength, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24, 4), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(28, 8), DateTimeOffset.UtcNow.UtcTicks);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(36, 2), (ushort)frameCount);
        MemoryMarshal.AsBytes(samples).CopyTo(packet.AsSpan(AudioHeaderLength));
        return packet;
    }

    public static bool TryGetPacketType(ReadOnlySpan<byte> packet, out PacketType type)
    {
        type = default;
        if (packet.Length < CommonHeaderLength)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(packet) != Magic || packet[5] != Version)
        {
            return false;
        }

        type = (PacketType)packet[4];
        return type is PacketType.Hello or PacketType.Audio;
    }

    public static bool TryReadHello(ReadOnlySpan<byte> packet, out HelloPacket hello)
    {
        hello = new HelloPacket(Guid.Empty, string.Empty);
        if (!TryGetPacketType(packet, out var type) || type != PacketType.Hello || packet.Length < HelloHeaderLength)
        {
            return false;
        }

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(CommonHeaderLength + 16, 2));
        if (packet.Length < HelloHeaderLength + nameLength)
        {
            return false;
        }

        var senderId = new Guid(packet.Slice(CommonHeaderLength, 16));
        var name = Encoding.UTF8.GetString(packet.Slice(HelloHeaderLength, nameLength));
        hello = new HelloPacket(senderId, name);
        return true;
    }

    public static bool TryReadAudio(ReadOnlySpan<byte> packet, out AudioPacket audio)
    {
        audio = new AudioPacket(Guid.Empty, 0, 0, 0, []);
        if (!TryGetPacketType(packet, out var type) || type != PacketType.Audio || packet.Length < AudioHeaderLength)
        {
            return false;
        }

        var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(36, 2));
        var sampleCount = frameCount * Channels;
        var payloadLength = sampleCount * sizeof(float);
        if (frameCount == 0 || packet.Length < AudioHeaderLength + payloadLength)
        {
            return false;
        }

        var senderId = new Guid(packet.Slice(CommonHeaderLength, 16));
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(24, 4));
        var timestampTicks = BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(28, 8));
        var samples = new float[sampleCount];
        MemoryMarshal.Cast<byte, float>(packet.Slice(AudioHeaderLength, payloadLength)).CopyTo(samples);
        audio = new AudioPacket(senderId, sequence, timestampTicks, frameCount, samples);
        return true;
    }

    private static void WriteCommonHeader(Span<byte> packet, PacketType type)
    {
        BinaryPrimitives.WriteInt32LittleEndian(packet, Magic);
        packet[4] = (byte)type;
        packet[5] = Version;
        packet[6] = 0;
        packet[7] = 0;
    }
}
