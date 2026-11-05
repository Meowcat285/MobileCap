namespace MobileCap.Core;

public sealed record CaptureSettings
{
    public CaptureMode Mode { get; init; } = CaptureMode.Scene;

    /// <summary>Off by default; the user opts in from Settings.</summary>
    public bool WriteGpsToExif { get; init; }

    /// <summary>Scene mode auto capture waits until the phone is this far from every earlier shot.</summary>
    public double SceneSpacingMetres { get; init; } = 0.3;

    /// <summary>Or has turned this far from every earlier shot.</summary>
    public double SceneSpacingDegrees { get; init; } = 15;
}

public enum CaptureMode
{
    Scene,
    Object,
}
