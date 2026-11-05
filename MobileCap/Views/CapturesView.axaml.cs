using Avalonia.Controls;
using Avalonia.Interactivity;
using MobileCap.ViewModels;

namespace MobileCap.Views;

public partial class CapturesView : ContentPage
{
    public CapturesView()
    {
        InitializeComponent();
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Navigation is not null && DataContext is CapturesViewModel vm)
            await Navigation.PushAsync(new SettingsView { DataContext = vm.Settings });
    }
}
