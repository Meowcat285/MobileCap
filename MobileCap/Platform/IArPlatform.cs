using Avalonia.Controls;
using MobileCap.Core;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;
using MobileCap.Core.Scene;

namespace MobileCap.Platform;

/// <summary>What a platform head supplies for AR capture. Set <see cref="App.Ar"/> before the app starts.</summary>
public interface IArPlatform
{
    /// <summary>Null where AR capture works; otherwise why it does not, for the captures screen.</summary>
    string? UnavailableReason { get; }

    DeviceInfo Device { get; }

    /// <summary>Where capture folders go.</summary>
    string CapturesDirectory { get; }

    /// <summary>One per capture screen; the screen disposes it when the capture ends.</summary>
    IArCapture Open(CaptureSettings settings);
}

public interface IArCapture : IAsyncDisposable
{
    IArSession Session { get; }

    IFrameSource Stills { get; }

    /// <summary>The camera feed with the marker trail drawn into it.</summary>
    Control View { get; }

    /// <summary>The latest fix, or null without one; only asked when GPS is on.</summary>
    Func<GeoLocation?>? Location { get; }

    /// <summary>Adds the shot to the marker trail. UI thread.</summary>
    void ShowShot(CaptureShot shot, GapWarning? gap);
}

public sealed class NoArPlatform(string reason, string capturesDirectory) : IArPlatform
{
    public string? UnavailableReason { get; } = reason;

    public DeviceInfo Device { get; } = new("Unknown", "Unknown", "MobileCap");

    public string CapturesDirectory { get; } = capturesDirectory;

    public IArCapture Open(CaptureSettings settings) => throw new PlatformNotSupportedException(UnavailableReason);
}
