using System.Collections.ObjectModel;

namespace MobileCap.ViewModels;

public partial class CapturesViewModel : ViewModelBase
{
    public CapturesViewModel(SettingsViewModel settings)
    {
        Settings = settings;
    }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<string> Captures { get; } = [];

    public bool HasNoCaptures => Captures.Count == 0;
}
