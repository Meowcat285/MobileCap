using System.Numerics;
using MobileCap.Core.Ar;
using MobileCap.Core.Scene;

namespace MobileCap.Core.Tests;

public class SceneSpacingTests
{
    private static CameraPose At(float x, double yaw = 0) => CameraPose.FromCameraToWorld(TestImages.Pose(new Vector3(x, 0, 0), yaw));

    [Fact]
    public void Far_enough_means_far_or_turned_from_every_earlier_shot()
    {
        var s = new SceneSpacing(0.3, 15);
        s.Add(At(0), 60);
        s.Add(At(1), 60);

        Assert.False(s.IsFarFromEveryShot(At(0.2f, 10)));
        Assert.True(s.IsFarFromEveryShot(At(0.2f, 16)));
        Assert.True(s.IsFarFromEveryShot(At(0.5f)));
        Assert.False(s.IsFarFromEveryShot(At(0.8f)));
    }

    [Fact]
    public void Trail_holds_every_shot_in_order()
    {
        var s = new SceneSpacing(0.3, 15);
        s.Add(At(0), 60);
        s.Add(At(0.4f), 60);

        Assert.Equal([0f, 0.4f], s.Trail.Select(p => p.Position.X));
    }

    [Fact]
    public void A_shot_overlapping_little_with_every_earlier_one_is_a_gap()
    {
        var s = new SceneSpacing(0.3, 15, minOverlap: 0.6, sceneDepthMetres: 1.5);

        Assert.Null(s.Add(At(0), 60));
        Assert.Null(s.Add(At(0.4f), 60));
        var gap = s.Add(At(2.4f), 60);
        Assert.Null(s.Add(At(2.0f), 60));
        var turned = s.Add(At(2.0f, 45), 60);

        Assert.Equal(new GapWarning(2, 1, 0), gap);
        Assert.NotNull(turned);
        Assert.Equal(4, turned.Value.Shot);
        Assert.Equal(0.25, turned.Value.Overlap, 3);
    }

    [Fact]
    public void Field_of_view_is_the_narrower_axis()
    {
        Assert.Equal(2 * Math.Atan(1512 / 3000.0) * 180 / Math.PI, SceneSpacing.FieldOfViewDegrees(TestImages.Intrinsics), 6);
    }
}
