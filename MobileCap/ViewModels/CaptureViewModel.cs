using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MobileCap.Core;
using MobileCap.Core.Capture;
using MobileCap.Core.Export;
using MobileCap.Platform;

namespace MobileCap.ViewModels;

/// <summary>The live capture screen: one Scene mode capture from start to Finish.</summary>
public partial class CaptureViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IArCapture _ar;
    private readonly CaptureController _controller;
    private bool _started, _stopped;

    public CaptureViewModel(IArPlatform platform, CaptureFolder folder, CaptureSettings settings, DateTimeOffset startedAt)
    {
        _ar = platform.Open(settings);
        _controller = new CaptureController(_ar.Session, _ar.Stills, folder, settings, platform.Device, startedAt,
            _ar.Location);
        _controller.StatusChanged += (_, s) => Dispatcher.UIThread.Post(() => Show(s));
        _controller.ShotKept += (_, k) => Dispatcher.UIThread.Post(() => Kept(k));
        _controller.StillFailed += (_, e) => Dispatcher.UIThread.Post(() => Warning = "Photo failed: " + e.Message);
        _ar.Session.Failed += (_, e) => Dispatcher.UIThread.Post(() => Stopped("AR stopped: " + e.Message));
        Show(_controller.Status);
    }

    public Control ArView => _ar.View;

    public string Name => _controller.Name;

    [ObservableProperty]
    public partial string Hint { get; private set; } = "Starting AR…";

    [ObservableProperty]
    public partial string Counts { get; private set; } = "";

    [ObservableProperty]
    public partial string Blur { get; private set; } = "";

    /// <summary>The latest gap warning or failure, until the next kept shot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string? Warning { get; private set; }

    public bool HasWarning => Warning is not null;

    [ObservableProperty]
    public partial bool CanShoot { get; private set; }

    [ObservableProperty]
    public partial bool IsFinishing { get; private set; }

    public bool AutoCapture
    {
        get => _controller.AutoCapture;
        set
        {
            if (_controller.AutoCapture == value) return;
            _controller.AutoCapture = value;
            OnPropertyChanged();
        }
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            await _controller.StartAsync();
        }
        catch (Exception e)
        {
            Stopped("AR could not start: " + e.Message);
            await _ar.DisposeAsync();
        }
    }

    public void Shutter() => _controller.Shutter();

    /// <summary>Writes the sidecars and releases the camera.</summary>
    public async Task<CaptureRecord> FinishAsync()
    {
        IsFinishing = true;
        CanShoot = false;
        Hint = "Saving…";
        try
        {
            return await _controller.FinishAsync();
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        await _ar.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>AR is gone; Finish still saves whatever was kept.</summary>
    private void Stopped(string why)
    {
        _stopped = true;
        Hint = why;
        CanShoot = false;
    }

    private void Show(CaptureStatus s)
    {
        if (IsFinishing || _stopped) return;
        Hint = HintFor(s);
        Counts = s.Gaps == 0 ? Photos(s.Shots) : $"{Photos(s.Shots)} · {s.Gaps} {(s.Gaps == 1 ? "gap" : "gaps")}";
        Blur = s.PredictedBlurPx is { } px ? $"motion {px:0.0} px" : "";
        CanShoot = s.Ready && !s.Busy;
    }

    private void Kept(KeptShot k)
    {
        _ar.ShowShot(k.Shot, k.Gap);
        Warning = k.Gap is { } g
            ? $"Gap: photo {g.Shot + 1} shares only {g.Overlap:P0} of its view with photo {g.Nearest + 1}."
            : k.Shot.Flagged ? $"Photo {k.Shot.Index + 1} kept blurry after {k.Shot.Tries} tries." : null;
    }

    private static string Photos(int n) => n == 1 ? "1 photo" : $"{n} photos";

    // Hints say what is holding the shutter, never where to go next.
    private static string HintFor(CaptureStatus s)
    {
        if (!s.Ready) return "Starting AR…";
        if (s.Busy) return "Taking photo…";
        return s.Hold switch
        {
            HoldReason.Tracking => "Tracking limited: move slowly over a detailed area",
            HoldReason.Warmup => "Hold steady",
            HoldReason.Moving => "Hold steady",
            HoldReason.TooClose => s.AutoCapture ? "Move or turn for the next photo" : "Tap the shutter",
            _ => s.AutoCapture ? "Taking photo…" : "Tap the shutter",
        };
    }
}
