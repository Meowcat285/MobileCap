using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MobileCap.Core.Export;
using MobileCap.Platform;

namespace MobileCap.ViewModels;

public partial class CapturesViewModel : ViewModelBase
{
    public CapturesViewModel(SettingsViewModel settings, IArPlatform ar)
    {
        Settings = settings;
        Ar = ar;
        Refresh();
    }

    public SettingsViewModel Settings { get; }

    public IArPlatform Ar { get; }

    public bool CanCapture => Ar.UnavailableReason is null;

    public ObservableCollection<string> Captures { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCaptures))]
    public partial int CaptureCount { get; private set; }

    public bool HasNoCaptures => CaptureCount == 0;

    /// <summary>The last capture's outcome, or why capture is unavailable.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    public CaptureViewModel NewCapture()
    {
        var startedAt = DateTimeOffset.Now;
        var folder = new CaptureFolder(UniqueFolder(CaptureFolder.NameFor(startedAt)));
        return new CaptureViewModel(Ar, folder, Settings.ToCaptureSettings(), startedAt);
    }

    public void Refresh()
    {
        Captures.Clear();
        if (Directory.Exists(Ar.CapturesDirectory))
        {
            foreach (var dir in Directory.EnumerateDirectories(Ar.CapturesDirectory, "capture_*")
                         .Select(Path.GetFileName)
                         .Order(StringComparer.Ordinal)
                         .Reverse())
                Captures.Add(dir!);
        }
        CaptureCount = Captures.Count;
        Message ??= Ar.UnavailableReason;
    }

    // Names are by the minute, so a second capture within a minute needs a suffix.
    private string UniqueFolder(string name)
    {
        var path = Path.Combine(Ar.CapturesDirectory, name);
        for (int i = 2; Directory.Exists(path); i++) path = Path.Combine(Ar.CapturesDirectory, $"{name}_{i}");
        return path;
    }
}
