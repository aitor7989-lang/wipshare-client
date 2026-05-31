using System.IO;

namespace WipShare.Client.Upload;

/// <summary>
/// Read-only forwarding stream that reports cumulative bytes read (throttled),
/// so an HTTP PUT streaming a file can surface real upload progress. Resets its
/// counter if the underlying stream is seeked back to the start (HttpClient may
/// rewind for an internal retry).
/// </summary>
internal sealed class ProgressStream : Stream
{
    private const long ReportEvery = 262_144; // ~256 KB between reports

    private readonly Stream _inner;
    private readonly Action<long> _report;
    private long _read;
    private long _lastReported;

    public ProgressStream(Stream inner, Action<long> report)
    {
        _inner = inner;
        _report = report;
    }

    private void Advance(int n)
    {
        if (n <= 0) return;
        _read += n;
        if (_read - _lastReported >= ReportEvery)
        {
            _lastReported = _read;
            try { _report(_read); } catch { /* progress reporting must never break the upload */ }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _inner.Read(buffer, offset, count);
        Advance(n);
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        int n = _inner.Read(buffer);
        Advance(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin)
    {
        long pos = _inner.Seek(offset, origin);
        if (pos == 0) { _read = 0; _lastReported = 0; }
        return pos;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
