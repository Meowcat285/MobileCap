using CommunityToolkit.Mvvm.ComponentModel;
using MobileCap.Core;

namespace MobileCap.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool WriteGpsToExif { get; set; } = new CaptureSettings().WriteGpsToExif;

    public CaptureSettings ToCaptureSettings() => new() { WriteGpsToExif = WriteGpsToExif };
}
