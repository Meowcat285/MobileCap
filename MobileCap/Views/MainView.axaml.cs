using Avalonia;
using Avalonia.Controls;
using MobileCap.ViewModels;

namespace MobileCap.Views;

public partial class MainView : NavigationPage
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (CurrentPage == null)
            await PushAsync(new CapturesView { DataContext = new CapturesViewModel(new SettingsViewModel()) });
    }
}
