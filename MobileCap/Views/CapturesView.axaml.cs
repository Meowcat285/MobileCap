using Avalonia.Controls;
using Avalonia.Interactivity;
using MobileCap.ViewModels;

namespace MobileCap.Views;

public partial class CapturesView : ContentPage
{
    private bool _opening;

    public CapturesView()
    {
        InitializeComponent();
    }

    private async void NewCaptureButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_opening || Navigation is null || DataContext is not CapturesViewModel vm) return;
        _opening = true;
        try
        {
            await OpenCaptureAsync(Navigation, vm);
        }
        finally
        {
            _opening = false;
        }
    }

    private static async Task OpenCaptureAsync(INavigation navigation, CapturesViewModel vm)
    {
        CaptureViewModel capture;
        try
        {
            capture = vm.NewCapture();
        }
        catch (Exception ex)
        {
            vm.Message = "Could not start a capture: " + ex.Message;
            return;
        }
        var page = new CaptureView { DataContext = capture };
        page.Finished += (_, record) =>
        {
            vm.Message = record.Shots.Count == 0
                ? "Nothing was captured."
                : $"Saved {record.Name}: {record.Shots.Count} photos, {record.Gaps.Count} gap warnings.";
            vm.Refresh();
        };
        page.Failed += (_, ex) =>
        {
            vm.Message = "Saving the capture failed: " + ex.Message;
            vm.Refresh();
        };
        await navigation.PushAsync(page);
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Navigation is not null && DataContext is CapturesViewModel vm)
            await Navigation.PushAsync(new SettingsView { DataContext = vm.Settings });
    }
}
