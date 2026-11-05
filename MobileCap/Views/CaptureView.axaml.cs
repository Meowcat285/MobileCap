using Avalonia.Controls;
using Avalonia.Interactivity;
using MobileCap.Core.Export;
using MobileCap.ViewModels;

namespace MobileCap.Views;

public partial class CaptureView : ContentPage
{
    public CaptureView()
    {
        InitializeComponent();
        NavigationPage.SetHasBackButton(this, false);
    }

    /// <summary>Set once the capture is saved, before the page is popped.</summary>
    public event EventHandler<CaptureRecord>? Finished;

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is CaptureViewModel vm) await vm.StartAsync();
    }

    private void ShutterButton_Click(object? sender, RoutedEventArgs e) => (DataContext as CaptureViewModel)?.Shutter();

    private async void FinishButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CaptureViewModel vm || vm.IsFinishing) return;
        var record = await vm.FinishAsync();
        Finished?.Invoke(this, record);
        if (Navigation is not null) await Navigation.PopAsync();
    }
}
