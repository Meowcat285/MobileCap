using System.Numerics;
using MobileCap.Core.Ar;

namespace MobileCap.Core.Export;

public static class ExifOrientation
{
    /// <summary>
    /// The EXIF Orientation tag for a still kept in sensor orientation, from which way world up
    /// (+y, as ARKit's gravity-aligned frame has it) falls in the image. 1 is upright, 3 upside
    /// down, 6 needs turning 90 degrees clockwise (an iPhone held upright), 8 counter-clockwise.
    /// </summary>
    public static int FromPose(CameraPose pose)
    {
        var right = Vector3.Dot(Vector3.UnitY, pose.Right);
        var up = Vector3.Dot(Vector3.UnitY, pose.Up);
        if (Math.Abs(up) >= Math.Abs(right)) return up >= 0 ? 1 : 3;
        return right > 0 ? 8 : 6;
    }
}
