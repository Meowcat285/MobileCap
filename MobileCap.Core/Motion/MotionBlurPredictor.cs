using System.Numerics;
using MobileCap.Core.Ar;

namespace MobileCap.Core.Motion;

/// <summary>
/// Predicts how many full-resolution pixels a still taken now would smear, from the tracked
/// motion and the frame's exposure time, before any pixels are read.
/// </summary>
/// <param name="sceneDepthMetres">
/// The distance translation is judged against. Without depth the nearest thing in view is
/// unknown, so this stands in for it; nearer scenes blur more for the same motion.
/// </param>
public sealed class MotionBlurPredictor(double sceneDepthMetres = 1.5)
{
    // Speed is averaged over this long rather than between consecutive frames, so tracker
    // jitter at 60 Hz does not read as motion. Exposures are 1 to 30 ms, well inside it.
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);

    private readonly Queue<ArFrame> _recent = new();

    public double SceneDepthMetres { get; } = sceneDepthMetres;

    /// <summary>
    /// Blur in pixels of a <paramref name="stillWidth"/> x <paramref name="stillHeight"/> still,
    /// or null until the window has a frame to compare against.
    /// </summary>
    public double? Update(ArFrame frame, int stillWidth, int stillHeight)
    {
        if (_recent.Count > 0 && frame.Timestamp <= _recent.Last().Timestamp) _recent.Clear();
        _recent.Enqueue(frame);
        while (_recent.Count > 2 && frame.Timestamp - _recent.ElementAt(1).Timestamp >= Window)
            _recent.Dequeue();

        var start = _recent.Peek();
        if (ReferenceEquals(start, frame)) return null;
        var dt = (frame.Timestamp - start.Timestamp).TotalSeconds;
        var k = frame.Intrinsics.ScaledTo(stillWidth, stillHeight);
        return Predict(start.CameraToWorld, frame.CameraToWorld, dt, frame.ExposureDuration,
            Math.Max(k.Fx, k.Fy), Math.Sqrt(stillWidth * (double)stillWidth + stillHeight * (double)stillHeight) / 2,
            SceneDepthMetres);
    }

    /// <summary>
    /// Pixels smeared during <paramref name="exposure"/> seconds at the mean motion between two
    /// poses <paramref name="dt"/> seconds apart. Turning about an axis across the view moves
    /// every pixel by focal x angle; rolling, and moving along the view, move the corners most.
    /// The terms are added, so the estimate errs high.
    /// </summary>
    public static double Predict(Matrix4x4 fromCameraToWorld, Matrix4x4 toCameraToWorld, double dt,
        double exposure, double focalPx, double halfDiagonalPx, double depthMetres)
    {
        if (dt <= 0 || exposure <= 0) return 0;
        Matrix4x4.Invert(fromCameraToWorld, out var worldToFrom);
        var toInFrom = toCameraToWorld * worldToFrom;

        var rotation = toInFrom with { M41 = 0, M42 = 0, M43 = 0 };
        var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
        var axis = new Vector3(q.X, q.Y, q.Z);
        var axisLength = axis.Length();
        // atan2 rather than acos(w): acos loses small angles to float rounding near w = 1.
        var angle = 2 * Math.Atan2(axisLength, Math.Abs(q.W));
        double across = 0, roll = 0;
        if (axisLength > 1e-9)
        {
            axis /= axisLength;
            across = angle * Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
            roll = angle * Math.Abs(axis.Z);
        }

        var move = new Vector3(toInFrom.M41, toInFrom.M42, toInFrom.M43);
        var sideways = Math.Sqrt(move.X * move.X + move.Y * move.Y);
        var along = Math.Abs(move.Z);

        var perSecond = focalPx * across + halfDiagonalPx * roll +
                        (focalPx * sideways + halfDiagonalPx * along) / depthMetres;
        return perSecond / dt * exposure;
    }
}
