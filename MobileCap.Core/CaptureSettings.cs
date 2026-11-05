namespace MobileCap.Core;

/// <summary>The thresholds below are first guesses until milestone 5 tunes them on real captures.</summary>
public sealed record CaptureSettings
{
    public CaptureMode Mode { get; init; } = CaptureMode.Scene;

    /// <summary>Off by default; the user opts in from Settings.</summary>
    public bool WriteGpsToExif { get; init; }

    /// <summary>Scene mode auto capture waits until the phone is this far from every earlier shot.</summary>
    public double SceneSpacingMetres { get; init; } = 0.3;

    /// <summary>Or has turned this far from every earlier shot.</summary>
    public double SceneSpacingDegrees { get; init; } = 15;

    /// <summary>Auto capture fires only while predicted motion blur is under this, in full-resolution pixels.</summary>
    public double MotionBlurGatePx { get; init; } = 0.7;

    /// <summary>A still blurred across more full-resolution pixels than this is retried.</summary>
    public float MaxBlurPx { get; init; } = 3;

    /// <summary>A still within this many dHash bits of the last kept one is a duplicate; 0 is off.</summary>
    public int SimilarBits { get; init; } = 6;

    /// <summary>After this many blurry tries at one spot the sharpest is kept and flagged.</summary>
    public int MaxTries { get; init; } = 3;

    /// <summary>Stands in for scene depth in the blur prediction and the gap warnings.</summary>
    public double SceneDepthMetres { get; init; } = 1.5;

    /// <summary>A shot sharing less of its view than this with every earlier shot raises a gap warning.</summary>
    public double MinOverlap { get; init; } = 0.6;
}

public enum CaptureMode
{
    Scene,
    Object,
}
