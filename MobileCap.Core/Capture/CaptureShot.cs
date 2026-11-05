using System.Numerics;
using MobileCap.Core.Ar;
using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Capture;

/// <summary>A kept photo and everything the exporters write about it.</summary>
/// <param name="Timestamp">The AR clock, for ordering and spacing in time.</param>
/// <param name="CameraToWorld">As in <see cref="ArFrame"/>.</param>
/// <param name="Intrinsics">At the still's own resolution, in sensor orientation.</param>
/// <param name="Tries">Stills taken at this spot, this one included.</param>
/// <param name="Flagged">Every try was blurry and this was the sharpest.</param>
public sealed record CaptureShot(
    int Index,
    string FileName,
    DateTimeOffset TakenAt,
    TimeSpan Timestamp,
    Matrix4x4 CameraToWorld,
    CameraIntrinsics Intrinsics,
    double ExposureDuration,
    int ExifOrientation,
    double? PredictedBlurPx,
    ShotJudgement Judgement,
    int Tries,
    bool Flagged,
    GeoLocation? Location)
{
    public CameraPose Pose => CameraPose.FromCameraToWorld(CameraToWorld);

    public static string FileNameFor(int index) => $"{index + 1:D6}.jpg";
}

/// <param name="AltitudeMetres">Above sea level.</param>
public sealed record GeoLocation(
    double Latitude,
    double Longitude,
    double? AltitudeMetres = null,
    double? HorizontalAccuracyMetres = null);

/// <param name="FocalLengthMm">The lens's real focal length, where the platform reports one.</param>
public sealed record DeviceInfo(string Make, string Model, string Software, double? FocalLengthMm = null);
