namespace MobileCap.Core.Ar;

/// <summary>Takes a full-resolution still without stopping AR tracking.</summary>
public interface IFrameSource
{
    Task<CapturedStill> CaptureStillAsync(CancellationToken cancellationToken = default);
}

/// <param name="Pixels">8-bit grey, row-major, used for the sharpness check.</param>
/// <param name="EncodedJpeg">The photo as written to images/, before EXIF is added.</param>
public sealed record CapturedStill(
    ArFrame Frame,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels,
    ReadOnlyMemory<byte> EncodedJpeg);
