using MobileCap.Core.Ar;

namespace MobileCap.Core.Scene;

/// <param name="Shot">The new shot's place in the trail.</param>
/// <param name="Nearest">The earlier shot it overlaps most, or the closest where none overlap.</param>
/// <param name="Overlap">Estimated share of the view the two have in common, 0 to 1.</param>
public readonly record struct GapWarning(int Shot, int Nearest, double Overlap);

/// <summary>
/// Scene mode's record of where photos were taken: the marker trail, whether the phone has
/// moved far enough to take another, and a warning when a new shot overlaps too little with
/// every earlier one. It never proposes a place to stand.
/// </summary>
/// <param name="sceneDepthMetres">The distance overlap is judged at; see MotionBlurPredictor.</param>
public sealed class SceneSpacing(double spacingMetres, double spacingDegrees,
    double minOverlap = 0.6, double sceneDepthMetres = 1.5)
{
    private readonly List<CameraPose> _trail = [];

    public IReadOnlyList<CameraPose> Trail => _trail;

    public double SpacingMetres { get; } = spacingMetres;

    public double SpacingDegrees { get; } = spacingDegrees;

    /// <summary>The 60% floor is the low end of the 60 to 80% overlap SfM wants.</summary>
    public double MinOverlap { get; } = minOverlap;

    public double SceneDepthMetres { get; } = sceneDepthMetres;

    public bool IsFarFromEveryShot(CameraPose pose) => _trail.All(shot => IsApart(shot, pose));

    public bool IsApart(CameraPose a, CameraPose b) =>
        a.DistanceTo(b) >= SpacingMetres || a.ViewAngleTo(b) >= SpacingDegrees;

    /// <param name="fieldOfViewDegrees">The narrower of the still's two fields of view.</param>
    public GapWarning? Add(CameraPose pose, double fieldOfViewDegrees)
    {
        var shot = _trail.Count;
        int nearest = -1;
        double best = -1, bestDistance = double.MaxValue;
        for (int i = 0; i < _trail.Count; i++)
        {
            var overlap = Overlap(_trail[i], pose, fieldOfViewDegrees, SceneDepthMetres);
            var distance = _trail[i].DistanceTo(pose);
            if (overlap > best || (overlap == best && distance < bestDistance))
                (best, bestDistance, nearest) = (overlap, distance, i);
        }
        _trail.Add(pose);
        return nearest >= 0 && best < MinOverlap ? new GapWarning(shot, nearest, best) : null;
    }

    /// <summary>
    /// A first-order estimate: the share left after turning, times the share left after moving
    /// sideways across the view's footprint at <paramref name="depthMetres"/>.
    /// </summary>
    public static double Overlap(CameraPose a, CameraPose b, double fieldOfViewDegrees, double depthMetres)
    {
        var turned = 1 - a.ViewAngleTo(b) / fieldOfViewDegrees;
        var footprint = 2 * depthMetres * Math.Tan(fieldOfViewDegrees * Math.PI / 360);
        var offset = b.Position - a.Position;
        var along = System.Numerics.Vector3.Dot(offset, a.Forward);
        var sideways = Math.Sqrt(Math.Max(offset.LengthSquared() - along * along, 0));
        var shifted = 1 - sideways / footprint;
        return Math.Max(turned, 0) * Math.Max(shifted, 0);
    }

    public static double FieldOfViewDegrees(CameraIntrinsics k) =>
        2 * Math.Atan(Math.Min(k.Width / (2 * k.Fx), k.Height / (2 * k.Fy))) * 180 / Math.PI;
}
