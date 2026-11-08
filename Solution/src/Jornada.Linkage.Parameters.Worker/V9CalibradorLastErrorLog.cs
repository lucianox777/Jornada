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
    // Never include exception messages, inner exceptions or stack traces here.
    // This file can accidentally be collected by generic artifact uploaders.

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

    /// <summary>Safe, stable code only. No message, stack, path or PII.</summary>
    public static string SafeErrorCode(Exception error) => error switch
    {
        FileNotFoundException => "V9_CALIBRATOR_FAILED:INPUT_NOT_FOUND",
        InvalidDataException => "V9_CALIBRATOR_FAILED:INVALID_DATA",
        IOException => "V9_CALIBRATOR_FAILED:IO_ERROR",
        ArgumentException => "V9_CALIBRATOR_FAILED:INVALID_ARGUMENTS",
        _ => "V9_CALIBRATOR_FAILED:UNEXPECTED"
    };

    /// <summary>Only the latest safe error code; in-place 10 KiB rewrite.</summary>
    public void WriteLast(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes = Encoding.UTF8.GetBytes(SafeErrorCode(error));
        reservedBytes.AsSpan().Fill((byte)' ');
        // A bounded constant diagnostic code always fits the 10 KiB reserve.
        bytes.CopyTo(reservedBytes, 0);

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
