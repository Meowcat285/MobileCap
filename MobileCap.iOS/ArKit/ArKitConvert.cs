using System.Numerics;
using ARKit;
using CoreGraphics;
using MobileCap.Core.Ar;

namespace MobileCap.iOS.ArKit;

internal static class ArKitConvert
{
    /// <summary>
    /// ARKit's simd matrices are column-major with the translation in column 3; System.Numerics
    /// is row-vector with it in row 4, so each ARKit column becomes a row.
    /// </summary>
    public static Matrix4x4 ToNumerics(NMatrix4 m)
    {
        Vector4 c0 = m.Column0, c1 = m.Column1, c2 = m.Column2, c3 = m.Column3;
        return new Matrix4x4(
            c0.X, c0.Y, c0.Z, c0.W,
            c1.X, c1.Y, c1.Z, c1.W,
            c2.X, c2.Y, c2.Z, c2.W,
            c3.X, c3.Y, c3.Z, c3.W);
    }

    /// <summary>ARKit's K is [fx 0 cx; 0 fy cy; 0 0 1], for the camera image in sensor orientation.</summary>
    public static CameraIntrinsics ToIntrinsics(NMatrix3 k, CGSize resolution) =>
        new(k.M11, k.M22, k.M13, k.M23, (int)resolution.Width, (int)resolution.Height);

    public static TrackingState ToTracking(ARTrackingState state) => state switch
    {
        ARTrackingState.Normal => TrackingState.Normal,
        ARTrackingState.Limited => TrackingState.Limited,
        _ => TrackingState.NotAvailable,
    };

    public static ArFrame ToFrame(ARFrame frame)
    {
        using var camera = frame.Camera;
        return new ArFrame(
            TimeSpan.FromSeconds(frame.Timestamp),
            ToTracking(camera.TrackingState),
            ToNumerics(camera.Transform),
            ToIntrinsics(camera.Intrinsics, camera.ImageResolution),
            camera.ExposureDuration);
    }
}
