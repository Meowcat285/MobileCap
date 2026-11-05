using System.Numerics;

namespace MobileCap.Core.Ar;

/// <summary>
/// The platform AR tracker (ARKit on iOS). Raises one <see cref="ArFrame"/> per camera frame.
/// </summary>
public interface IArSession : IAsyncDisposable
{
    event EventHandler<ArFrame>? FrameUpdated;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}

public enum TrackingState
{
    NotAvailable,
    Limited,
    Normal,
}

/// <param name="CameraToWorld">
/// OpenGL camera convention (x right, y up, looking down -z), metres, in System.Numerics
/// row-vector layout (position in M41 to M43). See <see cref="CameraPose"/>.
/// </param>
/// <param name="ExposureDuration">Seconds.</param>
public sealed record ArFrame(
    TimeSpan Timestamp,
    TrackingState Tracking,
    Matrix4x4 CameraToWorld,
    CameraIntrinsics Intrinsics,
    double ExposureDuration);

/// <summary>Pinhole intrinsics in pixels, in the sensor's native orientation.</summary>
public readonly record struct CameraIntrinsics(
    double Fx,
    double Fy,
    double Cx,
    double Cy,
    int Width,
    int Height)
{
    public CameraIntrinsics ScaledTo(int width, int height)
    {
        var sx = (double)width / Width;
        var sy = (double)height / Height;
        return new CameraIntrinsics(Fx * sx, Fy * sy, Cx * sx, Cy * sy, width, height);
    }
}
