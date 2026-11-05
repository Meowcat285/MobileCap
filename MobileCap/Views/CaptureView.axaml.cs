using Avalonia.Controls;
using Avalonia.Interactivity;
using MobileCap.Core.Export;
using MobileCap.ViewModels;

namespace MobileCap.Views;

public partial class CaptureView : ContentPage
{
    // An edge swipe or Android back would skip Finish and lose the capture; only Finish leaves.
    private bool _canLeave;

    public CaptureView()
    {
        InitializeComponent();
        NavigationPage.SetHasBackButton(this, false);
    }

    /// <summary>Set once the capture is saved, before the page is popped.</summary>
    public event EventHandler<CaptureRecord>? Finished;

    /// <summary>Saving failed; the photos already written stay in the folder.</summary>
    public event EventHandler<Exception>? Failed;

    // NativeControlHost follows layout but not render transforms, so a native view attached
    // while the page slides in stays where the slide left it. Show it once the page is in place.
    protected override void OnNavigatedTo(NavigatedToEventArgs e)
    {
        base.OnNavigatedTo(e);
        ArHost.IsVisible = true;
    }

    protected override void OnNavigatingFrom(NavigatingFromEventArgs e)
    {
        if (!_canLeave)
        {
            e.Cancel = true;
            return;
        }
        ArHost.IsVisible = false;
        base.OnNavigatingFrom(e);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is CaptureViewModel vm) await vm.StartAsync();
    }

    private void ShutterButton_Click(object? sender, RoutedEventArgs e) => (DataContext as CaptureViewModel)?.Shutter();

    private async void FinishButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CaptureViewModel vm || vm.IsFinishing) return;
        try
        {
            Finished?.Invoke(this, await vm.FinishAsync());
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex);
        }
        _canLeave = true;
        if (Navigation is not null) await Navigation.PopAsync();
    }
}
