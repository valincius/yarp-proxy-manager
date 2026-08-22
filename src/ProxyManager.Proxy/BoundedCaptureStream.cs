using System.Text;

namespace ProxyManager.Proxy;

/// <summary>
/// Pass-through stream that counts every byte and, up to a cap, buffers what passes
/// through so the last request/response bodies can be shown in the diagnostics UI.
/// Capture is dropped silently past the cap; the underlying stream is never blocked
/// and reads/writes behave exactly as on the wrapped stream.
/// </summary>
public sealed class BoundedCaptureStream(Stream inner, int cap) : Stream
{
    private readonly MemoryStream _buffer = new(Math.Min(cap, 64 * 1024));

    /// <summary>Every byte that passed through the stream (not capped).</summary>
    public long TotalBytes { get; private set; }

    /// <summary>Captured payload as UTF-8 text (up to the cap), or null when nothing was captured.</summary>
    public string? CapturedText =>
        _buffer.Length == 0 ? null : Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length);

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        if (read > 0)
        {
            Capture(buffer.AsSpan(offset, read));
        }

        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var task = inner.ReadAsync(buffer, cancellationToken);
        return AwaitRead(task, buffer);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        Capture(buffer.AsSpan(offset, count));
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var task = inner.WriteAsync(buffer, cancellationToken);
        return AwaitWrite(task, buffer);
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        var task = inner.DisposeAsync();
        GC.SuppressFinalize(this);
        return task;
    }

    private async ValueTask<int> AwaitRead(ValueTask<int> task, Memory<byte> buffer)
    {
        var read = await task.ConfigureAwait(false);
        if (read > 0)
        {
            Capture(buffer.Span[..read]);
        }

        return read;
    }

    private async ValueTask AwaitWrite(ValueTask task, ReadOnlyMemory<byte> buffer)
    {
        await task.ConfigureAwait(false);
        Capture(buffer.Span);
    }

    private void Capture(ReadOnlySpan<byte> data)
    {
        TotalBytes += data.Length;
        var remaining = cap - _buffer.Length;
        if (remaining > 0)
        {
            var take = (int)Math.Min(remaining, data.Length);
            _buffer.Write(data[..take]);
        }
    }
}
