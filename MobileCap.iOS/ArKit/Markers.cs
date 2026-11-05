using System.Numerics;
using MobileCap.Core.Capture;
using SceneKit;
using UIKit;

namespace MobileCap.iOS.ArKit;

/// <summary>The marker trail: a dot where each photo was taken, with a cone along its view.</summary>
internal static class Markers
{
    private const float DotRadius = 0.02f, ConeLength = 0.05f, ConeRadius = 0.012f;

    public static SCNNode ForShot(CaptureShot shot, bool gap)
    {
        var colour = gap ? UIColor.SystemOrange : shot.Flagged ? UIColor.SystemYellow : UIColor.SystemGreen;
        var p = shot.Pose.Position;
        // Quaternion.CreateFromRotationMatrix reads the row-vector camera-to-world, and gives
        // the same rotation SceneKit's column-vector orientation means.
        var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(shot.CameraToWorld with
        {
            M41 = 0, M42 = 0, M43 = 0,
        }));
        var node = new SCNNode
        {
            Geometry = Material(SCNSphere.Create(DotRadius), colour),
            Position = new SCNVector3(p.X, p.Y, p.Z),
            Orientation = new SCNQuaternion(q.X, q.Y, q.Z, q.W),
        };
        // SCNCone points along +y; turning it -90 degrees about x points it down the camera's -z.
        var cone = new SCNNode
        {
            Geometry = Material(SCNCone.Create(0, ConeRadius, ConeLength), colour),
            Position = new SCNVector3(0, 0, -(DotRadius + ConeLength / 2)),
            EulerAngles = new SCNVector3(-MathF.PI / 2, 0, 0),
        };
        node.AddChildNode(cone);
        return node;
    }

    private static SCNGeometry Material(SCNGeometry geometry, UIColor colour)
    {
        geometry.FirstMaterial!.Diffuse.Contents = colour;
        geometry.FirstMaterial.LightingModelName = SCNLightingModel.Constant;
        return geometry;
    }
}
