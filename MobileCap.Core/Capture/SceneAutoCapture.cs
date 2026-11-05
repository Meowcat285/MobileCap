using MobileCap.Core.Ar;
using MobileCap.Core.Export;
using MobileCap.Core.Motion;
using MobileCap.Core.Scene;
using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Capture;

public enum HoldReason
{
    None,
    Tracking,
    Warmup,
    Moving,
    TooClose,
}

/// <param name="PredictedBlurPx">Null until the predictor has a frame to compare against.</param>
public readonly record struct FrameAdvice(bool Fire, HoldReason Hold, double? PredictedBlurPx);

public sealed record KeptShot(CaptureShot Shot, ReadOnlyMemory<byte> Jpeg, GapWarning? Gap);

/// <param name="Kept">
/// Shots to write, in order. A still that ends a run of blurry tries elsewhere can also keep the
/// sharpest of that run, so there may be two.
/// </param>
public sealed record StillResult(ShotJudgement Judgement, IReadOnlyList<KeptShot> Kept);

/// <summary>
/// Scene mode auto capture, without the platform: says on each AR frame whether to take a still,
/// then rules on the still. Fires when tracking is normal, predicted motion blur is under the
/// gate, and the phone is far from every kept shot. A blurry still is retried at the same spot;
/// after <see cref="CaptureSettings.MaxTries"/> the sharpest is kept and flagged, as it is when
/// the phone leaves the spot first.
/// </summary>
public sealed class SceneAutoCapture
{
    private readonly CaptureSettings _settings;
    private readonly int _stillWidth, _stillHeight;
    private readonly MotionBlurPredictor _predictor;
    private readonly ShotJudge _judge;
    private readonly List<CaptureShot> _shots = [];
    private readonly List<GapWarning> _gaps = [];
    private readonly List<Attempt> _tries = [];
    private CameraPose? _duplicateAt;
    private double? _predictedAtFire;

    /// <param name="stillWidth">The size the platform's stills come out at, for the blur prediction.</param>
    public SceneAutoCapture(CaptureSettings settings, int stillWidth, int stillHeight)
    {
        _settings = settings;
        (_stillWidth, _stillHeight) = (stillWidth, stillHeight);
        _predictor = new MotionBlurPredictor(settings.SceneDepthMetres);
        _judge = new ShotJudge(settings.MaxBlurPx, settings.SimilarBits);
        Spacing = new SceneSpacing(settings.SceneSpacingMetres, settings.SceneSpacingDegrees,
            settings.MinOverlap, settings.SceneDepthMetres);
    }

    public SceneSpacing Spacing { get; }

    public IReadOnlyList<CaptureShot> Shots => _shots;

    public IReadOnlyList<GapWarning> Gaps => _gaps;

    public FrameAdvice OnFrame(ArFrame frame)
    {
        var predicted = _predictor.Update(frame, _stillWidth, _stillHeight);
        if (frame.Tracking != TrackingState.Normal) return new FrameAdvice(false, HoldReason.Tracking, predicted);
        if (predicted is null) return new FrameAdvice(false, HoldReason.Warmup, null);
        if (predicted > _settings.MotionBlurGatePx) return new FrameAdvice(false, HoldReason.Moving, predicted);

        var pose = CameraPose.FromCameraToWorld(frame.CameraToWorld);
        if (!Spacing.IsFarFromEveryShot(pose) || (_duplicateAt is { } dup && !Spacing.IsApart(dup, pose)))
            return new FrameAdvice(false, HoldReason.TooClose, predicted);

        _predictedAtFire = predicted;
        return new FrameAdvice(true, HoldReason.None, predicted);
    }

    /// <summary>Also takes manual-shutter stills, which skip the motion and spacing checks.</summary>
    public StillResult OnStill(CapturedStill still, DateTimeOffset takenAt, GeoLocation? location = null)
    {
        var pose = CameraPose.FromCameraToWorld(still.Frame.CameraToWorld);
        var kept = new List<KeptShot>();
        if (_tries.Count > 0 && Spacing.IsApart(_tries[0].Pose, pose)) kept.Add(CloseTries());

        var judgement = _judge.Judge(still.Pixels.Span, still.Width, still.Height);
        var attempt = new Attempt(still, judgement, takenAt, location, _predictedAtFire);
        _predictedAtFire = null;

        switch (judgement.Verdict)
        {
            case ShotVerdict.Duplicate:
                _duplicateAt = pose;
                break;
            case ShotVerdict.Sharp:
                kept.Add(Keep(attempt, tries: _tries.Count + 1, flagged: false));
                _tries.Clear();
                break;
            case ShotVerdict.Blurry:
                _tries.Add(attempt);
                if (_tries.Count >= _settings.MaxTries) kept.Add(CloseTries());
                break;
        }
        return new StillResult(judgement, kept);
    }

    /// <summary>Ends the capture, keeping the sharpest of any open run of blurry tries.</summary>
    public IReadOnlyList<KeptShot> Finish() => _tries.Count > 0 ? [CloseTries()] : [];

    public CaptureRecord ToRecord(string name, DateTimeOffset startedAt, DeviceInfo device) =>
        new(name, startedAt, device, _settings, _shots.ToArray(), _gaps.ToArray());

    private KeptShot CloseTries()
    {
        var best = _tries.MinBy(t => t.Judgement.BlurPx)!;
        var shot = Keep(best, _tries.Count, flagged: true);
        _tries.Clear();
        return shot;
    }

    private KeptShot Keep(Attempt a, int tries, bool flagged)
    {
        var frame = a.Still.Frame;
        var intrinsics = frame.Intrinsics.ScaledTo(a.Still.Width, a.Still.Height);
        var shot = new CaptureShot(
            _shots.Count,
            CaptureShot.FileNameFor(_shots.Count),
            a.TakenAt,
            frame.Timestamp,
            frame.CameraToWorld,
            intrinsics,
            frame.ExposureDuration,
            ExifOrientation.FromPose(a.Pose),
            a.PredictedBlurPx,
            a.Judgement,
            tries,
            flagged,
            a.Location);
        _shots.Add(shot);
        _judge.Kept(a.Judgement.Hash);
        var gap = Spacing.Add(a.Pose, SceneSpacing.FieldOfViewDegrees(intrinsics));
        if (gap is { } g) _gaps.Add(g);
        return new KeptShot(shot, a.Still.EncodedJpeg, gap);
    }

    private sealed record Attempt(
        CapturedStill Still,
        ShotJudgement Judgement,
        DateTimeOffset TakenAt,
        GeoLocation? Location,
        double? PredictedBlurPx)
    {
        public CameraPose Pose { get; } = CameraPose.FromCameraToWorld(Still.Frame.CameraToWorld);
    }
}
