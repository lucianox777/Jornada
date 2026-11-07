using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Preallocates an actual 10 KiB on-disk error-log payload before physical
/// calibration. Rewrites the same bytes in place, without truncating or
/// creating a new file when the drive is full. It is not a retry or a spill.
/// </summary>
public sealed class V9CalibradorLastErrorLog : IDisposable
{
    public const int ReservedBytes = 10 * 1024;
    public const string FileName = "calibrador.log";
    private const string TruncatedMarker = "\n[EXCECAO TRUNCADA AO LIMITE DE 10 KiB]\n";

    private readonly FileStream stream;
    private readonly byte[] reservedBytes;
    private bool disposed;

    private V9CalibradorLastErrorLog(FileStream stream, byte[] reservedBytes)
    {
        this.stream = stream;
        this.reservedBytes = reservedBytes;
    }

    public static V9CalibradorLastErrorLog Reserve(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        // FileMode.CreateNew protects prior immutable runs. FileStream.SetLength
        // alone is NOT sufficient: it may allocate a sparse file with no
        // physical blocks. Actually write all 10 KiB, then flush to disk.
        var stream = new FileStream(Path.Combine(directory, FileName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            bufferSize: 4096, options: FileOptions.WriteThrough);
        try
        {
            var reserved = new byte[ReservedBytes];
            Array.Fill(reserved, (byte)' ');
            stream.Write(reserved);
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return new V9CalibradorLastErrorLog(stream, reserved);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Only the latest exception; never append, truncate or grow the file.</summary>
    public void WriteLast(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes = Encoding.UTF8.GetBytes(error.ToString());
        reservedBytes.AsSpan().Fill((byte)' ');

        if (bytes.Length <= ReservedBytes)
        {
            bytes.CopyTo(reservedBytes, 0);
        }
        else
        {
            var marker = Encoding.UTF8.GetBytes(TruncatedMarker);
            var max = ReservedBytes - marker.Length;
            // Never split a UTF-8 multibyte sequence.
            while (max > 0 && (bytes[max] & 0xc0) == 0x80) max--;
            Array.Copy(bytes, 0, reservedBytes, 0, max);
            Array.Copy(marker, 0, reservedBytes, max, marker.Length);
        }

        stream.Position = 0;
        stream.Write(reservedBytes);
        stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.Dispose();
    }
}
