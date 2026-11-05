using System.Numerics;
using MobileCap.Core.Motion;

namespace MobileCap.Core.Tests;

public class MotionBlurPredictorTests
{
    private const double Exposure = 0.01, HalfDiagonal = 2520;

    [Fact]
    public void Standing_still_predicts_no_blur()
    {
        var pose = TestImages.Pose(new Vector3(1, 2, 3), 40);

        Assert.Equal(0, MotionBlurPredictor.Predict(pose, pose, 0.1, Exposure, 3000, HalfDiagonal, 1.5), 3);
    }

    [Fact]
    public void Panning_blurs_by_focal_times_angle_turned_during_the_exposure()
    {
        // 0.1 rad in 0.1 s is 1 rad/s; over 10 ms that is 0.01 rad, or 30 px at 3000 px focal.
        var from = TestImages.Pose(Vector3.Zero);
        var to = TestImages.Pose(Vector3.Zero, 0.1 * 180 / Math.PI);

        Assert.Equal(30, MotionBlurPredictor.Predict(from, to, 0.1, Exposure, 3000, HalfDiagonal, 1.5), 2);
    }

    [Fact]
    public void Rolling_blurs_the_corners_by_the_half_diagonal()
    {
        var from = TestImages.Pose(Vector3.Zero);
        var to = Matrix4x4.CreateRotationZ(0.1f);

        Assert.Equal(HalfDiagonal * 0.01, MotionBlurPredictor.Predict(from, to, 0.1, Exposure, 3000, HalfDiagonal, 1.5), 2);
    }

    [Fact]
    public void Moving_sideways_blurs_by_focal_times_speed_over_depth()
    {
        // 0.15 m/s at 1.5 m is 0.1 rad/s: 3 px over 10 ms.
        var from = TestImages.Pose(Vector3.Zero, 30);
        var to = TestImages.Pose(Vector3.Transform(new Vector3(0.015f, 0, 0), Matrix4x4.CreateRotationY((float)(30 * Math.PI / 180))), 30);

        Assert.Equal(3, MotionBlurPredictor.Predict(from, to, 0.1, Exposure, 3000, HalfDiagonal, 1.5), 3);
    }

    [Fact]
    public void Speed_is_averaged_over_100_ms_of_frames()
    {
        var p = new MotionBlurPredictor();
        Assert.Null(p.Update(TestImages.Frame(0, TestImages.Pose(Vector3.Zero)), 4032, 3024));

        double? last = null;
        for (int i = 1; i <= 12; i++)
            last = p.Update(TestImages.Frame(i / 60.0, TestImages.Pose(Vector3.Zero, i % 2 == 0 ? 0 : 0.2)), 4032, 3024);

        // Jitter of 0.2 degrees frame to frame, read over a window rather than one frame apart.
        var oneFrame = MotionBlurPredictor.Predict(TestImages.Pose(Vector3.Zero), TestImages.Pose(Vector3.Zero, 0.2),
            1 / 60.0, 0.01, 3000, HalfDiagonal, 1.5);
        Assert.NotNull(last);
        Assert.True(last < oneFrame / 5, $"{last} vs {oneFrame}");
    }
}
