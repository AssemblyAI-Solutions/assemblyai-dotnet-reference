using System.Threading.Channels;

namespace AssemblyAI.Reference.Audio;

/// <summary>
/// A read-only stream fed by a producer, so an HTTP request body can be uploaded while the audio
/// is still being captured. Write frames with <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>
/// as they arrive from the microphone and call <see cref="Complete"/> when the user stops talking.
/// HttpClient reads the stream as it goes, sending the body with chunked transfer encoding.
/// </summary>
public sealed class ProducerStream : Stream
{
    private readonly Channel<ReadOnlyMemory<byte>> _frames =
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private ReadOnlyMemory<byte> _current;

    /// <summary>Queues a frame for upload. The bytes are copied, so the caller can reuse its buffer.</summary>
    public new ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) =>
        _frames.Writer.WriteAsync(frame.ToArray(), cancellationToken);

    /// <summary>Marks the end of the audio. Pass an exception to abort the upload instead.</summary>
    public void Complete(Exception? error = null) => _frames.Writer.TryComplete(error);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_current.IsEmpty)
        {
            if (!await _frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0; // producer completed: end of stream
            }

            _frames.Reader.TryRead(out _current);
        }

        var count = Math.Min(buffer.Length, _current.Length);
        _current[..count].CopyTo(buffer);
        _current = _current[count..];
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
