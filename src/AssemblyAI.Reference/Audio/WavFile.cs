using System.Buffers.Binary;
using System.Text;

namespace AssemblyAI.Reference.Audio;

/// <summary>
/// A minimal reader for 16-bit PCM WAV files, used by the samples to turn a recording into
/// raw PCM frames for Streaming and live Dictation. Production capture code would read PCM
/// straight from the microphone instead.
/// </summary>
public sealed class WavFile
{
    private WavFile(int sampleRate, int channels, int bitsPerSample, byte[] pcmData)
    {
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        PcmData = pcmData;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int BitsPerSample { get; }

    /// <summary>The audio samples with the WAV header stripped off.</summary>
    public byte[] PcmData { get; }

    public int BytesPerSecond => SampleRate * Channels * (BitsPerSample / 8);

    public TimeSpan Duration => TimeSpan.FromSeconds((double)PcmData.Length / BytesPerSecond);

    public static WavFile Read(string path) => Parse(File.ReadAllBytes(path));

    public static WavFile Parse(byte[] bytes)
    {
        if (bytes.Length < 12
            || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int? sampleRate = null, channels = null, bits = null;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var body = offset + 8;

            if (id == "fmt ")
            {
                var format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14, 2));

                // 1 = PCM. 0xFFFE (WAVE_FORMAT_EXTENSIBLE) is also accepted as long as it's 16-bit.
                if (format != 1 && format != 0xFFFE)
                {
                    throw new InvalidDataException($"Only PCM WAV is supported (format tag {format}).");
                }
            }
            else if (id == "data")
            {
                if (sampleRate is null || channels is null || bits is null)
                {
                    throw new InvalidDataException("WAV 'data' chunk appeared before 'fmt '.");
                }

                if (bits != 16)
                {
                    throw new InvalidDataException($"Only 16-bit PCM is supported ({bits}-bit found).");
                }

                var length = Math.Min(size, bytes.Length - body);
                return new WavFile(sampleRate.Value, channels.Value, bits.Value, bytes.AsSpan(body, length).ToArray());
            }

            offset = body + size + (size % 2); // chunks are word-aligned
        }

        throw new InvalidDataException("WAV file has no 'data' chunk.");
    }

    /// <summary>Splits the PCM into frames of <paramref name="frame"/> length (the last one may be shorter).</summary>
    public IEnumerable<ReadOnlyMemory<byte>> Frames(TimeSpan frame)
    {
        var frameBytes = (int)(BytesPerSecond * frame.TotalSeconds);
        var blockAlign = Channels * (BitsPerSample / 8);
        frameBytes -= frameBytes % blockAlign;
        if (frameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Frame is shorter than one sample.");
        }

        for (var offset = 0; offset < PcmData.Length; offset += frameBytes)
        {
            yield return PcmData.AsMemory(offset, Math.Min(frameBytes, PcmData.Length - offset));
        }
    }
}
