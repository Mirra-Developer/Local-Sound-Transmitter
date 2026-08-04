using System.Buffers.Binary;
using NAudio.Wave;

namespace SoundTransportation.Shared;

public static class SampleNormalizer
{
    public static float[] ToTransportFormat(WaveFormat sourceFormat, ReadOnlySpan<byte> bytes)
    {
        var sourceFrames = bytes.Length / sourceFormat.BlockAlign;
        if (sourceFrames == 0)
        {
            return [];
        }

        var sourceStereo = new float[sourceFrames * AudioProtocol.Channels];
        for (var frame = 0; frame < sourceFrames; frame++)
        {
            var frameBytes = bytes.Slice(frame * sourceFormat.BlockAlign, sourceFormat.BlockAlign);
            var left = ReadChannel(sourceFormat, frameBytes, 0);
            var right = sourceFormat.Channels > 1 ? ReadChannel(sourceFormat, frameBytes, 1) : left;
            sourceStereo[frame * 2] = left;
            sourceStereo[frame * 2 + 1] = right;
        }

        if (sourceFormat.SampleRate == AudioProtocol.SampleRate)
        {
            return sourceStereo;
        }

        return ResampleStereo(sourceStereo, sourceFormat.SampleRate, AudioProtocol.SampleRate);
    }

    private static float ReadChannel(WaveFormat format, ReadOnlySpan<byte> frameBytes, int channel)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var offset = channel * bytesPerSample;
        var sampleBytes = frameBytes.Slice(offset, bytesPerSample);

        if (format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible && format.BitsPerSample == 32)
        {
            return BitConverter.ToSingle(sampleBytes);
        }

        return format.BitsPerSample switch
        {
            16 => BinaryPrimitives.ReadInt16LittleEndian(sampleBytes) / 32768f,
            24 => ReadPcm24(sampleBytes) / 8_388_608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(sampleBytes) / 2_147_483_648f,
            _ => 0f
        };
    }

    private static int ReadPcm24(ReadOnlySpan<byte> sampleBytes)
    {
        var value = sampleBytes[0] | sampleBytes[1] << 8 | sampleBytes[2] << 16;
        if ((value & 0x80_0000) != 0)
        {
            value |= unchecked((int)0xFF00_0000);
        }

        return value;
    }

    private static float[] ResampleStereo(float[] source, int sourceRate, int targetRate)
    {
        var sourceFrames = source.Length / AudioProtocol.Channels;
        var targetFrames = Math.Max(1, (int)Math.Round(sourceFrames * (double)targetRate / sourceRate));
        var target = new float[targetFrames * AudioProtocol.Channels];

        for (var targetFrame = 0; targetFrame < targetFrames; targetFrame++)
        {
            var sourcePosition = targetFrame * (double)sourceRate / targetRate;
            var sourceIndex = Math.Min((int)sourcePosition, sourceFrames - 1);
            var nextIndex = Math.Min(sourceIndex + 1, sourceFrames - 1);
            var fraction = (float)(sourcePosition - sourceIndex);

            target[targetFrame * 2] = Lerp(source[sourceIndex * 2], source[nextIndex * 2], fraction);
            target[targetFrame * 2 + 1] = Lerp(source[sourceIndex * 2 + 1], source[nextIndex * 2 + 1], fraction);
        }

        return target;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
