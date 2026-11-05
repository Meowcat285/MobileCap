using System.Numerics;

namespace MobileCap.Core.Ar;

/// <summary>
/// Where a camera is and which way it looks, read off a camera-to-world matrix in
/// System.Numerics' row-vector convention: rows 1 to 3 are the camera's x, y and z axes in
/// world coordinates, row 4 its position.
/// </summary>
public readonly record struct CameraPose(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Back)
{
    public Vector3 Forward => -Back;

    public static CameraPose FromCameraToWorld(Matrix4x4 m) => new(
        new Vector3(m.M41, m.M42, m.M43),
        Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13)),
        Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23)),
        Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33)));

    public double DistanceTo(CameraPose other) => Vector3.Distance(Position, other.Position);

    /// <summary>Angle between the two viewing directions, in degrees; roll does not count.</summary>
    public double ViewAngleTo(CameraPose other) => AngleDegrees(Forward, other.Forward);

    internal static double AngleDegrees(Vector3 a, Vector3 b)
    {
        var cos = Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f);
        return Math.Acos(cos) * 180 / Math.PI;
    }
}
