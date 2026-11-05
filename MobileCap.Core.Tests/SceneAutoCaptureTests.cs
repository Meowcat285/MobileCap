using System.Numerics;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;

namespace MobileCap.Core.Tests;

public class SceneAutoCaptureTests
{
    private const int W = 320, H = 240;
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 14, 32, 0, TimeSpan.Zero);

    private double _clock;

    private readonly SceneAutoCapture _capture = new(new CaptureSettings(), W, H);

    /// <summary>Holds the phone still at <paramref name="at"/> long enough for a prediction.</summary>
    private FrameAdvice HoldStill(Vector3 at, double yaw = 0)
    {
        FrameAdvice advice = default;
        for (int i = 0; i < 8; i++)
        {
            _clock += 1 / 60.0;
            advice = _capture.OnFrame(TestImages.Frame(_clock, TestImages.Pose(at, yaw)));
        }
        return advice;
    }

    private StillResult Still(Vector3 at, int scene, int smear = 0)
    {
        var pixels = TestImages.Blocks(W, H, 40, scene);
        if (smear > 0) pixels = TestImages.SmearX(pixels, W, H, smear);
        var frame = TestImages.Frame(_clock, TestImages.Pose(at));
        return _capture.OnStill(new CapturedStill(frame, W, H, pixels, new byte[] { (byte)scene, (byte)smear }), Now);
    }

    [Fact]
    public void Holds_for_tracking_warmup_and_motion()
    {
        Assert.Equal(HoldReason.Warmup, _capture.OnFrame(TestImages.Frame(0, TestImages.Pose(Vector3.Zero))).Hold);
        Assert.Equal(HoldReason.Tracking,
            _capture.OnFrame(TestImages.Frame(0.02, TestImages.Pose(Vector3.Zero), tracking: TrackingState.Limited)).Hold);
        var panning = _capture.OnFrame(TestImages.Frame(0.04, TestImages.Pose(Vector3.Zero, 5)));
        Assert.Equal(HoldReason.Moving, panning.Hold);
        Assert.True(panning.PredictedBlurPx > 0.7);
    }

    [Fact]
    public void Fires_when_still_and_away_from_every_kept_shot()
    {
        Assert.True(HoldStill(Vector3.Zero).Fire);
        var kept = Assert.Single(Still(Vector3.Zero, scene: 1).Kept);

        Assert.Equal(HoldReason.TooClose, HoldStill(new Vector3(0.1f, 0, 0)).Hold);
        Assert.True(HoldStill(new Vector3(0.4f, 0, 0)).Fire);
        Assert.Equal("000001.jpg", kept.Shot.FileName);
        Assert.Equal(TestImages.Intrinsics.ScaledTo(W, H), kept.Shot.Intrinsics);
        Assert.Equal(1, kept.Shot.ExifOrientation);
        Assert.Equal(0, kept.Shot.PredictedBlurPx!.Value, 3);
        Assert.Equal(new byte[] { 1, 0 }, kept.Jpeg.ToArray());
    }

    [Fact]
    public void After_three_blurry_tries_the_sharpest_is_kept_and_flagged()
    {
        Still(Vector3.Zero, scene: 1);
        var spot = new Vector3(0.5f, 0, 0);

        Assert.Empty(Still(spot, scene: 2, smear: 19).Kept);
        Assert.Empty(Still(spot, scene: 2, smear: 7).Kept);
        var third = Still(spot, scene: 2, smear: 13);

        var kept = Assert.Single(third.Kept);
        Assert.True(kept.Shot.Flagged);
        Assert.Equal(3, kept.Shot.Tries);
        Assert.Equal(new byte[] { 2, 7 }, kept.Jpeg.ToArray());
        Assert.Equal(2, _capture.Shots.Count);
    }

    [Fact]
    public void Leaving_a_spot_keeps_its_sharpest_try_first()
    {
        Still(Vector3.Zero, scene: 1);
        Still(new Vector3(0.5f, 0, 0), scene: 2, smear: 13);

        var next = Still(new Vector3(1.0f, 0, 0), scene: 3);

        Assert.Equal(2, next.Kept.Count);
        Assert.True(next.Kept[0].Shot.Flagged);
        Assert.Equal("000002.jpg", next.Kept[0].Shot.FileName);
        Assert.False(next.Kept[1].Shot.Flagged);
        Assert.Equal("000003.jpg", next.Kept[1].Shot.FileName);
    }

    [Fact]
    public void A_duplicate_is_dropped_and_blocks_its_spot()
    {
        Still(Vector3.Zero, scene: 1);
        var dup = Still(new Vector3(0.4f, 0, 0), scene: 1);

        Assert.Equal(Sharpness.ShotVerdict.Duplicate, dup.Judgement.Verdict);
        Assert.Empty(dup.Kept);
        Assert.Equal(HoldReason.TooClose, HoldStill(new Vector3(0.45f, 0, 0)).Hold);
        Assert.True(HoldStill(new Vector3(0.8f, 0, 0)).Fire);
    }

    [Fact]
    public void Finish_keeps_an_open_run_of_tries_and_the_record_has_the_gaps()
    {
        Still(Vector3.Zero, scene: 1);
        Still(new Vector3(5, 0, 0), scene: 2, smear: 13);

        var last = Assert.Single(_capture.Finish());
        var record = _capture.ToRecord("capture", Now, new DeviceInfo("Apple", "iPhone", "MobileCap"));

        Assert.True(last.Shot.Flagged);
        Assert.NotNull(last.Gap);
        Assert.Equal(2, record.Shots.Count);
        Assert.Equal(1, Assert.Single(record.Gaps).Shot);
    }
}
