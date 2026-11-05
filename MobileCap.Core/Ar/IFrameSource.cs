namespace MobileCap.Core.Ar;

/// <summary>Takes a full-resolution still without stopping AR tracking.</summary>
public interface IFrameSource
{
    /// <summary>
    /// What stills will come out as. ARKit says only by taking one, so this may take a still
    /// once the session is running; call it after <see cref="IArSession.StartAsync"/>.
    /// </summary>
    Task<StillFormat> GetStillFormatAsync(CancellationToken cancellationToken = default);

    Task<CapturedStill> CaptureStillAsync(CancellationToken cancellationToken = default);
}

/// <param name="FocalLengthMm">The lens's real focal length, where the platform reports one.</param>
public sealed record StillFormat(int Width, int Height, double? FocalLengthMm = null);

/// <param name="Pixels">8-bit grey, row-major, used for the sharpness check.</param>
/// <param name="EncodedJpeg">The photo as written to images/, before EXIF is added.</param>
public sealed record CapturedStill(
    ArFrame Frame,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels,
    ReadOnlyMemory<byte> EncodedJpeg);
